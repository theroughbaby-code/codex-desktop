using System;
using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenCodexCommand : PluginDynamicCommand
{
    public OpenCodexCommand()
        : base("Open Codex", "Opens Codex Desktop using the installed Codex CLI.", "App", DeviceType.All)
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        if (CodexLocator.TryFindCodexExecutable(out var codexPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = codexPath,
                Arguments = "app",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "codex",
            Arguments = "app",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }
}
