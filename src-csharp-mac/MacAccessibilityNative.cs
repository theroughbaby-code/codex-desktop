using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Loupedeck.CodexDesktopPlugin;

internal static class MacAccessibilityNative
{
    private const String ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const String CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const UInt32 Utf8Encoding = 0x08000100;
    private const Int32 AxUnavailable = Int32.MinValue;
    private const Int32 AxSuccess = 0;
    private const Int32 AxCannotComplete = -25204;
    private const UInt32 AxValueCgPoint = 1;
    private const UInt32 AxValueCgSize = 2;
    private const Int32 CfNumberSInt64Type = 4;
    private const UInt32 CgHidEventTap = 0;
    private const UInt32 CgEventLeftMouseDown = 1;
    private const UInt32 CgEventLeftMouseUp = 2;
    private const UInt32 CgMouseButtonLeft = 0;
    private const Int32 AccessibilityProbeMaximumDepth = 12;
    private const Int32 AccessibilityProbeMaximumNodes = 128;
    private const Int32 AccessibilityProbeReadyNodeCount = 20;
    private const Int32 AccessibilityProbeReadyContentRoleCount = 3;
    private const Int32 MaximumEnhancedAccessibilityRequests = 3;
    private static readonly TimeSpan ManualAccessibilitySettleTime =
        TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan EnhancedAccessibilitySettleTime =
        TimeSpan.FromMilliseconds(3000);
    private static readonly TimeSpan AccessibilityProbeInterval =
        TimeSpan.FromMilliseconds(85);
    private static readonly TimeSpan AccessibilityQuickProbeTime =
        TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan AccessibilityRetryCooldown =
        TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AccessibilityReprobeInterval =
        TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<String, IntPtr> NativeStrings =
        new(StringComparer.Ordinal);
    private static readonly Lazy<IntPtr> ApplicationServicesHandle =
        new(() => NativeLibrary.Load(ApplicationServices));
    private static readonly Lazy<IntPtr> CoreFoundationHandle =
        new(() => NativeLibrary.Load(CoreFoundation));
    private static readonly ConcurrentDictionary<ProcessIdentity, AccessibilityBootstrapState>
        AccessibilityBootstrapStates = new();
    private static String accessibilityBootstrapDiagnostic =
        "AX bootstrap has not run for this plugin process.";

    public static Boolean IsTrusted(Boolean prompt)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            if (!prompt)
            {
                return AXIsProcessTrusted();
            }

            var promptKey = ReadGlobalReference(ApplicationServicesHandle.Value, "kAXTrustedCheckOptionPrompt");
            var trueValue = ReadGlobalReference(CoreFoundationHandle.Value, "kCFBooleanTrue");
            if (promptKey == IntPtr.Zero || trueValue == IntPtr.Zero)
            {
                return AXIsProcessTrusted();
            }

            var options = CFDictionaryCreate(
                IntPtr.Zero,
                new[] { promptKey },
                new[] { trueValue },
                1,
                IntPtr.Zero,
                IntPtr.Zero);
            if (options == IntPtr.Zero)
            {
                return AXIsProcessTrusted();
            }

            try
            {
                return AXIsProcessTrustedWithOptions(options);
            }
            finally
            {
                CFRelease(options);
            }
        }
        catch
        {
            return false;
        }
    }

    public static MacAxElement? CreateApplication(Int32 processId)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = AXUIElementCreateApplication(processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            _ = AXUIElementSetMessagingTimeout(handle, 0.35F);
            EnsureAccessibilityTree(processId, handle);
            var application = new MacAxElement(handle);
            handle = IntPtr.Zero;
            return application;
        }
        catch
        {
            try
            {
                Release(handle);
            }
            catch
            {
            }

            return null;
        }
    }

    public static String AccessibilityBootstrapDiagnostic
        => Volatile.Read(ref accessibilityBootstrapDiagnostic);

    public static MacAxElement RetainElement(IntPtr element)
        => new(CFRetain(element));

    public static Boolean TryCopyElement(
        IntPtr element,
        String attribute,
        out MacAxElement? value)
    {
        value = null;
        if (!TryCopyAttributeValue(element, attribute, out var nativeValue))
        {
            return false;
        }

        if (CFGetTypeID(nativeValue) != AXUIElementGetTypeID())
        {
            CFRelease(nativeValue);
            return false;
        }

        value = new MacAxElement(nativeValue);
        return true;
    }

    public static void ForEachElement(IntPtr element, String attribute, Action<IntPtr> visitor)
        => ForEachElement(element, attribute, visitor, reverse: false);

    public static void ForEachElementReverse(
        IntPtr element,
        String attribute,
        Action<IntPtr> visitor)
        => ForEachElement(element, attribute, visitor, reverse: true);

    private static void ForEachElement(
        IntPtr element,
        String attribute,
        Action<IntPtr> visitor,
        Boolean reverse)
    {
        if (!TryCopyAttributeValue(element, attribute, out var value))
        {
            return;
        }

        try
        {
            if (CFGetTypeID(value) != CFArrayGetTypeID())
            {
                return;
            }

            var count = CFArrayGetCount(value).ToInt64();
            var first = reverse ? count - 1 : 0;
            var last = reverse ? -1 : count;
            var step = reverse ? -1 : 1;
            for (var index = first; index != last; index += step)
            {
                var child = CFArrayGetValueAtIndex(value, new IntPtr(index));
                if (child != IntPtr.Zero && CFGetTypeID(child) == AXUIElementGetTypeID())
                {
                    visitor(child);
                }
            }
        }
        finally
        {
            CFRelease(value);
        }
    }

    public static String ReadString(IntPtr element, String attribute)
    {
        if (!TryCopyAttributeValue(element, attribute, out var value))
        {
            return String.Empty;
        }

        try
        {
            return CFGetTypeID(value) == CFStringGetTypeID()
                ? ReadNativeString(value)
                : String.Empty;
        }
        finally
        {
            CFRelease(value);
        }
    }

    public static Boolean? ReadBoolean(IntPtr element, String attribute)
    {
        if (!TryCopyAttributeValue(element, attribute, out var value))
        {
            return null;
        }

        try
        {
            return CFGetTypeID(value) == CFBooleanGetTypeID()
                ? CFBooleanGetValue(value)
                : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    public static Int64? ReadInteger(IntPtr element, String attribute)
    {
        if (!TryCopyAttributeValue(element, attribute, out var value))
        {
            return null;
        }

        try
        {
            if (CFGetTypeID(value) != CFNumberGetTypeID())
            {
                return null;
            }

            return CFNumberGetValue(value, CfNumberSInt64Type, out var number)
                ? number
                : null;
        }
        finally
        {
            CFRelease(value);
        }
    }

    public static Boolean IsSameElement(IntPtr left, IntPtr right)
        => left != IntPtr.Zero && right != IntPtr.Zero && CFEqual(left, right);

    public static Boolean IsFrontmostFocusedWindow(IntPtr element, IntPtr expectedWindow)
    {
        if (element == IntPtr.Zero
            || expectedWindow == IntPtr.Zero
            || AXUIElementGetPid(element, out var processId) != AxSuccess
            || processId <= 0)
        {
            return false;
        }

        using var application = CreateApplication(processId);
        if (application is null
            || ReadBoolean(application.Handle, "AXFrontmost") != true
            || !TryCopyElement(application.Handle, "AXFocusedWindow", out var focusedWindow)
            || focusedWindow is null)
        {
            return false;
        }

        using (focusedWindow)
        {
            return IsSameElement(expectedWindow, focusedWindow.Handle);
        }
    }

    public static IReadOnlySet<String> ReadActionNames(IntPtr element)
    {
        var actions = new HashSet<String>(StringComparer.Ordinal);
        IntPtr nativeActions = IntPtr.Zero;
        try
        {
            if (AXUIElementCopyActionNames(element, out nativeActions) != AxSuccess
                || nativeActions == IntPtr.Zero
                || CFGetTypeID(nativeActions) != CFArrayGetTypeID())
            {
                return actions;
            }

            var count = CFArrayGetCount(nativeActions);
            for (IntPtr index = 0; index.ToInt64() < count.ToInt64(); index += 1)
            {
                var action = CFArrayGetValueAtIndex(nativeActions, index);
                if (action != IntPtr.Zero && CFGetTypeID(action) == CFStringGetTypeID())
                {
                    var name = ReadNativeString(action);
                    if (name.Length > 0)
                    {
                        actions.Add(name);
                    }
                }
            }
        }
        catch
        {
        }
        finally
        {
            if (nativeActions != IntPtr.Zero)
            {
                CFRelease(nativeActions);
            }
        }

        return actions;
    }

    public static Boolean TryPerformAdvertisedAction(IntPtr element, String action)
    {
        // Electron may return AX success for unsupported actions. Trust the
        // advertised action list first so a no-op is never reported as invoked.
        if (!ReadActionNames(element).Contains(action))
        {
            return false;
        }

        try
        {
            var result = AXUIElementPerformAction(element, NativeString(action));
            if (result == AxCannotComplete)
            {
                Thread.Sleep(35);
                result = AXUIElementPerformAction(element, NativeString(action));
            }

            return result == AxSuccess;
        }
        catch
        {
            return false;
        }
    }

    public static Boolean TrySetTrue(IntPtr element, String attribute)
        => SetTrue(element, attribute) == AxSuccess;

    public static Boolean TryClickCenter(IntPtr element)
    {
        if (!TryReadPoint(element, "AXPosition", out var position)
            || !TryReadSize(element, "AXSize", out var size)
            || !IsFinite(position.X)
            || !IsFinite(position.Y)
            || !IsFinite(size.Width)
            || !IsFinite(size.Height)
            || size.Width <= 0
            || size.Height <= 0)
        {
            return false;
        }

        var center = new CgPoint(
            position.X + (size.Width / 2),
            position.Y + (size.Height / 2));
        if (!IsFinite(center.X) || !IsFinite(center.Y))
        {
            return false;
        }

        IntPtr locationEvent = IntPtr.Zero;
        IntPtr mouseDown = IntPtr.Zero;
        IntPtr mouseUp = IntPtr.Zero;
        var originalPosition = default(CgPoint);
        var shouldRestorePointer = false;
        try
        {
            locationEvent = CGEventCreate(IntPtr.Zero);
            if (locationEvent == IntPtr.Zero)
            {
                return false;
            }

            originalPosition = CGEventGetLocation(locationEvent);
            if (!IsFinite(originalPosition.X) || !IsFinite(originalPosition.Y))
            {
                return false;
            }

            shouldRestorePointer = true;

            mouseDown = CGEventCreateMouseEvent(
                IntPtr.Zero,
                CgEventLeftMouseDown,
                center,
                CgMouseButtonLeft);
            mouseUp = CGEventCreateMouseEvent(
                IntPtr.Zero,
                CgEventLeftMouseUp,
                center,
                CgMouseButtonLeft);
            if (mouseDown == IntPtr.Zero || mouseUp == IntPtr.Zero)
            {
                return false;
            }

            CGEventPost(CgHidEventTap, mouseDown);
            CGEventPost(CgHidEventTap, mouseUp);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (shouldRestorePointer)
            {
                _ = CGWarpMouseCursorPosition(originalPosition);
            }

            Release(mouseUp);
            Release(mouseDown);
            Release(locationEvent);
        }
    }

    public static Boolean IsNearWindowTopLeft(IntPtr element, IntPtr window)
    {
        if (!TryReadPoint(element, "AXPosition", out var elementPosition)
            || !TryReadSize(element, "AXSize", out var elementSize)
            || !TryReadPoint(window, "AXPosition", out var windowPosition)
            || !TryReadSize(window, "AXSize", out var windowSize)
            || !IsFinite(elementPosition.X)
            || !IsFinite(elementPosition.Y)
            || !IsFinite(elementSize.Width)
            || !IsFinite(elementSize.Height)
            || !IsFinite(windowPosition.X)
            || !IsFinite(windowPosition.Y)
            || !IsFinite(windowSize.Width)
            || !IsFinite(windowSize.Height)
            || elementSize.Width <= 0
            || elementSize.Height <= 0
            || elementSize.Width > 500
            || elementSize.Height > 120)
        {
            return false;
        }

        var relativeX = elementPosition.X - windowPosition.X;
        var relativeY = elementPosition.Y - windowPosition.Y;
        return relativeX >= -2
            && relativeY >= -2
            && relativeX <= Math.Min(520, windowSize.Width * 0.45)
            && relativeY <= Math.Min(190, windowSize.Height * 0.25);
    }

    public static Boolean IsNearWindowBottomComposer(IntPtr element, IntPtr window)
    {
        if (!TryReadPoint(element, "AXPosition", out var elementPosition)
            || !TryReadSize(element, "AXSize", out var elementSize)
            || !TryReadPoint(window, "AXPosition", out var windowPosition)
            || !TryReadSize(window, "AXSize", out var windowSize)
            || !IsFinite(elementPosition.X)
            || !IsFinite(elementPosition.Y)
            || !IsFinite(elementSize.Width)
            || !IsFinite(elementSize.Height)
            || !IsFinite(windowPosition.X)
            || !IsFinite(windowPosition.Y)
            || !IsFinite(windowSize.Width)
            || !IsFinite(windowSize.Height)
            || elementSize.Width <= 0
            || elementSize.Height <= 0
            || elementSize.Width > 180
            || elementSize.Height > 140
            || windowSize.Width <= 0
            || windowSize.Height <= 0)
        {
            return false;
        }

        var relativeCenterX = elementPosition.X
            + (elementSize.Width / 2)
            - windowPosition.X;
        var relativeCenterY = elementPosition.Y
            + (elementSize.Height / 2)
            - windowPosition.Y;
        return relativeCenterX >= windowSize.Width * 0.35
            && relativeCenterX <= windowSize.Width + 2
            && relativeCenterY >= windowSize.Height * 0.55
            && relativeCenterY <= windowSize.Height + 2;
    }

    public static void Release(IntPtr value)
    {
        if (value != IntPtr.Zero)
        {
            CFRelease(value);
        }
    }

    private static Boolean TryCopyAttributeValue(
        IntPtr element,
        String attribute,
        out IntPtr value)
    {
        value = IntPtr.Zero;
        try
        {
            return AXUIElementCopyAttributeValue(element, NativeString(attribute), out value) == AxSuccess
                && value != IntPtr.Zero;
        }
        catch
        {
            value = IntPtr.Zero;
            return false;
        }
    }

    private static void EnsureAccessibilityTree(Int32 processId, IntPtr application)
    {
        if (!IsTrusted(false))
        {
            return;
        }

        var identity = ReadProcessIdentity(processId);
        var candidateState = new AccessibilityBootstrapState();
        var state = AccessibilityBootstrapStates.GetOrAdd(
            identity,
            candidateState);
        if (ReferenceEquals(state, candidateState))
        {
            foreach (var staleIdentity in AccessibilityBootstrapStates.Keys)
            {
                if (staleIdentity != identity
                    && (staleIdentity.ProcessId == processId
                        || !IsSameRunningProcess(staleIdentity)))
                {
                    _ = AccessibilityBootstrapStates.TryRemove(staleIdentity, out _);
                }
            }
        }

        if (state.TreeReady && state.RequestsExhausted)
        {
            return;
        }

        lock (state.Gate)
        {
            if (state.TreeReady && state.RequestsExhausted)
            {
                return;
            }

            if (state.RequestsExhausted)
            {
                if (DateTime.UtcNow >= state.NextReadinessProbeAtUtc)
                {
                    state.TreeReady = ProbeForRichAccessibilityTree(
                        application,
                        AccessibilityQuickProbeTime);
                    state.NextReadinessProbeAtUtc =
                        DateTime.UtcNow + AccessibilityReprobeInterval;
                    if (state.TreeReady)
                    {
                        RecordAccessibilityBootstrapDiagnostic(
                            identity,
                            state,
                            TimeSpan.Zero,
                            "ready-after-settle");
                    }
                }

                return;
            }

            if (state.EnhancedRequestCount > 0)
            {
                if (DateTime.UtcNow < state.NextRetryAtUtc)
                {
                    return;
                }

                var retryStopwatch = Stopwatch.StartNew();
                state.TreeReady = ProbeForRichAccessibilityTree(
                    application,
                    AccessibilityQuickProbeTime);
                state.EnhancedResult = SetTrue(application, "AXEnhancedUserInterface");
                state.EnhancedRequestCount++;
                state.TreeReady = WaitForRichAccessibilityTree(
                    application,
                    EnhancedAccessibilitySettleTime);
                var retryRequestWasNotTransient =
                    !IsTransientSetFailure(state.EnhancedResult);
                state.RequestsExhausted = retryRequestWasNotTransient
                    || state.EnhancedRequestCount >= MaximumEnhancedAccessibilityRequests;
                if (!state.RequestsExhausted)
                {
                    state.NextRetryAtUtc = DateTime.UtcNow
                        + TimeSpan.FromSeconds(state.EnhancedRequestCount);
                }
                else if (!state.TreeReady)
                {
                    state.NextReadinessProbeAtUtc =
                        DateTime.UtcNow + AccessibilityReprobeInterval;
                }

                RecordAccessibilityBootstrapDiagnostic(
                    identity,
                    state,
                    retryStopwatch.Elapsed,
                    state.RequestsExhausted
                        ? "enhanced-requests-exhausted"
                        : "transient-retry-pending");
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            state.TreeReadyBeforeRequest = ProbeForRichAccessibilityTree(
                application,
                AccessibilityQuickProbeTime);

            // Reading the application role opts Chromium into its public
            // native/basic accessibility mode before private compatibility
            // attributes are considered.
            _ = ReadString(application, "AXRole");

            // Electron exposes this attribute specifically for third-party
            // native accessibility clients. Some older Electron releases
            // apply the request while still returning an AX error, so tree
            // readiness is authoritative rather than the setter result.
            state.ManualResult = SetTrueWithCannotCompleteRetry(
                application,
                "AXManualAccessibility");
            state.TreeReady = WaitForRichAccessibilityTree(
                application,
                ManualAccessibilitySettleTime);
            if (state.ManualResult != AxSuccess || !state.TreeReady)
            {
                // Current ChatGPT releases use Chromium's BrowserCrApplication,
                // which exposes AXEnhancedUserInterface instead. Request it
                // once normally; only a clearly transient delivery failure can
                // trigger a bounded retry after the full settle window.
                state.EnhancedResult = SetTrue(application, "AXEnhancedUserInterface");
                state.EnhancedRequestCount = 1;
                state.TreeReady = WaitForRichAccessibilityTree(
                    application,
                    EnhancedAccessibilitySettleTime);
            }

            var manualRequestWasDelivered = state.ManualResult == AxSuccess;
            var enhancedRequestWasNotTransient = state.EnhancedRequestCount > 0
                && !IsTransientSetFailure(state.EnhancedResult);
            state.RequestsExhausted = state.EnhancedRequestCount > 0
                ? enhancedRequestWasNotTransient
                : manualRequestWasDelivered;
            if (!state.RequestsExhausted)
            {
                state.NextRetryAtUtc = DateTime.UtcNow + AccessibilityRetryCooldown;
            }
            else if (!state.TreeReady)
            {
                state.NextReadinessProbeAtUtc =
                    DateTime.UtcNow + AccessibilityReprobeInterval;
            }

            RecordAccessibilityBootstrapDiagnostic(
                identity,
                state,
                stopwatch.Elapsed,
                state.RequestsExhausted
                    ? "requests-complete"
                    : "transient-retry-pending");
        }
    }

    private static Int32 SetTrueWithCannotCompleteRetry(IntPtr element, String attribute)
    {
        var result = SetTrue(element, attribute);
        if (result != AxCannotComplete)
        {
            return result;
        }

        Thread.Sleep(35);
        return SetTrue(element, attribute);
    }

    private static Int32 SetTrue(IntPtr element, String attribute)
    {
        try
        {
            var trueValue = ReadGlobalReference(CoreFoundationHandle.Value, "kCFBooleanTrue");
            return trueValue == IntPtr.Zero
                ? AxUnavailable
                : AXUIElementSetAttributeValue(element, NativeString(attribute), trueValue);
        }
        catch
        {
            return AxUnavailable;
        }
    }

    private static Boolean IsTransientSetFailure(Int32 result)
        => result is AxCannotComplete or AxUnavailable;

    private static Boolean WaitForRichAccessibilityTree(IntPtr application, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (HasRichAccessibilityTree(application, stopwatch, timeout))
            {
                return true;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            Thread.Sleep(remaining < AccessibilityProbeInterval
                ? remaining
                : AccessibilityProbeInterval);
        }

        return false;
    }

    private static Boolean ProbeForRichAccessibilityTree(
        IntPtr application,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        return HasRichAccessibilityTree(application, stopwatch, timeout);
    }

    private static Boolean HasRichAccessibilityTree(
        IntPtr application,
        Stopwatch stopwatch,
        TimeSpan timeout)
    {
        if (stopwatch.Elapsed >= timeout)
        {
            return false;
        }

        _ = TryCopyElement(application, "AXFocusedWindow", out var focusedWindow);

        // Keep the retained element alive while AXWindows is visited so the
        // focused window can be skipped instead of counted twice.
        using (focusedWindow)
        {
            if (focusedWindow is not null
                && HasRichAccessibilitySubtree(
                    focusedWindow.Handle,
                    stopwatch,
                    timeout))
            {
                return true;
            }

            var isReady = false;
            if (stopwatch.Elapsed < timeout)
            {
                ForEachElement(
                    application,
                    "AXWindows",
                    window =>
                    {
                        if (!isReady
                            && stopwatch.Elapsed < timeout
                            && (focusedWindow is null
                                || !IsSameElement(window, focusedWindow.Handle))
                            && HasRichAccessibilitySubtree(
                                window,
                                stopwatch,
                                timeout))
                        {
                            isReady = true;
                        }
                    });
            }

            return isReady;
        }
    }

    private static Boolean HasRichAccessibilitySubtree(
        IntPtr root,
        Stopwatch stopwatch,
        TimeSpan timeout)
    {
        var probe = new AccessibilityTreeProbe();
        ProbeAccessibilityTree(root, 0, probe, stopwatch, timeout);
        return probe.IsReady;
    }

    private static void ProbeAccessibilityTree(
        IntPtr element,
        Int32 depth,
        AccessibilityTreeProbe probe,
        Stopwatch stopwatch,
        TimeSpan timeout)
    {
        if (probe.IsReady
            || depth > AccessibilityProbeMaximumDepth
            || probe.NodesVisited >= AccessibilityProbeMaximumNodes
            || stopwatch.Elapsed >= timeout)
        {
            return;
        }

        probe.NodesVisited++;
        probe.MaximumDepth = Math.Max(probe.MaximumDepth, depth);
        var role = ReadString(element, "AXRole");
        if (role == "AXWebArea")
        {
            probe.SawWebArea = true;
        }

        if (depth >= 2 && IsWebContentRole(role))
        {
            probe.ContentRoleCount++;
        }

        probe.IsReady = probe.SawWebArea
            || (probe.NodesVisited > AccessibilityProbeReadyNodeCount
                && probe.MaximumDepth >= 3
                && probe.ContentRoleCount >= AccessibilityProbeReadyContentRoleCount);
        if (probe.IsReady)
        {
            return;
        }

        if (stopwatch.Elapsed < timeout)
        {
            ForEachElement(
                element,
                "AXChildren",
                child => ProbeAccessibilityTree(
                    child,
                    depth + 1,
                    probe,
                    stopwatch,
                    timeout));
        }
    }

    private static Boolean IsWebContentRole(String role)
        => role is "AXHeading"
            or "AXLink"
            or "AXList"
            or "AXRow"
            or "AXScrollArea"
            or "AXStaticText"
            or "AXTextArea"
            or "AXTextField";

    private static ProcessIdentity ReadProcessIdentity(Int32 processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return new ProcessIdentity(processId, process.StartTime.ToUniversalTime().Ticks);
        }
        catch
        {
            return new ProcessIdentity(processId, 0);
        }
    }

    private static Boolean IsSameRunningProcess(ProcessIdentity identity)
    {
        try
        {
            using var process = Process.GetProcessById(identity.ProcessId);
            return process.StartTime.ToUniversalTime().Ticks == identity.StartTimeTicks;
        }
        catch
        {
            return false;
        }
    }

    private static void RecordAccessibilityBootstrapDiagnostic(
        ProcessIdentity identity,
        AccessibilityBootstrapState state,
        TimeSpan elapsed,
        String outcome)
    {
        var enhancedResult = state.EnhancedRequestCount == 0
            ? "not-requested"
            : state.EnhancedResult.ToString();
        Volatile.Write(
            ref accessibilityBootstrapDiagnostic,
            $"AX bootstrap pid={identity.ProcessId}, outcome={outcome}, "
            + $"ready-before={state.TreeReadyBeforeRequest}, "
            + $"manual-result={state.ManualResult}, "
            + $"enhanced-result={enhancedResult}, "
            + $"enhanced-requests={state.EnhancedRequestCount}, "
            + $"tree-ready={state.TreeReady}, elapsed-ms={elapsed.TotalMilliseconds:F0}.");
    }

    private static Boolean TryReadPoint(
        IntPtr element,
        String attribute,
        out CgPoint point)
    {
        point = default;
        if (!TryCopyAttributeValue(element, attribute, out var value))
        {
            return false;
        }

        try
        {
            const UInt32 type = AxValueCgPoint;
            return CFGetTypeID(value) == AXValueGetTypeID()
                && AXValueGetType(value) == type
                && AXValueGetPoint(value, type, out point);
        }
        catch
        {
            point = default;
            return false;
        }
        finally
        {
            CFRelease(value);
        }
    }

    private static Boolean TryReadSize(
        IntPtr element,
        String attribute,
        out CgSize size)
    {
        size = default;
        if (!TryCopyAttributeValue(element, attribute, out var value))
        {
            return false;
        }

        try
        {
            const UInt32 type = AxValueCgSize;
            return CFGetTypeID(value) == AXValueGetTypeID()
                && AXValueGetType(value) == type
                && AXValueGetSize(value, type, out size);
        }
        catch
        {
            size = default;
            return false;
        }
        finally
        {
            CFRelease(value);
        }
    }

    private static Boolean IsFinite(Double value)
        => !Double.IsNaN(value) && !Double.IsInfinity(value);

    private static IntPtr NativeString(String value)
        => NativeStrings.GetOrAdd(
            value,
            text => CFStringCreateWithCString(IntPtr.Zero, text, Utf8Encoding));

    private static String ReadNativeString(IntPtr value)
    {
        var bufferSize = CFStringGetMaximumSizeForEncoding(
            CFStringGetLength(value),
            Utf8Encoding) + 1;
        if (bufferSize.ToInt64() <= 1)
        {
            return String.Empty;
        }

        var buffer = Marshal.AllocHGlobal(bufferSize);
        try
        {
            return CFStringGetCString(value, buffer, bufferSize, Utf8Encoding)
                ? Marshal.PtrToStringUTF8(buffer)?.Trim() ?? String.Empty
                : String.Empty;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr ReadGlobalReference(IntPtr library, String symbol)
    {
        var export = NativeLibrary.GetExport(library, symbol);
        return export == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(export);
    }

    private readonly record struct ProcessIdentity(Int32 ProcessId, Int64 StartTimeTicks);

    private sealed class AccessibilityBootstrapState
    {
        public Object Gate { get; } = new();

        public Int32 EnhancedRequestCount { get; set; }

        public Int32 EnhancedResult { get; set; } = AxUnavailable;

        public Int32 ManualResult { get; set; } = AxUnavailable;

        public DateTime NextReadinessProbeAtUtc { get; set; }

        public DateTime NextRetryAtUtc { get; set; }

        public volatile Boolean RequestsExhausted;

        public volatile Boolean TreeReady;

        public Boolean TreeReadyBeforeRequest { get; set; }
    }

    private sealed class AccessibilityTreeProbe
    {
        public Boolean IsReady { get; set; }

        public Boolean SawWebArea { get; set; }

        public Int32 ContentRoleCount { get; set; }

        public Int32 MaximumDepth { get; set; }

        public Int32 NodesVisited { get; set; }
    }

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean AXIsProcessTrusted();

    [DllImport(ApplicationServices)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean AXIsProcessTrustedWithOptions(IntPtr options);

    [DllImport(ApplicationServices)]
    private static extern IntPtr AXUIElementCreateApplication(Int32 processId);

    [DllImport(ApplicationServices)]
    private static extern UIntPtr AXUIElementGetTypeID();

    [DllImport(ApplicationServices)]
    private static extern UIntPtr AXValueGetTypeID();

    [DllImport(ApplicationServices)]
    private static extern UInt32 AXValueGetType(IntPtr value);

    [DllImport(ApplicationServices, EntryPoint = "AXValueGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean AXValueGetPoint(
        IntPtr value,
        UInt32 type,
        out CgPoint point);

    [DllImport(ApplicationServices, EntryPoint = "AXValueGetValue")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean AXValueGetSize(
        IntPtr value,
        UInt32 type,
        out CgSize size);

    [DllImport(ApplicationServices)]
    private static extern Int32 AXUIElementCopyAttributeValue(
        IntPtr element,
        IntPtr attribute,
        out IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern Int32 AXUIElementCopyActionNames(IntPtr element, out IntPtr names);

    [DllImport(ApplicationServices)]
    private static extern Int32 AXUIElementPerformAction(IntPtr element, IntPtr action);

    [DllImport(ApplicationServices)]
    private static extern Int32 AXUIElementSetAttributeValue(
        IntPtr element,
        IntPtr attribute,
        IntPtr value);

    [DllImport(ApplicationServices)]
    private static extern Int32 AXUIElementSetMessagingTimeout(IntPtr element, Single timeoutInSeconds);

    [DllImport(ApplicationServices)]
    private static extern Int32 AXUIElementGetPid(IntPtr element, out Int32 processId);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(ApplicationServices)]
    private static extern CgPoint CGEventGetLocation(IntPtr eventReference);

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreateMouseEvent(
        IntPtr source,
        UInt32 mouseType,
        CgPoint mouseCursorPosition,
        UInt32 mouseButton);

    [DllImport(ApplicationServices)]
    private static extern void CGEventPost(UInt32 tap, IntPtr eventReference);

    [DllImport(ApplicationServices)]
    private static extern Int32 CGWarpMouseCursorPosition(CgPoint newCursorPosition);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(
        IntPtr allocator,
        [MarshalAs(UnmanagedType.LPUTF8Str)] String text,
        UInt32 encoding);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringGetLength(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringGetMaximumSizeForEncoding(IntPtr length, UInt32 encoding);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean CFStringGetCString(
        IntPtr value,
        IntPtr buffer,
        IntPtr bufferSize,
        UInt32 encoding);

    [DllImport(CoreFoundation)]
    private static extern UIntPtr CFStringGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern UIntPtr CFBooleanGetTypeID();

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean CFBooleanGetValue(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern UIntPtr CFNumberGetTypeID();

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean CFNumberGetValue(
        IntPtr value,
        Int32 numberType,
        out Int64 result);

    [DllImport(CoreFoundation)]
    private static extern UIntPtr CFArrayGetTypeID();

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetCount(IntPtr array);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, IntPtr index);

    [DllImport(CoreFoundation)]
    private static extern UIntPtr CFGetTypeID(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFRetain(IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr value);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern Boolean CFEqual(IntPtr left, IntPtr right);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFDictionaryCreate(
        IntPtr allocator,
        [In] IntPtr[] keys,
        [In] IntPtr[] values,
        IntPtr count,
        IntPtr keyCallbacks,
        IntPtr valueCallbacks);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CgPoint(Double X, Double Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct CgSize(Double Width, Double Height);
}
