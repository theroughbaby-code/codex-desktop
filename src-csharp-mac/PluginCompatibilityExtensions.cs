namespace Loupedeck.CodexDesktopPlugin;

internal static class PluginCompatibilityExtensions
{
    public static Boolean IsApplicationActive(this Plugin plugin) => plugin.IsActive();
}
