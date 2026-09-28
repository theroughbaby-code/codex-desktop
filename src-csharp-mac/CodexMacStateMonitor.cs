namespace Loupedeck.CodexDesktopPlugin;

internal static class CodexMacStateMonitor
{
    private static readonly TimeSpan ActivePollInterval = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(1400);
    private static readonly Object SyncRoot = new();

    private static Timer? timer;
    private static Int32 approvalSubscribers;
    private static Int32 stopSubscribers;
    private static Int32 refreshInProgress;
    private static MacAccessibilitySnapshot snapshot = MacAccessibilitySnapshot.Untrusted();

    public static event Action? ApprovalChanged;

    public static event Action? StopChanged;

    public static Boolean HasActiveTurn
    {
        get
        {
            lock (SyncRoot)
            {
                return snapshot.HasActiveTurn;
            }
        }
    }

    public static Boolean HasActionableApproval(ApprovalDecision decision)
    {
        lock (SyncRoot)
        {
            return snapshot.HasActionableApproval(decision);
        }
    }

    public static void StartApproval()
    {
        lock (SyncRoot)
        {
            approvalSubscribers++;
            StartTimer();
        }
    }

    public static void StopApproval()
    {
        lock (SyncRoot)
        {
            approvalSubscribers = Math.Max(0, approvalSubscribers - 1);
            StopTimerIfUnused();
        }
    }

    public static void StartStop()
    {
        lock (SyncRoot)
        {
            stopSubscribers++;
            StartTimer();
        }
    }

    public static void StopStop()
    {
        lock (SyncRoot)
        {
            stopSubscribers = Math.Max(0, stopSubscribers - 1);
            StopTimerIfUnused();
        }
    }

    public static void RefreshSoon()
    {
        lock (SyncRoot)
        {
            timer?.Change(TimeSpan.FromMilliseconds(60), Timeout.InfiniteTimeSpan);
        }
    }

    public static MacActionAttempt TryInvokeApproval(ApprovalDecision decision)
    {
        var current = CodexMacAccessibility.Scan();
        try
        {
            if (!current.IsTrusted)
            {
                current.Dispose();
                current = CodexMacAccessibility.Scan(promptForPermission: true);
            }

            UpdateSnapshot(current.Clone());
            return CodexMacAccessibility.TryInvokeApproval(current, decision);
        }
        finally
        {
            current.Dispose();
        }
    }

    public static MacActionAttempt TryStop()
    {
        // A running Electron turn can replace the Stop node at any time. Never
        // invoke the cached element used for device state; act on a fresh tree.
        var current = CodexMacAccessibility.Scan();
        try
        {
            if (!current.IsTrusted)
            {
                current.Dispose();
                current = CodexMacAccessibility.Scan(promptForPermission: true);
            }

            UpdateSnapshot(current.Clone());
            return CodexMacAccessibility.TryStop(current);
        }
        finally
        {
            current.Dispose();
        }
    }

    private static void StartTimer()
        => timer ??= new Timer(_ => Refresh(), null, TimeSpan.Zero, Timeout.InfiniteTimeSpan);

    private static void StopTimerIfUnused()
    {
        if (approvalSubscribers != 0 || stopSubscribers != 0)
        {
            return;
        }

        timer?.Dispose();
        timer = null;
        var previous = snapshot;
        snapshot = MacAccessibilitySnapshot.Untrusted();
        previous.Dispose();
    }

    private static void Refresh()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) != 0)
        {
            return;
        }

        var dueTime = IdlePollInterval;
        try
        {
            var current = CodexMacAccessibility.Scan();
            dueTime = current.HasPendingApproval || current.HasActiveTurn
                ? ActivePollInterval
                : IdlePollInterval;
            UpdateSnapshot(current);
        }
        catch
        {
        }
        finally
        {
            Volatile.Write(ref refreshInProgress, 0);
            lock (SyncRoot)
            {
                timer?.Change(dueTime, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private static MacAccessibilitySnapshot ReadSnapshot()
    {
        lock (SyncRoot)
        {
            return snapshot.Clone();
        }
    }

    private static void UpdateSnapshot(MacAccessibilitySnapshot next)
    {
        Action? approvalChanged = null;
        Action? stopChanged = null;
        MacAccessibilitySnapshot previous;
        lock (SyncRoot)
        {
            previous = snapshot;
            snapshot = next;
            if (ApprovalState(previous) != ApprovalState(next))
            {
                approvalChanged = ApprovalChanged;
            }

            if (previous.HasActiveTurn != next.HasActiveTurn)
            {
                stopChanged = StopChanged;
            }
        }

        previous.Dispose();
        approvalChanged?.Invoke();
        stopChanged?.Invoke();
    }

    private static (Boolean Approve, Boolean AlwaysApprove, Boolean Deny) ApprovalState(
        MacAccessibilitySnapshot value)
        => (
            value.HasActionableApproval(ApprovalDecision.Approve),
            value.HasActionableApproval(ApprovalDecision.AlwaysApprove),
            value.HasActionableApproval(ApprovalDecision.Deny));
}
