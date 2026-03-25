/// <summary>
/// Tiny shim exe used by tests to simulate external CLI tools (docker, podman, helm, wsl, etc.).
///
/// Usage:
///   Crosspose.TestShim --stdout <file> [--stderr <file>] [--exit <code>]
///   Crosspose.TestShim --stdout-inline <text> [--stderr-inline <text>] [--exit <code>]
///
/// The shim writes the contents of the specified file (or inline text) to stdout/stderr,
/// then exits with the given code. This lets tests point a real ProcessRunner at this exe
/// instead of docker/podman/helm, so the full pipeline (process spawn, output capture,
/// JSON parsing) is exercised end-to-end with deterministic output.
///
/// Tests create fixture files with captured real output, then configure ProcessRunner
/// to invoke this shim instead of the real tool.
/// </summary>

var stdoutFile = GetArg(args, "--stdout");
var stderrFile = GetArg(args, "--stderr");
var stdoutInline = GetArg(args, "--stdout-inline");
var stderrInline = GetArg(args, "--stderr-inline");
var exitCode = int.TryParse(GetArg(args, "--exit"), out var code) ? code : 0;

// Write stdout
if (!string.IsNullOrEmpty(stdoutFile) && File.Exists(stdoutFile))
{
    Console.Write(File.ReadAllText(stdoutFile));
}
else if (!string.IsNullOrEmpty(stdoutInline))
{
    Console.Write(stdoutInline);
}

// Write stderr
if (!string.IsNullOrEmpty(stderrFile) && File.Exists(stderrFile))
{
    Console.Error.Write(File.ReadAllText(stderrFile));
}
else if (!string.IsNullOrEmpty(stderrInline))
{
    Console.Error.Write(stderrInline);
}

return exitCode;

static string? GetArg(string[] args, string name)
{
    for (int i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }
    return null;
}
