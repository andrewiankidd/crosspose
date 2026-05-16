using Crosspose.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Crosspose.Doctor.Core.Checks;

public sealed class DockerUsersGroupCheck : ICheckFix
{
    public string Name => "docker-users-group";
    public string Description => "Ensures the current user is a member of the 'docker-users' local group.";
    public bool IsAdditional => false;
    public string AdditionalKey => string.Empty;
    public bool CanFix => true;

    public async Task<CheckResult> RunAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var username = CurrentUserName();
        var result = await runner.RunAsync("powershell",
            $"-NoProfile -NonInteractive -Command \"(Get-LocalGroupMember -Group 'docker-users' -ErrorAction SilentlyContinue).Name -contains '{username}'\"",
            cancellationToken: cancellationToken);

        var output = result.StandardOutput.Trim();
        if (string.Equals(output, "True", StringComparison.OrdinalIgnoreCase))
            return CheckResult.Success($"{username} is a member of docker-users.");

        if (string.IsNullOrWhiteSpace(output) && !result.IsSuccess)
            return CheckResult.Failure("Unable to query docker-users group. Is Docker Desktop installed?");

        return CheckResult.Failure($"{username} is not a member of the 'docker-users' group.");
    }

    public async Task<FixResult> FixAsync(ProcessRunner runner, ILogger logger, CancellationToken cancellationToken)
    {
        var username = CurrentUserName();
        var result = await runner.RunAsync("net",
            $"localgroup docker-users \"{username}\" /add",
            cancellationToken: cancellationToken);

        if (!result.IsSuccess)
        {
            var output = (result.StandardOutput + result.StandardError).ToLowerInvariant();
            if (output.Contains("already a member"))
                return FixResult.Success($"{username} is already a member of docker-users.");

            return FixResult.Failure($"Failed to add {username} to docker-users: {result.StandardError.Trim()}");
        }

        return FixResult.Success($"{username} added to docker-users group.");
    }

    private static string CurrentUserName()
    {
        var domain = Environment.UserDomainName;
        var user = Environment.UserName;
        return string.IsNullOrWhiteSpace(domain) || domain.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? user
            : $"{domain}\\{user}";
    }
}
