# Codex Desktop

Codex Desktop plugin for Logitech MX Keypad.

Logi Options+ plugin for controlling ChatGPT/Codex Desktop on Windows from an MX Creative Console Keypad.

## Status

Step 2 adds the first testable action: opening ChatGPT/Codex Desktop from the MX Creative Console Keypad.

## Package

- Display name: Codex Desktop
- Logi plugin id: CodexDesktop
- Version: 1.0.4
- Runtime: Node.js plugin through Logi Plugin Service
- Target device: MX Creative Console Keypad through Logi Options+

## Actions

| Action | Description |
| --- | --- |
| Open Codex | Finds `codex.exe` and runs `codex app`. |
| ChatGPT | Opens/focuses the desktop app and switches to ChatGPT mode. |
| Codex | Opens/focuses the desktop app and switches to Codex mode. |

## Codex Discovery

The plugin checks for Codex in this order:

1. `CODEX_CLI` environment variable, when set to a full `codex.exe` path.
2. `codex.exe` on `PATH`.
3. `%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe`, choosing the newest executable.

## Desktop Mode Switching

The ChatGPT/Codex switch actions use a packaged Windows UI Automation script:

```powershell
dist\scripts\switch-chatgpt-codex.ps1 -Mode ChatGPT
dist\scripts\switch-chatgpt-codex.ps1 -Mode Codex
```

OpenAI's current desktop documentation describes ChatGPT and Codex as selectable from the top-left menu. No public desktop-app API for changing that mode is documented, so these actions use the accessible Windows UI tree.

## Brand Assets

The plugin icon uses `OpenAI-black-monoblossom.png` from OpenAI's official 2025 logo package. OpenAI owns the OpenAI and ChatGPT marks; this plugin uses the mark to identify the service it controls and should not imply endorsement.

## Development

Install dependencies:

```powershell
npm install
```

Type-check:

```powershell
npm run typecheck
```

Verify Codex Desktop discovery:

```powershell
npm run doctor
```

Build:

```powershell
npm run build
```

Link to Logi Plugin Service:

```powershell
npm run link
```

Package as `.lplug4`:

```powershell
npm run build:pack
```

Verify that `pluginapi.dll` is absent from plugin assets and generated packages:

```powershell
npm run verify:no-pluginapi-dll
```

## Versioning

Start at `1.0.0`. Before each new build that should be tested or packaged, bump the patch version:

```powershell
npm run version:bump-patch
```
