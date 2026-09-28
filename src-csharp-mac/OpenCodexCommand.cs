using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenCodexCommand : PluginDynamicCommand
{
    public OpenCodexCommand()
        : base("Open Codex", "Opens ChatGPT and Codex Desktop.", "App", DeviceType.All)
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            ArgumentList = { "-b", "com.openai.codex" },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }
}
