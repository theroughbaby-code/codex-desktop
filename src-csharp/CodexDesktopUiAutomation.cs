using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Automation;

namespace Loupedeck.CodexDesktopPlugin;

internal static class CodexDesktopUiAutomation
{
    private static readonly ControlType[] InteractiveTypes =
    {
        ControlType.Button,
        ControlType.ComboBox,
        ControlType.MenuItem,
    };

    public static Boolean TryOpenModelPicker()
    {
        var candidate = ReadControls()
            .Select(control => new { Control = control, Score = ModelControlScore(control) })
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .Select(item => item.Control)
            .FirstOrDefault();

        return candidate is not null && TryOpenControl(candidate.Element);
    }

    private static Int32 ModelControlScore(UiControl control)
    {
        var name = control.Name;
        var help = control.HelpText;
        if (EqualsIgnoreCase(name, "Open model picker")) return 120;
        if (Contains(name, "Select ChatGPT model")) return 115;
        if (EqualsIgnoreCase(name, "Select model")) return 110;
        if (Contains(name, "model picker")) return 105;
        if (Contains(help, "Select ChatGPT model")) return 100;
        if (Contains(help, "select model") || Contains(help, "model picker")) return 95;
        if (Contains(name, "GPT-") || Contains(name, "Codex model")) return 60;
        return 0;
    }

    private static IReadOnlyList<UiControl> ReadControls()
    {
        var controls = new List<UiControl>();
        try
        {
            var conditions = InteractiveTypes
                .Select(type => (Condition)new PropertyCondition(AutomationElement.ControlTypeProperty, type))
                .ToArray();
            var condition = new OrCondition(conditions);

            foreach (var root in ReadRoots())
            {
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, condition))
                {
                    if (TryReadControl(element, out var control))
                    {
                        controls.Add(control);
                    }
                }
            }
        }
        catch
        {
            // Electron can replace the accessibility tree between enumeration calls.
        }

        return controls;
    }

    private static IEnumerable<AutomationElement> ReadRoots()
    {
        var processIds = Process.GetProcessesByName("ChatGPT")
            .Concat(Process.GetProcessesByName("ChatGPT Classic"))
            .Select(process => process.Id)
            .ToHashSet();
        if (processIds.Count == 0)
        {
            return Array.Empty<AutomationElement>();
        }

        return AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
            .Cast<AutomationElement>()
            .Where(element => TryGetProcessId(element, out var processId) && processIds.Contains(processId))
            .ToArray();
    }

    private static Boolean TryReadControl(AutomationElement element, out UiControl control)
    {
        control = null!;
        try
        {
            if (!element.Current.IsEnabled || element.Current.IsOffscreen)
            {
                return false;
            }

            var name = element.Current.Name?.Trim() ?? String.Empty;
            var helpText = element.Current.HelpText?.Trim() ?? String.Empty;
            if (name.Length == 0 && helpText.Length == 0)
            {
                return false;
            }

            control = new UiControl(element, name, helpText);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static Boolean TryOpenControl(AutomationElement element)
    {
        try
        {
            if (element.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expandPattern))
            {
                ((ExpandCollapsePattern)expandPattern).Expand();
                return true;
            }

            if (element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern))
            {
                ((InvokePattern)invokePattern).Invoke();
                return true;
            }
        }
        catch (Exception exception) when (exception is ElementNotAvailableException or InvalidOperationException)
        {
        }

        return false;
    }

    private static Boolean TryGetProcessId(AutomationElement element, out Int32 processId)
    {
        processId = 0;
        try
        {
            processId = element.Current.ProcessId;
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static Boolean EqualsIgnoreCase(String left, String right)
        => left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static Boolean Contains(String text, String value)
        => text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private sealed record UiControl(AutomationElement Element, String Name, String HelpText);
}
