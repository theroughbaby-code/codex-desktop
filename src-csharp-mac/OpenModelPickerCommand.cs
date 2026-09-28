using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class OpenModelPickerCommand : PluginDynamicCommand
{
    public OpenModelPickerCommand()
        : base(
            "Open model picker",
            "Opens the model picker for the active Codex composer.",
            "General",
            DeviceType.All)
    {
    }

    protected override void RunCommand(String actionParameter)
    {
        this.Log.Info("Open model picker action triggered.");
        if (!this.ActivateCodex())
        {
            this.Log.Warning("Open model picker could not activate ChatGPT/Codex Desktop.");
            return;
        }

        var attempt = CodexMacAccessibility.TryOpenModelPicker();
        if (attempt == MacActionAttempt.PermissionRequired)
        {
            attempt = CodexMacAccessibility.TryOpenModelPicker(promptForPermission: true);
        }

        if (attempt == MacActionAttempt.Invoked)
        {
            this.Log.Info("Open model picker invoked the accessible model control.");
            return;
        }

        if (attempt == MacActionAttempt.Clicked)
        {
            this.Log.Info("Open model picker clicked the accessible model control.");
            return;
        }

        // Codex Desktop registers this command as literal Control+Shift+M on
        // macOS. Use it when the composer has not exposed a model control.
        this.Plugin.ClientApplication.SendKeyboardShortcut(
            VirtualKeyCode.KeyM,
            ModifierKey.Control | ModifierKey.Shift);
        if (attempt == MacActionAttempt.PermissionRequired)
        {
            this.Log.Warning(
                "Open model picker sent Control+Shift+M because Accessibility permission is unavailable. "
                + CodexMacAccessibility.AccessibilityRemediation);
        }
        else
        {
            this.Log.Info(
                "Open model picker sent Control+Shift+M because no usable accessible model control was found.");
        }
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
