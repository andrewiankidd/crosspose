using System.Collections.Generic;
using System.Threading;
using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

public interface IVirtualizationPlatformRunner
{
    string BaseCommand { get; }

    /// <summary>
    /// Executes the platform command with the provided arguments and returns the process result.
    /// </summary>
    Task<ProcessResult> ExecAsync(IEnumerable<string> args, IReadOnlyDictionary<string, string>? environment = null, CancellationToken cancellationToken = default);
}

public record PlatformCommandResult(string Platform, ProcessResult Result)
{
    public bool HasError => !Result.IsSuccess;
    public string Error => Result.StandardError;
}
