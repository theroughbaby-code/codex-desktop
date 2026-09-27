using System;
using System.Diagnostics;
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
        this.Log.Info($"Approval action '{this.DisplayName}' triggered.");
        var attempt = CodexApprovalMonitor.TryInvoke(this.decision);
        if (attempt == ApprovalAttempt.NoApproval)
        {
            if (!this.EnsureCodexIsActive())
            {
                this.Log.Warning($"Approval action '{this.DisplayName}' could not activate Codex Desktop.");
                return;
            }

            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyA, ModifierKey.Control | ModifierKey.Alt);
            attempt = this.WaitAfterNavigation();
        }

        if (attempt == ApprovalAttempt.ReadyForKeyboardFallback)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Return, ModifierKey.None);
            this.Log.Info($"Approval action '{this.DisplayName}' used Enter on the matched focused control.");
        }
        else if (attempt == ApprovalAttempt.Invoked)
        {
            this.Log.Info($"Approval action '{this.DisplayName}' invoked the matched Codex control.");
        }
        else if (attempt == ApprovalAttempt.FoundButUnavailable)
        {
            this.Log.Warning($"Approval action '{this.DisplayName}' found a pending request, but its control was unavailable.");
        }
        else
        {
            this.Log.Warning($"Approval action '{this.DisplayName}' found no accessible pending request after navigation.");
        }

        CodexApprovalMonitor.RefreshSoon();
    }

    protected override BitmapImage GetCommandImage(String actionParameter, PluginImageSize imageSize)
        => PluginResources.ReadImage(this.GetStateImageName());

    private Boolean EnsureCodexIsActive()
    {
        if (this.Plugin.IsApplicationActive())
        {
            return true;
        }

        this.Plugin.ClientApplication.Activate();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500))
        {
            Thread.Sleep(25);
            if (this.Plugin.IsApplicationActive())
            {
                return true;
            }
        }

        return false;
    }

    private ApprovalAttempt WaitAfterNavigation()
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500))
        {
            var result = CodexApprovalMonitor.TryInvoke(this.decision);
            if (result != ApprovalAttempt.NoApproval)
            {
                return result;
            }

            Thread.Sleep(50);
        }

        return ApprovalAttempt.NoApproval;
    }

    private String GetStateImageName()
    {
        var pending = CodexApprovalMonitor.HasPendingApproval;
        return this.decision switch
        {
            ApprovalDecision.Approve => pending ? "ApprovalApprovePending.png" : "ApprovalApproveIdle.png",
            ApprovalDecision.AlwaysApprove => pending ? "ApprovalAlwaysPending.png" : "ApprovalAlwaysIdle.png",
            _ => pending ? "ApprovalDenyPending.png" : "ApprovalDenyIdle.png",
        };
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
