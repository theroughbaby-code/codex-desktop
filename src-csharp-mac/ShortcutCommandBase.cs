namespace Loupedeck.CodexDesktopPlugin;

public abstract class ShortcutCommandBase : PluginDynamicCommand
{
    private readonly VirtualKeyCode? key;
    private readonly Char? character;
    private readonly ModifierKey modifiers;

    protected ShortcutCommandBase(String displayName, String description, String groupName, VirtualKeyCode key, ModifierKey modifiers)
        : base(displayName, description, groupName, DeviceType.All)
    {
        this.key = key;
        this.modifiers = modifiers;
    }

    protected ShortcutCommandBase(String displayName, String description, String groupName, Char character, ModifierKey modifiers)
        : base(displayName, description, groupName, DeviceType.All)
    {
        this.character = character;
        this.modifiers = modifiers;
    }

    protected override void RunCommand(String actionParameter)
    {
        if (!this.EnsureApplicationIsActive())
        {
            return;
        }

        if (this.character.HasValue)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(this.character.Value, this.modifiers);
        }
        else if (this.key.HasValue)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(this.key.Value, this.modifiers);
        }
    }

    private Boolean EnsureApplicationIsActive()
    {
        if (this.Plugin.IsApplicationActive())
        {
            return true;
        }

        this.Plugin.ClientApplication.Activate();
        for (var attempt = 0; attempt < 10; attempt++)
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
