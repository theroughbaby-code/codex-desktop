# Codex Desktop

**A Windows application plugin for controlling ChatGPT and Codex Desktop from the Logitech MX Creative Keypad in Logi Options+.**

- Current version: **1.1.0**
- Platform: **Windows**
- Device profile: **Logitech MX Creative Keypad**
- Download: [CodexDesktop-1.1.0.lplug4](./CodexDesktop-1.1.0.lplug4)
- Support and bug reports: [GitHub Issues](https://github.com/theroughbaby-code/codex-desktop/issues)

## Latest: 1.1.0

- Prepared the Windows application plugin for marketplace submission with a Codex-bound default Keypad profile.

## Installation

1. Download `CodexDesktop-1.1.0.lplug4`.
2. Double-click the package and complete installation in Logi Options+.
3. Open Codex Desktop. The included nine-action Keypad profile is tied to the ChatGPT/Codex desktop application.

## Actions

| Action | Description |
| --- | --- |
| Open Codex | Finds `codex.exe` and runs `codex app`. |
| Custom Prompt | Sends a custom prompt to active codex chat. |
| Usage status | Shows remaining Codex usage and refreshes it when pressed. Requires a one-time `codex login` for the CLI app-server. |
| Shortcut actions | Sends ChatGPT/Codex Desktop keyboard shortcuts through the Logi C# SDK, grouped by category. |

The action library includes chat creation and management, navigation, panels, project controls, app settings, Custom Prompt, model selection, Codex usage status, and direct switching between GPT, Work, and Codex views.

## Package

- Display name: Codex Desktop
- Logi plugin id: CodexDesktop
- Version: 1.1.0
- Plugin type: Windows application plugin
- Runtime: C# plugin through Logi Plugin Service
- Target device: MX Creative Console Keypad through Logi Options+
- Default profile: One MX Creative Keypad page with nine Codex actions
- License: MIT

## Notes

Detailed release history lives in [CHANGELOG.md](./CHANGELOG.md). The project is available under the [MIT License](./LICENSE).
