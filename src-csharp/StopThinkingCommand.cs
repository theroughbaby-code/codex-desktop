using System;
using System.Threading;

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
        if (!this.EnsureCodexIsActive())
        {
            return;
        }

        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (CodexStopMonitor.TryStopActiveTurn())
            {
                CodexStopMonitor.RefreshSoon();
                return;
            }

            Thread.Sleep(20);
        }
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(new BitmapColor(0, 0, 0, 0));

        var size = Math.Min(builder.Width, builder.Height);
        var centerX = builder.Width / 2;
        var centerY = builder.Height / 2;
        var radius = Math.Max(8, (Int32)(size * 0.34F));
        var active = CodexStopMonitor.HasActiveTurn;
        var dark = new BitmapColor(17, 23, 20);
        var light = new BitmapColor(245, 247, 243);
        var red = new BitmapColor(238, 68, 68);
        var outerStroke = Math.Max(7F, size / 14F);
        var innerStroke = Math.Max(3.5F, size / 28F);
        var squareSize = Math.Max(8, (Int32)(size * 0.24F));
        var squareX = centerX - squareSize / 2;
        var squareY = centerY - squareSize / 2;

        if (active)
        {
            builder.FillCircle(centerX, centerY, radius, red);
            builder.DrawArc(centerX, centerY, radius, 0F, 359.9F, dark, outerStroke);
            builder.FillRectangle(squareX, squareY, squareSize, squareSize, light);
            builder.DrawRectangle(squareX, squareY, squareSize, squareSize, dark);
        }
        else
        {
            builder.DrawArc(centerX, centerY, radius, 0F, 359.9F, dark, outerStroke);
            builder.DrawArc(centerX, centerY, radius, 0F, 359.9F, light, innerStroke);
            builder.FillRectangle(squareX, squareY, squareSize, squareSize, dark);
            var inset = Math.Max(2, squareSize / 7);
            builder.FillRectangle(
                squareX + inset,
                squareY + inset,
                squareSize - inset * 2,
                squareSize - inset * 2,
                light);
        }

        return builder.ToImage();
    }

    private Boolean EnsureCodexIsActive()
    {
        if (this.Plugin.IsApplicationActive())
        {
            return true;
        }

        this.Plugin.ClientApplication.Activate();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            Thread.Sleep(20);
            if (this.Plugin.IsApplicationActive())
            {
                return true;
            }
        }

        return false;
    }

    private void HandleStateChanged() => this.ActionImageChanged();
}
