namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenTerminalCommand : ShortcutCommandBase
{
    public OpenTerminalCommand()
        : base(
            "Open terminal",
            "Opens or closes the integrated terminal.",
            "Panels",
            '`',
            ModifierKey.Control)
    {
    }
}
