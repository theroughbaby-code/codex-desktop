# Logitech Marketplace submission checklist

Version 1.1.13 is the current release candidate. Its release artifact is one `.lplug4` package containing the native Windows and macOS assemblies:

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

## Build the release candidate

After changing the release version or icon, refresh the curated profile metadata and embedded icon:

```bash
npm run prepare:profile
```

Build each assembly on its native operating system. `npm run pack:windows` writes the Windows DLL to `artifacts/win/`, and `npm run build:mac-smoke` writes the Mac DLL to `artifacts/mac/`:

```text
artifacts/win/CodexDesktopPlugin.dll
artifacts/mac/CodexDesktopPlugin.dll
```

Create the universal archive with the default package command:

```bash
npm run pack
```

`npm run pack:universal` invokes the same universal packer directly. Alternative artifact paths can be supplied to that command:

```bash
npm run pack:universal -- \
  --windows-dll /path/to/windows/CodexDesktopPlugin.dll \
  --mac-dll /path/to/macos/CodexDesktopPlugin.dll
```

`npm run pack:windows` remains available for a Windows-only development package. It is not the Marketplace release artifact.

The universal pack command requires both DLLs, includes the MIT license, excludes `PluginApi.dll`, and runs the official `LogiPluginTool pack` and `verify` commands.

## Cross-platform behavior

- Both assemblies must expose every action assigned by `DefaultProfile70.lp5`: New chat, Open Codex, Settings, New standalone chat, Usage status, Open model picker, Approve, Stop Thinking, and Deny.
- Usage status runs the user's local `codex app-server` and requests `account/rateLimits/read`. The Mac assembly must use macOS Codex CLI discovery; the Windows assembly uses Windows executable discovery.
- On macOS, Approve, Always approve, Deny, and Stop Thinking use the local Accessibility API. Logi Plugin Service therefore needs permission under **System Settings > Privacy & Security > Accessibility**. Open model picker uses the desktop shortcut.
- State-aware actions must reacquire their exact control after activating Codex and remain inactive when no usable target is available.
- The curated production profile must contain only intended native plugin names. `npm run generate:test-profile` writes its 61-action developer profile to `artifacts/test-profiles/` and must never replace `package/profiles/DefaultProfile70.lp5`.

## Local data handling

- The plugin has no analytics or external telemetry service.
- Usage status obtains rate-limit state through the locally installed Codex CLI. Authentication stays under CLI control; the plugin does not read, bundle, or store credentials or access tokens.
- Accessibility inspection and invocation happen locally in the running ChatGPT/Codex Desktop application. The plugin does not upload or persist approval text or chat contents.
- Custom Prompt values are stored as Options+ action settings and are sent only to the active ChatGPT/Codex Desktop composer.

Use this statement in the Marketplace listing and developer privacy material. Revisit it before submission if the implementation begins collecting personal data or adds any network service outside the local Codex CLI session.

## Required pre-submission validation

- Confirm version `1.1.13` in `package.json`, `LoupedeckPackage.yaml`, both assembly metadata records, the archive filename, README, and changelog.
- Install the same universal package on Windows and macOS.
- Confirm application matching and Adapt to App on both systems.
- Exercise every default-profile action on an MX Creative Keypad.
- Confirm Usage status handles signed-in, signed-out, unavailable, and reset-time states on both systems.
- Confirm Approve, Always approve, Deny, and Stop Thinking light and act only in the correct state, including after Codex activation and an Accessibility-tree rerender.
- Confirm the archive contains both platform DLLs and no `PluginApi.dll`, local paths, credentials, logs, or build outputs.
- Run `LogiPluginTool metadata` and review the public listing fields, support URL, and release version.
- Prepare the Marketplace title, short and long descriptions, category and tags, developer identity, support link, icon, screenshots or banner assets, Developer EULA, and privacy statement.
- Submit the verified `.lplug4` through the [Logitech Marketplace contribution form](https://marketplace.logitech.com/contribute).

## Official references

- [Cross-platform plugin structure](https://logitech.github.io/actions-sdk-docs/csharp/tutorial/plugin-structure/)
- [Plugin icon requirements](https://logitech.github.io/actions-sdk-docs/csharp/icons/plugin-icon/)
- [Packaging and distribution](https://logitech.github.io/actions-sdk-docs/csharp/plugin-development/distributing-the-plugin/)
- [Marketplace approval guidelines](https://logitech.github.io/actions-sdk-docs/marketplace-approval-guidelines/)
- [Logitech Marketplace Developer Agreement](https://www.logitech.com/en-us/legal/developer-agreement)
