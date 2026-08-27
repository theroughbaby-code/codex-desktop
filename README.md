# Codex Desktop

Codex Desktop plugin for Logitech MX Keypad.

Logi Options+ plugin for controlling ChatGPT/Codex Desktop on Windows from an MX Creative Console Keypad.

## Status

Step 1 scaffold is intentionally minimal. It defines the Logi Node.js plugin package, metadata, build scripts, and asset folders. Step 2 will add the first testable action: opening ChatGPT/Codex Desktop.

## Package

- Display name: Codex Desktop
- Logi plugin id: CodexDesktop
- Version: 1.0.0
- Runtime: Node.js plugin through Logi Plugin Service
- Target device: MX Creative Console Keypad through Logi Options+

## Development

Install dependencies:

```powershell
npm install
```

Type-check:

```powershell
npm run typecheck
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

## Versioning

Start at `1.0.0`. Before each new build that should be tested or packaged, bump the patch version:

```powershell
npm run version:bump-patch
```
