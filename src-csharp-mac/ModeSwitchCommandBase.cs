using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public abstract class ModeSwitchCommandBase : PluginDynamicCommand
{
    private readonly MacDesktopMode mode;

    protected ModeSwitchCommandBase(
        String displayName,
        String description,
        String groupName,
        MacDesktopMode mode)
        : base(displayName, description, groupName, DeviceType.All)
    {
        this.mode = mode;
    }

    protected override void RunCommand(String actionParameter)
    {
        this.Log.Info($"Mode switch action '{this.DisplayName}' triggered.");
        if (!this.ActivateCodex())
        {
            this.Log.Warning($"Mode switch action '{this.DisplayName}' could not activate ChatGPT/Codex Desktop.");
            return;
        }

        var attempt = CodexMacAccessibility.TrySwitchMode(
            this.mode,
            trace: message => this.Log.Info(
                $"Mode switch action '{this.DisplayName}': {message}"));
        if (attempt == MacActionAttempt.PermissionRequired)
        {
            attempt = CodexMacAccessibility.TrySwitchMode(
                this.mode,
                promptForPermission: true,
                trace: message => this.Log.Info(
                    $"Mode switch action '{this.DisplayName}': {message}"));
        }

        if (this.mode != MacDesktopMode.Codex
            && attempt is (MacActionAttempt.NoTarget
                or MacActionAttempt.ReadyForKeyboardFallback
                or MacActionAttempt.Unavailable))
        {
            this.Log.Info(
                $"Mode switch action '{this.DisplayName}' is opening the exact command-menu fallback.");
            this.Plugin.ClientApplication.SendKeyboardShortcut(
                VirtualKeyCode.KeyK,
                ModifierKey.ControlOrCommand);
            attempt = CodexMacAccessibility.TryInvokeModeCommand(
                this.mode,
                TimeSpan.FromMilliseconds(2000));
            this.Log.Info(
                $"Mode switch action '{this.DisplayName}' command-menu fallback completed with {attempt}.");
        }

        if (attempt == MacActionAttempt.AlreadyActive)
        {
            this.Log.Info($"Mode switch action '{this.DisplayName}' found the requested mode already active.");
            return;
        }

        if (attempt == MacActionAttempt.Invoked)
        {
            this.Log.Info($"Mode switch action '{this.DisplayName}' invoked the accessible mode option.");
            return;
        }

        if (attempt == MacActionAttempt.Clicked)
        {
            this.Log.Info($"Mode switch action '{this.DisplayName}' clicked the accessible mode option.");
            return;
        }

        this.Log.Warning(
            attempt == MacActionAttempt.PermissionRequired
                ? $"Mode switch action '{this.DisplayName}' requires Accessibility permission. "
                    + CodexMacAccessibility.AccessibilityRemediation
                : attempt == MacActionAttempt.Unavailable
                    ? $"Mode switch action '{this.DisplayName}' found an exact mode control, but could not verify the requested mode."
                    : $"Mode switch action '{this.DisplayName}' found no exact accessible mode control.");
    }

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
}
