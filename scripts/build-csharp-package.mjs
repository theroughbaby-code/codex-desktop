import { cp, mkdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import path from 'node:path';

const root = process.cwd();
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const pluginName = 'CodexDesktop';
const version = packageJson.version;
const distPath = path.join(root, 'dist');
const buildOutputPath = path.join(root, 'src-csharp', 'bin', 'Release', 'net10.0-windows', 'CodexDesktopPlugin.dll');
const packagePath = path.join(root, `${pluginName}-${version}.lplug4`);

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
  .replace(/^description:.*$/m, 'description: Controls ChatGPT and Codex Desktop on Windows from Logitech MX Creative Keypad.')
  .replace(/^pluginFolderMac:.*\r?\n?/m, '');
await writeFile(metadataPath, metadata);

const windowsOutputPath = path.join(distPath, 'win');
await mkdir(windowsOutputPath, { recursive: true });
await cp(buildOutputPath, path.join(windowsOutputPath, 'CodexDesktopPlugin.dll'));
await rm(packagePath, { force: true });

const result = spawnSync('LogiPluginTool', ['pack', distPath, packagePath], {
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

console.log(`Prepared versioned package with LogiPluginTool: ${packagePath}`);

async function pathExists(targetPath) {
  try {
    await stat(targetPath);
    return true;
  } catch {
    return false;
  }
}
