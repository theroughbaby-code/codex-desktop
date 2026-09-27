using System;

namespace Loupedeck.CodexDesktopPlugin;

public sealed class CodexDesktopPlugin : Plugin
{
    public CodexDesktopPlugin() => PluginResources.Init(this.Assembly);

    public override Boolean UsesApplicationApiOnly => false;

    public override Boolean HasNoApplication => false;
}
