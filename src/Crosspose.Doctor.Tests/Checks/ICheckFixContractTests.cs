using Crosspose.Core.Diagnostics;
using Crosspose.Doctor.Core.Checks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Crosspose.Doctor.Tests.Checks;

/// <summary>
/// Contract tests that verify all ICheckFix implementations follow the expected patterns.
/// Runs against every check returned by CheckCatalog.
/// </summary>
public class ICheckFixContractTests
{
    private static readonly ProcessRunner Runner = new(NullLogger<ProcessRunner>.Instance);

    public static IEnumerable<object[]> AllBuiltInChecks()
    {
        var checks = Crosspose.Doctor.Core.CheckCatalog.LoadAll();
        foreach (var check in checks)
        {
            yield return new object[] { check };
        }
    }

    [Theory]
    [MemberData(nameof(AllBuiltInChecks))]
    public void Check_HasNonEmptyName(ICheckFix check)
    {
        Assert.False(string.IsNullOrWhiteSpace(check.Name));
    }

    [Theory]
    [MemberData(nameof(AllBuiltInChecks))]
    public void Check_HasNonEmptyDescription(ICheckFix check)
    {
        Assert.False(string.IsNullOrWhiteSpace(check.Description));
    }

    [Theory]
    [MemberData(nameof(AllBuiltInChecks))]
    public async Task RunAsync_DoesNotThrow(ICheckFix check)
    {
        // Every check should handle missing tools gracefully — return Failure, not throw
        var result = await check.RunAsync(Runner, NullLogger.Instance, CancellationToken.None);
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Theory]
    [MemberData(nameof(AllBuiltInChecks))]
    public void Check_IsNotAdditional(ICheckFix check)
    {
        // All built-in checks from LoadAll() without keys should not be additional
        Assert.False(check.IsAdditional);
    }
}
