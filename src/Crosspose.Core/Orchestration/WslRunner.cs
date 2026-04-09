using System.IO;
using Crosspose.Core.Diagnostics;

namespace Crosspose.Core.Orchestration;

public sealed class WslRunner : VirtualizationPlatformRunnerBase
{
    public WslRunner(ProcessRunner runner) : base("wsl", runner)
    {
    }

    /// <summary>
    /// Converts a Windows absolute path to its WSL /mnt/&lt;drive&gt;/... equivalent.
    /// Non-Windows paths are returned with backslashes replaced by forward slashes.
    /// </summary>
    public static string ToWslPath(string path)
    {
        path = Path.GetFullPath(path);
        if (path.Length >= 2 && path[1] == ':')
        {
            var drive = char.ToLowerInvariant(path[0]);
            var rest = path[2..].Replace('\\', '/').TrimStart('/');
            return $"/mnt/{drive}/{rest}";
        }
        return path.Replace('\\', '/');
    }
}
