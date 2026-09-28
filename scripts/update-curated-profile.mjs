import { readFile, writeFile } from 'node:fs/promises';
import { deflateRawSync, inflateRawSync } from 'node:zlib';
import path from 'node:path';

const root = process.cwd();
const profilePath = path.join(root, 'package', 'profiles', 'DefaultProfile70.lp5');
const iconPath = path.join(root, 'package', 'metadata', 'Icon256x256.png');
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const entries = readZip(await readFile(profilePath));
const crcTable = createCrcTable();

const profileInfo = readJson(entries, 'ProfileInfo.json');
profileInfo.description = 'Nine essential Codex Desktop actions for Logitech MX Creative Keypad.';
profileInfo.additionalNativePluginNames = [];
entries.set('ProfileInfo.json', jsonBytes(profileInfo));

const applicationInfo = readJson(entries, 'ApplicationInfo.json');
applicationInfo.description = 'Controls ChatGPT and Codex Desktop on Windows and macOS.';
entries.set('ApplicationInfo.json', jsonBytes(applicationInfo));

const metadataName = 'metadata/LoupedeckPackage.yaml';
const metadata = entries.get(metadataName)?.toString('utf8');
if (!metadata) {
  throw new Error(`Missing ${metadataName} in ${profilePath}.`);
}
entries.set(
  metadataName,
  Buffer.from(setYamlValue(metadata, 'version', String(packageJson.version)), 'utf8'),
);
entries.set('ApplicationIcon.png', await readFile(iconPath));

await writeFile(profilePath, writeZip(entries));
console.log(`Updated curated profile metadata and embedded icon for ${packageJson.version}.`);

function readJson(entriesMap, name) {
  const bytes = entriesMap.get(name);
  if (!bytes) throw new Error(`Missing ${name} in ${profilePath}.`);
  return JSON.parse(bytes.toString('utf8'));
}

function jsonBytes(value) {
  return Buffer.from(`${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

function setYamlValue(source, key, value) {
  const expression = new RegExp(`^${key}:.*$`, 'm');
  if (!expression.test(source)) throw new Error(`Missing '${key}' in profile metadata.`);
  return source.replace(expression, `${key}: ${value}`);
}

function readZip(bytes) {
  const endOffset = findEndOfCentralDirectory(bytes);
  const entryCount = bytes.readUInt16LE(endOffset + 10);
  let offset = bytes.readUInt32LE(endOffset + 16);
  const result = new Map();

  for (let index = 0; index < entryCount; index += 1) {
    expectSignature(bytes, offset, 0x02014b50, 'central directory');
    const compressionMethod = bytes.readUInt16LE(offset + 10);
    const compressedSize = bytes.readUInt32LE(offset + 20);
    const uncompressedSize = bytes.readUInt32LE(offset + 24);
    const nameLength = bytes.readUInt16LE(offset + 28);
    const extraLength = bytes.readUInt16LE(offset + 30);
    const commentLength = bytes.readUInt16LE(offset + 32);
    const localHeaderOffset = bytes.readUInt32LE(offset + 42);
    const name = bytes.subarray(offset + 46, offset + 46 + nameLength).toString('utf8');

    expectSignature(bytes, localHeaderOffset, 0x04034b50, `local entry ${name}`);
    const localNameLength = bytes.readUInt16LE(localHeaderOffset + 26);
    const localExtraLength = bytes.readUInt16LE(localHeaderOffset + 28);
    const dataOffset = localHeaderOffset + 30 + localNameLength + localExtraLength;
    const compressed = bytes.subarray(dataOffset, dataOffset + compressedSize);
    const content = compressionMethod === 0
      ? Buffer.from(compressed)
      : compressionMethod === 8
        ? inflateRawSync(compressed)
        : throwUnsupportedCompression(name, compressionMethod);
    if (content.length !== uncompressedSize) {
      throw new Error(`Unexpected uncompressed size for ${name}.`);
    }
    if (!name.endsWith('/')) result.set(name, content);
    offset += 46 + nameLength + extraLength + commentLength;
  }

  return result;
}

function writeZip(entriesMap) {
  const chunks = [];
  const centralDirectory = [];
  let offset = 0;

  for (const [nameText, data] of [...entriesMap.entries()].sort(([left], [right]) => left.localeCompare(right))) {
    const name = Buffer.from(nameText, 'utf8');
    const compressed = deflateRawSync(data, { level: 9 });
    const crc = crc32(data);
    const localHeader = Buffer.alloc(30);
    localHeader.writeUInt32LE(0x04034b50, 0);
    localHeader.writeUInt16LE(20, 4);
    localHeader.writeUInt16LE(0x0800, 6);
    localHeader.writeUInt16LE(8, 8);
    localHeader.writeUInt32LE(crc, 14);
    localHeader.writeUInt32LE(compressed.length, 18);
    localHeader.writeUInt32LE(data.length, 22);
    localHeader.writeUInt16LE(name.length, 26);
    chunks.push(localHeader, name, compressed);
    centralDirectory.push({ name, crc, compressedSize: compressed.length, size: data.length, offset });
    offset += localHeader.length + name.length + compressed.length;
  }

  const centralStart = offset;
  for (const record of centralDirectory) {
    const header = Buffer.alloc(46);
    header.writeUInt32LE(0x02014b50, 0);
    header.writeUInt16LE(20, 4);
    header.writeUInt16LE(20, 6);
    header.writeUInt16LE(0x0800, 8);
    header.writeUInt16LE(8, 10);
    header.writeUInt32LE(record.crc, 16);
    header.writeUInt32LE(record.compressedSize, 20);
    header.writeUInt32LE(record.size, 24);
    header.writeUInt16LE(record.name.length, 28);
    header.writeUInt32LE(record.offset, 42);
    chunks.push(header, record.name);
    offset += header.length + record.name.length;
  }

  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(centralDirectory.length, 8);
  end.writeUInt16LE(centralDirectory.length, 10);
  end.writeUInt32LE(offset - centralStart, 12);
  end.writeUInt32LE(centralStart, 16);
  chunks.push(end);
  return Buffer.concat(chunks);
}

function findEndOfCentralDirectory(bytes) {
  const minimum = Math.max(0, bytes.length - 65_557);
  for (let offset = bytes.length - 22; offset >= minimum; offset -= 1) {
    if (bytes.readUInt32LE(offset) === 0x06054b50) return offset;
  }
  throw new Error(`Unable to locate ZIP central directory in ${profilePath}.`);
}

function expectSignature(bytes, offset, expected, label) {
  if (offset < 0 || offset + 4 > bytes.length || bytes.readUInt32LE(offset) !== expected) {
    throw new Error(`Invalid ${label} ZIP signature.`);
  }
}

function throwUnsupportedCompression(name, method) {
  throw new Error(`Unsupported ZIP compression method ${method} for ${name}.`);
}

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) crc = (crc >>> 8) ^ crcTable[(crc ^ byte) & 0xff];
  return (crc ^ 0xffffffff) >>> 0;
}

function createCrcTable() {
  const table = new Uint32Array(256);
  for (let value = 0; value < 256; value += 1) {
    let current = value;
    for (let bit = 0; bit < 8; bit += 1) {
      current = current & 1 ? 0xedb88320 ^ (current >>> 1) : current >>> 1;
    }
    table[value] = current >>> 0;
  }
  return table;
}
