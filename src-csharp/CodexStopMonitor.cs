using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;

namespace Loupedeck.CodexDesktopPlugin;

internal static class CodexStopMonitor
{
    private static readonly Object SyncRoot = new();
    private static Timer? timer;
    private static Int32 subscribers;
    private static Int32 refreshInProgress;
    private static Boolean hasActiveTurn;

    public static event Action? Changed;

    public static Boolean HasActiveTurn
    {
        get
        {
            lock (SyncRoot)
            {
                return hasActiveTurn;
            }
        }
    }

    public static void Start()
    {
        lock (SyncRoot)
        {
            subscribers++;
            timer ??= new Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(350));
        }
    }

    public static void Stop()
    {
        lock (SyncRoot)
        {
            subscribers = Math.Max(0, subscribers - 1);
            if (subscribers != 0)
            {
                return;
            }

            timer?.Dispose();
            timer = null;
            hasActiveTurn = false;
        }
    }

    public static void RefreshSoon()
        => ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(75);
            Refresh();
        });

    public static Boolean TryStopActiveTurn()
    {
        var controls = ReadStopControls();
        if (controls.Count == 0)
        {
            return false;
        }

        var foregroundWindow = GetForegroundWindow();
        var target = controls.FirstOrDefault(control =>
            foregroundWindow != IntPtr.Zero && control.WindowHandle == foregroundWindow);
        if (target is null && controls.Count == 1)
        {
            target = controls[0];
        }

        return target is not null && TryInvokeControl(target.Element);
    }

    private static void Refresh()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var active = ReadStopControls().Count > 0;
            Action? changed = null;
            lock (SyncRoot)
            {
                if (active != hasActiveTurn)
                {
                    hasActiveTurn = active;
                    changed = Changed;
                }
            }

            changed?.Invoke();
        }
        catch
        {
            // Electron can replace the composer accessibility tree mid-scan.
        }
        finally
        {
            Volatile.Write(ref refreshInProgress, 0);
        }
    }

    private static IReadOnlyList<StopControl> ReadStopControls()
    {
        try
        {
            var processIds = Process.GetProcessesByName("ChatGPT")
                .Concat(Process.GetProcessesByName("ChatGPT Classic"))
                .Select(process => process.Id)
                .ToHashSet();
            if (processIds.Count == 0)
            {
                return Array.Empty<StopControl>();
            }

            var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(element => TryGetProcessId(element, out var processId) && processIds.Contains(processId));
            var buttonCondition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
            var controls = new List<StopControl>();

            foreach (var root in roots)
            {
                var windowHandle = TryGetWindowHandle(root);
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, buttonCondition))
                {
                    if (!IsVisibleEnabledStopControl(element))
                    {
                        continue;
                    }

                    controls.Add(new StopControl(element, windowHandle));
                }
            }

            return controls;
        }
        catch
        {
            return Array.Empty<StopControl>();
        }
    }

    private static Boolean IsVisibleEnabledStopControl(AutomationElement element)
    {
        try
        {
            if (!element.Current.IsEnabled || element.Current.IsOffscreen)
            {
                return false;
            }

            var name = element.Current.Name?.Trim() ?? String.Empty;
            var helpText = element.Current.HelpText?.Trim() ?? String.Empty;
            return IsStopLabel(name) || IsStopLabel(helpText);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static Boolean TryInvokeControl(AutomationElement element)
    {
        try
        {
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

    private static Boolean IsStopLabel(String value)
        => value.Equals("Stop", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Stop generating", StringComparison.OrdinalIgnoreCase)
            || value.Equals("Stop response", StringComparison.OrdinalIgnoreCase);

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

    private static IntPtr TryGetWindowHandle(AutomationElement root)
    {
        try
        {
            return new IntPtr(root.Current.NativeWindowHandle);
        }
        catch (ElementNotAvailableException)
        {
            return IntPtr.Zero;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private sealed record StopControl(AutomationElement Element, IntPtr WindowHandle);
}
