using System.IO;
using Crosspose.Core.Configuration;
using Crosspose.Core.Logging.Internal;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Crosspose.Core.Logging;

public static class CrossposeLoggerFactory
{
    /// <summary>
    /// Creates a logger factory with sanitization (JWT/secret masking) applied to all sinks (console, in-memory, file).
    /// File logging is enabled by default; override path via compose.log-file in crosspose.yml.
    /// </summary>
    public static ILoggerFactory Create(LogLevel minimumLogLevel = LogLevel.Information, InMemoryLogStore? logStore = null)
    {
        // Configure Serilog file sink
        var logFile = CrossposeEnvironment.LogFilePath
                     ?? Path.Combine(AppContext.BaseDirectory, "logs", "crosspose.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);

        var serilogLevel = Internal.SanitizingSerilogLoggerProvider.ConvertLogLevel(minimumLogLevel);
        var serilogLogger = new Serilog.LoggerConfiguration()
            .MinimumLevel.Is(serilogLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(logFile,
                rollingInterval: Serilog.RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(minimumLogLevel);
            builder.ClearProviders();

            // Sanitized console output
            builder.AddProvider(new SanitizingConsoleLoggerProvider(minimumLogLevel));

            // Optional in-memory sink (also sanitized)
            if (logStore is not null)
            {
                builder.AddProvider(new InMemoryLogProvider(logStore));
            }

            // File sink (Serilog) with sanitization wrapper
            builder.AddProvider(new Internal.SanitizingSerilogLoggerProvider(serilogLogger, serilogLevel));
        });
    }
}
