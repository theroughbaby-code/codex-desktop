import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';

const forbiddenName = 'pluginapi.dll';
const root = process.cwd();
const directoriesToScan = ['package', 'dist'];
const findings = [];

async function pathExists(targetPath) {
  try {
    await stat(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function scanDirectory(directory) {
  if (!await pathExists(directory)) {
    return;
  }

  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);

    if (entry.name.toLowerCase() === forbiddenName) {
      findings.push(entryPath);
    }

    if (entry.isDirectory()) {
      await scanDirectory(entryPath);
    }
  }
}

async function scanPackageArchives() {
  const entries = await readdir(root, { withFileTypes: true });

  for (const entry of entries) {
    if (!entry.isFile() || !entry.name.toLowerCase().endsWith('.lplug4')) {
      continue;
    }

    const archivePath = path.join(root, entry.name);
    const archiveBytes = await readFile(archivePath);
    const archiveText = archiveBytes.toString('latin1').toLowerCase();

    if (archiveText.includes(forbiddenName)) {
      findings.push(archivePath);
    }
  }
}

for (const directory of directoriesToScan) {
  await scanDirectory(path.join(root, directory));
}

await scanPackageArchives();

if (findings.length > 0) {
  console.error(`${forbiddenName} must not be included in this plugin package.`);
  for (const finding of findings) {
    console.error(`Found: ${finding}`);
  }
  process.exit(1);
}

console.log(`${forbiddenName} check passed.`);
