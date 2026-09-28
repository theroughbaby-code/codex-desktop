import { readdir, readFile, stat } from 'node:fs/promises';
import path from 'node:path';

const root = process.cwd();
const options = parseArguments(process.argv.slice(2));
const directory = resolvePath(options.directory);
const archive = resolvePath(options.archive);
const findings = [];
const workspacePath = root.toLowerCase();
const forbiddenChecks = [
  { label: 'PluginApi.dll', needles: ['pluginapi.dll'] },
  { label: 'local project path', needles: [workspacePath, workspacePath.replaceAll('\\', '/')] },
];

await requireDirectory(directory);
await requireFile(archive);
await scanDirectory(directory, directory);
await scanFile(archive, path.basename(archive));

if (findings.length > 0) {
  throw new Error(`Release output verification failed:\n- ${findings.join('\n- ')}`);
}

console.log(`Release output verified: ${archive}`);

function parseArguments(args) {
  const parsed = {};
  for (let index = 0; index < args.length; index += 2) {
    const key = args[index];
    const value = args[index + 1];
    if (!['--directory', '--archive'].includes(key) || !value || value.startsWith('--')) {
      throw new Error('Usage: node scripts/verify-release-output.mjs --directory <staged-directory> --archive <package.lplug4>');
    }
    parsed[key.slice(2)] = value;
  }
  if (!parsed.directory || !parsed.archive) {
    throw new Error('Both --directory and --archive are required.');
  }
  return parsed;
}

function resolvePath(value) {
  return path.isAbsolute(value) ? value : path.resolve(root, value);
}

async function scanDirectory(currentDirectory, baseDirectory) {
  for (const entry of await readdir(currentDirectory, { withFileTypes: true })) {
    const entryPath = path.join(currentDirectory, entry.name);
    const displayPath = path.relative(baseDirectory, entryPath).replaceAll('\\', '/');
    checkText(displayPath, displayPath);
    if (entry.isDirectory()) await scanDirectory(entryPath, baseDirectory);
    if (entry.isFile()) await scanFile(entryPath, displayPath);
  }
}

async function scanFile(filePath, displayPath) {
  const bytes = await readFile(filePath);
  checkText(displayPath, bytes.toString('latin1'));
}

function checkText(displayPath, source) {
  const lowerSource = source.toLowerCase();
  for (const check of forbiddenChecks) {
    for (const needle of new Set(check.needles)) {
      if (lowerSource.includes(needle)) findings.push(`${displayPath}: ${check.label}`);
    }
  }
}

async function requireDirectory(directoryPath) {
  try {
    if ((await stat(directoryPath)).isDirectory()) return;
  } catch {
    // Report a release-oriented error below.
  }
  throw new Error(`Release staging directory was not found at ${directoryPath}.`);
}

async function requireFile(filePath) {
  try {
    if ((await stat(filePath)).isFile()) return;
  } catch {
    // Report a release-oriented error below.
  }
  throw new Error(`Release package was not found at ${filePath}.`);
}
