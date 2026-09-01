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

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        var status = this.client.Status;
        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(new BitmapColor(0, 0, 0, 0));

        var size = Math.Min(builder.Width, builder.Height);
        var centerX = builder.Width / 2;
        var centerY = builder.Height / 2;
        var radius = Math.Max(8, size / 2 - Math.Max(5, size / 14));
        var stroke = Math.Max(3F, size / 18F);
        var muted = new BitmapColor(96, 103, 112);
        var bright = new BitmapColor(238, 240, 236);
        var accent = status.State == RateLimitState.Available
            ? new BitmapColor(68, 196, 142)
            : new BitmapColor(238, 184, 78);

        builder.DrawCircle(centerX, centerY, radius, muted);
        if (status.State == RateLimitState.Available && status.PrimaryRemainingPercent is not null)
        {
            builder.DrawArc(
                centerX,
                centerY,
                radius,
                -90F,
                360F * status.PrimaryRemainingPercent.Value / 100F,
                accent,
                stroke);
            builder.DrawText(
                $"{status.PrimaryRemainingPercent}%",
                0,
                centerY - size / 6,
                builder.Width,
                size / 3,
                bright,
                Math.Max(12, size / 4));
        }
        else
        {
            var label = status.State == RateLimitState.SignInRequired ? "SIGN IN" : status.State == RateLimitState.Loading ? "..." : "N/A";
            builder.DrawText(label, 0, centerY - size / 7, builder.Width, size / 3, accent, Math.Max(10, size / 7));
        }

        return builder.ToImage();
    }

    private void HandleUpdated() => this.ActionImageChanged();
}
