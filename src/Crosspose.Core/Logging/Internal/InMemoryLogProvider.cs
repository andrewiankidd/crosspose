using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Crosspose.Core.Logging.Internal;

public sealed class InMemoryLogStore
{
    private readonly ConcurrentQueue<string> _lines = new();
    public event Action<string>? OnWrite;

    public void Write(string line)
    {
        var sanitized = SecretCensor.Sanitize(line);
        _lines.Enqueue(sanitized);
        OnWrite?.Invoke(sanitized);
        while (_lines.Count > 1000 && _lines.TryDequeue(out _)) { }
    }

    public string ReadAll() => string.Join(Environment.NewLine, _lines.ToArray());

    public void Clear()
    {
        while (_lines.TryDequeue(out _)) { }
    }

    public IReadOnlyList<string> Snapshot() => _lines.ToArray();
}

internal sealed class InMemoryLogProvider : ILoggerProvider
{
    private readonly InMemoryLogStore _store;

    public InMemoryLogProvider(InMemoryLogStore store)
    {
        _store = store;
    }

    public ILogger CreateLogger(string categoryName) => new InMemoryLogger(_store, categoryName);

    public void Dispose()
    {
    }

    private sealed class InMemoryLogger : ILogger
    {
        private readonly InMemoryLogStore _store;
        private readonly string _category;

        public InMemoryLogger(InMemoryLogStore store, string category)
        {
            _store = store;
            _category = category;
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            var line = $"[{DateTime.Now:HH:mm:ss}] {logLevel,-11} {_category}: {message}";
            if (exception is not null)
            {
                line += $" | {exception}";
            }
            _store.Write(line);
        }
    }
}

internal static class SecretCensor
{
    private static readonly Regex JwtPattern = new(@"[A-Za-z0-9\-_]{20,}\.[A-Za-z0-9\-_]{20,}\.[A-Za-z0-9\-_]{20,}", RegexOptions.Compiled);
    private static readonly Regex BearerPattern = new(@"Bearer\s+[A-Za-z0-9\-_\.]{20,}", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sanitized = JwtPattern.Replace(input, "[REDACTED-JWT]");
        sanitized = BearerPattern.Replace(sanitized, "Bearer [REDACTED]");
        return sanitized;
    }
}
