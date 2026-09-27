using System;
using System.Reflection;

namespace Loupedeck.CodexDesktopPlugin;

internal static class PluginResources
{
    private static Assembly? assembly;

    public static void Init(Assembly value)
    {
        ArgumentNullException.ThrowIfNull(value);
        assembly = value;
    }

    public static String FindFile(String fileName)
        => GetAssembly().FindFileOrThrow(fileName);

    public static BitmapImage ReadImage(String resourceName)
        => GetAssembly().ReadImage(FindFile(resourceName));

    private static Assembly GetAssembly()
        => assembly ?? throw new InvalidOperationException("Plugin resources have not been initialized.");
}
