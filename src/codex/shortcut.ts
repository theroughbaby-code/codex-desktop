import { spawn } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { findCodexExecutable } from './locator';

export type ChatGptShortcut = 'Companion' | 'Search' | 'Browser';

export async function triggerChatGptShortcut(shortcut: ChatGptShortcut): Promise<void> {
  const codexPath = await findCodexExecutable();
  const distRoot = dirname(fileURLToPath(import.meta.url));
  const scriptPath = join(distRoot, 'scripts', 'send-chatgpt-shortcut.ps1');
  const args = [
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    scriptPath,
    '-Shortcut',
    shortcut
  ];

  if (codexPath) {
    args.push('-CodexPath', codexPath);
  }

  const child = spawn('powershell.exe', args, {
    detached: true,
    stdio: 'ignore',
    windowsHide: true
  });

  child.unref();
}
