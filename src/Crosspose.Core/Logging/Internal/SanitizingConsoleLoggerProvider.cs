using Microsoft.Extensions.Logging;

namespace Crosspose.Core.Logging.Internal;

internal sealed class SanitizingConsoleLoggerProvider : ILoggerProvider
{
    private readonly LogLevel _minimum;

    public SanitizingConsoleLoggerProvider(LogLevel minimum)
    {
        _minimum = minimum;
    }

    public ILogger CreateLogger(string categoryName) => new SanitizingConsoleLogger(categoryName, _minimum);

    public void Dispose()
    {
    }

    private sealed class SanitizingConsoleLogger : ILogger
    {
        private readonly string _category;
        private readonly LogLevel _min;

        public SanitizingConsoleLogger(string category, LogLevel min)
        {
            _category = category;
            _min = min;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= _min;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            var line = $"[{DateTime.Now:HH:mm:ss}] {logLevel,-11} {_category}: {message}";
            if (exception is not null)
            {
                line += $" | {exception}";
            }
            var sanitized = SecretCensor.Sanitize(line);
            Console.WriteLine(sanitized);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
