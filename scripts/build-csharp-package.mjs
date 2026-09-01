import { cp, mkdir, readdir, readFile, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = process.cwd();
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const pluginName = 'CodexDesktop';
const version = packageJson.version;
const distPath = path.join(root, 'dist');
const buildOutputPath = path.join(root, 'src-csharp', 'bin', 'Release', 'net10.0-windows', 'CodexDesktopPlugin.dll');
const packagePath = path.join(root, `${pluginName}-${version}.lplug4`);
const crcTable = createCrcTable();

await rm(distPath, { recursive: true, force: true });
await mkdir(distPath, { recursive: true });

for (const directory of ['metadata', 'actionicons', 'actionsymbols', 'assets', 'profiles']) {
  const source = path.join(root, 'package', directory);
  if (await pathExists(source)) {
    await cp(source, path.join(distPath, directory), { recursive: true });
  }
}

const windowsOutputPath = path.join(distPath, 'win');
await mkdir(windowsOutputPath, { recursive: true });
await cp(buildOutputPath, path.join(windowsOutputPath, 'CodexDesktopPlugin.dll'));
await rm(packagePath, { force: true });
await writeZipFromDirectory(distPath, packagePath);

console.log(`Prepared versioned package: ${packagePath}`);

async function pathExists(targetPath) {
  try {
    await stat(targetPath);
    return true;
  } catch {
    return false;
  }
}

async function listFiles(directory, baseDirectory = directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await listFiles(entryPath, baseDirectory));
      continue;
    }

    if (entry.isFile()) {
      files.push({
        absolutePath: entryPath,
        archivePath: path.relative(baseDirectory, entryPath).replaceAll('\\', '/'),
      });
    }
  }

  return files;
}

async function writeZipFromDirectory(directory, outputPath) {
  const files = await listFiles(directory);
  const chunks = [];
  const centralDirectory = [];
  let offset = 0;

  for (const file of files) {
    const data = await readFile(file.absolutePath);
    const name = Buffer.from(file.archivePath, 'utf8');
    const crc = crc32(data);
    const localHeader = Buffer.alloc(30);

    localHeader.writeUInt32LE(0x04034b50, 0);
    localHeader.writeUInt16LE(20, 4);
    localHeader.writeUInt16LE(0x0800, 6);
    localHeader.writeUInt16LE(0, 8);
    localHeader.writeUInt16LE(0, 10);
    localHeader.writeUInt16LE(0, 12);
    localHeader.writeUInt32LE(crc, 14);
    localHeader.writeUInt32LE(data.length, 18);
    localHeader.writeUInt32LE(data.length, 22);
    localHeader.writeUInt16LE(name.length, 26);
    localHeader.writeUInt16LE(0, 28);

    chunks.push(localHeader, name, data);
    centralDirectory.push({ file, name, crc, size: data.length, offset });
    offset += localHeader.length + name.length + data.length;
  }

  const centralStart = offset;
  for (const record of centralDirectory) {
    const header = Buffer.alloc(46);
    header.writeUInt32LE(0x02014b50, 0);
    header.writeUInt16LE(20, 4);
    header.writeUInt16LE(20, 6);
    header.writeUInt16LE(0x0800, 8);
    header.writeUInt16LE(0, 10);
    header.writeUInt16LE(0, 12);
    header.writeUInt16LE(0, 14);
    header.writeUInt32LE(record.crc, 16);
    header.writeUInt32LE(record.size, 20);
    header.writeUInt32LE(record.size, 24);
    header.writeUInt16LE(record.name.length, 28);
    header.writeUInt16LE(0, 30);
    header.writeUInt16LE(0, 32);
    header.writeUInt16LE(0, 34);
    header.writeUInt16LE(0, 36);
    header.writeUInt32LE(0, 38);
    header.writeUInt32LE(record.offset, 42);
    chunks.push(header, record.name);
    offset += header.length + record.name.length;
  }

  const centralSize = offset - centralStart;
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(0, 4);
  end.writeUInt16LE(0, 6);
  end.writeUInt16LE(centralDirectory.length, 8);
  end.writeUInt16LE(centralDirectory.length, 10);
  end.writeUInt32LE(centralSize, 12);
  end.writeUInt32LE(centralStart, 16);
  end.writeUInt16LE(0, 20);
  chunks.push(end);

  await writeFile(outputPath, Buffer.concat(chunks));
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc = (crc >>> 8) ^ crcTable[(crc ^ byte) & 0xff];
  }

  return (crc ^ 0xffffffff) >>> 0;
}

function createCrcTable() {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n += 1) {
    let c = n;
    for (let k = 0; k < 8; k += 1) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[n] = c >>> 0;
  }

  return table;
}
