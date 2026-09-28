namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenModelPickerCommand : ShortcutCommandBase
{
    public OpenModelPickerCommand()
        : base(
            "Open model picker",
            "Opens the model picker for the active Codex composer.",
            "General",
            VirtualKeyCode.KeyM,
            ModifierKey.ControlOrCommand | ModifierKey.Shift)
    {
    }
}
