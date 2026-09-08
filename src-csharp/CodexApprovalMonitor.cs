using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace Loupedeck.CodexDesktopPlugin;

public enum ApprovalDecision
{
    Approve,
    AlwaysApprove,
    Deny,
}

internal enum ApprovalAttempt
{
    NoApproval,
    Invoked,
    FoundButUnavailable,
}

internal static class CodexApprovalMonitor
{
    private static readonly Object SyncRoot = new();
    private static Timer? timer;
    private static Int32 subscribers;
    private static Int32 refreshInProgress;
    private static Boolean hasPendingApproval;

    public static event Action? Changed;

    public static Boolean HasPendingApproval
    {
        get
        {
            lock (SyncRoot)
            {
                return hasPendingApproval;
            }
        }
    }

    public static void Start()
    {
        lock (SyncRoot)
        {
            subscribers++;
            timer ??= new Timer(_ => Refresh(), null, TimeSpan.Zero, TimeSpan.FromMilliseconds(750));
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
            hasPendingApproval = false;
        }
    }

    public static void RefreshSoon()
        => ThreadPool.QueueUserWorkItem(_ =>
        {
            Thread.Sleep(100);
            Refresh();
        });

    public static ApprovalAttempt TryInvoke(ApprovalDecision decision)
    {
        var ui = ReadApprovalUi();
        if (!ui.HasApprovalSurface)
        {
            return ApprovalAttempt.NoApproval;
        }

        if (decision == ApprovalDecision.AlwaysApprove)
        {
            var persistentAction = ui.Controls.FirstOrDefault(control => IsPersistentApproval(control.Name));
            if (persistentAction is not null && TryInvokeControl(persistentAction.Element))
            {
                return ApprovalAttempt.Invoked;
            }

            var options = ui.Controls.FirstOrDefault(control => Contains(control.Name, "Approval options"));
            if (options is null || !TryOpenControl(options.Element))
            {
                return ApprovalAttempt.FoundButUnavailable;
            }

            Thread.Sleep(50);
            var expandedUi = ReadApprovalUi(requireSurface: false);
            persistentAction = expandedUi.Controls.FirstOrDefault(control => IsPersistentApproval(control.Name));
            return persistentAction is not null && TryInvokeControl(persistentAction.Element)
                ? ApprovalAttempt.Invoked
                : ApprovalAttempt.FoundButUnavailable;
        }

        var target = decision == ApprovalDecision.Approve ? ui.Approve : ui.Deny;
        return target is not null && TryInvokeControl(target.Element)
            ? ApprovalAttempt.Invoked
            : ApprovalAttempt.FoundButUnavailable;
    }

    private static void Refresh()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var pending = ReadApprovalUi().HasPending;
            Action? changed = null;
            lock (SyncRoot)
            {
                if (pending != hasPendingApproval)
                {
                    hasPendingApproval = pending;
                    changed = Changed;
                }
            }

            changed?.Invoke();
        }
        catch
        {
            // Accessibility can briefly disappear while Codex changes views.
        }
        finally
        {
            Volatile.Write(ref refreshInProgress, 0);
        }
    }

    private static ApprovalUi ReadApprovalUi(Boolean requireSurface = true)
    {
        try
        {
            var processIds = Process.GetProcessesByName("ChatGPT")
                .Select(process => process.Id)
                .ToHashSet();
            if (processIds.Count == 0)
            {
                return ApprovalUi.Empty;
            }

            var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition)
                .Cast<AutomationElement>()
                .Where(element => TryGetProcessId(element, out var processId) && processIds.Contains(processId));
            var controls = new List<ApprovalControl>();
            var hasStatusLabel = false;
            var interactiveCondition = new OrCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem));

            foreach (var root in roots)
            {
                foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, interactiveCondition))
                {
                    if (!TryReadControl(element, out var control))
                    {
                        continue;
                    }

                    controls.Add(control);
                }

                if (!hasStatusLabel)
                {
                    var status = root.FindFirst(
                        TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.NameProperty, "Awaiting approval"));
                    hasStatusLabel = status is not null && TryIsVisible(status);
                }
            }

            var approve = controls.FirstOrDefault(control => IsApprove(control.Name));
            var deny = controls.FirstOrDefault(control => IsDeny(control.Name));
            var hasSurface = approve is not null && deny is not null;
            if (requireSurface && !hasSurface && !hasStatusLabel)
            {
                return ApprovalUi.Empty;
            }

            return new ApprovalUi(controls, approve, deny, hasSurface, hasSurface || hasStatusLabel);
        }
        catch
        {
            return ApprovalUi.Empty;
        }
    }

    private static Boolean TryReadControl(AutomationElement element, out ApprovalControl control)
    {
        control = null!;
        try
        {
            if (!element.Current.IsEnabled || element.Current.IsOffscreen)
            {
                return false;
            }

            var name = element.Current.Name?.Trim() ?? String.Empty;
            if (name.Length == 0)
            {
                return false;
            }

            control = new ApprovalControl(element, name);
            return true;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
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

    private static Boolean TryIsVisible(AutomationElement element)
    {
        try
        {
            return !element.Current.IsOffscreen;
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

            return TryInvokeControl(element);
        }
        catch (InvalidOperationException)
        {
            return false;
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
            if (!element.TryGetCurrentPattern(InvokePattern.Pattern, out var invokePattern))
            {
                return false;
            }

            ((InvokePattern)invokePattern).Invoke();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
    }

    private static Boolean IsApprove(String name)
        => Contains(name, "Allow once")
            || Contains(name, "Apply changes")
            || Contains(name, "Allow network")
            || Contains(name, "Approve")
            || Contains(name, "Enter");

    private static Boolean IsDeny(String name)
        => Contains(name, "Deny") || Contains(name, "Escape");

    private static Boolean IsPersistentApproval(String name)
        => Contains(name, "Always allow") || Contains(name, "Allow this conversation");

    private static Boolean Contains(String value, String text)
        => value.Contains(text, StringComparison.OrdinalIgnoreCase);

    private sealed record ApprovalControl(AutomationElement Element, String Name);

    private sealed record ApprovalUi(
        IReadOnlyList<ApprovalControl> Controls,
        ApprovalControl? Approve,
        ApprovalControl? Deny,
        Boolean HasApprovalSurface,
        Boolean HasPending)
    {
        public static ApprovalUi Empty { get; } = new(
            Array.Empty<ApprovalControl>(),
            null,
            null,
            false,
            false);
    }
}
