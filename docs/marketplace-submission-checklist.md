# Logitech Marketplace submission checklist

The release artifact must be one `.lplug4` package containing separate Windows and macOS assemblies:

```text
metadata/LoupedeckPackage.yaml
metadata/Icon256x256.png
win/CodexDesktopPlugin.dll
mac/CodexDesktopPlugin.dll
LICENSE
```

The shared manifest declares both runtime folders:

```yaml
pluginFileName: CodexDesktopPlugin.dll
pluginFolderWin: win
pluginFolderMac: mac
```

Build each assembly on its native operating system, copy the results into `artifacts/win/` and `artifacts/mac/`, and create the final archive with:

```bash
npm run pack:universal
```

Alternative artifact paths can be supplied explicitly:

```bash
npm run pack:universal -- \
  --windows-dll /path/to/windows/CodexDesktopPlugin.dll \
  --mac-dll /path/to/macos/CodexDesktopPlugin.dll
```

The universal pack command requires both DLLs, includes the MIT license, excludes `PluginApi.dll`, and runs the official `LogiPluginTool pack` and `verify` commands.

## Release blockers

- Sync the canonical 1.1.12 Mac source from the originating machine. The installed 1.1.12 DLL contains code that is absent from this checkout and must not be used as the release source.
- Fix and retest the Mac Approve and Deny target-resolution path. Stop Thinking works, but approval actions can light without retaining an actionable target.
- Add `UsageStatusCommand` and its Codex rate-limit client to the Mac assembly. The bundled default profile assigns Usage status, but neither the checked-out Mac source nor the installed 1.1.12 Mac DLL exports that action.
- Confirm that every action referenced by `DefaultProfile70.lp5` exists in both assemblies: New chat, Open Codex, Settings, New standalone chat, Usage status, Open model picker, Approve, Stop Thinking, and Deny.
- Remove the unrelated `Cursivis` entry from `additionalNativePluginNames` in the default profile and recheck the exported profile on both platforms.
- Choose one release version and apply it to `package.json`, `LoupedeckPackage.yaml`, both assemblies, the archive filename, README, and changelog.

## Final validation

- Install the same universal package on Windows and macOS.
- Confirm application matching and Adapt to App on both systems.
- Exercise every default-profile action on an MX Creative Keypad.
- Confirm Usage status handles signed-in, signed-out, unavailable, and reset-time states on both systems.
- Confirm Approve, Always approve, Deny, and Stop Thinking only light and act in the correct state.
- Confirm the archive contains both platform DLLs and no `PluginApi.dll`, local paths, credentials, logs, or build outputs.
- Run `LogiPluginTool metadata` and confirm the public listing fields and release version.
- Prepare the Marketplace title, short and long descriptions, category/tags, developer identity, support link, icon, screenshots/banner assets, and Developer EULA.
- Publish a privacy statement if the release collects personal data. The current plugin should document that Usage status reads account limits through the user's local Codex CLI session and does not bundle credentials.
- Submit the verified `.lplug4` through the [Logitech Marketplace contribution form](https://marketplace.logitech.com/contribute).

## Official references

- [Cross-platform plugin structure](https://logitech.github.io/actions-sdk-docs/csharp/tutorial/plugin-structure/)
- [Plugin icon requirements](https://logitech.github.io/actions-sdk-docs/csharp/icons/plugin-icon/)
- [Packaging and distribution](https://logitech.github.io/actions-sdk-docs/csharp/plugin-development/distributing-the-plugin/)
- [Marketplace approval guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/)
- [Logitech Marketplace Developer Agreement](https://www.logitech.com/en-us/legal/developer-agreement)
