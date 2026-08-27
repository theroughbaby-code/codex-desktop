import { spawn } from 'node:child_process';

import { findCodexExecutable } from './locator';

export async function openCodexDesktop(): Promise<void> {
  const codexPath = await findCodexExecutable();

  if (!codexPath) {
    throw new Error(
      'Could not find codex.exe. Set CODEX_CLI to the full codex.exe path or reinstall Codex Desktop.'
    );
  }

  const child = spawn(codexPath, ['app'], {
    detached: true,
    stdio: 'ignore',
    windowsHide: true
  });

  child.unref();
}
