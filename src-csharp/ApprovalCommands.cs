using System;
using System.Threading;

namespace Loupedeck.CodexDesktopPlugin;

public abstract class ApprovalCommandBase : PluginDynamicCommand
{
    private readonly ApprovalDecision decision;

    protected ApprovalCommandBase(String displayName, String description, ApprovalDecision decision)
        : base(displayName, description, "Approvals", DeviceType.All)
    {
        this.decision = decision;
    }

    protected override Boolean OnLoad()
    {
        CodexApprovalMonitor.Changed += this.HandleStateChanged;
        CodexApprovalMonitor.Start();
        return true;
    }

    protected override Boolean OnUnload()
    {
        CodexApprovalMonitor.Changed -= this.HandleStateChanged;
        CodexApprovalMonitor.Stop();
        return true;
    }

    protected override void RunCommand(String actionParameter)
    {
        if (!this.EnsureCodexIsActive())
        {
            return;
        }

        var attempt = CodexApprovalMonitor.TryInvoke(this.decision);
        if (attempt == ApprovalAttempt.NoApproval)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyA, ModifierKey.Control | ModifierKey.Alt);
            attempt = this.TryAfterNavigation();
        }

        if (attempt == ApprovalAttempt.FoundButUnavailable)
        {
            if (this.decision == ApprovalDecision.Approve)
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Return, ModifierKey.None);
            }
            else if (this.decision == ApprovalDecision.Deny)
            {
                this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Escape, ModifierKey.None);
            }
        }

        CodexApprovalMonitor.RefreshSoon();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
    {
        using var builder = new BitmapBuilder(imageSize);
        builder.Clear(new BitmapColor(0, 0, 0, 0));

        var size = Math.Min(builder.Width, builder.Height);
        var centerX = builder.Width / 2F;
        var centerY = builder.Height / 2F;
        var radius = Math.Max(8F, size * 0.34F);
        var ringCenterX = builder.Width / 2;
        var ringCenterY = builder.Height / 2;
        var ringRadius = Math.Max(8, (Int32)(size * 0.34F));
        var pending = CodexApprovalMonitor.HasPendingApproval;
        var dark = new BitmapColor(17, 23, 20);
        var light = new BitmapColor(245, 247, 243);
        var accent = this.decision switch
        {
            ApprovalDecision.Approve => new BitmapColor(45, 201, 118),
            ApprovalDecision.AlwaysApprove => new BitmapColor(241, 177, 52),
            _ => new BitmapColor(238, 86, 82),
        };
        var glyph = pending ? dark : light;
        var outerStroke = Math.Max(7F, size / 13F);
        var innerStroke = Math.Max(3.5F, size / 26F);
        var ringOuterStroke = Math.Max(8F, size / 12F);
        var ringInnerStroke = Math.Max(4F, size / 24F);

        if (pending)
        {
            builder.FillCircle(centerX, centerY, radius, accent);
            builder.DrawArc(ringCenterX, ringCenterY, ringRadius, 0F, 359.9F, dark, ringOuterStroke);
        }
        else
        {
            builder.DrawArc(ringCenterX, ringCenterY, ringRadius, 0F, 359.9F, dark, ringOuterStroke);
            builder.DrawArc(ringCenterX, ringCenterY, ringRadius, 0F, 359.9F, light, ringInnerStroke);
        }

        if (this.decision == ApprovalDecision.Deny)
        {
            this.DrawX(builder, centerX, centerY, radius, glyph, pending ? innerStroke : outerStroke, pending);
        }
        else if (this.decision == ApprovalDecision.AlwaysApprove)
        {
            this.DrawCheck(builder, centerX - radius * 0.20F, centerY - radius * 0.10F, radius * 0.75F, glyph, pending ? innerStroke : outerStroke, pending);
            this.DrawCheck(builder, centerX + radius * 0.12F, centerY + radius * 0.20F, radius * 0.75F, glyph, pending ? innerStroke : outerStroke, pending);
        }
        else
        {
            this.DrawCheck(builder, centerX, centerY, radius, glyph, pending ? innerStroke : outerStroke, pending);
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
        for (var attempt = 0; attempt < 6; attempt++)
        {
            Thread.Sleep(30);
            if (this.Plugin.IsApplicationActive())
            {
                return true;
            }
        }

        return false;
    }

    private ApprovalAttempt TryAfterNavigation()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            Thread.Sleep(attempt == 0 ? 55 : 35);
            var result = CodexApprovalMonitor.TryInvoke(this.decision);
            if (result != ApprovalAttempt.NoApproval)
            {
                return result;
            }
        }

        return ApprovalAttempt.NoApproval;
    }

    private void DrawCheck(BitmapBuilder builder, Single centerX, Single centerY, Single radius, BitmapColor color, Single stroke, Boolean singleStroke)
    {
        var x1 = centerX - radius * 0.54F;
        var y1 = centerY;
        var x2 = centerX - radius * 0.12F;
        var y2 = centerY + radius * 0.38F;
        var x3 = centerX + radius * 0.58F;
        var y3 = centerY - radius * 0.42F;
        this.DrawGlyphLine(builder, x1, y1, x2, y2, color, stroke, singleStroke);
        this.DrawGlyphLine(builder, x2, y2, x3, y3, color, stroke, singleStroke);
    }

    private void DrawX(BitmapBuilder builder, Single centerX, Single centerY, Single radius, BitmapColor color, Single stroke, Boolean singleStroke)
    {
        var extent = radius * 0.48F;
        this.DrawGlyphLine(builder, centerX - extent, centerY - extent, centerX + extent, centerY + extent, color, stroke, singleStroke);
        this.DrawGlyphLine(builder, centerX + extent, centerY - extent, centerX - extent, centerY + extent, color, stroke, singleStroke);
    }

    private void DrawGlyphLine(BitmapBuilder builder, Single x1, Single y1, Single x2, Single y2, BitmapColor color, Single stroke, Boolean singleStroke)
    {
        if (!singleStroke)
        {
            builder.DrawLine(x1, y1, x2, y2, new BitmapColor(17, 23, 20), stroke);
            builder.DrawLine(x1, y1, x2, y2, color, Math.Max(2F, stroke * 0.45F));
            return;
        }

        builder.DrawLine(x1, y1, x2, y2, color, stroke);
    }

    private void HandleStateChanged() => this.ActionImageChanged();
}

public sealed class ApproveRequestCommand : ApprovalCommandBase
{
    public ApproveRequestCommand()
        : base(
            "Approve",
            "Approves the visible Codex request or opens the next chat needing attention.",
            ApprovalDecision.Approve)
    {
    }
}

public sealed class AlwaysApproveRequestCommand : ApprovalCommandBase
{
    public AlwaysApproveRequestCommand()
        : base(
            "Always approve",
            "Always approves the visible matching request or opens the next chat needing attention.",
            ApprovalDecision.AlwaysApprove)
    {
    }
}

public sealed class DenyRequestCommand : ApprovalCommandBase
{
    public DenyRequestCommand()
        : base(
            "Deny",
            "Denies the visible Codex request or opens the next chat needing attention.",
            ApprovalDecision.Deny)
    {
    }
}
