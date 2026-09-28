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
        this.Log.Info($"Shortcut action '{this.DisplayName}' triggered.");
        if (!this.EnsureApplicationIsActive())
        {
            this.Log.Warning(
                $"Shortcut action '{this.DisplayName}' could not activate ChatGPT/Codex Desktop.");
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

        var input = this.character.HasValue
            ? $"character '{this.character.Value}'"
            : $"key '{this.key}'";
        this.Log.Info(
            $"Shortcut action '{this.DisplayName}' dispatched {input} with modifiers '{this.modifiers}'.");
    }

    private Boolean EnsureApplicationIsActive()
    {
        if (this.Plugin.IsApplicationActive())
        {
            return true;
        }

        this.Plugin.ClientApplication.Activate();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            Thread.Sleep(25);
            if (this.Plugin.IsApplicationActive())
            {
                Thread.Sleep(75);
                return true;
            }
        }

        return false;
    }
}
