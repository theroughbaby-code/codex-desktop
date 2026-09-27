using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
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
    ReadyForKeyboardFallback,
    FoundButUnavailable,
}

internal enum ApprovalRole
{
    Approve,
    Persistent,
    Deny,
    Options,
}

internal static class CodexApprovalMonitor
{
    private const Int32 SwRestore = 9;
    private const String ApprovalSurfaceClass = "@container/approval-card";
    private static readonly TimeSpan PendingPollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan FullDiscoveryInterval = TimeSpan.FromSeconds(4);
    private static readonly AutomationProperty? AriaRoleProperty = AutomationProperty.LookupById(30101);
    private static readonly AutomationProperty? AriaPropertiesProperty = AutomationProperty.LookupById(30102);
    private static readonly Object SyncRoot = new();
    private static Timer? timer;
    private static Int32 subscribers;
    private static Int32 refreshInProgress;
    private static Boolean hasPendingApproval;
    private static IReadOnlyList<ApprovalUi> cachedPendingApprovals = Array.Empty<ApprovalUi>();
    private static DateTime nextFullDiscoveryUtc = DateTime.MinValue;
    private static String lastDiagnostic = String.Empty;

    internal static String LastDiagnostic => lastDiagnostic;

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
            cachedPendingApprovals = Array.Empty<ApprovalUi>();
            nextFullDiscoveryUtc = DateTime.MinValue;
            hasPendingApproval = false;
        }
    }

    public static void RefreshSoon()
    {
        lock (SyncRoot)
        {
            timer?.Change(TimeSpan.FromMilliseconds(60), Timeout.InfiniteTimeSpan);
        }
    }

    public static ApprovalAttempt TryInvoke(ApprovalDecision decision)
    {
        IReadOnlyList<ApprovalUi> candidates;
        lock (SyncRoot)
        {
            candidates = cachedPendingApprovals;
        }

        var attempt = TryInvokeFromCandidates(decision, candidates);
        if (attempt is ApprovalAttempt.Invoked or ApprovalAttempt.ReadyForKeyboardFallback)
        {
            return attempt;
        }

        var freshCandidates = ReadApprovalUis()
            .Where(ui => ui.HasPending)
            .ToArray();
        UpdateState(freshCandidates, resetDiscoveryDeadline: true);
        return TryInvokeFromCandidates(decision, freshCandidates);
    }

    private static ApprovalAttempt TryInvokeFromCandidates(
        ApprovalDecision decision,
        IReadOnlyList<ApprovalUi> candidates)
    {
        if (candidates.Count == 0)
        {
            return ApprovalAttempt.NoApproval;
        }

        var foundUnavailable = false;
        foreach (var ui in candidates)
        {
            if (decision == ApprovalDecision.AlwaysApprove)
            {
                if (ui.Persistent is not null)
                {
                    return TryInvokeMatchedControl(ui.WindowHandle, ApprovalRole.Persistent, ui.Persistent);
                }

                if (ui.Options is null || !TryActivateWindow(ui.WindowHandle) || !TryOpenControl(ui.Options.Element))
                {
                    foundUnavailable = true;
                    continue;
                }

                var expandedAttempt = WaitForPersistentApproval(ui.WindowHandle, ui.Options.Bounds);
                if (expandedAttempt != ApprovalAttempt.NoApproval)
                {
                    return expandedAttempt;
                }

                foundUnavailable = true;
                continue;
            }

            var role = decision == ApprovalDecision.Approve
                ? ApprovalRole.Approve
                : ApprovalRole.Deny;
            var target = ui.GetControl(role);
            if (target is null)
            {
                foundUnavailable = true;
                continue;
            }

            return TryInvokeMatchedControl(ui.WindowHandle, role, target);
        }

        return foundUnavailable
            ? ApprovalAttempt.FoundButUnavailable
            : ApprovalAttempt.NoApproval;
    }

    private static ApprovalAttempt WaitForPersistentApproval(IntPtr preferredWindow, ElementBounds optionsBounds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(1000))
        {
            var direct = ReadApprovalUis(preferredWindow)
                .Where(ui => ui.HasPending)
                .Select(ui => ui.Persistent)
                .FirstOrDefault(control => control is not null);
            if (direct is not null)
            {
                return TryInvokeMatchedControl(preferredWindow, ApprovalRole.Persistent, direct);
            }

            var menuAction = FindOpenPersistentMenuAction(preferredWindow, optionsBounds);
            if (menuAction is not null)
            {
                return TryInvokeTransientControl(menuAction);
            }

            Thread.Sleep(40);
        }

        return ApprovalAttempt.NoApproval;
    }

    private static ApprovalAttempt TryInvokeMatchedControl(
        IntPtr windowHandle,
        ApprovalRole role,
        ApprovalControl target)
    {
        var activated = TryActivateWindow(windowHandle);
        if (!activated)
        {
            return ApprovalAttempt.FoundButUnavailable;
        }

        if (TryInvokeControl(target.Element))
        {
            return ApprovalAttempt.Invoked;
        }

        var freshTarget = ReadApprovalUis(windowHandle)
            .Where(ui => ui.HasPending)
            .Select(ui => ui.GetControl(role))
            .Where(control => control is not null)
            .OrderBy(control => control!.Bounds.DistanceTo(target.Bounds))
            .FirstOrDefault();
        if (freshTarget is not null)
        {
            target = freshTarget;
        }

        if (TryInvokeControl(target.Element))
        {
            return ApprovalAttempt.Invoked;
        }

        return activated && TryFocusControl(target.Element)
            ? ApprovalAttempt.ReadyForKeyboardFallback
            : ApprovalAttempt.FoundButUnavailable;
    }

    private static ApprovalAttempt TryInvokeTransientControl(ApprovalControl target)
    {
        if (TryInvokeControl(target.Element))
        {
            return ApprovalAttempt.Invoked;
        }

        return TryFocusControl(target.Element)
            ? ApprovalAttempt.ReadyForKeyboardFallback
            : ApprovalAttempt.FoundButUnavailable;
    }

    private static void Refresh()
    {
        if (Interlocked.Exchange(ref refreshInProgress, 1) != 0)
        {
            return;
        }

        var nextInterval = IdlePollInterval;
        try
        {
            IReadOnlyList<ApprovalUi> previous;
            DateTime discoveryDeadline;
            lock (SyncRoot)
            {
                previous = cachedPendingApprovals;
                discoveryDeadline = nextFullDiscoveryUtc;
            }

            IReadOnlyList<ApprovalUi> pendingApprovals = Array.Empty<ApprovalUi>();
            var fullDiscovery = previous.Count == 0
                || DateTime.UtcNow >= discoveryDeadline
                || !TryRefreshCachedApprovals(previous, out pendingApprovals);
            if (fullDiscovery)
            {
                pendingApprovals = ReadApprovalUis()
                    .Where(ui => ui.HasPending)
                    .ToArray();
            }

            UpdateState(pendingApprovals, fullDiscovery);
            nextInterval = pendingApprovals.Count > 0
                ? PendingPollInterval
                : IdlePollInterval;
        }
        catch
        {
            // Codex can replace its accessibility tree while a view is changing.
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

    private static Boolean TryRefreshCachedApprovals(
        IReadOnlyList<ApprovalUi> previous,
        out IReadOnlyList<ApprovalUi> refreshed)
    {
        foreach (var ui in previous)
        {
            var controls = new[] { ui.Approve, ui.Persistent, ui.Deny, ui.Options }
                .Where(control => control is not null)
                .Cast<ApprovalControl>()
                .GroupBy(control => control.RuntimeKey)
                .Select(group => group.First())
                .ToArray();
            if (controls.Length == 0 || controls.Any(control => !IsAvailable(control.Element)))
            {
                refreshed = Array.Empty<ApprovalUi>();
                return false;
            }
        }

        refreshed = previous;
        return true;
    }

    private static void UpdateState(
        IReadOnlyList<ApprovalUi> pendingApprovals,
        Boolean resetDiscoveryDeadline)
    {
        Action? changed = null;
        lock (SyncRoot)
        {
            cachedPendingApprovals = pendingApprovals;
            if (resetDiscoveryDeadline)
            {
                nextFullDiscoveryUtc = DateTime.UtcNow + FullDiscoveryInterval;
            }

            var pending = pendingApprovals.Count > 0;
            if (pending != hasPendingApproval)
            {
                hasPendingApproval = pending;
                changed = Changed;
            }
        }

        changed?.Invoke();
    }

    private static IReadOnlyList<ApprovalUi> ReadApprovalUis()
    {
        var result = new List<ApprovalUi>();
        foreach (var windowHandle in GetCodexWindowHandles())
        {
            result.AddRange(ReadApprovalUis(windowHandle));
        }

        return result;
    }

    private static IReadOnlyList<ApprovalUi> ReadApprovalUis(IntPtr windowHandle)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var interactiveElements = ReadDecisionElements(root);
            var snapshots = interactiveElements
                .Select((element, index) =>
                    TryReadPotentialApprovalElement(element, index, out var snapshot)
                        ? snapshot
                        : null)
                .Where(snapshot => snapshot is not null)
                .Cast<ElementSnapshot>()
                .ToArray();
            var surfaces = FindApprovalSurfaces(
                snapshots.Select(snapshot => snapshot.Element).ToArray());
            if (surfaces.Count == 0)
            {
                surfaces = FindDirectApprovalSurfaces(root);
            }

            if (surfaces.Count > 0)
            {
                return surfaces
                    .Select(surface => BuildStructuralApprovalUi(windowHandle, surface))
                    .Where(ui => ui is not null)
                    .Cast<ApprovalUi>()
                    .ToArray();
            }

            var fallback = BuildLabelFallbackApprovalUi(windowHandle, snapshots);
            return fallback is null
                ? Array.Empty<ApprovalUi>()
                : new[] { fallback };
        }
        catch
        {
            // One inaccessible Codex window must not prevent scanning the others.
            return Array.Empty<ApprovalUi>();
        }
    }

    private static IReadOnlyList<AutomationElement> ReadDecisionElements(AutomationElement root)
    {
        var condition = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton));
        return root.FindAll(TreeScope.Descendants, condition)
            .Cast<AutomationElement>()
            .ToArray();
    }

    private static IReadOnlyList<ElementSnapshot> FindDirectApprovalSurfaces(AutomationElement root)
    {
        try
        {
            var condition = new OrCondition(
                new PropertyCondition(AutomationElement.ClassNameProperty, ApprovalSurfaceClass),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "codex-approval-surface"),
                new PropertyCondition(AutomationElement.AutomationIdProperty, "approval-request-card"),
                new PropertyCondition(AutomationElement.NameProperty, "codex-approval-surface"),
                new PropertyCondition(AutomationElement.HelpTextProperty, "codex-approval-surface"));
            var candidates = root.FindAll(TreeScope.Descendants, condition);
            var surfaces = new List<ElementSnapshot>();
            for (var index = 0; index < candidates.Count; index++)
            {
                if (TryReadElement(candidates[index], index, out var snapshot)
                    && IsApprovalSurface(snapshot))
                {
                    surfaces.Add(snapshot);
                }
            }

            return surfaces
                .GroupBy(surface => surface.RuntimeKey)
                .Select(group => group.First())
                .ToArray();
        }
        catch
        {
            return Array.Empty<ElementSnapshot>();
        }
    }

    private static IReadOnlyList<ElementSnapshot> FindApprovalSurfaces(
        IReadOnlyList<AutomationElement> controls)
    {
        var surfaces = new Dictionary<String, ElementSnapshot>(StringComparer.Ordinal);
        for (var index = 0; index < controls.Count; index++)
        {
            if (!TryFindApprovalSurfaceAncestor(controls[index], index, out var surface))
            {
                continue;
            }

            var key = surface.RuntimeKey.Length > 0
                ? surface.RuntimeKey
                : $"{surface.Bounds.X}:{surface.Bounds.Y}:{surface.Bounds.Width}:{surface.Bounds.Height}";
            surfaces.TryAdd(key, surface);
        }

        return surfaces.Values.ToArray();
    }

    private static Boolean TryFindApprovalSurfaceAncestor(
        AutomationElement element,
        Int32 documentOrder,
        out ElementSnapshot surface)
    {
        surface = null!;
        try
        {
            var parent = TreeWalker.RawViewWalker.GetParent(element);
            for (var depth = 0; parent is not null && depth < 24; depth++)
            {
                var className = ReadStringProperty(parent, AutomationElement.ClassNameProperty);
                var automationId = ReadStringProperty(parent, AutomationElement.AutomationIdProperty);
                var identity = JoinText(
                    ReadStringProperty(parent, AutomationElement.NameProperty),
                    ReadStringProperty(parent, AutomationElement.HelpTextProperty),
                    automationId,
                    ReadStringProperty(parent, AriaRoleProperty),
                    ReadStringProperty(parent, AriaPropertiesProperty));
                if (ContainsAny(
                        identity,
                        "data-codex-approval-surface",
                        "codex-approval-surface",
                        "approval-request-card",
                        ApprovalSurfaceClass)
                    || className.Contains(ApprovalSurfaceClass, StringComparison.OrdinalIgnoreCase))
                {
                    return TryReadElement(parent, documentOrder, out surface);
                }

                parent = TreeWalker.RawViewWalker.GetParent(parent);
            }
        }
        catch
        {
            // A rerendered ancestor simply makes this discovery pass miss it.
        }

        return false;
    }

    private static Boolean TryReadPotentialApprovalElement(
        AutomationElement element,
        Int32 documentOrder,
        out ElementSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            var current = element.Current;
            if (!current.IsEnabled || current.IsOffscreen)
            {
                return false;
            }

            var name = current.Name?.Trim() ?? String.Empty;
            var helpText = current.HelpText?.Trim() ?? String.Empty;
            var identity = JoinText(
                current.AutomationId?.Trim() ?? String.Empty,
                ReadStringProperty(element, AriaRoleProperty),
                ReadStringProperty(element, AriaPropertiesProperty));
            var hasStableRole = Enum.GetValues<ApprovalRole>()
                .Any(role => HasStableRole(identity, role));
            var hasTranslatedRole = ApprovalLabels.IsOneTime(name)
                || ApprovalLabels.IsOneTime(helpText)
                || ApprovalLabels.IsPersistent(name)
                || ApprovalLabels.IsPersistent(helpText)
                || ApprovalLabels.IsDeny(name)
                || ApprovalLabels.IsDeny(helpText)
                || ApprovalLabels.IsOptions(name)
                || ApprovalLabels.IsOptions(helpText);
            return (hasStableRole || hasTranslatedRole)
                && TryReadElement(element, documentOrder, out snapshot);
        }
        catch
        {
            return false;
        }
    }

    private static ApprovalUi? BuildStructuralApprovalUi(IntPtr windowHandle, ElementSnapshot surface)
    {
        try
        {
            var descendants = ReadDecisionElements(surface.Element);
            var controls = new List<ApprovalControl>();
            for (var index = 0; index < descendants.Count; index++)
            {
                if (TryReadControl(descendants[index], surface, index, out var control))
                {
                    controls.Add(control);
                }
            }

            var actions = controls
                .Where(IsDecisionAction)
                .OrderBy(control => control.DocumentOrder)
                .ToArray();
            var actionGroup = actions
                .Where(control => control.AncestorClasses.Any(IsApprovalActionGroupClass))
                .ToArray();
            if (actionGroup.Length < 2)
            {
                actionGroup = FindRelationshipActionGroup(actions);
            }

            var options = FindByStableRole(actionGroup, ApprovalRole.Options)
                ?? actionGroup.FirstOrDefault(control => ApprovalLabels.IsOptions(control.Name));
            options ??= actionGroup.Where(control => control.CanExpand).LastOrDefault();
            options ??= FindCompactTrailingControl(actionGroup);

            // Chromium can flatten the footer's nested decision group into a row
            // of siblings. Codex keeps the semantic order stable: an optional
            // leading persistent action, then Deny, Approve, and optional Options.
            var decisionActionCount = options is null ? 2 : 3;
            if (actionGroup.Length > decisionActionCount)
            {
                actionGroup = actionGroup
                    .Skip(actionGroup.Length - decisionActionCount)
                    .ToArray();
            }

            var primaryActions = actionGroup
                .Where(control => !SameControl(control, options))
                .ToArray();
            var deny = FindByStableRole(primaryActions, ApprovalRole.Deny)
                ?? primaryActions.FirstOrDefault(control => ApprovalLabels.IsDeny(control.Name));
            var approve = FindByStableRole(primaryActions, ApprovalRole.Approve)
                ?? primaryActions.FirstOrDefault(control => ApprovalLabels.IsOneTime(control.Name));

            options ??= FindByStableRole(actions, ApprovalRole.Options)
                ?? actions.FirstOrDefault(control => ApprovalLabels.IsOptions(control.Name));
            deny ??= FindByStableRole(actions, ApprovalRole.Deny)
                ?? actions.FirstOrDefault(control => ApprovalLabels.IsDeny(control.Name));
            approve ??= FindByStableRole(actions, ApprovalRole.Approve)
                ?? actions.FirstOrDefault(control => ApprovalLabels.IsOneTime(control.Name));

            // Codex renders this group in semantic DOM order: Deny, Approve,
            // then the optional compact approval-options trigger.
            deny ??= primaryActions.FirstOrDefault();
            approve ??= primaryActions.FirstOrDefault(control => !SameControl(control, deny));

            var persistent = FindByStableRole(actions, ApprovalRole.Persistent)
                ?? actions.FirstOrDefault(control => ApprovalLabels.IsPersistent(control.Name));
            if (persistent is null)
            {
                var anchor = approve ?? deny;
                if (anchor is not null)
                {
                    var firstGroupedOrder = actionGroup
                        .Select(item => item.DocumentOrder)
                        .DefaultIfEmpty(Int32.MaxValue)
                        .Min();
                    var leadingActions = actions
                        .Where(control => !actionGroup.Contains(control))
                        .Where(control => control.DocumentOrder < firstGroupedOrder)
                        .Where(control => control.Bounds.SharesRowWith(anchor.Bounds))
                        .Where(control => SharesNonSurfaceAncestor(control, anchor))
                        .ToArray();
                    if (leadingActions.Length == 1)
                    {
                        persistent = leadingActions[0];
                    }
                }
            }

            var hasPending = actions.Length > 0
                && (approve is not null || deny is not null || persistent is not null || options is not null);
            return new ApprovalUi(
                windowHandle,
                surface.RuntimeKey,
                surface.Bounds,
                controls,
                approve,
                persistent,
                deny,
                options,
                true,
                hasPending);
        }
        catch (Exception exception)
        {
            lastDiagnostic = $"BuildStructuralApprovalUi: {exception}";
            return null;
        }
    }

    private static ApprovalUi? BuildLabelFallbackApprovalUi(
        IntPtr windowHandle,
        IReadOnlyList<ElementSnapshot> elements)
    {
        var controls = elements
            .Where(snapshot => snapshot.IsInteractive)
            .Select(snapshot => ToApprovalControl(snapshot, Array.Empty<String>(), Array.Empty<String>()))
            .ToArray();
        var approve = FindByStableRole(controls, ApprovalRole.Approve)
            ?? controls.FirstOrDefault(control => ApprovalLabels.IsOneTime(control.Name));
        var deny = FindByStableRole(controls, ApprovalRole.Deny)
            ?? controls.FirstOrDefault(control => ApprovalLabels.IsDeny(control.Name));
        var persistent = FindByStableRole(controls, ApprovalRole.Persistent)
            ?? controls.FirstOrDefault(control => ApprovalLabels.IsPersistent(control.Name));
        var options = FindByStableRole(controls, ApprovalRole.Options)
            ?? controls.FirstOrDefault(control => ApprovalLabels.IsOptions(control.Name));
        var hasStatus = elements.Any(element => ApprovalLabels.IsStatus(element.SearchText));
        var hasDecisionPair = approve is not null && deny is not null;
        var hasSurface = hasDecisionPair
            || hasStatus && (approve is not null || deny is not null || persistent is not null || options is not null);

        return hasSurface
            ? new ApprovalUi(
                windowHandle,
                String.Empty,
                ElementBounds.Empty,
                controls,
                approve,
                persistent,
                deny,
                options,
                hasSurface,
                hasSurface || hasStatus)
            : null;
    }

    private static ApprovalControl? FindOpenPersistentMenuAction(
        IntPtr windowHandle,
        ElementBounds optionsBounds)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var descendants = ReadDecisionElements(root);
            var controls = new List<ApprovalControl>();
            for (var index = 0; index < descendants.Count; index++)
            {
                if (TryReadControl(descendants[index], null, index, out var control))
                {
                    controls.Add(control);
                }
            }

            var translated = FindByStableRole(controls, ApprovalRole.Persistent)
                ?? controls.FirstOrDefault(control => ApprovalLabels.IsPersistent(control.Name));
            if (translated is not null && translated.Bounds.DistanceTo(optionsBounds) < 900D)
            {
                return translated;
            }

            // The Codex split-button menu has two items in semantic order:
            // one-time approval first, conversation approval second.
            return controls
                .Where(control => control.ControlType == ControlType.MenuItem)
                .Where(control => control.ParentType != ControlType.MenuBar)
                .Where(control => control.ParentRuntimeKey.Length > 0)
                .GroupBy(control => control.ParentRuntimeKey)
                .Select(group => group.OrderBy(control => control.DocumentOrder).ToArray())
                .Where(group => group.Length == 2)
                .Where(group => group.Any(control => control.Bounds.DistanceTo(optionsBounds) < 900D))
                .OrderBy(group => group.Min(control => control.Bounds.DistanceTo(optionsBounds)))
                .Select(group => group[1])
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static ApprovalControl? FindCompactTrailingControl(IReadOnlyList<ApprovalControl> actionGroup)
    {
        if (actionGroup.Count < 3)
        {
            return null;
        }

        var trailing = actionGroup[^1];
        var preceding = actionGroup[^2];
        return trailing.Bounds.Width > 0D
            && preceding.Bounds.Width > 0D
            && (trailing.Bounds.Width <= 52D || trailing.Bounds.Width <= preceding.Bounds.Width * 0.65D)
            && trailing.Bounds.SharesRowWith(preceding.Bounds)
                ? trailing
                : null;
    }

    private static ApprovalControl[] FindRelationshipActionGroup(IReadOnlyList<ApprovalControl> actions)
        => actions
            .SelectMany(control => control.AncestorRuntimeKeys.Select((key, depth) => new { control, key, depth }))
            .Where(item => item.key.Length > 0)
            .GroupBy(item => item.key)
            .Select(group => new
            {
                Controls = group
                    .OrderBy(item => item.control.DocumentOrder)
                    .Select(item => item.control)
                    .GroupBy(control => control.RuntimeKey.Length > 0
                        ? control.RuntimeKey
                        : $"order:{control.DocumentOrder}")
                    .Select(controls => controls.First())
                    .ToArray(),
                AverageDepth = group.Average(item => item.depth),
            })
            .Where(group => group.Controls.Length >= 2)
            .Where(group => group.Controls.All(control => control.Bounds.SharesRowWith(group.Controls[0].Bounds)))
            .OrderBy(group => group.Controls.Length)
            .ThenBy(group => group.AverageDepth)
            .Select(group => group.Controls)
            .FirstOrDefault() ?? Array.Empty<ApprovalControl>();

    private static ApprovalControl? FindByStableRole(
        IEnumerable<ApprovalControl> controls,
        ApprovalRole role)
        => controls.FirstOrDefault(control => HasStableRole(control.IdentityText, role));

    private static Boolean HasStableRole(String identity, ApprovalRole role)
        => role switch
        {
            ApprovalRole.Approve => ContainsAny(
                identity,
                "approvalRequestCard.allowOnce",
                "approval.approve",
                "approval-approve",
                "approve-once",
                "allow-once"),
            ApprovalRole.Persistent => ContainsAny(
                identity,
                "approvalRequestCard.alwaysAllow",
                "approvalRequestCard.allowConversation",
                "always-approve",
                "always-allow",
                "allow-conversation"),
            ApprovalRole.Deny => ContainsAny(
                identity,
                "approvalRequestCard.deny",
                "approval.decline",
                "approval-deny",
                "decline-approval"),
            ApprovalRole.Options => ContainsAny(
                identity,
                "approvalRequestCard.approvalOptions",
                "approval-options",
                "approvalOptions"),
            _ => false,
        };

    private static Boolean IsApprovalSurface(ElementSnapshot snapshot)
        => ContainsAny(
                snapshot.IdentityText,
                "data-codex-approval-surface",
                "codex-approval-surface",
                "approval-request-card",
                ApprovalSurfaceClass)
            || ContainsAny(
                snapshot.SearchText,
                "data-codex-approval-surface",
                "codex-approval-surface",
                "approval-request-card")
            || snapshot.ClassName.Contains(ApprovalSurfaceClass, StringComparison.OrdinalIgnoreCase);

    private static Boolean IsApprovalActionGroupClass(String className)
        => className.Contains("ms-auto", StringComparison.OrdinalIgnoreCase)
            && className.Contains("approval-card", StringComparison.OrdinalIgnoreCase);

    private static Boolean IsDecisionAction(ApprovalControl control)
        => control.ControlType == ControlType.Button
            || control.ControlType == ControlType.MenuItem
            || control.ControlType == ControlType.CheckBox
            || control.ControlType == ControlType.RadioButton
            || control.ControlType == ControlType.Custom && (control.CanInvoke || control.IsKeyboardFocusable);

    private static Boolean SharesNonSurfaceAncestor(ApprovalControl left, ApprovalControl right)
        => left.AncestorRuntimeKeys.Intersect(right.AncestorRuntimeKeys).Any();

    private static Boolean SameControl(ApprovalControl control, ApprovalControl? other)
        => other is not null
            && control.RuntimeKey.Length > 0
            && String.Equals(control.RuntimeKey, other.RuntimeKey, StringComparison.Ordinal);

    private static Boolean TryReadElement(
        AutomationElement element,
        Int32 documentOrder,
        out ElementSnapshot snapshot)
    {
        snapshot = null!;
        try
        {
            var current = element.Current;
            if (current.IsOffscreen)
            {
                return false;
            }

            var name = current.Name?.Trim() ?? String.Empty;
            var helpText = current.HelpText?.Trim() ?? String.Empty;
            var automationId = current.AutomationId?.Trim() ?? String.Empty;
            var itemStatus = current.ItemStatus?.Trim() ?? String.Empty;
            var acceleratorKey = current.AcceleratorKey?.Trim() ?? String.Empty;
            var accessKey = current.AccessKey?.Trim() ?? String.Empty;
            var className = current.ClassName?.Trim() ?? String.Empty;
            var ariaRole = ReadStringProperty(element, AriaRoleProperty);
            var ariaProperties = ReadStringProperty(element, AriaPropertiesProperty);
            var type = current.ControlType;
            var isInteractive = current.IsEnabled && IsInteractiveType(type);
            var bounds = ReadBounds(element);
            var identityText = JoinText(automationId, ariaRole, ariaProperties);
            var searchText = JoinText(
                name,
                helpText,
                automationId,
                itemStatus,
                acceleratorKey,
                accessKey);
            var parent = TryGetParent(element);

            snapshot = new ElementSnapshot(
                element,
                name,
                searchText,
                identityText,
                className,
                type,
                parent?.ControlType,
                parent?.RuntimeKey ?? String.Empty,
                GetRuntimeKey(element),
                bounds,
                documentOrder,
                isInteractive,
                current.IsKeyboardFocusable,
                HasPattern(element, InvokePattern.Pattern) || HasPattern(element, SelectionItemPattern.Pattern),
                HasPattern(element, ExpandCollapsePattern.Pattern));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Boolean TryReadControl(
        AutomationElement element,
        ElementSnapshot? boundary,
        Int32 documentOrder,
        out ApprovalControl control)
    {
        control = null!;
        if (!TryReadElement(element, documentOrder, out var snapshot) || !snapshot.IsInteractive)
        {
            return false;
        }

        var ancestorClasses = new List<String>();
        var ancestorRuntimeKeys = new List<String>();
        try
        {
            var parent = TreeWalker.RawViewWalker.GetParent(element);
            for (var depth = 0; parent is not null && depth < 24; depth++)
            {
                var parentKey = GetRuntimeKey(parent);
                if (boundary is not null && String.Equals(parentKey, boundary.RuntimeKey, StringComparison.Ordinal))
                {
                    break;
                }

                var className = ReadStringProperty(parent, AutomationElement.ClassNameProperty);
                if (className.Length > 0)
                {
                    ancestorClasses.Add(className);
                }

                if (parentKey.Length > 0)
                {
                    ancestorRuntimeKeys.Add(parentKey);
                }

                parent = TreeWalker.RawViewWalker.GetParent(parent);
            }
        }
        catch
        {
            // The control metadata is still useful if an ancestor was rerendered.
        }

        control = ToApprovalControl(snapshot, ancestorClasses, ancestorRuntimeKeys);
        return true;
    }

    private static ApprovalControl ToApprovalControl(
        ElementSnapshot snapshot,
        IReadOnlyList<String> ancestorClasses,
        IReadOnlyList<String> ancestorRuntimeKeys)
        => new(
            snapshot.Element,
            snapshot.Name,
            snapshot.SearchText,
            snapshot.IdentityText,
            snapshot.ClassName,
            snapshot.ControlType,
            snapshot.ParentType,
            snapshot.ParentRuntimeKey,
            snapshot.RuntimeKey,
            snapshot.Bounds,
            snapshot.DocumentOrder,
            snapshot.IsKeyboardFocusable,
            snapshot.CanInvoke,
            snapshot.CanExpand,
            ancestorClasses,
            ancestorRuntimeKeys);

    private static ParentSnapshot? TryGetParent(AutomationElement element)
    {
        try
        {
            var parent = TreeWalker.RawViewWalker.GetParent(element);
            if (parent is null)
            {
                return null;
            }

            return new ParentSnapshot(parent.Current.ControlType, GetRuntimeKey(parent));
        }
        catch
        {
            return null;
        }
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

    private static ElementBounds ReadBounds(AutomationElement element)
    {
        try
        {
            var value = element.GetCurrentPropertyValue(AutomationElement.BoundingRectangleProperty, true);
            if (value == AutomationElement.NotSupported || value is null)
            {
                return ElementBounds.Empty;
            }

            var valueType = value.GetType();
            return new ElementBounds(
                ReadDoubleProperty(value, valueType, "X"),
                ReadDoubleProperty(value, valueType, "Y"),
                ReadDoubleProperty(value, valueType, "Width"),
                ReadDoubleProperty(value, valueType, "Height"));
        }
        catch
        {
            return ElementBounds.Empty;
        }
    }

    private static Double ReadDoubleProperty(Object value, Type valueType, String propertyName)
    {
        var propertyValue = valueType.GetProperty(propertyName)?.GetValue(value);
        return propertyValue is null ? 0D : Convert.ToDouble(propertyValue);
    }

    private static Boolean HasPattern(AutomationElement element, AutomationPattern pattern)
    {
        try
        {
            return element.TryGetCurrentPattern(pattern, out _);
        }
        catch
        {
            return false;
        }
    }

    private static Boolean IsAvailable(AutomationElement element)
    {
        try
        {
            return element.Current.IsEnabled && !element.Current.IsOffscreen;
        }
        catch
        {
            return false;
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

    private static Boolean IsInteractiveType(ControlType type)
        => type == ControlType.Button
            || type == ControlType.MenuItem
            || type == ControlType.Hyperlink
            || type == ControlType.CheckBox
            || type == ControlType.RadioButton
            || type == ControlType.Custom;

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
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(500))
        {
            if (GetForegroundWindow() == windowHandle)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
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
        catch
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

            if (element.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selectionPattern))
            {
                ((SelectionItemPattern)selectionPattern).Select();
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static Boolean TryFocusControl(AutomationElement element)
    {
        try
        {
            element.SetFocus();
            return true;
        }
        catch
        {
            return false;
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

    private sealed record ParentSnapshot(ControlType ControlType, String RuntimeKey);

    private sealed record ElementSnapshot(
        AutomationElement Element,
        String Name,
        String SearchText,
        String IdentityText,
        String ClassName,
        ControlType ControlType,
        ControlType? ParentType,
        String ParentRuntimeKey,
        String RuntimeKey,
        ElementBounds Bounds,
        Int32 DocumentOrder,
        Boolean IsInteractive,
        Boolean IsKeyboardFocusable,
        Boolean CanInvoke,
        Boolean CanExpand);

    private sealed record ApprovalControl(
        AutomationElement Element,
        String Name,
        String SearchText,
        String IdentityText,
        String ClassName,
        ControlType ControlType,
        ControlType? ParentType,
        String ParentRuntimeKey,
        String RuntimeKey,
        ElementBounds Bounds,
        Int32 DocumentOrder,
        Boolean IsKeyboardFocusable,
        Boolean CanInvoke,
        Boolean CanExpand,
        IReadOnlyList<String> AncestorClasses,
        IReadOnlyList<String> AncestorRuntimeKeys);

    private sealed record ElementBounds(Double X, Double Y, Double Width, Double Height)
    {
        public static ElementBounds Empty { get; } = new(0D, 0D, 0D, 0D);

        public Double DistanceTo(ElementBounds other)
        {
            var x = X + Width / 2D - (other.X + other.Width / 2D);
            var y = Y + Height / 2D - (other.Y + other.Height / 2D);
            return Math.Sqrt(x * x + y * y);
        }

        public Boolean SharesRowWith(ElementBounds other)
        {
            var overlap = Math.Min(Y + Height, other.Y + other.Height) - Math.Max(Y, other.Y);
            var minimumHeight = Math.Min(Height, other.Height);
            return minimumHeight > 0D && overlap >= minimumHeight * 0.45D;
        }
    }

    private sealed record ApprovalUi(
        IntPtr WindowHandle,
        String SurfaceKey,
        ElementBounds SurfaceBounds,
        IReadOnlyList<ApprovalControl> Controls,
        ApprovalControl? Approve,
        ApprovalControl? Persistent,
        ApprovalControl? Deny,
        ApprovalControl? Options,
        Boolean HasApprovalSurface,
        Boolean HasPending)
    {
        public ApprovalControl? GetControl(ApprovalRole role)
            => role switch
            {
                ApprovalRole.Approve => Approve,
                ApprovalRole.Persistent => Persistent,
                ApprovalRole.Deny => Deny,
                ApprovalRole.Options => Options,
                _ => null,
            };
    }
}
