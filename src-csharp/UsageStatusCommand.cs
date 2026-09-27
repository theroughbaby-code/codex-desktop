using System;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class UsageStatusCommand : PluginDynamicCommand
{
    private readonly CodexRateLimitClient client = new();

    public UsageStatusCommand()
        : base(
            "Usage status",
            "Shows remaining Codex usage and refreshes it when pressed.",
            "App",
            DeviceType.All)
    {
    }

    protected override Boolean OnLoad()
    {
        this.client.Updated += this.HandleUpdated;
        this.client.Start();
        return true;
    }

    protected override Boolean OnUnload()
    {
        this.client.Updated -= this.HandleUpdated;
        this.client.Dispose();
        return true;
    }

    protected override void RunCommand(String actionParameter)
        => _ = this.client.RefreshAsync();

    protected override String GetCommandDisplayName(String actionParameter, PluginImageSize imageSize)
    {
        var status = this.client.Status;
        return status.State switch
        {
            RateLimitState.Available when status.SecondaryRemainingPercent is not null
                => $"{status.PrimaryRemainingPercent}% / {status.SecondaryRemainingPercent}% left",
            RateLimitState.Available => $"{status.PrimaryRemainingPercent}% left",
            RateLimitState.SignInRequired => "Codex sign in",
            RateLimitState.Loading => "Usage loading",
            _ => "Usage unavailable",
        };
    }

    private void HandleUpdated() => this.ActionImageChanged();
}
