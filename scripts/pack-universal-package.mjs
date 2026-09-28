import { cp, mkdir, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { homedir } from 'node:os';
import path from 'node:path';

const root = process.cwd();
const options = parseArguments(process.argv.slice(2));
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const version = String(packageJson.version);
const pluginName = 'CodexDesktop';
const pluginFileName = 'CodexDesktopPlugin.dll';

if (!/^\d+\.\d+\.\d+$/.test(version)) {
  throw new Error(`Expected a three-part numeric package version, received '${version}'.`);
}

const windowsDll = resolveInputPath(
  options.windowsDll ?? process.env.WINDOWS_PLUGIN_DLL ?? path.join('artifacts', 'win', pluginFileName),
);
const macDll = resolveInputPath(
  options.macDll ?? process.env.MAC_PLUGIN_DLL ?? path.join('artifacts', 'mac', pluginFileName),
);
const distPath = path.join(root, 'dist-universal');
const packagePath = resolveInputPath(
  options.output ?? `${pluginName}-${version}.lplug4`,
);

await requireFile(windowsDll, 'Windows plugin artifact');
await requireFile(macDll, 'macOS plugin artifact');

await rm(distPath, { recursive: true, force: true });
await mkdir(distPath, { recursive: true });

for (const directory of ['metadata', 'actionicons', 'actionsymbols', 'assets', 'profiles']) {
  const source = path.join(root, 'package', directory);
  if (await pathExists(source)) {
    await cp(source, path.join(distPath, directory), { recursive: true });
  }
}

await cp(path.join(root, 'LICENSE'), path.join(distPath, 'LICENSE'));

const metadataPath = path.join(distPath, 'metadata', 'LoupedeckPackage.yaml');
let metadata = await readFile(metadataPath, 'utf8');
const metadataVersion = metadata.match(/^version:\s*(.+?)\s*$/m)?.[1];
if (metadataVersion !== version) {
  throw new Error(`Package metadata version '${metadataVersion ?? 'missing'}' does not match package.json '${version}'.`);
}
const metadataPluginFileName = metadata.match(/^pluginFileName:\s*(.+?)\s*$/m)?.[1];
if (metadataPluginFileName !== pluginFileName) {
  throw new Error(`Package metadata pluginFileName must be '${pluginFileName}'.`);
}
metadata = setYamlValue(metadata, 'description', packageJson.description);
metadata = setYamlValue(metadata, 'pluginFolderWin', 'win');
metadata = setYamlValue(metadata, 'pluginFolderMac', 'mac');
await writeFile(metadataPath, metadata);

const windowsDirectory = path.join(distPath, 'win');
const macDirectory = path.join(distPath, 'mac');
await mkdir(windowsDirectory, { recursive: true });
await mkdir(macDirectory, { recursive: true });
await cp(windowsDll, path.join(windowsDirectory, pluginFileName));
await cp(macDll, path.join(macDirectory, pluginFileName));

const forbidden = [];
await scanForPluginApi(distPath, forbidden);
if (forbidden.length > 0) {
  throw new Error(`PluginApi.dll must not be included in the release package:\n${forbidden.join('\n')}`);
}

const tool = await resolveLogiPluginTool();
await rm(packagePath, { force: true });
run(tool.command, [...tool.prefixArguments, 'pack', distPath, packagePath]);
run(tool.command, [...tool.prefixArguments, 'verify', packagePath]);
run(process.execPath, [path.join(root, 'scripts', 'verify-standalone-package.mjs')]);

console.log(`Universal Windows + macOS package ready: ${packagePath}`);

function parseArguments(argumentsList) {
  const parsed = {};
  for (let index = 0; index < argumentsList.length; index += 1) {
    const argument = argumentsList[index];
    const value = argumentsList[index + 1];
    if (!['--windows-dll', '--mac-dll', '--output'].includes(argument) || !value || value.startsWith('--')) {
      throw new Error(
        'Usage: npm run pack:universal -- --windows-dll <path> --mac-dll <path> [--output <path>]',
      );
    }

    const key = {
      '--windows-dll': 'windowsDll',
      '--mac-dll': 'macDll',
      '--output': 'output',
    }[argument];
    parsed[key] = value;
    index += 1;
  }
  return parsed;
}

function resolveInputPath(value) {
  return path.isAbsolute(value) ? value : path.resolve(root, value);
}

function setYamlValue(source, key, value) {
  const line = `${key}: ${value}`;
  const expression = new RegExp(`^${key}:.*$`, 'm');
  if (expression.test(source)) {
    return source.replace(expression, line);
  }

  const anchor = key.startsWith('pluginFolder') ? /^pluginFileName:.*$/m : /^displayName:.*$/m;
  if (!anchor.test(source)) {
    throw new Error(`Unable to add '${key}' to package metadata.`);
  }
  return source.replace(anchor, (match) => `${match}\n${line}`);
}

function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    stdio: 'pipe',
  });

  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(' ')} exited with status ${result.status ?? 1}.`);
  }
}

async function resolveLogiPluginTool() {
  const directCandidates = unique([
    process.env.LOGI_PLUGIN_TOOL,
    ...findOnPath(process.platform === 'win32' ? 'LogiPluginTool.exe' : 'LogiPluginTool'),
    ...findOnPath(process.platform === 'win32' ? 'logiplugintool.exe' : 'logiplugintool'),
  ]);
  for (const candidate of directCandidates) {
    if (await pathExists(candidate)) {
      return { command: candidate, prefixArguments: [] };
    }
  }

  const toolStoreRoot = path.join(homedir(), '.dotnet', 'tools', '.store', 'logiplugintool');
  const toolDlls = await findFiles(
    toolStoreRoot,
    (filePath) => path.basename(filePath) === 'LogiPluginTool.dll'
      && filePath.includes(`${path.sep}tools${path.sep}net8.0${path.sep}any${path.sep}`),
    8,
    25,
  );
  const dllCandidates = unique([
    process.env.LOGI_PLUGIN_TOOL_DLL,
    ...toolDlls.sort(compareLogiPluginToolPaths).reverse(),
  ]);
  const toolDll = await firstExisting(dllCandidates);
  if (!toolDll) {
    throw new Error('LogiPluginTool was not found. Install the official .NET tool or set LOGI_PLUGIN_TOOL_DLL.');
  }

  const dotnet = await firstExisting(unique([
    process.env.DOTNET,
    ...findOnPath(process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'),
    path.join(homedir(), '.dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'),
    '/usr/local/share/dotnet/dotnet',
    '/opt/homebrew/bin/dotnet',
  ]));
  if (!dotnet) {
    throw new Error('dotnet was not found. Install the .NET 8 SDK or set DOTNET.');
  }

  return { command: dotnet, prefixArguments: [toolDll] };
}

async function firstExisting(candidates) {
  for (const candidate of candidates) {
    if (await pathExists(candidate)) return candidate;
  }
  return null;
}

async function requireFile(filePath, label) {
  try {
    const info = await stat(filePath);
    if (info.isFile()) return;
  } catch {
    // Report the release-oriented error below.
  }
  throw new Error(`${label} was not found at ${filePath}. Build or copy both platform artifacts before packing.`);
}

async function pathExists(targetPath) {
  try {
    await stat(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function scanForPluginApi(directory, findings) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const entryPath = path.join(directory, entry.name);
    if (entry.name.toLowerCase() === 'pluginapi.dll') findings.push(entryPath);
    if (entry.isDirectory()) await scanForPluginApi(entryPath, findings);
  }
}

function findOnPath(command) {
  return (process.env.PATH ?? '')
    .split(path.delimiter)
    .filter(Boolean)
    .map((directory) => path.join(directory, command));
}

async function findFiles(directoryPath, predicate, maxDepth, limit, depth = 0, results = []) {
  if (depth > maxDepth || results.length >= limit || !await pathExists(directoryPath)) return results;
  let entries;
  try {
    entries = await readdir(directoryPath, { withFileTypes: true });
  } catch {
    return results;
  }
  for (const entry of entries) {
    if (results.length >= limit) break;
    const entryPath = path.join(directoryPath, entry.name);
    if (entry.isFile() && predicate(entryPath)) {
      results.push(entryPath);
    } else if (entry.isDirectory() && !entry.isSymbolicLink()) {
      await findFiles(entryPath, predicate, maxDepth, limit, depth + 1, results);
    }
  }
  return results;
}

function compareLogiPluginToolPaths(left, right) {
  return compareDottedVersions(versionFromLogiPluginToolPath(left), versionFromLogiPluginToolPath(right));
}

function versionFromLogiPluginToolPath(filePath) {
  return filePath.match(/logiplugintool[\\/]([^\\/]+)[\\/]/i)?.[1] ?? '0';
}

function compareDottedVersions(left, right) {
  const leftParts = left.split('.').map((part) => Number.parseInt(part, 10) || 0);
  const rightParts = right.split('.').map((part) => Number.parseInt(part, 10) || 0);
  const length = Math.max(leftParts.length, rightParts.length);
  for (let index = 0; index < length; index += 1) {
    const difference = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (difference !== 0) return difference;
  }
  return 0;
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}
