namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenTerminalCommand : MacDesktopCommandBase
{
    public OpenTerminalCommand()
        : base(
            "Open terminal",
            "Opens the integrated terminal.",
            "Panels",
            MacDesktopCommand.OpenTerminal)
    {
    }
}
