using Microsoft.Extensions.Logging;
using SerilogLogger = Serilog.ILogger;
using Serilog.Events;

namespace Crosspose.Core.Logging.Internal;

/// <summary>
/// Wraps a Serilog logger and applies secret sanitization before forwarding.
/// </summary>
internal sealed class SanitizingSerilogLoggerProvider : ILoggerProvider
{
    private readonly SerilogLogger _serilog;
    private readonly LogEventLevel _minimumLevel;

    public SanitizingSerilogLoggerProvider(SerilogLogger serilog, LogEventLevel minimumLevel)
        {
            _serilog = serilog;
            _minimumLevel = minimumLevel;
        }

    public static LogEventLevel ConvertLogLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    public ILogger CreateLogger(string categoryName) =>
        new SanitizingSerilogLogger(_serilog.ForContext("Category", categoryName), _minimumLevel);

    public void Dispose()
    {
        // Serilog logger is owned by caller; disposal handled there.
    }

    private sealed class SanitizingSerilogLogger : ILogger
    {
        private readonly SerilogLogger _serilog;
        private readonly LogEventLevel _minimumLevel;

        public SanitizingSerilogLogger(SerilogLogger serilog, LogEventLevel minimumLevel)
        {
            _serilog = serilog;
            _minimumLevel = minimumLevel;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => ConvertLogLevel(logLevel) >= _minimumLevel;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var level = ConvertLogLevel(logLevel);
            if (level < _minimumLevel) return;
            var message = SecretCensor.Sanitize(formatter(state, exception));
            var exceptionText = exception?.ToString();
            if (!string.IsNullOrEmpty(exceptionText))
            {
                exceptionText = SecretCensor.Sanitize(exceptionText);
                exception = new Exception(exceptionText);
            }

            _serilog.Write(level, exception, message);
        }

    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
