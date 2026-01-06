using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Checks;

public interface ICheckFix
{
    string Name { get; }
    string Description { get; }
    bool IsAdditional { get; }
    string AdditionalKey { get; }
    bool CanFix { get; }

    Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken);
    Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken);
}

public sealed record CheckResult(bool IsSuccessful, string Message)
{
    public static CheckResult Success(string message) => new(true, message);
    public static CheckResult Failure(string message) => new(false, message);
}

public sealed record FixResult(bool Succeeded, string Message)
{
    public static FixResult Success(string message) => new(true, message);
    public static FixResult Failure(string message) => new(false, message);
}
