namespace Loupedeck.CodexDesktopPlugin;

public sealed class StopThinkingCommand : PluginDynamicCommand
{
    public StopThinkingCommand()
        : base(
            "Stop thinking",
            "Stops the active Codex response and opens its chat.",
            "Chat",
            DeviceType.All)
    {
    }

    protected override Boolean OnLoad()
    {
        CodexMacStateMonitor.StopChanged += this.HandleStateChanged;
        CodexMacStateMonitor.StartStop();
        return true;
    }

    protected override Boolean OnUnload()
    {
        CodexMacStateMonitor.StopChanged -= this.HandleStateChanged;
        CodexMacStateMonitor.StopStop();
        return true;
    }

    protected override void RunCommand(String actionParameter)
    {
        _ = CodexMacStateMonitor.TryStop();
        CodexMacStateMonitor.RefreshSoon();
    }

    protected override BitmapImage GetCommandImage(
        String actionParameter,
        PluginImageSize imageSize)
        => PluginResources.ReadImage(
            CodexMacStateMonitor.HasActiveTurn
                ? "StopThinkingActive.png"
                : "StopThinkingIdle.png");

    private void HandleStateChanged() => this.ActionImageChanged();
}
