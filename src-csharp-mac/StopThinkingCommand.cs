using System.Diagnostics;

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
        this.Log.Info("Stop thinking action triggered.");
        if (!this.ActivateCodex())
        {
            this.Log.Warning("Stop thinking could not activate ChatGPT/Codex Desktop.");
            return;
        }

        var attempt = this.WaitForAction(TimeSpan.FromMilliseconds(1400));
        if (attempt == MacActionAttempt.ReadyForKeyboardFallback)
        {
            // Return is safe only after the exact Stop target accepted focus.
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.Return,
                ModifierKey.None);
            this.Log.Info("Stop thinking used Return on the matched focused control.");
        }
        else if (attempt == MacActionAttempt.Clicked)
        {
            this.Log.Info("Stop thinking clicked the matched Codex control.");
        }
        else if (attempt == MacActionAttempt.Invoked)
        {
            this.Log.Info("Stop thinking invoked the matched Codex control.");
        }
        else if (attempt == MacActionAttempt.PermissionRequired)
        {
            this.Log.Warning(
                $"Stop thinking requires Accessibility permission. "
                + CodexMacAccessibility.AccessibilityRemediation);
        }
        else if (attempt == MacActionAttempt.Unavailable)
        {
            this.Log.Warning("Stop thinking found a matching control, but it was unavailable.");
        }
        else
        {
            this.Log.Warning("Stop thinking found no accessible active response control.");
        }

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
                // Electron can replace its accessibility nodes during activation.
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
        do
        {
            var attempt = CodexMacStateMonitor.TryStop();
            if (attempt is MacActionAttempt.Invoked
                or MacActionAttempt.Clicked
                or MacActionAttempt.ReadyForKeyboardFallback
                or MacActionAttempt.PermissionRequired)
            {
                return attempt;
            }

            lastAttempt = attempt;
            Thread.Sleep(75);
        }
        while (stopwatch.Elapsed < timeout);

        return lastAttempt;
    }
}
