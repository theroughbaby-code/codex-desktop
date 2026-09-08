# Codex Desktop

**A Windows application plugin for controlling ChatGPT and Codex Desktop from the Logitech MX Creative Keypad in Logi Options+.**

- Current version: **1.1.6**
- Platform: **Windows**
- Device profile: **Logitech MX Creative Keypad**
- Download: [CodexDesktop-1.1.6.lplug4](./CodexDesktop-1.1.6.lplug4)
- Project and support: [GitHub repository](https://github.com/theroughbaby-code/codex-desktop)

## Latest: 1.1.6

- Added a guarded Stop thinking action that invokes the active response's visible Stop control.
- The Stop thinking Keypad icon turns red while Codex exposes an interruptible active response.
- Updated the bundled nine-action profile for the current submission build.

## Installation

1. Find the plugin in Logi marketplace and press install.
2. Open Codex Desktop. The included nine-action Keypad profile is tied to the ChatGPT/Codex desktop application.

## Actions

| Action | Description |
| --- | --- |
| Open Codex | Finds `codex.exe` and runs `codex app`. |
| Custom Prompt | Sends a custom prompt to active codex chat. |
| Usage status | Shows remaining Codex usage and refreshes it when pressed. Requires a one-time `codex login` for the CLI app-server. |
| Approve | Approves a visible request, or opens the next chat needing attention. |
| Always approve | Uses the strongest persistent approval offered by the visible request, or opens the next chat needing attention. |
| Deny | Denies a visible request, or opens the next chat needing attention. |
| Stop thinking | Stops the active visible Codex response and turns red while it can be interrupted. |
| Switch to GPT | Sends `Alt+1` to switch to GPT. |
| Switch to Codex | Sends `Alt+3` to switch to Codex. |
| Shortcut actions | Sends ChatGPT/Codex Desktop keyboard shortcuts through the Logi C# SDK, grouped by category. |

The action library includes guarded approval and interruption controls, chat creation and management, navigation, panels, project controls, app settings, Custom Prompt, model selection, Codex usage status, and direct GPT/Codex switching.

Codex Desktop does not currently expose another local client API for observing or resolving approval requests owned by the running desktop session. The approval buttons therefore use the visible Windows accessibility controls. Their live color can detect an exposed approval surface or status, but it cannot guarantee detection inside every hidden or unloaded chat.

## Package

- Display name: Codex Desktop
- Logi plugin id: CodexDesktop
- Version: 1.1.6
- Plugin type: Windows application plugin
- Runtime: C# plugin through Logi Plugin Service
- Target device: MX Creative Console Keypad through Logi Options+
- Default profile: One MX Creative Keypad page with nine Codex actions
- License: MIT

## Notes

Detailed release history lives in [CHANGELOG.md](./CHANGELOG.md). The project is available under the [MIT License](./LICENSE).
