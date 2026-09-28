using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

internal static class CodexMacAccessibility
{
    private const Int32 MaximumDepth = 42;
    private const Int32 MaximumNodesPerWindow = 7000;
    private static readonly TimeSpan ProductTransitionTimeout =
        TimeSpan.FromMilliseconds(3200);
    private static readonly TimeSpan ComposerTransitionTimeout =
        TimeSpan.FromMilliseconds(2400);

    internal const String BundleIdentifier = "com.openai.codex";
    internal const String AccessibilityRemediation =
        "Open System Settings > Privacy & Security > Accessibility, enable LogiPluginService "
        + "(add /Applications/Utilities/LogiPluginService.app with + if missing; ChatGPT is not sufficient), "
        + "then quit and reopen Logi Options+.";

    private enum ApprovalRole
    {
        Approve,
        Persistent,
        Deny,
        Options,
    }

    private enum MacProductMode
    {
        ChatSurface,
        Codex,
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
            if (application is null
                || MacAccessibilityNative.ReadBoolean(application.Handle, "AXFrontmost") != true)
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

    public static MacActionAttempt TryOpenModelPicker(Boolean promptForPermission = false)
    {
        if (!MacAccessibilityNative.IsTrusted(promptForPermission))
        {
            return MacActionAttempt.PermissionRequired;
        }

        var candidates = new List<ModelPickerCandidate>();
        try
        {
            foreach (var processId in GetCodexProcessIds())
            {
                using var application = MacAccessibilityNative.CreateApplication(processId);
                if (application is null
                    || MacAccessibilityNative.ReadBoolean(application.Handle, "AXFrontmost") != true
                    || !MacAccessibilityNative.TryCopyElement(
                        application.Handle,
                        "AXFocusedWindow",
                        out var focusedWindow)
                    || focusedWindow is null)
                {
                    continue;
                }

                using (focusedWindow)
                {
                    var budget = new ScanBudget();
                    TraverseModelPicker(
                        focusedWindow.Handle,
                        focusedWindow.Handle,
                        candidates,
                        0,
                        budget);
                }
            }

            var foundTarget = false;
            foreach (var candidate in candidates.OrderByDescending(item => item.Score))
            {
                foundTarget = true;
                var attempt = ToActionAttempt(candidate.Target.TryOpen());
                if (attempt != MacActionAttempt.Unavailable)
                {
                    return attempt;
                }
            }

            return foundTarget ? MacActionAttempt.Unavailable : MacActionAttempt.NoTarget;
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                candidate.Target.Dispose();
            }
        }
    }

    public static MacActionAttempt TrySwitchMode(
        MacDesktopMode mode,
        Boolean promptForPermission = false,
        Action<String>? trace = null)
    {
        if (!MacAccessibilityNative.IsTrusted(promptForPermission))
        {
            trace?.Invoke("Accessibility permission is unavailable.");
            return MacActionAttempt.PermissionRequired;
        }

        using (var initial = ScanModeControls(mode, includeProductItems: false))
        {
            if (IsRequestedModeActive(initial, mode))
            {
                trace?.Invoke("The requested product and composer state is already active.");
                return MacActionAttempt.AlreadyActive;
            }
        }

        if (mode == MacDesktopMode.Codex)
        {
            trace?.Invoke("Selecting Codex from the product switcher.");
            var codexAttempt = TrySwitchProductMode(mode, MacProductMode.Codex);
            trace?.Invoke($"Codex product transition completed with {codexAttempt}.");
            return codexAttempt;
        }

        using (var current = ScanModeControls(mode, includeProductItems: false))
        {
            if (current.ProductMode != MacProductMode.ChatSurface)
            {
                trace?.Invoke("Selecting ChatGPT from the product switcher.");
                var productAttempt = TrySwitchProductMode(mode, MacProductMode.ChatSurface);
                trace?.Invoke($"ChatGPT product transition completed with {productAttempt}.");
                if (productAttempt is not (
                    MacActionAttempt.Invoked
                    or MacActionAttempt.Clicked
                    or MacActionAttempt.AlreadyActive))
                {
                    return productAttempt;
                }
            }
            else
            {
                trace?.Invoke("The ChatGPT product surface is already active.");
            }
        }

        trace?.Invoke($"Selecting the {mode} composer mode on the settled ChatGPT surface.");
        var composerAttempt = TrySelectComposerMode(mode, ComposerTransitionTimeout);
        trace?.Invoke($"Composer selection completed with {composerAttempt}.");
        return composerAttempt;
    }

    public static MacActionAttempt TryInvokeModeCommand(
        MacDesktopMode mode,
        TimeSpan timeout)
    {
        if (mode == MacDesktopMode.Codex)
        {
            return MacActionAttempt.NoTarget;
        }

        if (!MacAccessibilityNative.IsTrusted(false))
        {
            return MacActionAttempt.PermissionRequired;
        }

        var stopwatch = Stopwatch.StartNew();
        var foundDialog = false;
        do
        {
            using var scan = ScanModeCommand(mode);
            foundDialog |= scan.HasExactDialog;
            if (scan.Target is not null)
            {
                var attempt = ToActionAttempt(scan.Target.TryPressOnly());
                if (!IsSuccessfulAction(attempt))
                {
                    return MacActionAttempt.Unavailable;
                }

                // Home exposes a verifiable pressed state. Existing ChatGPT
                // conversations do not, so an exact command invocation is the
                // terminal success signal when that control is absent.
                _ = WaitForActiveMode(mode, TimeSpan.FromMilliseconds(650));
                return attempt;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return foundDialog
            ? MacActionAttempt.NoTarget
            : MacActionAttempt.Unavailable;
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
                if (approval.Options.TryOpen() == MacTargetAction.Unavailable)
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

        var foundTarget = false;
        foreach (var target in snapshot.StopTargets
                     .OrderByDescending(item => item.IsFocusedWindow))
        {
            foundTarget = true;
            // Chromium can acknowledge AXPress while leaving the React button
            // untouched. A verified native click on the exact, current target
            // produces the same pointer path as the visible Stop control.
            var attempt = ToActionAttempt(target.TryClick());
            if (attempt == MacActionAttempt.Clicked
                && WaitForStopToClear(TimeSpan.FromMilliseconds(650)))
            {
                return attempt;
            }

            attempt = ToActionAttempt(target.TryPressOnly());
            if (attempt == MacActionAttempt.Invoked
                && WaitForStopToClear(TimeSpan.FromMilliseconds(650)))
            {
                return attempt;
            }

            if (target.TryFocus())
            {
                return MacActionAttempt.ReadyForKeyboardFallback;
            }
        }

        return foundTarget ? MacActionAttempt.Unavailable : MacActionAttempt.NoTarget;
    }

    private static Boolean WaitForStopToClear(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            using var current = Scan();
            if (current.IsTrusted && !current.HasActiveTurn)
            {
                return true;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
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
        var attempt = ToActionAttempt(target.TryPress());
        if (attempt != MacActionAttempt.Unavailable)
        {
            return attempt;
        }

        return target.TryFocus()
            ? MacActionAttempt.ReadyForKeyboardFallback
            : MacActionAttempt.Unavailable;
    }

    private static MacActionAttempt TrySwitchProductMode(
        MacDesktopMode requestedMode,
        MacProductMode productMode)
    {
        using var initial = ScanModeControls(requestedMode, includeProductItems: false);
        if (initial.ProductMode == productMode)
        {
            return MacActionAttempt.AlreadyActive;
        }

        if (initial.Trigger is null)
        {
            return MacActionAttempt.NoTarget;
        }

        var openAction = initial.Trigger.TryOpen();
        if (openAction == MacTargetAction.Unavailable)
        {
            return MacActionAttempt.Unavailable;
        }

        var itemAttempt = WaitForProductItemAction(
            requestedMode,
            productMode,
            TimeSpan.FromMilliseconds(900),
            forceNativeClick: false);
        if (IsSuccessfulAction(itemAttempt)
            && WaitForProductMode(productMode, ProductTransitionTimeout))
        {
            return itemAttempt;
        }

        if (itemAttempt == MacActionAttempt.Invoked)
        {
            var clickAttempt = WaitForProductItemAction(
                requestedMode,
                productMode,
                TimeSpan.FromMilliseconds(450),
                forceNativeClick: true);
            if (clickAttempt == MacActionAttempt.Clicked
                && WaitForProductMode(productMode, ProductTransitionTimeout))
            {
                return clickAttempt;
            }
        }

        if (openAction == MacTargetAction.Invoked)
        {
            using var retry = ScanModeControls(requestedMode, includeProductItems: false);
            if (retry.Trigger?.TryClick() == MacTargetAction.Clicked)
            {
                var clickAttempt = WaitForProductItemAction(
                    requestedMode,
                    productMode,
                    TimeSpan.FromMilliseconds(900),
                    forceNativeClick: true);
                if (clickAttempt == MacActionAttempt.Clicked
                    && WaitForProductMode(productMode, ProductTransitionTimeout))
                {
                    return clickAttempt;
                }
            }
        }

        return itemAttempt == MacActionAttempt.NoTarget
            ? MacActionAttempt.NoTarget
            : MacActionAttempt.Unavailable;
    }

    private static MacActionAttempt TrySelectComposerMode(
        MacDesktopMode mode,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastAttempt = MacActionAttempt.NoTarget;
        do
        {
            using var scan = ScanModeControls(mode, includeProductItems: false);
            if (IsRequestedModeActive(scan, mode))
            {
                return MacActionAttempt.AlreadyActive;
            }

            if (scan.ProductMode == MacProductMode.Codex)
            {
                return MacActionAttempt.Unavailable;
            }

            if (scan.ComposerButton is not null)
            {
                var action = scan.ComposerButton.TryPress();
                var attempt = ToActionAttempt(action);
                if (IsSuccessfulAction(attempt))
                {
                    if (WaitForActiveMode(mode, TimeSpan.FromMilliseconds(900)))
                    {
                        return attempt;
                    }

                    if (attempt == MacActionAttempt.Invoked)
                    {
                        using var retry = ScanModeControls(mode, includeProductItems: false);
                        if (retry.ComposerButton?.TryClick() == MacTargetAction.Clicked
                            && WaitForActiveMode(mode, TimeSpan.FromMilliseconds(900)))
                        {
                            return MacActionAttempt.Clicked;
                        }
                    }

                    return MacActionAttempt.Unavailable;
                }

                lastAttempt = MacActionAttempt.Unavailable;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return lastAttempt;
    }

    private static MacActionAttempt WaitForProductItemAction(
        MacDesktopMode requestedMode,
        MacProductMode productMode,
        TimeSpan timeout,
        Boolean forceNativeClick)
    {
        var stopwatch = Stopwatch.StartNew();
        var lastAttempt = MacActionAttempt.NoTarget;
        do
        {
            using var scan = ScanModeControls(requestedMode, includeProductItems: true);
            if (scan.ProductMode == productMode)
            {
                return MacActionAttempt.AlreadyActive;
            }

            if (scan.ProductItem is not null)
            {
                var action = forceNativeClick
                    ? scan.ProductItem.TryClick()
                    : scan.ProductItem.TryPress();
                var attempt = ToActionAttempt(action);
                if (IsSuccessfulAction(attempt))
                {
                    return attempt;
                }

                lastAttempt = MacActionAttempt.Unavailable;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return lastAttempt;
    }

    private static Boolean WaitForProductMode(
        MacProductMode productMode,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            using var scan = ScanModeControls(MacDesktopMode.Codex, includeProductItems: false);
            if (scan.ProductMode == productMode)
            {
                return true;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    private static Boolean WaitForActiveMode(MacDesktopMode mode, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            using var scan = ScanModeControls(mode, includeProductItems: false);
            if (IsRequestedModeActive(scan, mode))
            {
                return true;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    private static ModeCommandScan ScanModeCommand(MacDesktopMode mode)
    {
        var commandTitle = mode == MacDesktopMode.ChatGPT
            ? "Switch to Chat"
            : "Switch to Work";
        var candidates = new List<ModeCommandCandidate>();
        var searchState = new ModeCommandSearchState();
        try
        {
            foreach (var processId in GetCodexProcessIds())
            {
                using var application = MacAccessibilityNative.CreateApplication(processId);
                if (application is null
                    || MacAccessibilityNative.ReadBoolean(application.Handle, "AXFrontmost") != true
                    || !MacAccessibilityNative.TryCopyElement(
                        application.Handle,
                        "AXFocusedWindow",
                        out var focusedWindow)
                    || focusedWindow is null)
                {
                    continue;
                }

                using (focusedWindow)
                {
                    var budget = new ScanBudget();
                    TraverseModeCommandDialogs(
                        focusedWindow.Handle,
                        focusedWindow.Handle,
                        commandTitle,
                        candidates,
                        searchState,
                        0,
                        budget);

                    // Radix portals can be exposed beside AXWindows rather
                    // than below AXFocusedWindow. Only use this broader root
                    // after the focused tree failed to expose the exact dialog.
                    if (!searchState.FoundDialog)
                    {
                        var applicationBudget = new ScanBudget();
                        TraverseModeCommandDialogs(
                            application.Handle,
                            focusedWindow.Handle,
                            commandTitle,
                            candidates,
                            searchState,
                            0,
                            applicationBudget);
                    }
                }
            }

            var exactTargets = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();
            var target = exactTargets.Length switch
            {
                0 => null,
                1 => exactTargets[0],
                _ when exactTargets[0].Score > exactTargets[1].Score => exactTargets[0],
                _ => null,
            };
            return new ModeCommandScan(searchState.FoundDialog, target?.Target.Clone());
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                candidate.Target.Dispose();
            }
        }
    }

    private static void TraverseModeCommandDialogs(
        IntPtr element,
        IntPtr window,
        String commandTitle,
        ICollection<ModeCommandCandidate> candidates,
        ModeCommandSearchState searchState,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        if (budget.NodesVisited >= MaximumNodesPerWindow)
        {
            budget.WasTruncated = true;
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        var subrole = MacAccessibilityNative.ReadString(element, "AXSubrole");
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var labels = new[] { title, description, help, value };
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        var exactDialogRole = role == "AXDialog"
            || (role == "AXGroup"
                && subrole is "AXApplicationDialog" or "AXDialog");
        var exactDialogLabel = HasExactLabel(labels, "Command menu")
            || HasExactLabel(labels, "Search commands and past chats.");
        if (visible && exactDialogRole && exactDialogLabel)
        {
            searchState.FoundDialog = true;
            var itemBudget = new ScanBudget();
            TraverseModeCommandItems(
                element,
                window,
                commandTitle,
                candidates,
                false,
                0,
                itemBudget);
            return;
        }

        // Portalled dialogs and their list content are normally appended at
        // the end of Chromium's AX tree. Visit the newest nodes first so long
        // conversations cannot consume the bounded scan before the overlay.
        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseModeCommandDialogs(
                child,
                window,
                commandTitle,
                candidates,
                searchState,
                depth + 1,
                budget));
    }

    private static void TraverseModeCommandItems(
        IntPtr element,
        IntPtr window,
        String commandTitle,
        ICollection<ModeCommandCandidate> candidates,
        Boolean inCommandList,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth || budget.NodesVisited >= MaximumNodesPerWindow)
        {
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var labels = new[] { title, description, help, value };
        var currentCommandList = inCommandList || role is "AXList" or "AXListBox";
        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        var expectedRole = role is "AXButton"
            or "AXListBoxOption"
            or "AXMenuItem"
            or "AXRow"
            or "AXStaticText";
        var exactCommandLabel = HasExactLabel(labels, commandTitle)
            || HasExactDescendantLabel(
                element,
                new[] { commandTitle },
                maximumDepth: 3,
                maximumNodes: 24);
        if (enabled
            && visible
            && currentCommandList
            && expectedRole
            && exactCommandLabel)
        {
            var actions = MacAccessibilityNative.ReadActionNames(element);
            if (actions.Contains("AXPress"))
            {
                var score = role switch
                {
                    "AXMenuItem" or "AXListBoxOption" => 380,
                    "AXButton" => 360,
                    "AXRow" => 340,
                    _ => 300,
                };
                candidates.Add(new ModeCommandCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value)),
                    score));
            }
        }

        MacAccessibilityNative.ForEachElement(
            element,
            "AXChildren",
            child => TraverseModeCommandItems(
                child,
                window,
                commandTitle,
                candidates,
                currentCommandList,
                depth + 1,
                budget));
    }

    private static Boolean IsRequestedModeActive(ModeControlScan scan, MacDesktopMode mode)
        => mode == MacDesktopMode.Codex
            ? scan.ProductMode == MacProductMode.Codex
            : scan.ProductMode == MacProductMode.ChatSurface
                && scan.ComposerMode == mode;

    private static Boolean IsSuccessfulAction(MacActionAttempt attempt)
        => attempt is MacActionAttempt.Invoked
            or MacActionAttempt.Clicked
            or MacActionAttempt.AlreadyActive;

    private static ModeControlScan ScanModeControls(
        MacDesktopMode requestedMode,
        Boolean includeProductItems)
    {
        var candidates = new List<ModeCandidate>();
        try
        {
            foreach (var processId in GetCodexProcessIds())
            {
                using var application = MacAccessibilityNative.CreateApplication(processId);
                if (application is null
                    || MacAccessibilityNative.ReadBoolean(application.Handle, "AXFrontmost") != true
                    || !MacAccessibilityNative.TryCopyElement(
                        application.Handle,
                        "AXFocusedWindow",
                        out var focusedWindow)
                    || focusedWindow is null)
                {
                    continue;
                }

                using (focusedWindow)
                {
                    var budget = new ScanBudget();
                    TraverseModeControls(
                        focusedWindow.Handle,
                        focusedWindow.Handle,
                        requestedMode,
                        includeProductItems,
                        candidates,
                        false,
                        0,
                        budget);

                    // The Home composer follows the conversation viewport in
                    // the AX tree. On long chats, find its exact labelled group
                    // from the tail without expanding the scan budget globally.
                    var hasRequestedComposer = candidates.Any(candidate =>
                        candidate.Kind == ModeCandidateKind.ComposerButton
                        && candidate.DesktopMode == requestedMode);
                    var hasSelectedComposer = candidates.Any(candidate =>
                        candidate.Kind == ModeCandidateKind.ComposerButton
                        && candidate.Selected);
                    var detectedProductMode = candidates
                        .Where(candidate => candidate.Kind == ModeCandidateKind.ProductTrigger)
                        .OrderByDescending(candidate => candidate.Score)
                        .Select(candidate => candidate.ProductMode)
                        .FirstOrDefault();
                    var composerCanExist = requestedMode != MacDesktopMode.Codex
                        && detectedProductMode != MacProductMode.Codex;
                    if (composerCanExist
                        && (!hasRequestedComposer || !hasSelectedComposer)
                        && budget.WasTruncated)
                    {
                        var composerBudget = new ScanBudget();
                        TraverseComposerControlsReverse(
                            focusedWindow.Handle,
                            focusedWindow.Handle,
                            candidates,
                            false,
                            0,
                            composerBudget);
                    }

                    // The open product selector is a Radix portal. macOS may
                    // expose its AXMenu as an application child rather than a
                    // focused-window descendant. Menu-bar branches are skipped
                    // and candidates must remain in the window's top-left area.
                    if (includeProductItems
                        && !candidates.Any(candidate =>
                            candidate.Kind == ModeCandidateKind.ProductItem))
                    {
                        var menuBudget = new ScanBudget();
                        TraverseProductMenusReverse(
                            application.Handle,
                            focusedWindow.Handle,
                            requestedMode,
                            candidates,
                            inPopupMenu: false,
                            0,
                            menuBudget);
                    }
                }
            }

            var trigger = candidates
                .Where(candidate => candidate.Kind == ModeCandidateKind.ProductTrigger)
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            var productItem = includeProductItems
                ? candidates
                    .Where(candidate => candidate.Kind == ModeCandidateKind.ProductItem)
                    .OrderByDescending(candidate => candidate.Score)
                    .FirstOrDefault()
                : null;
            var composerButton = candidates
                .Where(candidate => candidate.Kind == ModeCandidateKind.ComposerButton
                    && candidate.DesktopMode == requestedMode
                    && candidate.Enabled)
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            var selectedComposer = candidates
                .Where(candidate => candidate.Kind == ModeCandidateKind.ComposerButton
                    && candidate.Selected)
                .OrderByDescending(candidate => candidate.Score)
                .FirstOrDefault();
            return new ModeControlScan(
                trigger?.ProductMode,
                selectedComposer?.DesktopMode,
                trigger?.Target.Clone(),
                productItem?.Target.Clone(),
                composerButton?.Target.Clone());
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                candidate.Target.Dispose();
            }
        }
    }

    private static void TraverseModeControls(
        IntPtr element,
        IntPtr window,
        MacDesktopMode requestedMode,
        Boolean includeProductItems,
        ICollection<ModeCandidate> candidates,
        Boolean inComposerMode,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth)
        {
            return;
        }

        if (budget.NodesVisited >= MaximumNodesPerWindow)
        {
            budget.WasTruncated = true;
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var labels = new[] { title, description, help, value };
        var currentComposerMode = inComposerMode
            || (role == "AXGroup" && HasExactLabel(labels, "Composer mode"));
        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;

        if (visible)
        {
            var triggerScore = ProductTriggerScore(
                element,
                window,
                role,
                labels,
                out var productMode);
            if (enabled && triggerScore > 0)
            {
                candidates.Add(new ModeCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value)),
                    ModeCandidateKind.ProductTrigger,
                    triggerScore,
                    productMode,
                    null,
                    false,
                    true));
            }

            if (includeProductItems && enabled && triggerScore == 0)
            {
                var productItemScore = ProductItemScore(
                    element,
                    window,
                    requestedMode,
                    role,
                    labels);
                if (productItemScore > 0)
                {
                    candidates.Add(new ModeCandidate(
                        new MacAxTarget(
                            element,
                            window,
                            role,
                            true,
                            FirstText(title, description, help, value)),
                        ModeCandidateKind.ProductItem,
                        productItemScore,
                        null,
                        null,
                        false,
                        true));
                }
            }

            var composerMode = currentComposerMode
                ? ParseComposerButtonMode(role, labels)
                : null;
            if (composerMode.HasValue)
            {
                var selected = MacAccessibilityNative.ReadInteger(element, "AXValue") == 1
                    || MacAccessibilityNative.ReadBoolean(element, "AXValue") == true
                    || MacAccessibilityNative.ReadBoolean(element, "AXSelected") == true;
                candidates.Add(new ModeCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value)),
                    ModeCandidateKind.ComposerButton,
                    300,
                    null,
                    composerMode,
                    selected,
                    enabled));
            }
        }

        MacAccessibilityNative.ForEachElement(
            element,
            "AXChildren",
            child => TraverseModeControls(
                child,
                window,
                requestedMode,
                includeProductItems,
                candidates,
                currentComposerMode,
                depth + 1,
                budget));
    }

    private static void TraverseComposerControlsReverse(
        IntPtr element,
        IntPtr window,
        ICollection<ModeCandidate> candidates,
        Boolean inComposerMode,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth || budget.NodesVisited >= MaximumNodesPerWindow)
        {
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var labels = new[] { title, description, help, value };
        var currentComposerMode = inComposerMode
            || (role == "AXGroup" && HasExactLabel(labels, "Composer mode"));
        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        var composerMode = currentComposerMode && visible
            ? ParseComposerButtonMode(role, labels)
            : null;
        if (composerMode.HasValue)
        {
            var selected = MacAccessibilityNative.ReadInteger(element, "AXValue") == 1
                || MacAccessibilityNative.ReadBoolean(element, "AXValue") == true
                || MacAccessibilityNative.ReadBoolean(element, "AXSelected") == true;
            candidates.Add(new ModeCandidate(
                new MacAxTarget(
                    element,
                    window,
                    role,
                    true,
                    FirstText(title, description, help, value)),
                ModeCandidateKind.ComposerButton,
                300,
                null,
                composerMode,
                selected,
                enabled));
        }

        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseComposerControlsReverse(
                child,
                window,
                candidates,
                currentComposerMode,
                depth + 1,
                budget));
    }

    private static void TraverseProductMenusReverse(
        IntPtr element,
        IntPtr window,
        MacDesktopMode requestedMode,
        ICollection<ModeCandidate> candidates,
        Boolean inPopupMenu,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth || budget.NodesVisited >= MaximumNodesPerWindow)
        {
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        if (role == "AXMenuBar")
        {
            return;
        }

        var currentPopupMenu = inPopupMenu || role == "AXMenu";
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var labels = new[] { title, description, help, value };
        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        if (currentPopupMenu && enabled && visible)
        {
            var score = ProductItemScore(
                element,
                window,
                requestedMode,
                role,
                labels);
            if (score > 0)
            {
                candidates.Add(new ModeCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value)),
                    ModeCandidateKind.ProductItem,
                    score,
                    null,
                    null,
                    false,
                    true));
            }
        }

        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseProductMenusReverse(
                child,
                window,
                requestedMode,
                candidates,
                currentPopupMenu,
                depth + 1,
                budget));
    }

    private static Int32 ProductTriggerScore(
        IntPtr element,
        IntPtr window,
        String role,
        IEnumerable<String> labels,
        out MacProductMode? productMode)
    {
        productMode = null;
        if (role is not ("AXButton" or "AXMenuButton" or "AXPopUpButton")
            || !MacAccessibilityNative.IsNearWindowTopLeft(element, window))
        {
            return 0;
        }

        const String prefix = "Switch mode, current mode: ";
        foreach (var label in labels.Select(value => value.Trim()))
        {
            if (!label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            productMode = ParseProductModeName(label[prefix.Length..]);
            return productMode.HasValue ? 320 : 0;
        }

        return 0;
    }

    private static Int32 ProductItemScore(
        IntPtr element,
        IntPtr window,
        MacDesktopMode requestedMode,
        String role,
        IEnumerable<String> labels)
    {
        if (role is not ("AXMenuItem" or "AXRadioButton")
            || !MacAccessibilityNative.IsNearWindowTopLeft(element, window))
        {
            return 0;
        }

        var targetLabels = requestedMode == MacDesktopMode.Codex
            ? new[] { "Codex" }
            : new[] { "ChatGPT", "ChatGPT Work" };
        if (targetLabels.Any(label => HasExactLabel(labels, label)))
        {
            return 300;
        }

        return HasExactDescendantLabel(element, targetLabels, 3, 24)
            ? 280
            : 0;
    }

    private static MacDesktopMode? ParseComposerButtonMode(
        String role,
        IEnumerable<String> labels)
    {
        if (role is not ("AXButton" or "AXCheckBox" or "AXRadioButton"))
        {
            return null;
        }

        if (HasExactLabel(labels, "Chat"))
        {
            return MacDesktopMode.ChatGPT;
        }

        return HasExactLabel(labels, "Work")
            ? MacDesktopMode.Work
            : null;
    }

    private static MacProductMode? ParseProductModeName(String value)
    {
        var normalized = value.Trim();
        if (normalized.Equals("Codex", StringComparison.OrdinalIgnoreCase))
        {
            return MacProductMode.Codex;
        }

        return normalized.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("ChatGPT Work", StringComparison.OrdinalIgnoreCase)
                ? MacProductMode.ChatSurface
                : null;
    }

    private static Boolean HasExactLabel(IEnumerable<String> values, String candidate)
        => values.Any(value => value.Trim().Equals(candidate, StringComparison.OrdinalIgnoreCase));

    private static Boolean HasExactDescendantLabel(
        IntPtr element,
        IReadOnlyCollection<String> candidates,
        Int32 maximumDepth,
        Int32 maximumNodes)
    {
        var visited = 0;
        return Find(element, 0);

        Boolean Find(IntPtr current, Int32 depth)
        {
            if (depth >= maximumDepth || visited >= maximumNodes)
            {
                return false;
            }

            var found = false;
            MacAccessibilityNative.ForEachElement(
                current,
                "AXChildren",
                child =>
                {
                    if (found || visited++ >= maximumNodes)
                    {
                        return;
                    }

                    var labels = new[]
                    {
                        MacAccessibilityNative.ReadString(child, "AXTitle"),
                        MacAccessibilityNative.ReadString(child, "AXDescription"),
                        MacAccessibilityNative.ReadString(child, "AXHelp"),
                        MacAccessibilityNative.ReadString(child, "AXValue"),
                    };
                    found = candidates.Any(candidate => HasExactLabel(labels, candidate))
                        || Find(child, depth + 1);
                });
            return found;
        }
    }

    private static MacActionAttempt ToActionAttempt(MacTargetAction action)
        => action switch
        {
            MacTargetAction.Invoked => MacActionAttempt.Invoked,
            MacTargetAction.Clicked => MacActionAttempt.Clicked,
            _ => MacActionAttempt.Unavailable,
        };

    private static void TraverseModelPicker(
        IntPtr element,
        IntPtr window,
        ICollection<ModelPickerCandidate> candidates,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth || budget.NodesVisited >= MaximumNodesPerWindow)
        {
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        var identifier = MacAccessibilityNative.ReadString(element, "AXIdentifier");
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        if (enabled && visible && IsInteractiveRole(role))
        {
            var score = ModelPickerScore(role, identifier, title, description, help, value);
            if (score > 0)
            {
                candidates.Add(new ModelPickerCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value)),
                    score));
            }
        }

        MacAccessibilityNative.ForEachElement(
            element,
            "AXChildren",
            child => TraverseModelPicker(
                child,
                window,
                candidates,
                depth + 1,
                budget));
    }

    private static Int32 ModelPickerScore(
        String role,
        String identifier,
        String title,
        String description,
        String help,
        String value)
    {
        var labels = new[] { title, description, help, value };
        if (labels.Any(label => EqualsIgnoreCase(label, "Open model picker"))) return 120;
        if (labels.Any(label => EqualsIgnoreCase(label, "Select ChatGPT model"))) return 115;
        if (labels.Any(label => EqualsIgnoreCase(label, "Select model"))) return 110;
        if (labels.Any(label => EqualsIgnoreCase(label, "Model picker"))) return 105;
        if (ContainsAny(
                identifier,
                "model-picker",
                "modelpicker",
                "model-selector",
                "modelselector",
                "select-model",
                "selectmodel")) return 100;
        if (role == "AXPopUpButton" && labels.Any(label => StartsWith(label, "GPT-"))) return 60;
        return 0;
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
        var approval = result.Approval;
        MacApprovalSurface? secondaryApproval = null;
        if (budget.WasTruncated)
        {
            // Chromium appends the newest conversation and approval card near
            // the end of the AX tree. A long conversation can consume the
            // forward scan budget before that visible card is reached.
            var reverseApprovalBuilder = new WindowScanBuilder(isFocusedWindow);
            var reverseApprovalBudget = new ScanBudget();
            TraverseApprovalTargetsReverse(
                window,
                window,
                isFocusedWindow,
                reverseApprovalBuilder,
                String.Empty,
                false,
                0,
                reverseApprovalBudget);
            var reverseApproval = reverseApprovalBuilder.Build().Approval;
            if (reverseApproval is not null)
            {
                if (CoversApprovalRoles(reverseApproval, approval))
                {
                    approval?.Dispose();
                }
                else
                {
                    // Keep disjoint partial coverage as a second exact surface.
                    // Invocation prefers the latest-first reverse result, then
                    // falls back to the still-current forward target by role.
                    secondaryApproval = approval;
                }

                approval = reverseApproval;
            }
        }

        if (approval is not null)
        {
            approvals.Add(approval);
        }

        if (secondaryApproval is not null)
        {
            approvals.Add(secondaryApproval);
        }

        var stopTargets = result.StopTargets;
        if (budget.WasTruncated)
        {
            var reverseTargets = new List<MacAxTarget>();
            var reverseBudget = new ScanBudget();
            TraverseStopTargetsReverse(
                window,
                window,
                isFocusedWindow,
                reverseTargets,
                String.Empty,
                false,
                0,
                reverseBudget);
            if (reverseTargets.Count > 0)
            {
                foreach (var stop in stopTargets)
                {
                    stop.Dispose();
                }

                stopTargets = reverseTargets;
            }
        }

        foreach (var stop in stopTargets)
        {
            stops.Add(stop);
        }
    }

    private static Boolean CoversApprovalRoles(
        MacApprovalSurface candidate,
        MacApprovalSurface? other)
        => other is null
            || (other.Approve is null || candidate.Approve is not null)
            && (other.Persistent is null || candidate.Persistent is not null)
            && (other.Deny is null || candidate.Deny is not null)
            && (other.Options is null || candidate.Options is not null);

    private static void TraverseApprovalTargetsReverse(
        IntPtr element,
        IntPtr window,
        Boolean isFocusedWindow,
        WindowScanBuilder builder,
        String ancestorIdentity,
        Boolean inApprovalSurface,
        Int32 depth,
        ScanBudget budget)
    {
        if (depth > MaximumDepth || budget.NodesVisited >= MaximumNodesPerWindow)
        {
            return;
        }

        budget.NodesVisited++;
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        if (role == "AXMenuBar")
        {
            return;
        }

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
        if (currentApprovalSurface)
        {
            builder.HasApprovalSurface = true;
        }

        if (ApprovalLabels.IsStatus(title)
            || ApprovalLabels.IsStatus(description)
            || ApprovalLabels.IsStatus(help)
            || ApprovalLabels.IsStatus(value))
        {
            builder.HasApprovalStatus = true;
        }

        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        if (enabled && visible && IsInteractiveRole(role))
        {
            var approvalRole = ClassifyApprovalControl(
                element,
                role,
                identity,
                title,
                description,
                help,
                value,
                out var descendantLabel);
            if (approvalRole.HasValue)
            {
                builder.AddApprovalCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        isFocusedWindow,
                        FirstText(title, description, help, value, descendantLabel)),
                    approvalRole,
                    currentApprovalSurface,
                    builder.NextReverseDocumentOrder());
            }
        }

        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseApprovalTargetsReverse(
                child,
                window,
                isFocusedWindow,
                builder,
                context,
                currentApprovalSurface,
                depth + 1,
                budget));
    }

    private static void TraverseStopTargetsReverse(
        IntPtr element,
        IntPtr window,
        Boolean isFocusedWindow,
        ICollection<MacAxTarget> stops,
        String ancestorIdentity,
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
        var currentComposer = inComposer || ContainsAny(
            identity,
            "composer",
            "prompt-textarea",
            "chat-input",
            "message-input");
        var enabled = MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        if (enabled && visible && IsInteractiveRole(role))
        {
            var stableStopIdentity = ContainsAny(
                identity,
                "composer-stop",
                "stop-composer",
                "stop-response",
                "stop-generating",
                "stop-turn",
                "interrupt-turn",
                "interrupt-response");
            var exactStopLabel = StopLabels.IsStop(title)
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
            var descendantStopLabel = !stableStopIdentity
                && !exactStopLabel
                && role == "AXButton"
                && !excludedContext
                && (currentComposer
                    || MacAccessibilityNative.IsNearWindowBottomComposer(element, window))
                && HasStopDescendantLabel(element, 3, 24);
            if (stableStopIdentity
                || (exactStopLabel && (currentComposer || !excludedContext))
                || descendantStopLabel)
            {
                stops.Add(new MacAxTarget(
                    element,
                    window,
                    role,
                    isFocusedWindow,
                    FirstText(title, description, help, value)));
            }
        }

        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseStopTargetsReverse(
                child,
                window,
                isFocusedWindow,
                stops,
                context,
                currentComposer,
                depth + 1,
                budget));
    }

    private static Boolean HasStopDescendantLabel(
        IntPtr element,
        Int32 maximumDepth,
        Int32 maximumNodes)
    {
        var visited = 0;
        return Find(element, 0);

        Boolean Find(IntPtr current, Int32 depth)
        {
            if (depth >= maximumDepth || visited >= maximumNodes)
            {
                return false;
            }

            var found = false;
            MacAccessibilityNative.ForEachElementReverse(
                current,
                "AXChildren",
                child =>
                {
                    if (found || visited++ >= maximumNodes)
                    {
                        return;
                    }

                    if (IsInteractiveRole(
                            MacAccessibilityNative.ReadString(child, "AXRole")))
                    {
                        return;
                    }

                    found = StopLabels.IsStop(
                            MacAccessibilityNative.ReadString(child, "AXTitle"))
                        || StopLabels.IsStop(
                            MacAccessibilityNative.ReadString(child, "AXDescription"))
                        || StopLabels.IsStop(
                            MacAccessibilityNative.ReadString(child, "AXHelp"))
                        || StopLabels.IsStop(
                            MacAccessibilityNative.ReadString(child, "AXValue"))
                        || Find(child, depth + 1);
                });
            return found;
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
        if (depth > MaximumDepth)
        {
            return;
        }

        if (budget.NodesVisited >= MaximumNodesPerWindow)
        {
            budget.WasTruncated = true;
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
            var approvalRole = ClassifyApprovalControl(
                element,
                role,
                identity,
                title,
                description,
                help,
                value,
                out var descendantApprovalLabel);
            if (approvalRole.HasValue || currentApprovalSurface)
            {
                builder.AddApprovalCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        isFocusedWindow,
                        FirstText(
                            title,
                            description,
                            help,
                            value,
                            descendantApprovalLabel)),
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
            var descendantStopLabel = !stableStopIdentity
                && !stopLabel
                && role == "AXButton"
                && !excludedContext
                && (currentComposer
                    || MacAccessibilityNative.IsNearWindowBottomComposer(element, window))
                && HasStopDescendantLabel(element, 3, 24);
            if (stableStopIdentity
                || (stopLabel && (currentComposer || !excludedContext))
                || descendantStopLabel)
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

    private static ApprovalRole? ClassifyApprovalControl(
        IntPtr element,
        String role,
        String identity,
        String title,
        String description,
        String help,
        String value,
        out String descendantLabel)
    {
        descendantLabel = String.Empty;
        var directRole = ClassifyApprovalRole(identity, title, description, help, value);
        if (directRole.HasValue || !CanUseDescendantApprovalLabel(role))
        {
            return directRole;
        }

        return FindDescendantApprovalRole(element, 3, 24, out descendantLabel);
    }

    private static ApprovalRole? FindDescendantApprovalRole(
        IntPtr element,
        Int32 maximumDepth,
        Int32 maximumNodes,
        out String accessibleLabel)
    {
        var visited = 0;
        var roles = new HashSet<ApprovalRole>();
        var foundLabel = String.Empty;
        Find(element, 0);
        accessibleLabel = roles.Count == 1 ? foundLabel : String.Empty;
        return roles.Count == 1 ? roles.Single() : null;

        void Find(IntPtr current, Int32 depth)
        {
            if (depth >= maximumDepth || visited >= maximumNodes)
            {
                return;
            }

            MacAccessibilityNative.ForEachElementReverse(
                current,
                "AXChildren",
                child =>
                {
                    if (visited++ >= maximumNodes)
                    {
                        return;
                    }

                    var childRole = MacAccessibilityNative.ReadString(child, "AXRole");
                    if (IsInteractiveRole(childRole))
                    {
                        // Do not promote an outer split/menu control from the
                        // labels of a different clickable descendant.
                        return;
                    }

                    var labels = new[]
                    {
                        MacAccessibilityNative.ReadString(child, "AXTitle"),
                        MacAccessibilityNative.ReadString(child, "AXDescription"),
                        MacAccessibilityNative.ReadString(child, "AXHelp"),
                        MacAccessibilityNative.ReadString(child, "AXValue"),
                    };
                    var role = ClassifyApprovalRole(labels);
                    if (role.HasValue)
                    {
                        roles.Add(role.Value);
                        foundLabel = roles.Count == 1
                            ? FirstText(labels)
                            : String.Empty;
                    }

                    Find(child, depth + 1);
                });
        }
    }

    private static Boolean CanUseDescendantApprovalLabel(String role)
        => role is "AXButton"
            or "AXMenuButton"
            or "AXMenuItem"
            or "AXPopUpButton";

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

    private static Boolean EqualsIgnoreCase(String value, String candidate)
        => value.Equals(candidate, StringComparison.OrdinalIgnoreCase);

    private static Boolean Contains(String value, String candidate)
        => value.Contains(candidate, StringComparison.OrdinalIgnoreCase);

    private static Boolean StartsWith(String value, String candidate)
        => value.StartsWith(candidate, StringComparison.OrdinalIgnoreCase);

    private sealed class ScanBudget
    {
        public Int32 NodesVisited { get; set; }

        public Boolean WasTruncated { get; set; }
    }

    private sealed record ApprovalCandidate(
        MacAxTarget Target,
        ApprovalRole? Role,
        Boolean InSurface,
        Int32 DocumentOrder);

    private sealed record ModelPickerCandidate(MacAxTarget Target, Int32 Score);

    private sealed record ModeCommandCandidate(MacAxTarget Target, Int32 Score);

    private sealed class ModeCommandSearchState
    {
        public Boolean FoundDialog { get; set; }
    }

    private sealed class ModeCommandScan : IDisposable
    {
        public ModeCommandScan(Boolean hasExactDialog, MacAxTarget? target)
        {
            this.HasExactDialog = hasExactDialog;
            this.Target = target;
        }

        public Boolean HasExactDialog { get; }

        public MacAxTarget? Target { get; }

        public void Dispose() => this.Target?.Dispose();
    }

    private enum ModeCandidateKind
    {
        ProductTrigger,
        ProductItem,
        ComposerButton,
    }

    private sealed record ModeCandidate(
        MacAxTarget Target,
        ModeCandidateKind Kind,
        Int32 Score,
        MacProductMode? ProductMode,
        MacDesktopMode? DesktopMode,
        Boolean Selected,
        Boolean Enabled);

    private sealed class ModeControlScan : IDisposable
    {
        public ModeControlScan(
            MacProductMode? productMode,
            MacDesktopMode? composerMode,
            MacAxTarget? trigger,
            MacAxTarget? productItem,
            MacAxTarget? composerButton)
        {
            this.ProductMode = productMode;
            this.ComposerMode = composerMode;
            this.Trigger = trigger;
            this.ProductItem = productItem;
            this.ComposerButton = composerButton;
        }

        public MacProductMode? ProductMode { get; }

        public MacDesktopMode? ComposerMode { get; }

        public MacAxTarget? Trigger { get; }

        public MacAxTarget? ProductItem { get; }

        public MacAxTarget? ComposerButton { get; }

        public void Dispose()
        {
            this.Trigger?.Dispose();
            this.ProductItem?.Dispose();
            this.ComposerButton?.Dispose();
        }
    }

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

        public Int32 NextReverseDocumentOrder() => -this.documentOrder++;

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
