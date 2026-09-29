# Codex Desktop

**A Logitech MX Creative Keypad plugin for controlling ChatGPT and Codex Desktop on Windows and macOS through Logi Options+.**

- Current release candidate: **1.1.19**
- Platforms: **Windows and macOS**
- Release artifact: **one universal `.lplug4` package**
- Device profile: **Logitech MX Creative Keypad**
- Project: [GitHub repository](https://github.com/theroughbaby-code/codex-desktop)
- Support: [GitHub issues](https://github.com/theroughbaby-code/codex-desktop/issues)

## 1.1.19 release candidate

- Packages the Windows and macOS plugin assemblies together in one `CodexDesktop-1.1.19.lplug4` archive.
- Adds native macOS shortcut dispatch, application activation, approval controls, model selection, Stop Thinking, and Codex usage status.
- Activates ChatGPT's web accessibility tree from LogiPluginService on both Electron and Chromium-based Mac releases, then waits for real web content before state-aware actions scan it.
- Opens Review and Terminal through exact native or in-app command-menu controls on macOS, then verifies the visible panel before reporting success.
- Switches Chat, Work, and Codex through localized product and composer controls, with exact command-menu fallback and verified transition state.
- Opens the macOS model selector through its accessible control first and uses the app's literal `Control+Shift+M` binding as fallback.
- Finds the newest approval and Stop Thinking controls first in long conversations and verifies Stop after a native click before reporting success.
- Uses each operating system's local accessibility API for state-aware actions and reacquires the actionable control after bringing Codex to the foreground.
- Reads Codex usage through the user's locally installed Codex CLI session on both platforms.

## Installation

The same `.lplug4` archive is used on Windows and macOS. During release validation, install the generated archive through Logi Options+. After Marketplace approval, install Codex Desktop directly from Logitech Marketplace.

Open ChatGPT/Codex Desktop after installation. The included nine-action Keypad profile is bound to the desktop application.

Usage status requires the Codex CLI and a one-time `codex login` on each computer.

### macOS Accessibility setup

Approve, Always approve, Deny, Stop Thinking, Open model picker, and the GPT/Work/Codex mode actions inspect visible controls through macOS Accessibility. The process that needs permission is **LogiPluginService**, not ChatGPT.

1. Press one of those actions once. macOS should show an Accessibility prompt for **LogiPluginService**; choose **Open System Settings** and enable it.
2. If macOS does not show the prompt, open **System Settings > Privacy & Security > Accessibility**.
3. Enable **LogiPluginService**. If it is missing, click **+**, press **Command+Shift+G**, enter `/Applications/Utilities/LogiPluginService.app`, and choose **Open**.
4. Quit and reopen Logi Options+ after changing the permission, then open ChatGPT/Codex Desktop and retry the action.

Adding ChatGPT to the Accessibility list does not grant the plugin access because LogiPluginService is the process that performs the action.

## Actions

| Action | Description |
| --- | --- |
| Open Codex | Opens the Codex desktop application. |
| Custom Prompt | Sends a configured prompt to the active Codex chat. |
| Usage status | Shows remaining Codex usage and refreshes it when pressed. |
| Approve | Approves a visible request, or opens the next chat needing attention. |
| Always approve | Uses the strongest persistent approval offered by the visible request, or opens the next chat needing attention. |
| Deny | Denies a visible request, or opens the next chat needing attention. |
| Stop Thinking | Stops the active visible Codex response and turns red while it can be interrupted. |
| Open model picker | Opens the visible model selector in the active composer. |
| Shortcut actions | Sends the matching ChatGPT/Codex Desktop shortcut, grouped by category. |

The action library also includes chat creation and management, navigation, panels, project controls, app settings, and direct GPT/Codex switching.

Approve, Always approve, Deny, Stop Thinking, and the macOS mode-switch actions operate only after the plugin finds the corresponding live control. Windows uses UI Automation and macOS uses Accessibility. Open model picker uses an accessibility-first path with shortcut fallback on both systems. A lit state reports an accessible approval or Stop control; hidden and unloaded chats may not expose their controls until Codex opens them.

## Privacy and local data use

The plugin does not include analytics or an external telemetry service. It does not upload or persist Codex account credentials, access tokens, approval text, or chat contents.

Usage status starts the locally installed `codex app-server` process and requests only `account/rateLimits/read`. Authentication remains managed by the Codex CLI. Approval, model, and Stop Thinking actions inspect and invoke the running desktop application's accessibility controls locally. A Custom Prompt value is stored as an Options+ action setting and sent only to the active ChatGPT/Codex Desktop composer.

## Package

- Display name: Codex Desktop
- Logi plugin id: CodexDesktop
- Version: 1.1.19
- Plugin type: Cross-platform application plugin
- Runtimes: C# assemblies in `win/` and `mac/`
- Target device: MX Creative Console Keypad through Logi Options+
- Default profile: One MX Creative Keypad page with nine Codex actions
- License: MIT

The release package has this platform layout:

```text
metadata/LoupedeckPackage.yaml
metadata/Icon256x256.png
win/CodexDesktopPlugin.dll
mac/CodexDesktopPlugin.dll
LICENSE
```

`npm run pack:windows` builds `artifacts/win/CodexDesktopPlugin.dll` on Windows, and `npm run build:mac-smoke` builds `artifacts/mac/CodexDesktopPlugin.dll` on macOS. `npm run pack` combines those native artifacts into the universal archive. Run `npm run prepare:profile` after changing the release version or icon.

Detailed release history lives in [CHANGELOG.md](./CHANGELOG.md). The project is available under the [MIT License](./LICENSE).
