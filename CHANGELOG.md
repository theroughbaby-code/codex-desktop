# Changelog

## 1.0.7 - 2026-08-27

- Removed the separate ChatGPT and Codex mode-switch actions because the UI Automation approach was not reliable in Options+ testing.
- Checked current local app integration points: no registered ChatGPT/OpenAI/Codex URL protocol was available on this machine.
- Checked Codex CLI surfaces: `codex app` only launches the desktop app, and `remote-control`/`app-server` target Codex agent/session control rather than desktop ChatGPT/Codex navigation.
- Kept the plugin to the reliable Open Codex action until a supported app API or stable deep link for desktop mode navigation exists.

## 1.0.6 - 2026-08-27

- Reworked the plugin icon from black-on-transparent to off-white-on-transparent so it remains visible in Options+ dark mode.
- Cropped and scaled the OpenAI mark to occupy more of the 256 px icon canvas.
- Deferred ChatGPT/Codex mode switching changes to step 4, where the plugin should first look for supported app/API routes and only keep a toggle action if it can be made reliable.

## 1.0.5 - 2026-08-27

- Diagnosed the failed installed-package test from Logi Plugin Service logs.
- Found that the installer command returned success, but Options+ invoked a stale action while `CodexDesktop` was still installed as a development junction to the local `dist` folder.
- Changed package builds to rename `CodexDesktop.lplug4` into a versioned artifact such as `CodexDesktop-1.0.5.lplug4`.
- Added standalone package verification that fails if generated assets or `.lplug4` archives contain `pluginapi.dll`, the local project path, or the public GitHub repository link.
- Kept the public README brief and moved detailed release notes here.

## 1.0.4 - 2026-08-27

- Added Switch to ChatGPT and Switch to Codex actions for the desktop app's top-left mode switcher.
- Added a packaged Windows UI Automation helper for ChatGPT/Codex mode switching.
- Replaced the plugin icon with OpenAI's official black monoblossom asset from the 2025 logo package.
- Added distinct action icons and action symbols for ChatGPT and Codex switching.

## 1.0.3 - 2026-08-27

- Removed placeholder files from generated package folders before packing.

## 1.0.2 - 2026-08-27

- Added packaging verification that fails if `pluginapi.dll` is found in `package`, `dist`, or the generated `.lplug4`.
- Wired the `pluginapi.dll` guard into `npm run build:pack`.

## 1.0.1 - 2026-08-27

- Added the first Logitech command action: Open Codex.
- Added Windows Codex CLI discovery before launching the desktop app.
- Added action icon and action symbol assets for the Open Codex action.

## 1.0.0 - 2026-08-27

- Created the initial Logi Options+ Node.js plugin scaffold for Codex Desktop.
- Added Logitech `plugin4` metadata with Node.js runtime and `CodexDesktop` plugin id.
- Added TypeScript, tsup, and Logitech SDK build configuration.
- Added source, package asset, action icon, and action symbol directories.
- Added a patch-version bump helper for future test/package builds.
- Verified that type-check and build pass.
