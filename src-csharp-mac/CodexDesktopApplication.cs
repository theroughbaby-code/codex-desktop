namespace Loupedeck.CodexDesktopPlugin;

public sealed class CodexDesktopApplication : ClientApplication
{
    protected override String[] GetProcessNames() => new[] { "ChatGPT", "ChatGPT Classic" };

    protected override String[] GetBundleNames() => new[] { "com.openai.codex" };

    public override ClientApplicationStatus GetApplicationStatus()
    {
        var appExists = CodexLocator.TryFindCodexExecutable(out _)
            || Directory.Exists("/Applications/ChatGPT.app")
            || Directory.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Applications",
                "ChatGPT.app"));
        var processIsRunning = this.GetRunningAndSupportedProcessNames().Any();
        return appExists || processIsRunning
            ? ClientApplicationStatus.Installed
            : ClientApplicationStatus.Unknown;
    }
}

internal static class CodexLocator
{
    private static readonly String[] BundledRelativePaths =
    {
        "Contents/Resources/codex-cli/bin/codex",
        "Contents/Resources/codex-cli/CodexCLI.app/Contents/MacOS/codex",
    };

    public static Boolean TryFindCodexExecutable(out String path)
    {
        path = String.Empty;

        var configuredPath = Environment.GetEnvironmentVariable("CODEX_CLI");
        if (IsExecutable(configuredPath))
        {
            path = configuredPath!;
            return true;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var appPath in new[]
        {
            "/Applications/ChatGPT.app",
            Path.Combine(home, "Applications", "ChatGPT.app"),
        })
        {
            foreach (var relativePath in BundledRelativePaths)
            {
                var candidate = Path.Combine(appPath, relativePath);
                if (IsExecutable(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }

        foreach (var candidate in EnumeratePathCandidates("codex").Concat(new[]
        {
            Path.Combine(home, ".local", "bin", "codex"),
            Path.Combine(home, ".npm-global", "bin", "codex"),
            "/opt/homebrew/bin/codex",
            "/usr/local/bin/codex",
        }))
        {
            if (IsExecutable(candidate))
            {
                path = candidate;
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<String> EnumeratePathCandidates(String executableName)
        => (Environment.GetEnvironmentVariable("PATH") ?? String.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, executableName));

    private static Boolean IsExecutable(String? path)
    {
        if (String.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        try
        {
            var mode = File.GetUnixFileMode(path);
            return (mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch
        {
            return true;
        }
    }
}
