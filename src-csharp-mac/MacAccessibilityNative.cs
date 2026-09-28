using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Loupedeck.CodexDesktopPlugin;

internal static class MacAccessibilityNative
{
    private const String ApplicationServices = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const String CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const UInt32 Utf8Encoding = 0x08000100;
    private const Int32 AxSuccess = 0;
    private const Int32 AxCannotComplete = -25204;
    private const UInt32 AxValueCgPoint = 1;
    private const UInt32 AxValueCgSize = 2;
    private const Int32 CfNumberSInt64Type = 4;
    private const UInt32 CgHidEventTap = 0;
    private const UInt32 CgEventLeftMouseDown = 1;
    private const UInt32 CgEventLeftMouseUp = 2;
    private const UInt32 CgMouseButtonLeft = 0;

    private static readonly ConcurrentDictionary<String, IntPtr> NativeStrings =
        new(StringComparer.Ordinal);
    private static readonly Lazy<IntPtr> ApplicationServicesHandle =
        new(() => NativeLibrary.Load(ApplicationServices));
    private static readonly Lazy<IntPtr> CoreFoundationHandle =
        new(() => NativeLibrary.Load(CoreFoundation));

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
        try
        {
            var handle = AXUIElementCreateApplication(processId);
            if (handle == IntPtr.Zero)
            {
                return null;
            }

            _ = AXUIElementSetMessagingTimeout(handle, 0.35F);
            return new MacAxElement(handle);
        }
        catch
        {
            return null;
        }
    }

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

            var count = CFArrayGetCount(value);
            for (IntPtr index = 0; index.ToInt64() < count.ToInt64(); index += 1)
            {
                var child = CFArrayGetValueAtIndex(value, index);
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
    {
        try
        {
            var trueValue = ReadGlobalReference(CoreFoundationHandle.Value, "kCFBooleanTrue");
            return trueValue != IntPtr.Zero
                && AXUIElementSetAttributeValue(element, NativeString(attribute), trueValue) == AxSuccess;
        }
        catch
        {
            return false;
        }
    }

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
