# Changelog

## 1.1.17 - 2026-09-28 (release candidate)

- Corrected recent-chat navigation, Review, Terminal, and the adjacent affected tab actions to send ChatGPT's physical Control shortcuts on macOS instead of Command shortcuts that switch applications or windows.
- Extended GPT, Work, and Codex switching for Electron's measured surface transition delay and added latest-first scans for rebuilt composer controls, product menus, and exact command-menu results.
- Made Stop Thinking search the newest composer controls first and verify that the active turn ends before reporting a native click as successful.
- Made Approve and Deny recognize exact labels exposed only by child accessibility nodes and scan the newest approval card first in long conversations.
- Added the macOS Accessibility prompt path and explicit setup instructions for `/Applications/Utilities/LogiPluginService.app`; ChatGPT itself does not require this permission.

## 1.1.16 - 2026-09-28 (release candidate)

- Made Switch to GPT, Switch to Work, and Switch to Codex drive the visible macOS product and composer controls through Accessibility, with an exact command-menu fallback when an existing conversation does not expose the Home composer toggle.
- Removed the unreliable macOS number-key fallback that can be intercepted by tab navigation; Windows keeps its `Alt+1`, `Alt+2`, and `Alt+3` bindings.

## 1.1.15 - 2026-09-28 (release candidate)

- Corrected Open model picker on macOS to use Codex Desktop's literal `Control+Shift+M` binding instead of `Command+Shift+M`.
- Added an accessibility-first model-picker path with a native center-click fallback for Chromium controls that advertise a successful menu action without opening.
- Added the same native click fallback to macOS Approve, Always approve, Deny, and Stop Thinking when Electron omits a usable press action.
- Restored the pointer after native accessibility clicks and added distinct invocation, click, keyboard-fallback, permission, unavailable, and missing-target logs.

## 1.1.14 - 2026-09-28 (release candidate)

- Fixed macOS Stop Thinking after Electron accessibility-tree rerenders by activating ChatGPT and scanning a fresh control tree before invocation.
- Added bounded retries, multi-window target selection, and a guarded Return fallback only after the exact Stop control accepts focus.
- Added explicit plugin log results for invoked, unavailable, missing, and permission-blocked Stop actions.

## 1.1.13 - 2026-09-28 (release candidate)

- Added one universal `.lplug4` layout containing separate `win/CodexDesktopPlugin.dll` and `mac/CodexDesktopPlugin.dll` assemblies under a shared manifest and default profile.
- Added the native macOS plugin path with Mac shortcut mappings, ChatGPT bundle activation, approval controls, model selection, and Stop Thinking support.
- Added Codex usage status on macOS through the same local `codex app-server` rate-limit request used on Windows, with Mac-specific Codex CLI discovery.
- Reworked macOS state-aware actions to reacquire their exact Accessibility target after activating Codex and to remain inactive when no actionable control is available.
- Documented the macOS Accessibility permission required by Logi Plugin Service and the plugin's local handling of usage, approval, model, and interruption state.
- Made `npm run pack` create the universal package while retaining `npm run pack:windows` for Windows-only development builds.
- Moved the generated 61-action developer test profile under `artifacts/test-profiles/` so it cannot replace the curated production profile.
- Updated the package support link to the repository's GitHub Issues page.

## 1.1.9 - 2026-09-27

- Added a read-only macOS environment audit that records the ChatGPT bundle identity, architectures, URL schemes, AppleScript support, Codex CLI, Logitech runtime paths, build tools, profiles, and relevant processes without collecting credentials.
- Added a Mac Step 1 runbook covering the remaining Accessibility, application-profile, Adapt to App, and physical-Keypad checks.
- Extended approval-surface discovery to recognize stable accessibility names and help text when Chromium omits DOM automation IDs and CSS classes.
- Extended Stop thinking discovery to accept a stable composer marker exposed through accessibility help text while retaining translated labels only as guarded fallbacks.
- Updated the approval and Stop UI Automation fixtures for current Chromium accessibility behavior and revalidated English, Czech, German, French, Chinese, and label-independent structural paths.
- Rebuilt and verified the standalone package with `LogiPluginTool`; `PluginApi.dll` remains excluded.

## 1.1.8 - 2026-09-27

- Replaced runtime-generated approval and Stop thinking bitmaps with embedded 80x80 PNG state images loaded through the Logitech SDK resource API.
- Preserved neutral and live green, amber, and red device states while avoiding the dynamic-image serialization errors found in the QA trace.
- Removed the runtime-generated Usage status bitmap; its packaged SVG remains stable while its button label continues to show live remaining percentages.
- Added deterministic generation of the embedded state images to the build pipeline.
- Switched release archive creation to the official `LogiPluginTool pack` command.
- Stamped the plugin DLL with package, file, informational, and assembly version metadata sourced from `package.json`.
- Made the Codex app-server client report the generated package version instead of a separately maintained hardcoded value.

## 1.1.7 - 2026-09-08

- Made Stop thinking language-independent at its primary path by matching the stable Codex composer action structure, with every official stop label bundled in the current Codex Desktop release as a guarded fallback.
- Prevented Stop thinking from matching unrelated voice, trace, audio, and page controls that happen to use the same translated Stop label.
- Added Stop thinking UI Automation coverage for English, Czech, German, French, Chinese, a stable-ID unknown-label case, actual invocation, and an unrelated Stop-control decoy.
- Cached detected approval and Stop controls so device presses invoke the current target directly; a full accessibility rescan now occurs only when the cache is empty, stale, or due for a periodic safety refresh.
- Removed generic Codex activation from the approval and Stop fast paths, activating the exact matched chat window instead.
- Reduced Stop monitoring to a cheap 300 ms read of the cached composer action, with full discovery every eight seconds and a two-second retry only when no composer control is cached.
- Narrowed approval discovery to likely decision controls and their approval-card ancestors, reducing measured cold scans from about 3.5 seconds to about 0.26 seconds on the test system.
- Changed approval monitoring to 1500 ms idle discovery and cheap 250 ms cached validation while a request is pending, balancing prompt device feedback with low background CPU use.
- Made approval discovery language-independent at its primary path by prioritizing semantic automation IDs, Codex's approval-card marker, shared ancestor relationships, and fixed decision-control order.
- Added fallback labels for every approval translation bundled with the current Codex Desktop release, including one-time approval, conversation approval, approval options, and denial.
- Added repeatable Windows UI Automation coverage for English, German, French, and an unknown-label fixture that verifies the structural path without translated text.
- Removed an accidental runtime dependency on the SDK's `DistinctBy` helper from approval relationship matching; `PluginApi.dll` remains host-owned and excluded from the package.
- Reacquired approval controls by semantic role after window activation so localized captions and accessibility-tree rerenders do not invalidate an action.
- Replaced whole-desktop accessibility scans with direct enumeration of visible Codex window handles, preventing unrelated protected windows from aborting approval discovery.
- Added approval support for both `ChatGPT` and `ChatGPT Classic` processes and preferred the foreground Codex window when several are open.
- Broadened approval detection across accessible names, help text, automation IDs, item status, control roles, and current approval wording.
- Focused the exact window and matched control before guarded keyboard fallback, while retaining the rule that ordinary chats never receive an unverified approval command.
- Replaced short fixed navigation delays with bounded state polling for Codex activation, attention navigation, and persistent approval menus.
- Added per-action SDK log messages that distinguish successful invocation, focused-key fallback, unavailable controls, missing requests, and activation failures.

## 1.1.6 - 2026-09-08

- Added Stop thinking under Chat to interrupt the active visible Codex response.
- Detected running responses through Codex's enabled, visible `Stop` accessibility control instead of relying on an undocumented shortcut or a separate app-server session.
- Added a 350 ms guarded state monitor and dynamic Keypad artwork that changes from neutral to red only while a matching Stop control is available.
- Preferred the foreground Codex window when invoking Stop and performed no action when no unambiguous interrupt control was found.
- Added matching editable and packaged rounded stop-button SVG assets.
- Updated the bundled nine-action default profile and bound it to the Codex desktop `ChatGPT` process.
- Aligned the read-only Codex usage client's reported version with the `1.1.6` package.

## 1.1.5 - 2026-09-08

- Replaced the approval actions' one-pixel runtime circles with explicit variable-width arcs, fixing the thin borders shown on the MX Creative Keypad.
- Kept the decision glyphs thinner than the new high-contrast circular borders.

## 1.1.4 - 2026-09-08

- Tripled the circular outline widths in the editable and packaged Approve, Always approve, and Deny SVG assets while preserving the thinner decision glyphs.
- Reworked Open model picker to invoke the active composer's visible accessibility control first, retaining `Ctrl+Shift+M` only as a guarded fallback.
- Added a short activation-state wait to shortcut actions so a command is not sent to the previously focused Windows application.

## 1.1.3 - 2026-09-07

- Added Approve, Always approve, and Deny actions under a dedicated Approvals group.
- Invoked visible Codex approval controls through Windows accessibility instead of sending unguarded keystrokes into ordinary chats.
- Added a safe fallback that activates Codex, opens the next chat needing attention with `Ctrl+Alt+A`, and acts only when an approval surface is positively detected.
- Used Codex's native `Enter` and `Escape` approval shortcuts only as guarded fallbacks after an approval surface has been found.
- Added live neutral/green/amber/red Keypad artwork driven by Logitech's dynamic action-image refresh API.
- Added matching rounded SVG action icons and Options+ picker symbols to the editable icon set and standalone package.
- Documented the current limitation that Codex does not expose its desktop session's pending approval requests to a second app-server client.

## 1.1.2 - 2026-09-04

- Replaced the low-contrast transparent plugin mark with a dark-green (`#081c05`) rounded-square badge.
- Added a larger light ChatGPT mark, subtle upper-left glare, soft depth, and a restrained edge highlight for legibility at small sizes.
- Updated the application icon embedded in the default MX Creative Keypad profile to match the new plugin badge.

## 1.1.1 - 2026-09-04

- Added the public GitHub repository as the package `homePageUrl` while retaining it as the support URL.

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
