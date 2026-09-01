# Changelog

## 1.1.0 - 2026-09-01

- Added marketplace-ready author, copyright, MIT license, license URL, support URL, device, application, and activation metadata.
- Declared the plugin as a Windows-only application plugin and moved its runtime assembly into the package's `win` directory.
- Explicitly marked the runtime as application-bound and shortcut-driven rather than universal or API-only.
- Included the supplied `DefaultProfile70.lp5`, tied to the `ChatGPT` desktop process and `CodexDesktop` native plugin, with nine MX Creative Keypad action assignments.
- Removed default-profile regeneration from release builds and added validation that preserves and checks the supplied marketplace profile.
- Renamed the visible Switch to Chat action to Switch to GPT while preserving its internal action ID and existing profile assignment.
- Replaced the Settings action icon and picker symbol with a standard rounded cogwheel.

## 1.0.20 - 2026-09-01

- Reworked only Custom Prompt: it now places the complete configured Unicode string on the Windows clipboard, sends `Ctrl+V` to the already-active Codex chat, waits 50 ms for the paste event, and presses Enter.
- Removed the unreliable SDK string and chunk dispatch paths that truncated or dropped prompt text after 16 characters.
- Added short clipboard-access retries for temporary clipboard contention while retaining the Action Editor textbox's unlimited default length.

## 1.0.19 - 2026-09-01

- Fixed Custom Prompt values longer than the SDK's 16-character text-dispatch limit by sending them as ordered 16-character chunks before pressing Enter.
- Preserved UTF-16 surrogate pairs at chunk boundaries so supplementary Unicode characters such as emoji are not split.
- Removed the plugin's 4,000-character Action Editor limit; Custom Prompt now uses the SDK textbox's unlimited default and imposes no plugin-level character maximum.
- Changed the action description to `Sends a custom prompt to active codex chat.`

## 1.0.18 - 2026-09-01

- Fixed Custom Prompt so configured text is sent through the Logi SDK string text-input path instead of treating every character as an individual application shortcut.
- Custom Prompt now runs only when Codex Desktop is already active, preserving the user's focused chat composer, and presses Enter after dispatching the complete configured value.

## 1.0.17 - 2026-09-01

- Reimplemented Open terminal as a dedicated native SDK action that sends the backtick character with Control, avoiding the US-layout-specific `Oem3` virtual-key binding.
- Removed Copy Session ID from the action catalog, generated command set, icon assets, and default test profile.
- Renamed the visible Send prompt action to Custom Prompt while preserving its internal action identity for existing Options+ assignments.
- Changed Custom Prompt to dispatch every configured character in order and press Enter only after the complete text has been sent.
- Regenerated the default MX Creative Keypad profile with all 62 available actions across seven test pages.

## 1.0.16 - 2026-09-01

- Removed Toggle Voice Chat because Codex Desktop does not assign its proposed shortcut by default.
- Replaced the Toggle pin artwork and picker symbol with a conventional upright pushpin.
- Added `profiles/DefaultProfile70.lp5`, a clean seven-page Logitech MX Creative Keypad test profile containing every remaining action.
- Configured the parameterized Send prompt action in the test profile with a harmless profile-validation prompt.
- Added deterministic default-profile generation to the build and included the `profiles` directory in standalone packages.

## 1.0.15 - 2026-09-01

- Removed the heavy dual stroke from numerals in all numbered chat and recent-chat icons, reduced their size slightly, and changed them to a medium-weight fill for clearer Keypad rendering.
- Expanded every action-icon and action-symbol viewBox from `0 0 24 24` to `-1 -1 26 26`, adding centered edge padding to prevent artwork from being clipped by the device renderer.

## 1.0.14 - 2026-09-01

- Reduced the line weight of all 64 Keypad action icons and all 64 Options+ action-picker symbols by exactly 20% to improve small-size readability.
- Changed Keypad dual-stroke widths from 3.4/1.8 to 2.72/1.44 and picker-symbol strokes from 1.8 to 1.44 while preserving transparent backgrounds, rounded joins, and rounded caps.
- Added `icons/actionicons` and `icons/actionsymbols` as editable SVG source folders and updated the asset generator to preserve existing artwork during future builds.

## 1.0.13 - 2026-08-28

- Added a Usage status action backed by Codex app-server's read-only `account/rateLimits/read` method.
- The action displays the primary remaining percentage as a live gauge, includes the secondary remaining percentage in its display name when available, refreshes every two minutes, and refreshes immediately when pressed.
- Verified that a standalone app-server process does not inherit the desktop app session on this machine. The action reports `Codex sign in` until the user completes the supported one-time `codex login`; it does not read credentials, tokens, browser storage, or desktop UI state.
- Parsed the endpoint's plan, reset time, and credit balance fields for forward-compatible status handling while keeping the Keypad display concise.
- Replaced the generic action art with 64 paired semantic SVG icons and action-picker symbols. The set uses transparent backgrounds, rounded strokes and joins, consistent sizing, and dual light/dark outlines for contrast in both Options+ themes.
- Added a short, action-specific description to every shortcut command and refined the Open Codex and Send prompt descriptions.
- Kept all runtime actions native to the C# SDK except Open Codex's direct `codex app` process launch; no PowerShell helper or `PluginApi.dll` is included in the package.

## 1.0.12 - 2026-08-27

- Changed Next chat to `Ctrl+Shift+]` and Previous chat to `Ctrl+Shift+[` to match the current Codex Desktop shortcuts.
- Added a configurable Send prompt action using `ActionEditorCommand` and an `ActionEditorTextbox` shown when the action is assigned.
- The Send prompt action types its configured value through the Logi SDK keyboard API and presses Enter; it does not invoke PowerShell or ship a helper process.
- Removed the unconditional 80 ms delay from shortcut actions. Codex activation now occurs only when the app is not already active, so actions used in the active Codex window dispatch immediately.

## 1.0.11 - 2026-08-27

- Reworked the plugin runtime from Node.js shortcut shim actions to a native C# Logi SDK plugin.
- Implemented all shortcut actions as `PluginDynamicCommand` classes that call `ClientApplication.SendKeyboardShortcut(...)`.
- Bound the plugin to the running Codex/ChatGPT desktop process names `ChatGPT` and `ChatGPT Classic` so SDK activation can target the desktop app before sending shortcuts.
- Removed the packaged PowerShell shortcut helper and omitted custom action logging from the shipped plugin.
- Regenerated action icons and symbols with C# full-class filenames so Options+ can discover them under the SDK naming convention.
- Added a standalone C# packaging path that includes only the plugin DLL plus package metadata/icons, and keeps `PluginApi.dll` out of the `.lplug4`.

## 1.0.10 - 2026-08-27

- Added the full requested keyboard shortcut action catalog grouped under Chat, Navigation, Panels, Project, App, and General.
- Replaced per-action shortcut classes with one generic shortcut action implementation.
- Updated the shortcut helper to parse arbitrary key-combo strings and log action name, display name, keys, detected window, focus result, and send result.
- Implemented duplicate shortcut mappings intentionally where the app exposes multiple command names for the same shortcut, such as previous recently viewed chat and previous tab.

## 1.0.9 - 2026-08-27

- Added plugin-owned JSONL logging at `%LOCALAPPDATA%\Logi\LogiPluginService\PluginData\CodexDesktop\codex-desktop.log`.
- Shortcut actions now wait for the packaged helper and log started, closed, finished, or failed states.
- Replaced PowerShell `SendKeys` with `user32.dll` `SendInput` to send more realistic keyboard events.
- The helper logs app launch, window detection, focus result, and shortcut send status for each action press.

## 1.0.8 - 2026-08-27

- Added Companion, Search, and Browser actions using documented ChatGPT Desktop keyboard shortcuts.
- Added a packaged PowerShell shortcut helper that opens/focuses ChatGPT/Codex Desktop before sending focus-dependent shortcuts.
- Kept ChatGPT/Codex mode switching out because no supported desktop mode-switch API, URL protocol, or CLI command was found.
- Added dark-mode action icons and action symbols for the new actions.

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
