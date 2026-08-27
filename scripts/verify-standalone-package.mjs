import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';

const root = process.cwd();
const directoriesToScan = ['package', 'dist'];
const findings = [];
const workspacePath = root.toLowerCase();
const workspacePathForward = workspacePath.replaceAll('\\', '/');

const forbiddenChecks = [
  {
    label: 'pluginapi.dll',
    needles: ['pluginapi.dll']
  },
  {
    label: 'local project path',
    needles: [
      workspacePath,
      workspacePathForward,
      'c:\\users\\theroughbaby\\desktop\\codex\\codex plugin',
      'c:/users/theroughbaby/desktop/codex/codex plugin'
    ]
  },
  {
    label: 'repository link',
    needles: [
      'github.com/theroughbaby-code/codex-desktop',
      'theroughbaby-code/codex-desktop'
    ]
  }
];

async function pathExists(targetPath) {
  try {
    await stat(targetPath);
    return true;
  } catch {
    return false;
  }
}

function checkText(label, source, text) {
  const lowerText = text.toLowerCase();

  for (const check of forbiddenChecks) {
    for (const needle of check.needles) {
      if (lowerText.includes(needle)) {
        findings.push(`${source}: ${check.label} (${needle})`);
      }
    }
  }
}

async function scanFile(filePath, displayPath) {
  const bytes = await readFile(filePath);
  const text = bytes.toString('latin1');
  checkText('file', displayPath, text);
}

async function scanDirectory(directory, baseDirectory = directory) {
  if (!await pathExists(directory)) {
    return;
  }

  const entries = await readdir(directory, { withFileTypes: true });

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);
    const relativePath = path.relative(baseDirectory, entryPath).replaceAll('\\', '/');
    checkText('path', relativePath, relativePath);

    if (entry.isDirectory()) {
      await scanDirectory(entryPath, baseDirectory);
      continue;
    }

    if (entry.isFile()) {
      await scanFile(entryPath, relativePath);
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
    const archiveText = archiveBytes.toString('latin1');

    checkText('archive name', entry.name, entry.name);
    checkText('archive bytes', entry.name, archiveText);
  }
}

for (const directory of directoriesToScan) {
  await scanDirectory(path.join(root, directory));
}

await scanPackageArchives();

if (findings.length > 0) {
  console.error('Standalone package verification failed.');
  for (const finding of findings) {
    console.error(`Found: ${finding}`);
  }
  process.exit(1);
}

console.log('Standalone package verification passed.');
