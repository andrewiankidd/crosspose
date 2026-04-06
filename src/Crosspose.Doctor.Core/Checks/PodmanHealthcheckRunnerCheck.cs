using Crosspose.Core.Configuration;
using Crosspose.Core.Diagnostics;
using Crosspose.Core.Orchestration;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

/// <summary>
/// Drives podman container healthchecks that would normally be triggered by a systemd timer.
/// The crosspose-data WSL distro has no systemd, so healthchecks must be run externally.
/// Without this, containers with `depends_on: condition: service_healthy` are stuck in
/// Created state forever because the dependency never transitions out of "starting".
///
/// RunAsync is both the check and the worker: it finds all running containers with health
/// status "starting" and calls `podman healthcheck run` for each. It returns failure if
/// any remain starting after the run (i.e., the healthcheck itself failed).
/// </summary>
public sealed class PodmanHealthcheckRunnerCheck : ICheckFix
{
    public string Name => "podman-healthcheck-runner";
    public string Description => "Drives podman container healthchecks (no systemd). Required for service_healthy depends_on to work.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => false;
    public bool AutoFix => false;
    public int CheckIntervalSeconds => 10;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var wsl = new WslRunner(runner);
        var distro = CrossposeEnvironment.WslDistro;

        // Find all running containers whose healthcheck is in "starting" state.
        var startingResult = await wsl.ExecAsync(
            ["-d", distro, "--", "podman", "ps", "--filter", "health=starting", "--format", "{{.Names}}"],
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!startingResult.IsSuccess)
            return CheckResult.Success("No podman containers accessible.");

        var starting = ParseNames(startingResult.StandardOutput);
        if (starting.Count == 0)
            return CheckResult.Success("All podman containers with healthchecks are past starting state.");

        // Run the healthcheck for each. The result tells us whether it passed this cycle.
        var failed = new List<string>();
        foreach (var name in starting)
        {
            var hc = await wsl.ExecAsync(
                ["-d", distro, "--", "podman", "healthcheck", "run", name],
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (hc.IsSuccess)
                logger.LogInformation("Healthcheck passed for {Container} — transitioning to healthy.", name);
            else
            {
                logger.LogWarning("Healthcheck failed for {Container}: {Error}", name, hc.StandardError.Trim());
                failed.Add(name);
            }
        }

        if (failed.Count > 0)
            return CheckResult.Failure($"Healthcheck failed for: {string.Join(", ", failed)}");

        return CheckResult.Success($"Ran healthchecks for {starting.Count} container(s) — all passed.");
    }

    public Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
        => Task.FromResult(FixResult.Failure("No separate fix — RunAsync drives healthchecks directly."));

    private static List<string> ParseNames(string output) =>
        output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Where(l => !string.IsNullOrWhiteSpace(l))
              .ToList();
}
