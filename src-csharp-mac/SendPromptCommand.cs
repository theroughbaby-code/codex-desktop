using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class SendPromptCommand : ActionEditorCommand
{
    private const String PromptControlName = "Prompt";
    private const Int32 PasteSettleDelayMilliseconds = 50;

    public SendPromptCommand()
        : base(DeviceType.All)
    {
        this.Name = "SendPrompt";
        this.DisplayName = "Custom Prompt";
        this.Description = "Sends a custom prompt to the active Codex chat.";
        this.GroupName = "Chat";

        this.ActionEditor.AddControlEx(
            new ActionEditorTextbox(
                    PromptControlName,
                    "Prompt:",
                    "Enter the prompt to send when the assigned control is pressed.")
                .SetRequired()
                .SetPlaceholder("Enter a custom prompt..."));
    }

    protected override Boolean RunCommand(ActionEditorActionParameters actionParameters)
    {
        if (!actionParameters.TryGetString(PromptControlName, out var prompt)
            || String.IsNullOrWhiteSpace(prompt)
            || !this.ActivateCodex()
            || !TrySetClipboardText(prompt))
        {
            return false;
        }

        this.Plugin.ClientApplication.SendKeyboardShortcut(
            VirtualKeyCode.KeyV,
            ModifierKey.ControlOrCommand);
        Thread.Sleep(PasteSettleDelayMilliseconds);
        this.Plugin.ClientApplication.SendKeyboardShortcut(
            VirtualKeyCode.Return,
            ModifierKey.None);
        return true;
    }

    private Boolean ActivateCodex()
    {
        this.Plugin.ClientApplication.Activate();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(1500))
        {
            if (this.Plugin.IsApplicationActive())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return false;
    }

    private static Boolean TrySetClipboardText(String text)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/pbcopy",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                },
            };
            if (!process.Start())
            {
                return false;
            }

            process.StandardInput.Write(text);
            process.StandardInput.Close();
            if (!process.WaitForExit(1000))
            {
                process.Kill();
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
