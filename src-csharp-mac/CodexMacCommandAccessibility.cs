using System.Diagnostics;

namespace Loupedeck.CodexDesktopPlugin;

internal static partial class CodexMacAccessibility
{
    private static readonly Object CommandPaletteGate = new();
    private static readonly TimeSpan CommandTreeScanTimeout =
        TimeSpan.FromMilliseconds(900);
    private static readonly TimeSpan DesktopCommandTransitionTimeout =
        TimeSpan.FromMilliseconds(3200);

    public static MacActionAttempt TryInvokeDesktopCommand(
        MacDesktopCommand command,
        Boolean promptForPermission = false,
        Action<String>? trace = null)
    {
        if (!MacAccessibilityNative.IsTrusted(promptForPermission))
        {
            trace?.Invoke("Accessibility permission is unavailable.");
            return MacActionAttempt.PermissionRequired;
        }

        if (command == MacDesktopCommand.OpenReviewTab)
        {
            var review = ScanFocusedSurface(CommandSurface.SelectedReviewTab);
            if (review.ScannedFocusedWindow
                && review.IsPresent
                && review.HasReviewControls)
            {
                trace?.Invoke("The Review tab is already selected and visible.");
                return MacActionAttempt.AlreadyActive;
            }
        }

        if (command == MacDesktopCommand.OpenTerminal)
        {
            var terminal = ScanTerminalSurface();
            if (terminal.ScannedFocusedWindow && terminal.IsVisible)
            {
                trace?.Invoke("The integrated terminal is already visible.");
                return MacActionAttempt.AlreadyActive;
            }

            trace?.Invoke("Looking for the app's native Terminal menu item.");
            var nativeAttempt = TryInvokeNativeMenuCommand(command);
            if (IsSuccessfulAction(nativeAttempt))
            {
                if (WaitForTerminalSurface(DesktopCommandTransitionTimeout))
                {
                    trace?.Invoke("The app opened an accessible integrated terminal.");
                    return nativeAttempt;
                }

                trace?.Invoke(
                    "The native Terminal item was invoked, but no terminal appeared in this app context.");
                return MacActionAttempt.Unavailable;
            }

            trace?.Invoke(
                "The native Terminal item was unavailable; trying the app command menu.");
        }

        var attempt = TryInvokeCommandPaletteCommand(
            command,
            TimeSpan.FromMilliseconds(2400),
            trace);
        if (!IsSuccessfulAction(attempt))
        {
            return attempt;
        }

        if (command == MacDesktopCommand.OpenReviewTab)
        {
            if (WaitForSelectedReviewTab(DesktopCommandTransitionTimeout))
            {
                trace?.Invoke("Verified the selected Review tab.");
                return attempt;
            }

            trace?.Invoke(
                "The Review command was delivered, but no selected Review tab appeared.");
            return MacActionAttempt.Unavailable;
        }

        if (command == MacDesktopCommand.OpenTerminal)
        {
            return WaitForTerminalSurface(DesktopCommandTransitionTimeout)
                ? attempt
                : MacActionAttempt.Unavailable;
        }

        return attempt;
    }

    private static MacActionAttempt TryInvokeCommandPaletteCommand(
        MacDesktopCommand command,
        TimeSpan timeout,
        Action<String>? trace = null)
    {
        lock (CommandPaletteGate)
        {
            return TryInvokeCommandPaletteCommandLocked(command, timeout, trace);
        }
    }

    private static MacActionAttempt TryInvokeCommandPaletteCommandLocked(
        MacDesktopCommand command,
        TimeSpan timeout,
        Action<String>? trace)
    {
        var spec = MacDesktopCommandCatalog.Get(command);
        var commandMenuIsOpen = false;
        using (var existing = ScanCommandPalette(spec))
        {
            commandMenuIsOpen = existing.HasCommandSurface;
            if (existing.Target is not null)
            {
                if (existing.Target.HasUsableFrame())
                {
                    trace?.Invoke($"Found '{spec.Name}' in an already open command menu.");
                    return InvokeCommandPaletteTarget(spec, existing.Target);
                }

                _ = existing.Target.TryScrollToVisible();
            }
            else if (existing.FilterInput is not null
                && existing.FilterInput.TrySetValue(String.Empty))
            {
                trace?.Invoke("Cleared the existing command-menu filter.");
                Thread.Sleep(80);
            }
            else if (existing.ScrollTarget is not null)
            {
                _ = existing.ScrollTarget.TryScrollToVisible();
            }
        }

        if (!commandMenuIsOpen)
        {
            var openAttempt = TryInvokeNativeMenuCommand(MacDesktopCommand.OpenCommandMenu);
            if (!IsSuccessfulAction(openAttempt))
            {
                trace?.Invoke(
                    "The native Open command menu item was unavailable; posting Command-K to the ChatGPT process.");
                openAttempt = TryPostFallbackKey(MacDesktopCommand.OpenCommandMenu);
            }

            if (!IsSuccessfulAction(openAttempt))
            {
                trace?.Invoke("Could not open the app command menu.");
                return openAttempt;
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var foundSurface = commandMenuIsOpen;
        var scrolledGroup = false;
        var scrolledRow = false;
        do
        {
            using var scan = ScanCommandPalette(spec);
            foundSurface |= scan.HasCommandSurface;
            if (scan.Target is not null)
            {
                if (scan.Target.HasUsableFrame())
                {
                    trace?.Invoke($"Found the exact '{spec.Name}' command row.");
                    return InvokeCommandPaletteTarget(spec, scan.Target);
                }

                if (!scrolledRow && scan.Target.TryScrollToVisible())
                {
                    scrolledRow = true;
                    trace?.Invoke($"Scrolled the exact '{spec.Name}' row into view.");
                    Thread.Sleep(80);
                    continue;
                }
            }

            if (!scrolledGroup
                && scan.ScrollTarget is not null
                && scan.ScrollTarget.TryScrollToVisible())
            {
                scrolledGroup = true;
                trace?.Invoke($"Scrolled the '{spec.Name}' command group into view.");
                Thread.Sleep(80);
                continue;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        trace?.Invoke(
            foundSurface
                ? $"The command menu opened, but '{spec.Name}' is not available in this app context."
                : "No accessible command-menu surface appeared.");
        return foundSurface
            ? MacActionAttempt.NoTarget
            : MacActionAttempt.Unavailable;
    }

    private static MacActionAttempt InvokeCommandPaletteTarget(
        MacDesktopCommandSpec spec,
        MacAxTarget target)
    {
        // Current Chromium exposes cmdk rows as AXStaticText and only a native
        // click dispatches the React handler. The caller verifies the resulting
        // Review, Terminal, or mode state; palette dismissal is not authoritative
        // on a long AX tree.
        var clickAttempt = ToActionAttempt(target.TryClick());
        if (IsSuccessfulAction(clickAttempt))
        {
            return clickAttempt;
        }

        var pressAttempt = ToActionAttempt(target.TryPressOnly());
        if (IsSuccessfulAction(pressAttempt))
        {
            return pressAttempt;
        }

        return MacActionAttempt.Unavailable;
    }

    private static CommandPaletteScan ScanCommandPalette(MacDesktopCommandSpec spec)
    {
        var candidates = new List<CommandPaletteCandidate>();
        var groups = new List<MacAxTarget>();
        var filters = new List<MacAxTarget>();
        var state = new CommandPaletteSearchState();
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
                    var focusedBudget = new ScanBudget();
                    var focusedStopwatch = Stopwatch.StartNew();
                    TraverseCommandPaletteReverse(
                        focusedWindow.Handle,
                        focusedWindow.Handle,
                        spec,
                        candidates,
                        state,
                        inDialog: false,
                        inCommandList: false,
                        commandList: IntPtr.Zero,
                        expectedGroupDepth: -1,
                        requireFocusedWindowOwnership: false,
                        0,
                        focusedBudget,
                        focusedStopwatch,
                        groups,
                        filters);
                    state.ScannedFocusedWindow |= !focusedBudget.WasTruncated;

                    if (!state.FoundSurface)
                    {
                        var applicationBudget = new ScanBudget();
                        var applicationStopwatch = Stopwatch.StartNew();
                        TraverseCommandPaletteReverse(
                            application.Handle,
                            focusedWindow.Handle,
                            spec,
                            candidates,
                            state,
                            inDialog: false,
                            inCommandList: false,
                            commandList: IntPtr.Zero,
                            expectedGroupDepth: -1,
                            requireFocusedWindowOwnership: true,
                            0,
                            applicationBudget,
                            applicationStopwatch,
                            groups,
                            filters);
                    }
                }
            }

            var ordered = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();
            var target = ordered.Length switch
            {
                0 => null,
                1 => ordered[0],
                _ when ordered[0].Score > ordered[1].Score => ordered[0],
                _ => null,
            };
            return new CommandPaletteScan(
                state.ScannedFocusedWindow,
                state.FoundSurface,
                target?.Target.Clone(),
                groups.Count == 1 ? groups[0].Clone() : null,
                filters.Count == 1 ? filters[0].Clone() : null);
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                candidate.Target.Dispose();
            }

            foreach (var group in groups)
            {
                group.Dispose();
            }

            foreach (var filter in filters)
            {
                filter.Dispose();
            }
        }
    }

    private static void TraverseCommandPaletteReverse(
        IntPtr element,
        IntPtr window,
        MacDesktopCommandSpec spec,
        ICollection<CommandPaletteCandidate> candidates,
        CommandPaletteSearchState state,
        Boolean inDialog,
        Boolean inCommandList,
        IntPtr commandList,
        Int32 expectedGroupDepth,
        Boolean requireFocusedWindowOwnership,
        Int32 depth,
        ScanBudget budget,
        Stopwatch stopwatch,
        ICollection<MacAxTarget> groups,
        ICollection<MacAxTarget> filters)
    {
        if (depth > MaximumDepth
            || budget.NodesVisited >= MaximumNodesPerWindow
            || stopwatch.Elapsed >= CommandTreeScanTimeout)
        {
            budget.WasTruncated = true;
            return;
        }

        budget.NodesVisited++;
        MacAccessibilityNative.SetMessagingTimeout(element, 0.12F);
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        if (role == "AXMenuBar")
        {
            return;
        }

        var subrole = MacAccessibilityNative.ReadString(element, "AXSubrole");
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        var title = MacAccessibilityNative.ReadString(element, "AXTitle");
        var description = MacAccessibilityNative.ReadString(element, "AXDescription");
        var help = MacAccessibilityNative.ReadString(element, "AXHelp");
        var value = MacAccessibilityNative.ReadString(element, "AXValue");
        var labels = new[] { title, description, help, value };
        var exactDialogRole = role == "AXDialog"
            || (role == "AXGroup"
                && subrole is "AXApplicationDialog" or "AXDialog");
        var startsDialog = !inDialog
            && visible
            && exactDialogRole
            && MacDesktopCommandLocalization.CommandDialogLabels.Any(
                label => HasExactLabel(labels, label))
            && (!requireFocusedWindowOwnership
                || MacAccessibilityNative.IsOwnedByWindow(element, window));
        var currentDialog = inDialog || startsDialog;
        var startsCommandList = currentDialog
            && !inCommandList
            && role is "AXList" or "AXListBox";
        var currentCommandList = inCommandList || startsCommandList;
        var currentCommandListElement = startsCommandList
            ? element
            : commandList;
        if (visible
            && currentDialog
            && role is "AXList" or "AXListBox")
        {
            state.FoundSurface = true;
        }

        if (visible
            && currentDialog
            && role == "AXComboBox"
            && MacDesktopCommandLocalization.CommandDialogLabels.Any(
                label => HasExactLabel(labels, label)))
        {
            filters.Add(new MacAxTarget(
                element,
                window,
                role,
                true,
                FirstText(title, description, help, value)));
        }

        var startsExpectedGroup = visible
            && currentDialog
            && currentCommandList
            && expectedGroupDepth < 0
            && role == "AXGroup"
            && subrole is "AXApplicationGroup" or "AXGroup" or ""
            && spec.CommandGroupLabels.Any(label => HasExactLabel(labels, label));
        var currentGroupDepth = startsExpectedGroup ? 0 : expectedGroupDepth;
        if (startsExpectedGroup)
        {
            groups.Add(new MacAxTarget(
                element,
                window,
                role,
                true,
                FirstText(title, description, help, value)));
        }

        if (visible
            && currentDialog
            && currentCommandList
            && currentGroupDepth == 1
            && MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false
            && IsCommandPaletteItemRole(role))
        {
            var exactLabel = spec.Labels.Any(label => HasExactLabel(labels, label))
                || HasExactDescendantLabel(
                    element,
                    spec.Labels,
                    4,
                    48,
                    stopwatch);
            var exactShortcut = HasShortcutHint(labels, spec.ShortcutHints)
                || HasDescendantShortcutHint(
                    element,
                    spec.ShortcutHints,
                    4,
                    48,
                    stopwatch);
            if (exactLabel)
            {
                var score = role switch
                {
                    "AXListBoxOption" => 500,
                    "AXMenuItem" => 480,
                    "AXButton" => 460,
                    "AXRow" => 440,
                    "AXStaticText" => 420,
                    _ => 400,
                };
                score += 100;

                if (exactShortcut)
                {
                    score += 60;
                }

                candidates.Add(new CommandPaletteCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value),
                        currentCommandListElement),
                    score));
            }
        }

        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseCommandPaletteReverse(
                child,
                window,
                spec,
                candidates,
                state,
                currentDialog,
                currentCommandList,
                currentCommandListElement,
                currentGroupDepth >= 0 ? currentGroupDepth + 1 : -1,
                requireFocusedWindowOwnership,
                depth + 1,
                budget,
                stopwatch,
                groups,
                filters));
    }

    private static Boolean IsCommandPaletteItemRole(String role)
        => role is "AXListBoxOption"
            or "AXMenuItem"
            or "AXButton"
            or "AXRow"
            or "AXStaticText";

    private static Boolean HasShortcutHint(
        IEnumerable<String> values,
        IReadOnlySet<String> shortcutHints)
    {
        if (shortcutHints.Count == 0)
        {
            return false;
        }

        var normalizedHints = shortcutHints
            .Select(NormalizeShortcut)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return values
            .Select(NormalizeShortcut)
            .Any(normalizedHints.Contains);
    }

    private static Boolean HasDescendantShortcutHint(
        IntPtr element,
        IReadOnlySet<String> shortcutHints,
        Int32 maximumDepth,
        Int32 maximumNodes,
        Stopwatch stopwatch)
    {
        if (shortcutHints.Count == 0)
        {
            return false;
        }

        var visited = 0;
        return Find(element, 0);

        Boolean Find(IntPtr current, Int32 depth)
        {
            if (depth >= maximumDepth
                || visited >= maximumNodes
                || stopwatch.Elapsed >= CommandTreeScanTimeout)
            {
                return false;
            }

            MacAccessibilityNative.SetMessagingTimeout(current, 0.12F);
            var found = false;
            MacAccessibilityNative.ForEachElement(
                current,
                "AXChildren",
                child =>
                {
                    if (found
                        || visited++ >= maximumNodes
                        || stopwatch.Elapsed >= CommandTreeScanTimeout)
                    {
                        return;
                    }

                    MacAccessibilityNative.SetMessagingTimeout(child, 0.12F);
                    var labels = new[]
                    {
                        MacAccessibilityNative.ReadString(child, "AXTitle"),
                        MacAccessibilityNative.ReadString(child, "AXDescription"),
                        MacAccessibilityNative.ReadString(child, "AXHelp"),
                        MacAccessibilityNative.ReadString(child, "AXValue"),
                    };
                    found = HasShortcutHint(labels, shortcutHints)
                        || Find(child, depth + 1);
                });
            return found;
        }
    }

    private static String NormalizeShortcut(String value)
    {
        var normalized = value
            .Trim()
            .Replace("Control", "Ctrl", StringComparison.OrdinalIgnoreCase)
            .Replace("Command", "Cmd", StringComparison.OrdinalIgnoreCase)
            .Replace("⌃", "Ctrl", StringComparison.Ordinal)
            .Replace("⇧", "Shift", StringComparison.Ordinal)
            .Replace("⌘", "Cmd", StringComparison.Ordinal)
            .Replace("⌥", "Alt", StringComparison.Ordinal);
        return new String(normalized
            .Where(character => !Char.IsWhiteSpace(character)
                && character is not '+' and not '-' and not '·')
            .ToArray());
    }

    private static Boolean WaitForTerminalSurface(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            var scan = ScanFocusedSurface(CommandSurface.Terminal);
            if (scan.ScannedFocusedWindow && scan.IsPresent)
            {
                return true;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    private static Boolean WaitForSelectedReviewTab(TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            var scan = ScanFocusedSurface(CommandSurface.SelectedReviewTab);
            if (scan.ScannedFocusedWindow && scan.IsPresent)
            {
                return true;
            }

            Thread.Sleep(45);
        }
        while (stopwatch.Elapsed < timeout);

        return false;
    }

    private static FocusedSurfaceScan ScanTerminalSurface()
        => ScanFocusedSurface(CommandSurface.Terminal);

    private static FocusedSurfaceScan ScanFocusedSurface(CommandSurface surface)
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
                var state = new FocusedSurfaceSearchState();
                var budget = new ScanBudget();
                var stopwatch = Stopwatch.StartNew();
                TraverseFocusedSurfaceReverse(
                    focusedWindow.Handle,
                    focusedWindow.Handle,
                    surface,
                    state,
                    inVisibleTabPanel: false,
                    inVisibleReviewPanel: false,
                    0,
                    budget,
                    stopwatch);
                return new FocusedSurfaceScan(
                    state.IsPresent || !budget.WasTruncated,
                    state.IsPresent,
                    state.HasReviewControls);
            }
        }

        return new FocusedSurfaceScan(false, false, false);
    }

    private static void TraverseFocusedSurfaceReverse(
        IntPtr element,
        IntPtr window,
        CommandSurface surface,
        FocusedSurfaceSearchState state,
        Boolean inVisibleTabPanel,
        Boolean inVisibleReviewPanel,
        Int32 depth,
        ScanBudget budget,
        Stopwatch stopwatch)
    {
        if (state.IsPresent)
        {
            return;
        }

        if (depth > MaximumDepth
            || budget.NodesVisited >= MaximumNodesPerWindow
            || stopwatch.Elapsed >= CommandTreeScanTimeout)
        {
            budget.WasTruncated = true;
            return;
        }

        budget.NodesVisited++;
        MacAccessibilityNative.SetMessagingTimeout(element, 0.12F);
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        if (role == "AXMenuBar")
        {
            return;
        }

        var subrole = MacAccessibilityNative.ReadString(element, "AXSubrole");
        var labels = new[]
        {
            MacAccessibilityNative.ReadString(element, "AXTitle"),
            MacAccessibilityNative.ReadString(element, "AXDescription"),
            MacAccessibilityNative.ReadString(element, "AXHelp"),
            MacAccessibilityNative.ReadString(element, "AXValue"),
        };
        var isTabPanel = role == "AXGroup" && subrole == "AXTabPanel";
        var isDisplayedTabPanel = isTabPanel
            && MacAccessibilityNative.HasUsableFrame(element, 40, 20)
            && MacAccessibilityNative.IsElementCenterInside(element, window);
        var currentVisibleTabPanel = isTabPanel
            ? isDisplayedTabPanel
            : inVisibleTabPanel;
        var isDisplayedReviewPanel = isDisplayedTabPanel
            && MacDesktopCommandLocalization.ReviewTabLabels.Any(
                label => HasExactLabel(labels, label));
        var currentVisibleReviewPanel = isTabPanel
            ? isDisplayedReviewPanel
            : inVisibleReviewPanel;
        var visible = MacAccessibilityNative.ReadBoolean(element, "AXVisible") != false
            && MacAccessibilityNative.ReadBoolean(element, "AXHidden") != true;
        if (visible)
        {
            if (surface == CommandSurface.Terminal)
            {
                state.TerminalIsPresent = currentVisibleTabPanel
                    && role is "AXTextArea" or "AXTextField"
                    && HasExactLabel(labels, "Terminal input")
                    && MacAccessibilityNative.HasUsableFrame(element, 4, 8)
                    && MacAccessibilityNative.IsElementCenterInside(element, window);
            }
            else if (surface == CommandSurface.SelectedReviewTab)
            {
                state.HasVisibleReviewPanel |= isDisplayedReviewPanel;
                state.HasSelectedReviewTab |= IsTabRole(role, subrole)
                    && MacDesktopCommandLocalization.ReviewTabLabels.Any(
                        label => HasExactLabel(labels, label))
                    && IsSelected(element)
                    && MacAccessibilityNative.HasUsableFrame(element, 20, 10)
                    && MacAccessibilityNative.IsElementCenterInside(element, window);
                state.HasReviewControls |= currentVisibleReviewPanel
                    && role == "AXGroup"
                    && subrole == "AXApplicationGroup"
                    && MacDesktopCommandLocalization.IsReviewControlsGroup(labels)
                    && MacAccessibilityNative.HasUsableFrame(element, 20, 10)
                    && MacAccessibilityNative.IsElementCenterInside(element, window);
            }
        }

        if (state.IsPresent)
        {
            return;
        }

        MacAccessibilityNative.ForEachElementReverse(
            element,
            "AXChildren",
            child => TraverseFocusedSurfaceReverse(
                child,
                window,
                surface,
                state,
                currentVisibleTabPanel,
                currentVisibleReviewPanel,
                depth + 1,
                budget,
                stopwatch));
    }

    private static Boolean IsTabRole(String role, String subrole)
        => role is "AXRadioButton" or "AXTab"
            || subrole == "AXTabButton";

    private static Boolean IsSelected(IntPtr element)
        => MacAccessibilityNative.ReadBoolean(element, "AXSelected") == true
            || MacAccessibilityNative.ReadBoolean(element, "AXValue") == true
            || MacAccessibilityNative.ReadInteger(element, "AXValue") == 1;

    private static MacActionAttempt TryInvokeNativeMenuCommand(MacDesktopCommand command)
    {
        var spec = MacDesktopCommandCatalog.Get(command);
        var candidates = new List<NativeMenuCandidate>();
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
                    if (!MacAccessibilityNative.TryCopyElement(
                            application.Handle,
                            "AXMenuBar",
                            out var menuBar)
                        || menuBar is null)
                    {
                        continue;
                    }

                    using (menuBar)
                    {
                        var budget = new ScanBudget();
                        var menuStopwatch = Stopwatch.StartNew();
                        TraverseNativeMenu(
                            menuBar.Handle,
                            focusedWindow.Handle,
                            spec,
                            candidates,
                            0,
                            budget,
                            menuStopwatch);
                    }
                }
            }

            var ordered = candidates
                .OrderByDescending(candidate => candidate.Score)
                .ToArray();
            var target = ordered.Length switch
            {
                0 => null,
                1 => ordered[0],
                _ when ordered[0].Score > ordered[1].Score => ordered[0],
                _ => null,
            };
            return target is null
                ? MacActionAttempt.NoTarget
                : ToActionAttempt(target.Target.TryPressOnce());
        }
        finally
        {
            foreach (var candidate in candidates)
            {
                candidate.Target.Dispose();
            }
        }
    }

    private static void TraverseNativeMenu(
        IntPtr element,
        IntPtr window,
        MacDesktopCommandSpec spec,
        ICollection<NativeMenuCandidate> candidates,
        Int32 depth,
        ScanBudget budget,
        Stopwatch stopwatch)
    {
        if (depth > 12
            || budget.NodesVisited >= 1200
            || stopwatch.Elapsed >= CommandTreeScanTimeout)
        {
            budget.WasTruncated = true;
            return;
        }

        budget.NodesVisited++;
        MacAccessibilityNative.SetMessagingTimeout(element, 0.12F);
        var role = MacAccessibilityNative.ReadString(element, "AXRole");
        if (role == "AXMenuItem"
            && MacAccessibilityNative.ReadBoolean(element, "AXEnabled") != false)
        {
            var title = MacAccessibilityNative.ReadString(element, "AXTitle");
            var description = MacAccessibilityNative.ReadString(element, "AXDescription");
            var help = MacAccessibilityNative.ReadString(element, "AXHelp");
            var value = MacAccessibilityNative.ReadString(element, "AXValue");
            var labels = new[] { title, description, help, value };
            var exactLabel = spec.Labels.Any(label => HasExactLabel(labels, label));
            var exactAccelerator = MatchesNativeMenuAccelerator(element, spec);
            if (exactLabel || exactAccelerator)
            {
                candidates.Add(new NativeMenuCandidate(
                    new MacAxTarget(
                        element,
                        window,
                        role,
                        true,
                        FirstText(title, description, help, value)),
                    (exactLabel ? 500 : 0) + (exactAccelerator ? 400 : 0)));
            }
        }

        MacAccessibilityNative.ForEachElement(
            element,
            "AXChildren",
            child => TraverseNativeMenu(
                child,
                window,
                spec,
                candidates,
                depth + 1,
                budget,
                stopwatch));
    }

    private static Boolean MatchesNativeMenuAccelerator(
        IntPtr element,
        MacDesktopCommandSpec spec)
    {
        if (spec.MenuCommandModifiers is null)
        {
            return false;
        }

        var modifiers = MacAccessibilityNative.ReadInteger(
            element,
            "AXMenuItemCmdModifiers");
        if (modifiers != spec.MenuCommandModifiers)
        {
            return false;
        }

        var commandCharacter = MacAccessibilityNative.ReadString(
            element,
            "AXMenuItemCmdChar");
        if (!String.IsNullOrWhiteSpace(spec.MenuCommandCharacter)
            && commandCharacter.Trim().Equals(
                spec.MenuCommandCharacter,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return spec.MenuVirtualKey.HasValue
            && MacAccessibilityNative.ReadInteger(
                element,
                "AXMenuItemCmdVirtualKey") == spec.MenuVirtualKey.Value;
    }

    private static MacActionAttempt TryPostFallbackKey(MacDesktopCommand command)
    {
        var spec = MacDesktopCommandCatalog.Get(command);
        if (!spec.FallbackKeyCode.HasValue)
        {
            return MacActionAttempt.NoTarget;
        }

        foreach (var processId in GetCodexProcessIds())
        {
            using var application = MacAccessibilityNative.CreateApplication(processId);
            if (application is not null
                && MacAccessibilityNative.ReadBoolean(application.Handle, "AXFrontmost") == true)
            {
                return MacAccessibilityNative.TryPostKeyStroke(
                    processId,
                    spec.FallbackKeyCode.Value,
                    spec.FallbackKeyFlags)
                    ? MacActionAttempt.Invoked
                    : MacActionAttempt.Unavailable;
            }
        }

        return MacActionAttempt.NoTarget;
    }

    private sealed record CommandPaletteCandidate(MacAxTarget Target, Int32 Score);

    private sealed record NativeMenuCandidate(MacAxTarget Target, Int32 Score);

    private sealed class CommandPaletteSearchState
    {
        public Boolean ScannedFocusedWindow { get; set; }

        public Boolean FoundSurface { get; set; }
    }

    private sealed class CommandPaletteScan : IDisposable
    {
        public CommandPaletteScan(
            Boolean scannedFocusedWindow,
            Boolean hasCommandSurface,
            MacAxTarget? target,
            MacAxTarget? scrollTarget,
            MacAxTarget? filterInput)
        {
            this.ScannedFocusedWindow = scannedFocusedWindow;
            this.HasCommandSurface = hasCommandSurface;
            this.Target = target;
            this.ScrollTarget = scrollTarget;
            this.FilterInput = filterInput;
        }

        public Boolean ScannedFocusedWindow { get; }

        public Boolean HasCommandSurface { get; }

        public MacAxTarget? Target { get; }

        public MacAxTarget? ScrollTarget { get; }

        public MacAxTarget? FilterInput { get; }

        public void Dispose()
        {
            this.Target?.Dispose();
            this.ScrollTarget?.Dispose();
            this.FilterInput?.Dispose();
        }
    }

    private enum CommandSurface
    {
        Terminal,
        SelectedReviewTab,
    }

    private sealed class FocusedSurfaceSearchState
    {
        public Boolean TerminalIsPresent { get; set; }

        public Boolean HasSelectedReviewTab { get; set; }

        public Boolean HasVisibleReviewPanel { get; set; }

        public Boolean HasReviewControls { get; set; }

        public Boolean IsPresent
            => this.TerminalIsPresent
                || (this.HasSelectedReviewTab && this.HasVisibleReviewPanel);
    }

    private readonly record struct FocusedSurfaceScan(
        Boolean ScannedFocusedWindow,
        Boolean IsVisible,
        Boolean HasReviewControls)
    {
        public Boolean IsPresent => this.IsVisible;
    }
}
