import { cp, mkdir, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { homedir } from 'node:os';
import path from 'node:path';

const root = process.cwd();
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const version = String(packageJson.version);
const pluginName = 'CodexDesktop';
const distPath = path.join(root, 'dist-mac-smoke');
const assemblyPath = path.join(root, 'src-csharp-mac', 'bin', 'Release', 'net8.0', 'CodexDesktopPlugin.dll');
const packagePath = path.join(root, `${pluginName}-${version}-mac-smoke.lplug4`);

if (process.platform !== 'darwin') {
  throw new Error(`Mac smoke package build requires macOS; current platform is ${process.platform}.`);
}

const dotnetPath = await resolveDotnet();
const logiPluginToolPath = await resolveLogiPluginTool();
const pluginApiPath = await resolvePluginApi(logiPluginToolPath);
const pluginApiDir = withTrailingSeparator(path.dirname(pluginApiPath));
const childEnv = childProcessEnvironment(dotnetPath);

console.log(`Using PluginApi.dll: ${pluginApiPath}`);

run('node', ['scripts/generate-csharp-shortcuts.mjs']);
run(dotnetPath, [
  'build',
  path.join(root, 'src-csharp-mac', 'CodexDesktopMacPlugin.csproj'),
  '--configuration', 'Release',
  `-p:PluginApiDir=${pluginApiDir}`,
  `-p:Version=${version}`,
  `-p:AssemblyVersion=${version}.0`,
  `-p:FileVersion=${version}.0`,
  `-p:InformationalVersion=${version}`,
], { env: childEnv });

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
metadata = metadata
  .replace(/^description:.*$/m, 'description: Controls ChatGPT and Codex Desktop on macOS from Logitech MX Creative Keypad.')
  .replace(/^pluginFolderWin:.*\r?\n?/m, '');
if (!/^pluginFolderMac:/m.test(metadata)) {
  metadata = metadata.replace(/^(pluginFileName:.*)$/m, '$1\npluginFolderMac: mac');
}
await writeFile(metadataPath, metadata);

const macDirectory = path.join(distPath, 'mac');
await mkdir(macDirectory, { recursive: true });
await cp(assemblyPath, path.join(macDirectory, 'CodexDesktopPlugin.dll'));

await rm(packagePath, { force: true });
run(dotnetPath, [logiPluginToolPath, 'pack', distPath, packagePath], { env: childEnv });
run(dotnetPath, [logiPluginToolPath, 'verify', packagePath], { env: childEnv });

const forbidden = [];
await scanForPluginApi(distPath, forbidden);
if (forbidden.length > 0) {
  throw new Error(`PluginApi.dll must not be included in the smoke package:\n${forbidden.join('\n')}`);
}

console.log(`Mac smoke package ready: ${packagePath}`);

function run(command, args, options = {}) {
  const result = spawnSync(command, args, {
    cwd: root,
    encoding: 'utf8',
    stdio: 'pipe',
    env: options.env ?? process.env,
  });

  if (result.stdout) process.stdout.write(result.stdout);
  if (result.stderr) process.stderr.write(result.stderr);
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`${command} ${args.join(' ')} exited with status ${result.status ?? 1}.`);
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

async function resolveDotnet() {
  const candidates = unique([
    process.env.DOTNET,
    ...findOnPath('dotnet'),
    path.join(homedir(), '.dotnet', 'dotnet'),
    '/usr/local/share/dotnet/dotnet',
    '/opt/homebrew/bin/dotnet',
  ]);

  for (const candidate of candidates) {
    if (await pathExists(candidate)) return candidate;
  }

  throw new Error('dotnet was not found. Install .NET 8 or set DOTNET to the absolute dotnet host path.');
}

async function resolveLogiPluginTool() {
  const toolStoreRoot = path.join(homedir(), '.dotnet', 'tools', '.store', 'logiplugintool');
  const toolDlls = await findFiles(
    toolStoreRoot,
    (filePath) => path.basename(filePath) === 'LogiPluginTool.dll'
      && filePath.includes(`${path.sep}tools${path.sep}net8.0${path.sep}any${path.sep}`),
    8,
    25,
  );
  const candidates = unique([
    process.env.LOGI_PLUGIN_TOOL_DLL,
    ...toolDlls.sort(compareLogiPluginToolPaths).reverse(),
  ]);

  for (const candidate of candidates) {
    if (await pathExists(candidate)) return candidate;
  }

  throw new Error('LogiPluginTool was not found in the local .NET tool store. Install the official LogiPluginTool before building the Mac smoke package.');
}

async function resolvePluginApi(logiPluginToolPath) {
  const pluginApiPath = path.join(path.dirname(logiPluginToolPath), 'PluginApi.dll');
  if (await pathExists(pluginApiPath)) return pluginApiPath;

  throw new Error(`The official LogiPluginTool package does not include PluginApi.dll next to ${logiPluginToolPath}.`);
}

function childProcessEnvironment(dotnetPath) {
  const dotnetRoot = path.dirname(dotnetPath);
  const toolPath = path.join(dotnetRoot, 'tools');
  return {
    ...process.env,
    DOTNET_ROOT: process.env.DOTNET_ROOT || dotnetRoot,
    PATH: unique([dotnetRoot, toolPath, ...(process.env.PATH ?? '').split(path.delimiter)])
      .filter(Boolean)
      .join(path.delimiter),
  };
}

function findOnPath(command) {
  return (process.env.PATH ?? '')
    .split(path.delimiter)
    .filter(Boolean)
    .map((directory) => path.join(directory, command));
}

async function findFiles(directoryPath, predicate, maxDepth, limit, depth = 0, results = []) {
  if (depth > maxDepth || results.length >= limit || !await pathExists(directoryPath)) {
    return results;
  }

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
  return filePath.match(/logiplugintool\/([^/]+)\//i)?.[1] ?? '0';
}

function compareDottedVersions(left, right) {
  const leftParts = left.split('.').map((part) => Number.parseInt(part, 10) || 0);
  const rightParts = right.split('.').map((part) => Number.parseInt(part, 10) || 0);
  const length = Math.max(leftParts.length, rightParts.length);
  for (let index = 0; index < length; index += 1) {
    const diff = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (diff !== 0) return diff;
  }
  return 0;
}

function withTrailingSeparator(directoryPath) {
  return directoryPath.endsWith(path.sep) ? directoryPath : `${directoryPath}${path.sep}`;
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}
