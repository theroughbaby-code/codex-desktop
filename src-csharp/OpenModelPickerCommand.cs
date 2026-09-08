using System;
using System.Threading;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenModelPickerCommand : PluginDynamicCommand
{
    public OpenModelPickerCommand()
        : base(
            "Open model picker",
            "Opens the model picker for the active Codex composer.",
            "General",
            DeviceType.All)
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        if (!this.EnsureCodexIsActive())
        {
            return;
        }

        if (CodexDesktopUiAutomation.TryOpenModelPicker())
        {
            return;
        }

        // The native command remains the safest fallback when Electron has not
        // exposed the composer control to Windows accessibility yet.
        this.Plugin.ClientApplication.SendKeyboardShortcut(
            VirtualKeyCode.KeyM,
            ModifierKey.Control | ModifierKey.Shift);
    }

    private Boolean EnsureCodexIsActive()
    {
        if (this.Plugin.IsApplicationActive())
        {
            return true;
        }

        this.Plugin.ClientApplication.Activate();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            Thread.Sleep(20);
            if (this.Plugin.IsApplicationActive())
            {
                return true;
            }
        }

        return false;
    }
}
