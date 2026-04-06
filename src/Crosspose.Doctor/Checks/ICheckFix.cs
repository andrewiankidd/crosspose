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

    /// <summary>
    /// When true this check requires external network/cloud connectivity.
    /// It is suppressed when offline mode is active.
    /// </summary>
    bool RequiresConnectivity => false;

    /// <summary>
    /// How often the background monitor re-runs this check, in seconds.
    /// Fast/critical checks use low values; slow install checks use high values.
    /// </summary>
    int CheckIntervalSeconds => 60;

    /// <summary>
    /// When true, the background monitor calls FixAsync automatically on failure.
    /// Only set on high-confidence, low-risk fixes (removing stale state, recreating containers).
    /// </summary>
    bool AutoFix => false;

    /// <summary>
    /// Check names that must have a passing result before AutoFix is allowed to run.
    /// Prevents cascading fixes when a foundational dependency (e.g. WSL, podman) is
    /// in a bad state — auto-fixing against a broken substrate makes things worse.
    /// </summary>
    IReadOnlyList<string> AutoFixRequires => [];

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
