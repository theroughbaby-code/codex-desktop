using System;
using System.Threading;

namespace Loupedeck.CodexDesktopPlugin;

public abstract class ShortcutCommandBase : PluginDynamicCommand
{
    private readonly VirtualKeyCode key;
    private readonly ModifierKey modifiers;

    protected ShortcutCommandBase(String displayName, String description, String groupName, VirtualKeyCode key, ModifierKey modifiers)
        : base(displayName, description, groupName, DeviceType.All)
    {
        this.key = key;
        this.modifiers = modifiers;
    }

    protected override void RunCommand(String actionParameter)
    {
        if (!this.EnsureApplicationIsActive())
        {
            return;
        }

        this.Plugin.ClientApplication.SendKeyboardShortcut(this.key, this.modifiers);
    }

    private Boolean EnsureApplicationIsActive()
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
