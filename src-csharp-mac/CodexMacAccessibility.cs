using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

internal static class CodexMacAccessibility
{
    private const Int32 MaximumDepth = 42;
    private const Int32 MaximumNodesPerWindow = 7000;

    internal const String BundleIdentifier = "com.openai.codex";

    private enum ApprovalRole
    {
        Approve,
        Persistent,
        Deny,
        Options,
    }

    public static MacAccessibilitySnapshot Scan(Boolean promptForPermission = false)
    {
        if (!MacAccessibilityNative.IsTrusted(promptForPermission))
        {
            return MacAccessibilitySnapshot.Untrusted();
        }

        var approvals = new List<MacApprovalSurface>();
        var stops = new List<MacAxTarget>();
        foreach (var processId in GetCodexProcessIds())
        {
            using var application = MacAccessibilityNative.CreateApplication(processId);
            if (application is null)
            {
                continue;
            }

            _ = MacAccessibilityNative.TryCopyElement(
                application.Handle,
                "AXFocusedWindow",
                out var focusedWindow);
            using (focusedWindow)
            {
                var visitedWindow = false;
                MacAccessibilityNative.ForEachElement(
                    application.Handle,
                    "AXWindows",
                    window =>
                    {
                        visitedWindow = true;
                        ScanWindow(
                            window,
                            focusedWindow is not null
                                && MacAccessibilityNative.IsSameElement(window, focusedWindow.Handle),
                            approvals,
                            stops);
                    });

                if (!visitedWindow && focusedWindow is not null)
                {
                    ScanWindow(focusedWindow.Handle, true, approvals, stops);
                }
            }
        }

        return new MacAccessibilitySnapshot(true, approvals, stops);
    }

    public static MacActionAttempt TryInvokeApproval(
        MacAccessibilitySnapshot snapshot,
        ApprovalDecision decision)
    {
        if (!snapshot.IsTrusted)
        {
            return MacActionAttempt.PermissionRequired;
        }

        var foundSurface = false;
        var foundTarget = false;
        foreach (var approval in snapshot.Approvals
                     .OrderByDescending(item => item.IsFocusedWindow))
        {
            foundSurface = true;
            if (decision == ApprovalDecision.AlwaysApprove)
            {
                if (approval.Persistent is not null)
                {
                    foundTarget = true;
                    var persistentAttempt = Invoke(approval.Persistent);
                    if (persistentAttempt != MacActionAttempt.Unavailable)
                    {
                        return persistentAttempt;
                    }

                    continue;
                }

                if (approval.Options is null)
                {
                    continue;
                }

                foundTarget = true;
                if (!approval.Options.TryOpen())
                {
                    continue;
                }

                var expandedAttempt = WaitForPersistentApproval();
                if (expandedAttempt is not (MacActionAttempt.NoTarget or MacActionAttempt.Unavailable))
                {
                    return expandedAttempt;
                }

                continue;
            }

            var target = decision == ApprovalDecision.Approve
                ? approval.Approve
                : approval.Deny;
            if (target is not null)
            {
                foundTarget = true;
                var attempt = Invoke(target);
                if (attempt != MacActionAttempt.Unavailable)
                {
                    return attempt;
                }
            }
        }

        return foundSurface || foundTarget
            ? MacActionAttempt.Unavailable
            : MacActionAttempt.NoTarget;
    }

    public static MacActionAttempt TryStop(MacAccessibilitySnapshot snapshot)
    {
        if (!snapshot.IsTrusted)
        {
            return MacActionAttempt.PermissionRequired;
        }

        var target = snapshot.StopTargets
            .OrderByDescending(item => item.IsFocusedWindow)
            .FirstOrDefault();
        return target is null ? MacActionAttempt.NoTarget : Invoke(target);
    }

    private static MacActionAttempt WaitForPersistentApproval()
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < TimeSpan.FromMilliseconds(1100))
        {
            using var snapshot = Scan();
            var target = snapshot.Approvals
                .OrderByDescending(item => item.IsFocusedWindow)
                .Select(item => item.Persistent)
                .FirstOrDefault(item => item is not null);
            if (target is not null)
            {
                return Invoke(target);
            }

            Thread.Sleep(45);
        }

        return MacActionAttempt.NoTarget;
    }

    private static MacActionAttempt Invoke(MacAxTarget target)
    {
        if (target.TryPress())
        {
            return MacActionAttempt.Invoked;
        }

        return target.TryFocus()
            ? MacActionAttempt.ReadyForKeyboardFallback
            : MacActionAttempt.Unavailable;
    }

    private static void ScanWindow(
        IntPtr window,
        Boolean isFocusedWindow,
        ICollection<MacApprovalSurface> approvals,
        ICollection<MacAxTarget> stops)
    {
        var builder = new WindowScanBuilder(isFocusedWindow);
        var budget = new ScanBudget();
        Traverse(
            window,
            window,
            isFocusedWindow,
            builder,
            String.Empty,
            false,
            false,
            0,
            budget);

        var result = builder.Build();
        if (result.Approval is not null)
        {
            approvals.Add(result.Approval);
        }

        foreach (var stop in result.StopTargets)
        {
            stops.Add(stop);
        }
    }

    private static void Traverse(
        IntPtr element,
        IntPtr window,
        Boolean isFocusedWindow,
        WindowScanBuilder builder,
        String ancestorIdentity,
        Boolean inApprovalSurface,
        Boolean inComposer,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth || budget.NodesVisited >= MaximumNodesPerWindow)
        {
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        var subrole = MacAccessibilityNative.ReadString(element, "AXSubrole");
        var identifier = MacAccessibilityNative.ReadString(element, "AXIdentifier");
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var identity = JoinText(identifier, role, subrole, title, description, help, value);
        var context = JoinContext(ancestorIdentity, identity);
        var currentApprovalSurface = inApprovalSurface || ContainsAny(
            identity,
            "data-codex-approval-surface",
            "codex-approval-surface",
            "approval-request-card",
            "@container/approval-card");
        var currentComposer = inComposer || ContainsAny(
            identity,
            "composer",
            "prompt-textarea",
            "chat-input",
            "message-input");

        if (ApprovalLabels.IsStatus(title)
            || ApprovalLabels.IsStatus(description)
            || ApprovalLabels.IsStatus(help)
            || ApprovalLabels.IsStatus(value))
        {
            builder.HasApprovalStatus = true;
        }

        if (currentApprovalSurface)
        {
            builder.HasApprovalSurface = true;
        }

        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        if (enabled && visible && IsInteractiveRole(role))
        {
            var approvalRole = ClassifyApprovalRole(
                identity,
                title,
                description,
                help,
                value);
            if (approvalRole.HasValue || currentApprovalSurface)
            {
                builder.AddApprovalCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        isFocusedWindow,
                        FirstText(title, description, help)),
                    approvalRole,
                    currentApprovalSurface,
                    builder.NextDocumentOrder());
            }

            var stableStopIdentity = ContainsAny(
                identity,
                "composer-stop",
                "stop-composer",
                "stop-response",
                "stop-generating",
                "stop-turn",
                "interrupt-turn",
                "interrupt-response");
            var stopLabel = StopLabels.IsStop(title)
                || StopLabels.IsStop(description)
                || StopLabels.IsStop(help)
                || StopLabels.IsStop(value);
            var excludedContext = ContainsAny(
                context,
                "voice",
                "audio",
                "recording",
                "dictation",
                "trace");
            if (stableStopIdentity || (stopLabel && (currentComposer || !excludedContext)))
            {
                builder.AddStopTarget(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        isFocusedWindow,
                        FirstText(title, description, help)));
            }
        }

        MacAccessibilityNative.ForEachElement(
            element,
            "AXChildren",
            child => Traverse(
                child,
                window,
                isFocusedWindow,
                builder,
                context,
                currentApprovalSurface,
                currentComposer,
                depth + 1,
                budget));
    }

    private static ApprovalRole? ClassifyApprovalRole(params String[] values)
    {
        var identity = JoinText(values);
        if (ContainsAny(
                identity,
                "approvalRequestCard.allowOnce",
                "approval.approve",
                "approval-approve",
                "approve-once",
                "allow-once")
            || values.Any(ApprovalLabels.IsOneTime))
        {
            return ApprovalRole.Approve;
        }

        if (ContainsAny(
                identity,
                "approvalRequestCard.alwaysAllow",
                "approvalRequestCard.allowConversation",
                "always-approve",
                "always-allow",
                "allow-conversation")
            || values.Any(ApprovalLabels.IsPersistent))
        {
            return ApprovalRole.Persistent;
        }

        if (ContainsAny(
                identity,
                "approvalRequestCard.deny",
                "approval.decline",
                "approval-deny",
                "decline-approval")
            || values.Any(ApprovalLabels.IsDeny))
        {
            return ApprovalRole.Deny;
        }

        if (ContainsAny(
                identity,
                "approvalRequestCard.approvalOptions",
                "approval-options",
                "approvalOptions")
            || values.Any(ApprovalLabels.IsOptions))
        {
            return ApprovalRole.Options;
        }

        return null;
    }

    private static IReadOnlyList<Int32> GetCodexProcessIds()
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

        return processIds.ToArray();
    }

    private static Boolean IsInteractiveRole(String role)
        => role is "AXButton"
            or "AXCheckBox"
            or "AXLink"
            or "AXMenuButton"
            or "AXMenuItem"
            or "AXPopUpButton"
            or "AXRadioButton";

    private static String JoinText(params String[] values)
        => String.Join(' ', values.Where(value => !String.IsNullOrWhiteSpace(value)));

    private static String JoinContext(String ancestors, String current)
    {
        var result = JoinText(ancestors, current);
        return result.Length <= 2200 ? result : result[^2200..];
    }

    private static String FirstText(params String[] values)
        => values.FirstOrDefault(value => !String.IsNullOrWhiteSpace(value)) ?? String.Empty;

    private static Boolean ContainsAny(String value, params String[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));

    private sealed class ScanBudget
    {
        public Int32 NodesVisited { get; set; }
    }

    private sealed record ApprovalCandidate(
        MacAxTarget Target,
        ApprovalRole? Role,
        Boolean InSurface,
        Int32 DocumentOrder);

    private sealed record WindowScanResult(
        MacApprovalSurface? Approval,
        IReadOnlyList<MacAxTarget> StopTargets);

    private sealed class WindowScanBuilder
    {
        private readonly Boolean isFocusedWindow;
        private readonly List<ApprovalCandidate> approvalCandidates = new();
        private readonly List<MacAxTarget> stopTargets = new();
        private Int32 documentOrder;

        public WindowScanBuilder(Boolean isFocusedWindow)
            => this.isFocusedWindow = isFocusedWindow;

        public Boolean HasApprovalSurface { get; set; }

        public Boolean HasApprovalStatus { get; set; }

        public Int32 NextDocumentOrder() => this.documentOrder++;

        public void AddApprovalCandidate(
            MacAxTarget target,
            ApprovalRole? role,
            Boolean inSurface,
            Int32 order)
            => this.approvalCandidates.Add(new ApprovalCandidate(target, role, inSurface, order));

        public void AddStopTarget(MacAxTarget target) => this.stopTargets.Add(target);

        public WindowScanResult Build()
        {
            var surfaceCandidates = this.approvalCandidates
                .Where(candidate => candidate.InSurface)
                .OrderBy(candidate => candidate.DocumentOrder)
                .ToArray();
            var approve = this.Find(ApprovalRole.Approve);
            var persistent = this.Find(ApprovalRole.Persistent);
            var deny = this.Find(ApprovalRole.Deny);
            var options = this.Find(ApprovalRole.Options)
                ?? surfaceCandidates.LastOrDefault(candidate =>
                    candidate.Target.Role is "AXPopUpButton" or "AXMenuButton");
            var hasRecognizedDecision = approve is not null || persistent is not null || deny is not null;
            var decisionCandidates = surfaceCandidates
                .Where(candidate => !ReferenceEquals(candidate, options))
                .ToArray();

            if (!hasRecognizedDecision
                && this.HasApprovalSurface
                && decisionCandidates.Length >= 2)
            {
                deny ??= decisionCandidates[^2];
                approve ??= decisionCandidates[^1];
                if (decisionCandidates.Length >= 3)
                {
                    persistent ??= decisionCandidates[0];
                }
            }

            var canApprove = approve is not null;
            var canAlwaysApprove = persistent is not null || options is not null;
            var canDeny = deny is not null;

            var hasDecisionTarget = approve is not null || persistent is not null || deny is not null;
            var hasStructuredSurface = this.HasApprovalSurface && decisionCandidates.Length >= 2;
            var hasRecognizedRole = this.approvalCandidates.Any(candidate =>
                candidate.Role is ApprovalRole.Approve
                    or ApprovalRole.Persistent
                    or ApprovalRole.Deny);
            var shouldExposeApproval = hasDecisionTarget
                && (hasStructuredSurface || hasRecognizedRole || this.HasApprovalStatus);

            var selected = new HashSet<MacAxTarget>(ReferenceEqualityComparer.Instance);
            MacApprovalSurface? approval = null;
            if (shouldExposeApproval)
            {
                AddSelected(approve, selected);
                AddSelected(persistent, selected);
                AddSelected(deny, selected);
                AddSelected(options, selected);
                approval = new MacApprovalSurface(
                    this.isFocusedWindow,
                    approve?.Target,
                    persistent?.Target,
                    deny?.Target,
                    options?.Target,
                    canApprove,
                    canAlwaysApprove,
                    canDeny);
            }

            foreach (var candidate in this.approvalCandidates)
            {
                if (!selected.Contains(candidate.Target))
                {
                    candidate.Target.Dispose();
                }
            }

            return new WindowScanResult(approval, this.stopTargets);
        }

        private ApprovalCandidate? Find(ApprovalRole role)
            => this.approvalCandidates.FirstOrDefault(candidate => candidate.Role == role);

        private static void AddSelected(
            ApprovalCandidate? candidate,
            ISet<MacAxTarget> selected)
        {
            if (candidate is not null)
            {
                selected.Add(candidate.Target);
            }
        }
    }
}
