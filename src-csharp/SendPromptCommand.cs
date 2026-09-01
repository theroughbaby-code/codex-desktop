using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class SendPromptCommand : ActionEditorCommand
{
    private const String PromptControlName = "Prompt";
    private const Int32 ClipboardRetryCount = 5;
    private const Int32 ClipboardRetryDelayMilliseconds = 20;
    private const Int32 PasteSettleDelayMilliseconds = 50;

    public SendPromptCommand()
        : base(DeviceType.All)
    {
        this.Name = "SendPrompt";
        this.DisplayName = "Custom Prompt";
        this.Description = "Sends a custom prompt to active codex chat.";
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
        if (!actionParameters.TryGetString(PromptControlName, out var prompt) || String.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        if (!this.Plugin.IsApplicationActive())
        {
            return false;
        }

        if (!TrySetClipboardText(prompt))
        {
            return false;
        }

        this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.KeyV, ModifierKey.Control);
        Thread.Sleep(PasteSettleDelayMilliseconds);
        this.Plugin.ClientApplication.SendKeyboardShortcut(VirtualKeyCode.Return);
        return true;
    }

    private static Boolean TrySetClipboardText(String text)
    {
        var succeeded = false;
        var clipboardThread = new Thread(() =>
        {
            for (var attempt = 0; attempt < ClipboardRetryCount; attempt++)
            {
                try
                {
                    Clipboard.SetText(text, TextDataFormat.UnicodeText);
                    succeeded = true;
                    return;
                }
                catch (ExternalException) when (attempt < ClipboardRetryCount - 1)
                {
                    Thread.Sleep(ClipboardRetryDelayMilliseconds);
                }
            }
        });

        clipboardThread.SetApartmentState(ApartmentState.STA);
        clipboardThread.Start();
        clipboardThread.Join();
        return succeeded;
    }
}
