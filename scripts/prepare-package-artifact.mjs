import { rm, rename, stat } from 'node:fs/promises';
import path from 'node:path';

const root = process.cwd();
const packageJson = await import(new URL('../package.json', import.meta.url), {
  with: { type: 'json' }
});

const pluginName = 'CodexDesktop';
const version = packageJson.default.version;
const sourcePath = path.join(root, `${pluginName}.lplug4`);
const versionedPath = path.join(root, `${pluginName}-${version}.lplug4`);

try {
  await stat(sourcePath);
} catch {
  console.error(`Expected package not found: ${sourcePath}`);
  process.exit(1);
}

await rm(versionedPath, { force: true });
await rename(sourcePath, versionedPath);

console.log(`Prepared versioned package: ${versionedPath}`);
