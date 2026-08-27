import { spawn } from 'node:child_process';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

import { findCodexExecutable } from './locator';

export type ChatGptDesktopMode = 'ChatGPT' | 'Codex';

export async function switchChatGptDesktopMode(mode: ChatGptDesktopMode): Promise<void> {
  const codexPath = await findCodexExecutable();
  const distRoot = dirname(fileURLToPath(import.meta.url));
  const scriptPath = join(distRoot, 'scripts', 'switch-chatgpt-codex.ps1');

  const args = [
    '-NoProfile',
    '-ExecutionPolicy',
    'Bypass',
    '-File',
    scriptPath,
    '-Mode',
    mode
  ];

  if (codexPath) {
    args.push('-CodexPath', codexPath);
  }

  await new Promise<void>((resolve, reject) => {
    const child = spawn('powershell.exe', args, {
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true
    });

    let stderr = '';
    let stdout = '';

    child.stdout?.on('data', (chunk) => {
      stdout += chunk.toString();
    });

    child.stderr?.on('data', (chunk) => {
      stderr += chunk.toString();
    });

    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) {
        resolve();
        return;
      }

      const details = [stderr.trim(), stdout.trim()].filter(Boolean).join('\n');
      reject(new Error(details || `Mode switch failed with exit code ${code}.`));
    });
  });
}
