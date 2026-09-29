# Logitech Marketplace listing draft

## Listing fields

- **Name:** Codex Desktop
- **Category:** Productivity
- **Platforms:** Windows and macOS
- **Device:** Logitech MX Creative Keypad
- **Developer:** TheRoughBaby
- **Support:** https://github.com/theroughbaby-code/codex-desktop/issues
- **Source and documentation:** https://github.com/theroughbaby-code/codex-desktop
- **License:** MIT
- **Suggested tags:** ChatGPT, Codex, productivity, developer tools, AI

## Short description

Control ChatGPT and Codex Desktop from the Logitech MX Creative Keypad on Windows and macOS.

## Long description

Codex Desktop puts common ChatGPT and Codex Desktop controls on the Logitech MX Creative Keypad. The included one-page profile opens Codex, starts chats, shows remaining Codex usage, opens the model picker, approves or denies visible requests, and stops an active response.

The same plugin package installs the native Windows or macOS assembly for the current computer. State-aware actions use the operating system's local accessibility service and light only when the corresponding visible control is available. Regular shortcut actions cover chat management, navigation, panels, projects, settings, and GPT/Codex switching.

Usage status reads rate-limit information from the user's locally installed Codex CLI session. The plugin contains no analytics or external telemetry service and does not upload or persist credentials, access tokens, approval text, or chat contents.

On macOS, **LogiPluginService** needs Accessibility permission for state-aware controls and mode switching; ChatGPT itself does not. The plugin requests access on first use. If macOS suppresses the prompt after an earlier denial, enable `/Applications/Utilities/LogiPluginService.app` manually under **System Settings > Privacy & Security > Accessibility**, restart Logi Options+, and retry. Usage status requires the Codex CLI and a one-time `codex login` on each computer.

## Submission assets

- 256×256 transparent PNG plugin icon with artwork inside the centered 192×192 safe area.
- Windows screenshot showing the included nine-action MX Creative Keypad profile.
- macOS screenshot showing the same profile in Logi Options+.
- Device photo or product screenshot showing the live Approve, Stop Thinking, and Deny states.
- Optional banner built from the same icon and device-state palette.

## Reviewer notes

- The `.lplug4` contains `win/CodexDesktopPlugin.dll` and `mac/CodexDesktopPlugin.dll` under one shared manifest.
- `PluginApi.dll` is supplied by Logi Plugin Service and is not bundled.
- Approval and Stop Thinking operate only after a matching accessible control has been found.
- The plugin communicates only with the local ChatGPT/Codex Desktop app and the local Codex CLI process.
- Source is available under the MIT license at the project URL.

## Final values to add in the submission form

- Marketplace account/developer identity
- Developer EULA acceptance
- Final screenshots and banner URLs/files
- Verified universal `CodexDesktop-1.1.19.lplug4`
- Windows and macOS validation notes from physical MX Creative Keypad testing
