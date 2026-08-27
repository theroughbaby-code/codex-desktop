# Changelog

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
