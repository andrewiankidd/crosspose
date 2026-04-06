using System.Collections.Concurrent;
using Crosspose.Core.Diagnostics;
using Crosspose.Doctor.Checks;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor;

/// <summary>
/// Runs Doctor checks on independent async loops, each at their own interval.
/// Checks that declare AutoFix=true have FixAsync called automatically on failure,
/// subject to AutoFixRequires pre-conditions all being currently passing.
/// The GUI subscribes to CheckUpdated/AutoFixApplied to refresh the UI without polling.
/// </summary>
public sealed class DoctorMonitor : IDisposable
{
    private readonly IReadOnlyList<ICheckFix> _checks;
    private readonly ProcessRunner _runner;
    private readonly ILoggerFactory _loggerFactory;
    private CancellationTokenSource? _cts;

    // Tracks the latest result per check name so AutoFixRequires can be evaluated.
    private readonly ConcurrentDictionary<string, bool> _latestResults = new(StringComparer.OrdinalIgnoreCase);

    public event Action<ICheckFix, CheckResult>? CheckUpdated;
    public event Action<ICheckFix, FixResult>? AutoFixApplied;

    public DoctorMonitor(IReadOnlyList<ICheckFix> checks, ProcessRunner runner, ILoggerFactory loggerFactory)
    {
        _checks = checks;
        _runner = runner;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Starts all check loops. Each check runs immediately, then repeats on its own interval.
    /// Fire-and-forget — returns immediately.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        // Stagger check starts by 200ms each to avoid an initial burst of concurrent WSL/podman calls.
        var delay = 0;
        foreach (var check in _checks)
        {
            _ = RunLoopWithInitialDelayAsync(check, TimeSpan.FromMilliseconds(delay), _cts.Token);
            delay += 200;
        }
    }

    private async Task RunLoopWithInitialDelayAsync(ICheckFix check, TimeSpan initialDelay, CancellationToken ct)
    {
        if (initialDelay > TimeSpan.Zero)
        {
            try { await Task.Delay(initialDelay, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
        await RunLoopAsync(check, ct).ConfigureAwait(false);
    }

    // Maximum time allowed for a single check RunAsync call before it is cancelled.
    // Prevents slow or hung wsl/podman commands from stacking up when WSL is overloaded.
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(25);

    private async Task RunLoopAsync(ICheckFix check, CancellationToken ct)
    {
        var logger = _loggerFactory.CreateLogger(check.Name);

        while (!ct.IsCancellationRequested)
        {
            CheckResult result;
            try
            {
                using var checkCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                checkCts.CancelAfter(CheckTimeout);
                result = await check.RunAsync(_runner, logger, checkCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timed out (not the monitor being stopped) — treat as transient failure, don't break the loop.
                logger.LogDebug("Check {Name} timed out after {Timeout}s — skipping this cycle.", check.Name, (int)CheckTimeout.TotalSeconds);
                result = CheckResult.Failure($"Check timed out after {(int)CheckTimeout.TotalSeconds}s.");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Check {Name} threw unexpectedly", check.Name);
                result = CheckResult.Failure($"Unexpected error: {ex.Message}");
            }

            _latestResults[check.Name] = result.IsSuccessful;
            CheckUpdated?.Invoke(check, result);

            if (!result.IsSuccessful && check.AutoFix && check.CanFix && AutoFixPreConditionsMet(check, logger))
            {
                FixResult fix;
                try
                {
                    fix = await check.FixAsync(_runner, logger, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Auto-fix for {Name} threw unexpectedly", check.Name);
                    fix = FixResult.Failure($"Unexpected error: {ex.Message}");
                }

                AutoFixApplied?.Invoke(check, fix);

                if (fix.Succeeded)
                {
                    // Verify immediately after a successful fix so the UI reflects the healed state.
                    try
                    {
                        var verify = await check.RunAsync(_runner, logger, ct).ConfigureAwait(false);
                        _latestResults[check.Name] = verify.IsSuccessful;
                        CheckUpdated?.Invoke(check, verify);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { /* best-effort verify */ }
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(check.CheckIntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
        }
    }

    private bool AutoFixPreConditionsMet(ICheckFix check, ILogger logger)
    {
        var requires = check.AutoFixRequires;
        if (requires.Count == 0) return true;

        foreach (var name in requires)
        {
            if (!_latestResults.TryGetValue(name, out var passing) || !passing)
            {
                logger.LogDebug(
                    "AutoFix for {Check} skipped — pre-condition '{Requires}' is not passing.",
                    check.Name, name);
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
