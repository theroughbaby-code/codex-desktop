import { readFile, writeFile } from 'node:fs/promises';

const packageJsonPath = new URL('../package.json', import.meta.url);
const metadataPath = new URL('../package/metadata/LoupedeckPackage.yaml', import.meta.url);

const packageJson = JSON.parse(await readFile(packageJsonPath, 'utf8'));
const [major, minor, patch = '0'] = String(packageJson.version).split('.');
const nextVersion = `${major}.${minor}.${Number(patch) + 1}`;

packageJson.version = nextVersion;
await writeFile(packageJsonPath, `${JSON.stringify(packageJson, null, 2)}\n`);

const metadata = await readFile(metadataPath, 'utf8');
const nextMetadata = metadata.replace(/^version:\s*.+$/m, `version: ${nextVersion}`);
await writeFile(metadataPath, nextMetadata);

console.log(`Codex Desktop version bumped to ${nextVersion}`);
