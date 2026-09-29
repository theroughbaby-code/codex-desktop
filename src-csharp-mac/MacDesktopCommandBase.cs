using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public abstract class MacDesktopCommandBase : PluginDynamicCommand
{
    private readonly MacDesktopCommand command;

    protected MacDesktopCommandBase(
        String displayName,
        String description,
        String groupName,
        MacDesktopCommand command)
        : base(displayName, description, groupName, DeviceType.All)
    {
        this.command = command;
    }

    protected override void RunCommand(String actionParameter)
    {
        this.Log.Info($"Verified Mac action '{this.DisplayName}' triggered.");
        if (!this.EnsureApplicationIsActive())
        {
            this.Log.Warning(
                $"Verified Mac action '{this.DisplayName}' could not activate ChatGPT/Codex Desktop.");
            return;
        }

        var attempt = CodexMacAccessibility.TryInvokeDesktopCommand(
            this.command,
            trace: message => this.Log.Info(
                $"Verified Mac action '{this.DisplayName}': {message}"));
        if (attempt == MacActionAttempt.PermissionRequired)
        {
            attempt = CodexMacAccessibility.TryInvokeDesktopCommand(
                this.command,
                promptForPermission: true,
                trace: message => this.Log.Info(
                    $"Verified Mac action '{this.DisplayName}': {message}"));
        }

        if (attempt is MacActionAttempt.AlreadyActive
            or MacActionAttempt.Invoked
            or MacActionAttempt.Clicked)
        {
            this.Log.Info(
                $"Verified Mac action '{this.DisplayName}' completed with {attempt}.");
            return;
        }

        this.Log.Warning(
            attempt == MacActionAttempt.PermissionRequired
                ? $"Verified Mac action '{this.DisplayName}' requires Accessibility permission. "
                    + CodexMacAccessibility.AccessibilityRemediation
                : attempt == MacActionAttempt.NoTarget
                    ? $"Verified Mac action '{this.DisplayName}' is unavailable in the current app context."
                    : $"Verified Mac action '{this.DisplayName}' could not verify that the app handled the command. "
                        + MacAccessibilityNative.AccessibilityBootstrapDiagnostic);
    }

    private Boolean EnsureApplicationIsActive()
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
