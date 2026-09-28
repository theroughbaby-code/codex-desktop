namespace Loupedeck.CodexDesktopPlugin;

public sealed class CodexDesktopApplication : ClientApplication
{
    protected override String[] GetProcessNames() => new[] { "ChatGPT" };

    public override ClientApplicationStatus GetApplicationStatus()
    {
        var appExists = Directory.Exists("/Applications/ChatGPT.app");
        var processIsRunning = this.GetRunningAndSupportedProcessNames().Any();
        return appExists || processIsRunning
            ? ClientApplicationStatus.Installed
            : ClientApplicationStatus.Unknown;
    }
}
