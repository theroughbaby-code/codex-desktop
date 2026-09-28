using System.Reflection;

namespace Loupedeck.CodexDesktopPlugin;

internal static class PluginBuildInfo
{
    public static String Version { get; } = ReadVersion();

    private static String ReadVersion()
    {
        var assembly = typeof(PluginBuildInfo).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!String.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion.Split('+', 2)[0];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
