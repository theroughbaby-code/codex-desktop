using System;

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
        if (!this.Plugin.IsApplicationActive())
        {
            this.Plugin.ClientApplication.Activate();
        }

        this.Plugin.ClientApplication.SendKeyboardShortcut(this.key, this.modifiers);
    }
}
