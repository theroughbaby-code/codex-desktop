import { access, readdir, stat } from 'node:fs/promises';
import { constants } from 'node:fs';
import { homedir } from 'node:os';
import path from 'node:path';

async function isExecutable(filePath) {
  try {
    await access(filePath, constants.X_OK);
    return true;
  } catch {
    return false;
  }
}

async function findLatestCodexFromInstallRoot(root) {
  try {
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

async function findCodexExecutable() {
  if (process.env.CODEX_CLI && await isExecutable(process.env.CODEX_CLI)) {
    return { source: 'CODEX_CLI', path: process.env.CODEX_CLI };
  }

  for (const entry of (process.env.PATH ?? '').split(path.delimiter).filter(Boolean)) {
    const candidate = path.join(entry, 'codex.exe');
    if (await isExecutable(candidate)) {
      return { source: 'PATH', path: candidate };
    }
  }

  const roots = [
    process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'OpenAI', 'Codex', 'bin'),
    path.join(homedir(), 'AppData', 'Local', 'OpenAI', 'Codex', 'bin')
  ].filter(Boolean);

  for (const root of roots) {
    const candidate = await findLatestCodexFromInstallRoot(root);
    if (candidate) {
      return { source: root, path: candidate };
    }
  }

  return null;
}

const codex = await findCodexExecutable();

if (!codex) {
  console.error('Codex Desktop check failed: could not find codex.exe.');
  console.error('Set CODEX_CLI to the full codex.exe path, or reinstall Codex Desktop.');
  process.exit(1);
}

console.log('Codex Desktop check passed.');
console.log(`Source: ${codex.source}`);
console.log(`Path: ${codex.path}`);
