using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class CodexDesktopApplication : ClientApplication
{
    protected override String[] GetProcessNames() => new[] { "ChatGPT", "ChatGPT Classic" };

    public override ClientApplicationStatus GetApplicationStatus()
        => CodexLocator.TryFindCodexExecutable(out _) || this.GetRunningAndSupportedProcessNames().Any()
            ? ClientApplicationStatus.Installed
            : ClientApplicationStatus.Unknown;
}

internal static class CodexLocator
{
    public static Boolean TryFindCodexExecutable(out String path)
    {
        path = String.Empty;

        var fromEnvironment = Environment.GetEnvironmentVariable("CODEX_CLI");
        if (IsExecutable(fromEnvironment))
        {
            path = fromEnvironment!;
            return true;
        }

        foreach (var candidate in EnumeratePathCandidates("codex.exe"))
        {
            if (IsExecutable(candidate))
            {
                path = candidate;
                return true;
            }
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var binRoot = Path.Combine(localAppData, "OpenAI", "Codex", "bin");
        if (Directory.Exists(binRoot))
        {
            foreach (var directory in Directory.EnumerateDirectories(binRoot).OrderByDescending(Directory.GetLastWriteTimeUtc))
            {
                var candidate = Path.Combine(directory, "codex.exe");
                if (IsExecutable(candidate))
                {
                    path = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    private static String[] EnumeratePathCandidates(String executableName)
    {
        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? String.Empty;
        return pathVariable
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, executableName))
            .ToArray();
    }

    private static Boolean IsExecutable(String? path)
        => !String.IsNullOrWhiteSpace(path) && File.Exists(path);
}
