import { mkdir, readdir, rm, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const root = process.cwd();
const dotnetRoot = 'C:\\Program Files\\dotnet';
const logiRoot = 'C:\\Program Files\\Logi\\LogiPluginService';
const targetFramework = 'net10.0-windows';
const outputDirectory = path.join(root, 'src-csharp', 'bin', 'Release', targetFramework);
const outputFile = path.join(outputDirectory, 'CodexDesktopPlugin.dll');

await rm(outputDirectory, { recursive: true, force: true });
await mkdir(outputDirectory, { recursive: true });

const sdkVersion = await newestDirectory(path.join(dotnetRoot, 'sdk'));
const cscPath = path.join(dotnetRoot, 'sdk', sdkVersion, 'Roslyn', 'bincore', 'csc.dll');
const runtimeVersion = await newestDirectory(path.join(dotnetRoot, 'shared', 'Microsoft.NETCore.App'));
const runtimeDirectory = path.join(dotnetRoot, 'shared', 'Microsoft.NETCore.App', runtimeVersion);
const desktopVersion = await newestDirectory(path.join(dotnetRoot, 'shared', 'Microsoft.WindowsDesktop.App'));
const desktopDirectory = path.join(dotnetRoot, 'shared', 'Microsoft.WindowsDesktop.App', desktopVersion);

const sources = (await readdir(path.join(root, 'src-csharp')))
  .filter((fileName) => fileName.endsWith('.cs'))
  .map((fileName) => path.join(root, 'src-csharp', fileName));

const references = [
  ...await dllsIn(runtimeDirectory),
  ...await dllsIn(desktopDirectory),
  ...await dllsIn(logiRoot),
];

const responseFile = path.join(outputDirectory, 'compile.rsp');
const responseLines = [
  '-noconfig',
  '-nostdlib+',
  '-target:library',
  '-langversion:latest',
  '-nullable:enable',
  '-optimize+',
  `-out:${quoteForResponseFile(outputFile)}`,
  ...references.map((reference) => `-reference:${quoteForResponseFile(reference)}`),
  ...sources.map(quoteForResponseFile),
];

await writeFile(responseFile, responseLines.join('\n'));

const result = spawnSync('dotnet', [
  cscPath,
  `@${responseFile}`,
], {
  cwd: root,
  encoding: 'utf8',
  stdio: 'pipe',
});

if (result.stdout) {
  process.stdout.write(result.stdout);
}

if (result.stderr) {
  process.stderr.write(result.stderr);
}

if (result.error) {
  console.error(result.error.message);
  process.exit(1);
}

if (result.status !== 0) {
  process.exit(result.status ?? 1);
}

console.log(`Compiled ${outputFile}`);

async function newestDirectory(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const names = entries
    .filter((entry) => entry.isDirectory())
    .map((entry) => entry.name)
    .sort(compareVersions)
    .reverse();

  if (!names.length) {
    throw new Error(`No versions found in ${directory}`);
  }

  return names[0];
}

async function dllsIn(directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  return entries
    .filter((entry) => entry.isFile() && entry.name.endsWith('.dll'))
    .filter((entry) => isManagedReferenceName(entry.name))
    .map((entry) => path.join(directory, entry.name));
}

function isManagedReferenceName(fileName) {
  const lowerName = fileName.toLowerCase();
  if (
    lowerName.includes('native') ||
    lowerName.endsWith('_cor3.dll') ||
    lowerName.startsWith('api-ms-') ||
    lowerName.startsWith('mscordaccore_') ||
    [
      'clrjit.dll',
      'clretwrc.dll',
      'clrgc.dll',
      'clrgcexp.dll',
      'coreclr.dll',
      'd3dcompiler_47_cor3.dll',
      'e_sqlite3.dll',
      'hostfxr.dll',
      'hostpolicy.dll',
      'libharfbuzzsharp.dll',
      'libskiasharp.dll',
      'msalruntime.dll',
      'mscordaccore.dll',
      'mscordbi.dll',
      'mscorrc.dll',
      'msquic.dll',
      'msvcp140.dll',
      'penimc_cor3.dll',
      'presentationnative_cor3.dll',
      'vcruntime140.dll',
      'vcruntime140_1.dll',
      'vcruntime140_cor3.dll',
      'webview2loader.dll',
      'wpfgfx_cor3.dll',
    ].includes(lowerName)
  ) {
    return false;
  }

  return true;
}

function compareVersions(left, right) {
  const leftParts = left.split('.').map(Number);
  const rightParts = right.split('.').map(Number);
  const length = Math.max(leftParts.length, rightParts.length);

  for (let index = 0; index < length; index += 1) {
    const difference = (leftParts[index] ?? 0) - (rightParts[index] ?? 0);
    if (difference !== 0) {
      return difference;
    }
  }

  return 0;
}

function quoteForResponseFile(value) {
  return `"${value.replaceAll('"', '\\"')}"`;
}
