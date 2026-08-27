import { access } from 'node:fs/promises';
import { constants } from 'node:fs';
import { homedir } from 'node:os';
import path from 'node:path';

async function isExecutable(filePath: string): Promise<boolean> {
  try {
    await access(filePath, constants.X_OK);
    return true;
  } catch {
    return false;
  }
}

function getPathEntries(): string[] {
  return (process.env.PATH ?? '')
    .split(path.delimiter)
    .map((entry) => entry.trim())
    .filter(Boolean);
}

function getCodexBinRoots(): string[] {
  const roots = new Set<string>();
  const localAppData = process.env.LOCALAPPDATA;

  if (localAppData) {
    roots.add(path.join(localAppData, 'OpenAI', 'Codex', 'bin'));
  }

  roots.add(path.join(homedir(), 'AppData', 'Local', 'OpenAI', 'Codex', 'bin'));

  return [...roots];
}

async function findLatestCodexFromInstallRoot(root: string): Promise<string | null> {
  try {
    const { readdir, stat } = await import('node:fs/promises');
    const entries = await readdir(root, { withFileTypes: true });
    const candidates = [];

    for (const entry of entries) {
      if (!entry.isDirectory()) {
        continue;
      }

      const candidate = path.join(root, entry.name, 'codex.exe');
      if (await isExecutable(candidate)) {
        const info = await stat(candidate);
        candidates.push({ candidate, modifiedMs: info.mtimeMs });
      }
    }

    candidates.sort((left, right) => right.modifiedMs - left.modifiedMs);
    return candidates[0]?.candidate ?? null;
  } catch {
    return null;
  }
}

export async function findCodexExecutable(): Promise<string | null> {
  const configuredPath = process.env.CODEX_CLI;

  if (configuredPath && await isExecutable(configuredPath)) {
    return configuredPath;
  }

  for (const entry of getPathEntries()) {
    const candidate = path.join(entry, 'codex.exe');
    if (await isExecutable(candidate)) {
      return candidate;
    }
  }

  for (const root of getCodexBinRoots()) {
    const candidate = await findLatestCodexFromInstallRoot(root);
    if (candidate) {
      return candidate;
    }
  }

  return null;
}
