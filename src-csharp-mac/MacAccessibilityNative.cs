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

    public static Boolean IsSameElement(IntPtr left, IntPtr right)
        => left != IntPtr.Zero && right != IntPtr.Zero && CFEqual(left, right);

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
}
