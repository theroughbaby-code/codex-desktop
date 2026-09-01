using System;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenTerminalCommand : PluginDynamicCommand
{
    public OpenTerminalCommand()
        : base("Open terminal", "Opens the integrated terminal with Ctrl+`.", "Panels", DeviceType.All)
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        if (!this.Plugin.IsApplicationActive())
        {
            this.Plugin.ClientApplication.Activate();
        }

        this.Plugin.ClientApplication.SendKeyboardShortcut('`', ModifierKey.Control);
    }
}
