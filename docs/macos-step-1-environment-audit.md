# macOS Step 1: Environment Audit

This audit records the Mac identities and runtime paths needed before adding a `mac/` plugin assembly. It is read-only and does not install, uninstall, or modify ChatGPT, Codex, Logi Options+, or Logi Plugin Service.

## Prerequisites

- A Mac with the current ChatGPT desktop app installed.
- Logi Options+ and the MX Creative Keypad connected.
- Node.js 22 or newer.
- Keep ChatGPT and Logi Options+ open while collecting the report.

## Run

From the project folder on the Mac:

```sh
npm run audit:mac
```

The command writes JSON and Markdown reports to `output/macos-audit/`. The report contains no account credentials or general environment-variable dump.

## What It Detects

- macOS version, build, hardware model, and CPU architecture.
- ChatGPT/Codex application path, bundle identifier, version, executable architecture, URL schemes, and code-signing identity.
- Whether the installed app includes an AppleScript scripting dictionary.
- Codex CLI location, version, and `codex app` availability.
- Logi Options+ and Logi Plugin Service process names.
- `LogiPluginTool`, `PluginApi.dll`, plugin installation directories, recent log filenames, and `.lp5` profile files.
- Node.js and .NET installations available for building the Mac assembly.

## Manual Checks

The following checks require the Mac UI and are intentionally not automated:

1. Press a state-aware plugin action once and confirm macOS prompts for **LogiPluginService**. If it does not, open **System Settings > Privacy & Security > Accessibility**, enable **LogiPluginService**, or add `/Applications/Utilities/LogiPluginService.app` with the **+** button. ChatGPT itself is not the requesting process. Restart Logi Options+ after changing the permission.
2. In Logi Options+, create or export a minimal profile adapted to ChatGPT. Keep its `.lp5` file with the generated report.
3. Confirm that bringing ChatGPT to the foreground causes Options+ to select the ChatGPT application profile.
4. After the first Mac test plugin is generated, install it with `LogiPluginTool` and confirm one basic shortcut action reaches ChatGPT.

## Completion Criteria

Step 1 is complete when the report identifies the ChatGPT bundle and process, the Mac Logi runtime and `PluginApi.dll`, a usable build runtime, and a working application-linked sample action on the physical Keypad.
