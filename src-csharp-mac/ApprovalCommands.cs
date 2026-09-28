using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public abstract class ApprovalCommandBase : PluginDynamicCommand
{
    private readonly ApprovalDecision decision;

    protected ApprovalCommandBase(
        String displayName,
        String description,
        ApprovalDecision decision)
        : base(displayName, description, "Approvals", DeviceType.All)
        => this.decision = decision;

    protected override Boolean OnLoad()
    {
        CodexMacStateMonitor.ApprovalChanged += this.HandleStateChanged;
        CodexMacStateMonitor.StartApproval();
        return true;
    }

    protected override Boolean OnUnload()
    {
        CodexMacStateMonitor.ApprovalChanged -= this.HandleStateChanged;
        CodexMacStateMonitor.StopApproval();
        return true;
    }

    protected override void RunCommand(String actionParameter)
    {
        this.Log.Info($"Approval action '{this.DisplayName}' triggered.");

        // Activating can replace Electron's accessibility nodes. Always activate
        // first, then let the monitor scan a fresh tree before invoking a target.
        if (!this.ActivateCodex())
        {
            this.Log.Warning($"Approval action '{this.DisplayName}' could not activate ChatGPT/Codex Desktop.");
            return;
        }

        var attempt = CodexMacStateMonitor.TryInvokeApproval(this.decision);
        if (attempt == MacActionAttempt.Unavailable)
        {
            attempt = this.WaitForAction(TimeSpan.FromMilliseconds(750));
        }

        if (attempt == MacActionAttempt.NoTarget)
        {
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.KeyA,
                ModifierKey.ControlOrCommand | ModifierKey.AltOrOption);
            attempt = this.WaitForAction(TimeSpan.FromMilliseconds(1500));
        }

        if (attempt == MacActionAttempt.ReadyForKeyboardFallback)
        {
            // The fallback is safe only after the matching target accepted focus.
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                this.decision == ApprovalDecision.Deny
                    ? VirtualKeyCode.Escape
                    : VirtualKeyCode.Return,
                ModifierKey.None);
            this.Log.Info(
                $"Approval action '{this.DisplayName}' used "
                + (this.decision == ApprovalDecision.Deny ? "Escape" : "Return")
                + " on the matched focused control.");
        }
        else if (attempt == MacActionAttempt.Clicked)
        {
            this.Log.Info($"Approval action '{this.DisplayName}' clicked the matched Codex control.");
        }
        else if (attempt == MacActionAttempt.Invoked)
        {
            this.Log.Info($"Approval action '{this.DisplayName}' invoked the matched Codex control.");
        }
        else if (attempt == MacActionAttempt.PermissionRequired)
        {
            this.Log.Warning(
                $"Approval action '{this.DisplayName}' requires Accessibility permission. "
                + CodexMacAccessibility.AccessibilityRemediation);
        }
        else if (attempt == MacActionAttempt.Unavailable)
        {
            this.Log.Warning(
                $"Approval action '{this.DisplayName}' found a pending request, but its control was unavailable. "
                + MacAccessibilityNative.AccessibilityBootstrapDiagnostic);
        }
        else
        {
            this.Log.Warning(
                $"Approval action '{this.DisplayName}' found no accessible pending request after navigation. "
                + MacAccessibilityNative.AccessibilityBootstrapDiagnostic);
        }

        CodexMacStateMonitor.RefreshSoon();
    }

    protected override BitmapImage GetCommandImage(
        String actionParameter,
        PluginImageSize imageSize)
        => PluginResources.ReadImage(this.GetStateImageName());

    private Boolean ActivateCodex()
    {
        if (this.Plugin.IsApplicationActive())
        {
            return true;
        }

        this.Plugin.ClientApplication.Activate();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500))
        {
            if (this.Plugin.IsApplicationActive())
            {
                Thread.Sleep(75);
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private MacActionAttempt WaitForAction(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastAttempt = MacActionAttempt.NoTarget;
        while (stopwatch.Elapsed < timeout)
        {
            Thread.Sleep(75);
            var attempt = CodexMacStateMonitor.TryInvokeApproval(this.decision);
            if (attempt is MacActionAttempt.Invoked
                or MacActionAttempt.Clicked
                or MacActionAttempt.ReadyForKeyboardFallback
                or MacActionAttempt.PermissionRequired)
            {
                return attempt;
            }

            lastAttempt = attempt;
        }

        return lastAttempt;
    }

    private String GetStateImageName()
    {
        var actionable = CodexMacStateMonitor.HasActionableApproval(this.decision);
        return this.decision switch
        {
            ApprovalDecision.Approve => actionable
                ? "ApprovalApprovePending.png"
                : "ApprovalApproveIdle.png",
            ApprovalDecision.AlwaysApprove => actionable
                ? "ApprovalAlwaysPending.png"
                : "ApprovalAlwaysIdle.png",
            _ => actionable
                ? "ApprovalDenyPending.png"
                : "ApprovalDenyIdle.png",
        };
    }

    private void HandleStateChanged() => this.ActionImageChanged();
}

public sealed class ApproveRequestCommand : ApprovalCommandBase
{
    public ApproveRequestCommand()
        : base(
            "Approve",
            "Approves the pending Codex request and opens its chat.",
            ApprovalDecision.Approve)
    {
    }
}

public sealed class AlwaysApproveRequestCommand : ApprovalCommandBase
{
    public AlwaysApproveRequestCommand()
        : base(
            "Always approve",
            "Always approves the pending matching request and opens its chat.",
            ApprovalDecision.AlwaysApprove)
    {
    }
}

public sealed class DenyRequestCommand : ApprovalCommandBase
{
    public DenyRequestCommand()
        : base(
            "Deny",
            "Denies the pending Codex request and opens its chat.",
            ApprovalDecision.Deny)
    {
    }
}
