using System;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class StopThinkingCommand : PluginDynamicCommand
{
    public StopThinkingCommand()
        : base(
            "Stop thinking",
            "Stops the active Codex response when one is running.",
            "Chat",
            DeviceType.All)
    {
    }

    protected override Boolean OnLoad()
    {
        CodexStopMonitor.Changed += this.HandleStateChanged;
        CodexStopMonitor.Start();
        return true;
    }

    protected override Boolean OnUnload()
    {
        CodexStopMonitor.Changed -= this.HandleStateChanged;
        CodexStopMonitor.Stop();
        return true;
    }

    protected override void RunCommand(String actionParameter)
    {
        if (CodexStopMonitor.TryStopActiveTurn())
        {
            CodexStopMonitor.RefreshSoon();
        }
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        => PluginResources.ReadImage(
            CodexStopMonitor.HasActiveTurn
                ? "StopThinkingActive.png"
                : "StopThinkingIdle.png");

    private void HandleStateChanged() => this.ActionImageChanged();
}
