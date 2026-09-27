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
    private const Int32 SwRestore = 9;
    private static readonly TimeSpan CachedStatePollInterval = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan NoControlPollInterval = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan FullDiscoveryInterval = TimeSpan.FromSeconds(8);
    private static readonly AutomationProperty? AriaRoleProperty = AutomationProperty.LookupById(30101);
    private static readonly AutomationProperty? AriaPropertiesProperty = AutomationProperty.LookupById(30102);
    private static readonly Object SyncRoot = new();
    private static Timer? timer;
    private static Int32 subscribers;
    private static Int32 refreshInProgress;
    private static Boolean hasActiveTurn;
    private static IReadOnlyList<ComposerAction> cachedActions = Array.Empty<ComposerAction>();
    private static DateTime nextFullDiscoveryUtc = DateTime.MinValue;

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
            timer ??= new Timer(_ => Refresh(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);
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
            cachedActions = Array.Empty<ComposerAction>();
            nextFullDiscoveryUtc = DateTime.MinValue;
            hasActiveTurn = false;
        }
    }

    public static void RefreshSoon()
    {
        lock (SyncRoot)
        {
            timer?.Change(TimeSpan.FromMilliseconds(60), Timeout.InfiniteTimeSpan);
        }
    }

    public static Boolean TryStopActiveTurn()
    {
        IReadOnlyList<ComposerAction> candidates;
        lock (SyncRoot)
        {
            candidates = cachedActions.Where(action => action.IsStop).ToArray();
        }

        if (TryInvokePreferredControl(candidates))
        {
            return true;
        }

        var freshActions = ReadComposerActions();
        UpdateState(freshActions, resetDiscoveryDeadline: true);
        return TryInvokePreferredControl(freshActions.Where(action => action.IsStop).ToArray());
    }

    private static void Refresh()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) != 0)
        {
            return;
        }

        var nextInterval = NoControlPollInterval;
        try
        {
            IReadOnlyList<ComposerAction> previous;
            DateTime discoveryDeadline;
            lock (SyncRoot)
            {
                previous = cachedActions;
                discoveryDeadline = nextFullDiscoveryUtc;
            }

            IReadOnlyList<ComposerAction> actions = Array.Empty<ComposerAction>();
            var fullDiscovery = previous.Count == 0
                || DateTime.UtcNow >= discoveryDeadline
                || !TryRefreshCachedActions(previous, out actions);
            if (fullDiscovery)
            {
                actions = ReadComposerActions();
            }

            UpdateState(actions, fullDiscovery);
            nextInterval = actions.Count > 0
                ? CachedStatePollInterval
                : NoControlPollInterval;
        }
        catch
        {
            // Electron can replace the composer accessibility node mid-read.
        }
        finally
        {
            Volatile.Write(ref refreshInProgress, 0);
            lock (SyncRoot)
            {
                timer?.Change(nextInterval, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static Boolean TryRefreshCachedActions(
        IReadOnlyList<ComposerAction> previous,
        out IReadOnlyList<ComposerAction> refreshed)
    {
        var actions = new List<ComposerAction>(previous.Count);
        foreach (var action in previous)
        {
            if (!TryRefreshCachedAction(action, out var current))
            {
                refreshed = Array.Empty<ComposerAction>();
                return false;
            }

            actions.Add(current);
        }

        refreshed = actions;
        return true;
    }

    private static Boolean TryRefreshCachedAction(
        ComposerAction previous,
        out ComposerAction refreshed)
    {
        refreshed = null!;
        try
        {
            var current = previous.Element.Current;
            if (!current.IsEnabled || current.IsOffscreen)
            {
                return false;
            }

            var name = current.Name?.Trim() ?? String.Empty;
            var helpText = current.HelpText?.Trim() ?? String.Empty;
            refreshed = previous with
            {
                IsStop = previous.HasStableStopIdentity
                    || StopLabels.IsStop(name)
                    || StopLabels.IsStop(helpText),
            };
            return true;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException
                or InvalidOperationException
                or COMException)
        {
            return false;
        }
    }

    private static void UpdateState(IReadOnlyList<ComposerAction> actions, Boolean resetDiscoveryDeadline)
    {
        Action? changed = null;
        lock (SyncRoot)
        {
            cachedActions = actions;
            if (resetDiscoveryDeadline)
            {
                nextFullDiscoveryUtc = DateTime.UtcNow + FullDiscoveryInterval;
            }

            var active = actions.Any(action => action.IsStop);
            if (active != hasActiveTurn)
            {
                hasActiveTurn = active;
                changed = Changed;
            }
        }

        changed?.Invoke();
    }

    private static IReadOnlyList<ComposerAction> ReadComposerActions()
    {
        var actions = new List<ComposerAction>();
        foreach (var windowHandle in GetCodexWindowHandles())
        {
            actions.AddRange(ReadComposerActions(windowHandle));
        }

        return actions;
    }

    private static IReadOnlyList<ComposerAction> ReadStopControls(IntPtr windowHandle)
        => ReadComposerActions(windowHandle)
            .Where(action => action.IsStop)
            .ToArray();

    private static IReadOnlyList<ComposerAction> ReadComposerActions(IntPtr windowHandle)
    {
        try
        {
            var actions = new List<ComposerAction>();
            var root = AutomationElement.FromHandle(windowHandle);
            var buttonCondition = new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button);
            var buttons = root.FindAll(TreeScope.Descendants, buttonCondition);
            for (var index = 0; index < buttons.Count; index++)
            {
                if (TryReadComposerAction(buttons[index], windowHandle, requireInvokePattern: true, out var action))
                {
                    actions.Add(action);
                }
            }

            return actions;
        }
        catch
        {
            // One changing or inaccessible window must not hide the others.
            return Array.Empty<ComposerAction>();
        }
    }

    private static Boolean TryReadComposerAction(
        AutomationElement element,
        IntPtr windowHandle,
        Boolean requireInvokePattern,
        out ComposerAction action)
    {
        action = null!;
        try
        {
            var current = element.Current;
            if (!current.IsEnabled || current.IsOffscreen)
            {
                return false;
            }

            var name = current.Name?.Trim() ?? String.Empty;
            var helpText = current.HelpText?.Trim() ?? String.Empty;
            var automationId = current.AutomationId?.Trim() ?? String.Empty;
            var className = current.ClassName?.Trim() ?? String.Empty;
            var ariaRole = ReadStringProperty(element, AriaRoleProperty);
            var ariaProperties = ReadStringProperty(element, AriaPropertiesProperty);
            var identity = JoinText(automationId, className, helpText, ariaRole, ariaProperties);
            var hasStableStopIdentity = ContainsAny(
                identity,
                "composer-stop",
                "stop-composer",
                "stop-response",
                "stop-generating",
                "stop-turn",
                "interrupt-turn",
                "interrupt-response");
            var hasComposerButtonStructure = className.Contains(
                    "size-token-button-composer",
                    StringComparison.OrdinalIgnoreCase)
                && (className.Contains("bg-composer-primary", StringComparison.OrdinalIgnoreCase)
                    || className.Contains("button-composer-primary", StringComparison.OrdinalIgnoreCase));
            if (!hasStableStopIdentity && !hasComposerButtonStructure)
            {
                return false;
            }

            if (requireInvokePattern && !element.TryGetCurrentPattern(InvokePattern.Pattern, out _))
            {
                return false;
            }

            var isStop = hasStableStopIdentity
                || StopLabels.IsStop(name)
                || StopLabels.IsStop(helpText);
            action = new ComposerAction(
                element,
                windowHandle,
                GetRuntimeKey(element),
                isStop,
                hasStableStopIdentity);
            return true;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException
                or InvalidOperationException
                or COMException)
        {
            return false;
        }
    }

    private static Boolean TryInvokePreferredControl(IReadOnlyList<ComposerAction> controls)
    {
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

        return target is not null
            && TryActivateWindow(target.WindowHandle)
            && TryInvokeControl(target.Element);
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
        catch (Exception exception) when (
            exception is ElementNotAvailableException
                or InvalidOperationException
                or COMException)
        {
        }

        return false;
    }

    private static Boolean TryActivateWindow(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return false;
        }

        if (IsIconic(windowHandle))
        {
            ShowWindowAsync(windowHandle, SwRestore);
        }

        if (GetForegroundWindow() == windowHandle)
        {
            return true;
        }

        SetForegroundWindow(windowHandle);
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(350))
        {
            if (GetForegroundWindow() == windowHandle)
            {
                return true;
            }

            Thread.Sleep(15);
        }

        return false;
    }

    private static IReadOnlyList<IntPtr> GetCodexWindowHandles()
    {
        var processIds = GetCodexProcessIds();
        if (processIds.Count == 0)
        {
            return Array.Empty<IntPtr>();
        }

        var foregroundWindow = GetForegroundWindow();
        var windows = new List<IntPtr>();
        EnumWindows((windowHandle, _) =>
        {
            if (!IsWindowVisible(windowHandle))
            {
                return true;
            }

            GetWindowThreadProcessId(windowHandle, out var processId);
            if (processIds.Contains(unchecked((Int32)processId)))
            {
                windows.Add(windowHandle);
            }

            return true;
        }, IntPtr.Zero);

        return windows
            .Distinct()
            .OrderByDescending(windowHandle => windowHandle == foregroundWindow)
            .ToArray();
    }

    private static HashSet<Int32> GetCodexProcessIds()
    {
        var processIds = new HashSet<Int32>();
        foreach (var processName in new[] { "ChatGPT", "ChatGPT Classic" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    processIds.Add(process.Id);
                }
            }
        }

        return processIds;
    }

    private static String ReadStringProperty(AutomationElement element, AutomationProperty? property)
    {
        if (property is null)
        {
            return String.Empty;
        }

        try
        {
            var value = element.GetCurrentPropertyValue(property, true);
            return value == AutomationElement.NotSupported
                ? String.Empty
                : value?.ToString()?.Trim() ?? String.Empty;
        }
        catch
        {
            return String.Empty;
        }
    }

    private static String GetRuntimeKey(AutomationElement element)
    {
        try
        {
            return String.Join('.', element.GetRuntimeId());
        }
        catch
        {
            return String.Empty;
        }
    }

    private static String JoinText(params String[] values)
        => String.Join(' ', values.Where(value => value.Length > 0));

    private static Boolean ContainsAny(String value, params String[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern UInt32 GetWindowThreadProcessId(IntPtr windowHandle, out UInt32 processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean ShowWindowAsync(IntPtr windowHandle, Int32 command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern Boolean SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private delegate Boolean EnumWindowsCallback(IntPtr windowHandle, IntPtr parameter);

    private sealed record ComposerAction(
        AutomationElement Element,
        IntPtr WindowHandle,
        String RuntimeKey,
        Boolean IsStop,
        Boolean HasStableStopIdentity);
}
