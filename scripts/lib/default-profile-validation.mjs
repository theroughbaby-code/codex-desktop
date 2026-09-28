import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { inflateRawSync } from 'node:zlib';
import path from 'node:path';

export const defaultProfileActionTypes = Object.freeze([
  'Loupedeck.CodexDesktopPlugin.NewChatCommand',
  'Loupedeck.CodexDesktopPlugin.OpenCodexCommand',
  'Loupedeck.CodexDesktopPlugin.SettingsCommand',
  'Loupedeck.CodexDesktopPlugin.NewStandaloneChatCommand',
  'Loupedeck.CodexDesktopPlugin.UsageStatusCommand',
  'Loupedeck.CodexDesktopPlugin.OpenModelPickerCommand',
  'Loupedeck.CodexDesktopPlugin.ApproveRequestCommand',
  'Loupedeck.CodexDesktopPlugin.StopThinkingCommand',
  'Loupedeck.CodexDesktopPlugin.DenyRequestCommand',
]);

const actionPrefix = '$CodexDesktop___';

export function verifyDefaultProfile(profilePath, expectedVersion) {
  const entries = readZipEntries(profilePath);
  const applicationInfo = readJsonEntry(entries, profilePath, 'ApplicationInfo.json');
  const profileInfo = readJsonEntry(entries, profilePath, 'ProfileInfo.json');
  const advancedInfo = readJsonEntry(entries, profilePath, 'metadata/AdvancedInfo.json');
  const profileMetadata = readTextEntry(entries, profilePath, 'metadata/LoupedeckPackage.yaml');
  const applicationIcon = entries.get('ApplicationIcon.png');
  const errors = [];

  expect(applicationInfo.name === '@_codexdesktop', 'application name must be @_codexdesktop');
  expect(applicationInfo.deviceType === 'Loupedeck70', 'application profile must target MX Creative Keypad');
  expect(applicationInfo.nativePluginName === 'CodexDesktop', 'application must use the CodexDesktop native plugin');
  expect(applicationInfo.hasNativePlugin === true, 'application must declare its native plugin');
  expect(applicationInfo.processOrBundleName === 'ChatGPT', 'application must bind to the ChatGPT desktop process');
  expect(profileInfo.name === applicationInfo.defaultProfileName, 'default profile ID must match ProfileInfo');
  expect(profileInfo.applicationName === applicationInfo.name, 'profile must reference the Codex application');
  expect(profileInfo.nativePluginName === 'CodexDesktop', 'profile must use the CodexDesktop native plugin');
  expect(profileInfo.hasNativePlugin === true, 'profile must declare its native plugin');
  expect(profileInfo.deviceType === 'Loupedeck70', 'profile must target MX Creative Keypad');
  expect(applicationIcon?.length > 0, 'default profile must contain ApplicationIcon.png');

  const additionalNativePlugins = profileInfo.additionalNativePluginNames ?? [];
  expect(Array.isArray(additionalNativePlugins), 'additionalNativePluginNames must be an array');
  if (Array.isArray(additionalNativePlugins)) {
    expect(
      additionalNativePlugins.length === 0,
      `additionalNativePluginNames must be empty, found: ${additionalNativePlugins.join(', ')}`,
    );
  }

  expect(
    Array.isArray(advancedInfo.additionalPluginNames)
      && advancedInfo.additionalPluginNames.length === 1
      && advancedInfo.additionalPluginNames[0] === 'CodexDesktop',
    'AdvancedInfo additionalPluginNames must contain only CodexDesktop',
  );

  const internalProfileVersion = yamlScalar(profileMetadata, 'version');
  expect(
    internalProfileVersion === expectedVersion,
    `internal profile metadata version '${internalProfileVersion ?? 'missing'}' must match package version '${expectedVersion}'`,
  );
  expect(yamlScalar(profileMetadata, 'type') === 'Profile5', 'internal profile metadata type must be Profile5');
  expect(
    yamlScalar(profileMetadata, 'name') === profileInfo.name,
    'internal profile metadata name must match the profile ID',
  );

  const controls = profileInfo.layout?.layoutModes
    ?.flatMap((mode) => mode.workspaces ?? [])
    .flatMap((workspace) => workspace.pressPages ?? [])
    .flatMap((page) => page.controls ?? []) ?? [];
  const assignedActions = controls.flatMap((control) => [control.pressAction, control.rotateAction])
    .filter((action) => typeof action === 'string');
  const expectedActions = defaultProfileActionTypes.map((typeName) => `${actionPrefix}${typeName}`);
  const actualActionSet = new Set(assignedActions);
  const expectedActionSet = new Set(expectedActions);

  expect(controls.length === 9, `default profile must contain exactly 9 assigned controls, found ${controls.length}`);
  expect(
    controls.every((control) => Number.isInteger(control.controlId))
      && new Set(controls.map((control) => control.controlId)).size === controls.length,
    'default profile control IDs must be unique integers',
  );
  expect(
    assignedActions.length === 9 && actualActionSet.size === 9,
    `default profile must assign exactly 9 unique actions, found ${assignedActions.length} assignments and ${actualActionSet.size} unique actions`,
  );

  const missingActions = expectedActions.filter((action) => !actualActionSet.has(action));
  const unexpectedActions = assignedActions.filter((action) => !expectedActionSet.has(action));
  expect(missingActions.length === 0, `default profile is missing actions: ${missingActions.join(', ')}`);
  expect(unexpectedActions.length === 0, `default profile contains unexpected actions: ${unexpectedActions.join(', ')}`);
  expect(
    Array.isArray(profileInfo.profileActions) && profileInfo.profileActions.length === 0,
    'default profile must not contain hidden profile actions',
  );

  if (errors.length > 0) {
    throw new Error(`Default profile verification failed for ${profilePath}:\n- ${errors.join('\n- ')}`);
  }

  return {
    actionTypes: [...defaultProfileActionTypes],
    controlCount: controls.length,
    internalProfileVersion,
    applicationIconSha256: applicationIcon ? sha256(applicationIcon) : null,
  };

  function expect(condition, message) {
    if (!condition) errors.push(message);
  }
}

function sha256(bytes) {
  return createHash('sha256').update(bytes).digest('hex');
}

function readJsonEntry(entries, profilePath, entryName) {
  return JSON.parse(readTextEntry(entries, profilePath, entryName));
}

function readTextEntry(entries, profilePath, entryName) {
  const entry = entries.get(entryName);
  if (!entry) throw new Error(`Default profile is missing ${entryName}: ${profilePath}`);
  return entry.toString('utf8');
}

function readZipEntries(profilePath) {
  const bytes = readFileSync(profilePath);
  const endOffset = findEndOfCentralDirectory(bytes, profilePath);
  const entryCount = bytes.readUInt16LE(endOffset + 10);
  const centralDirectoryOffset = bytes.readUInt32LE(endOffset + 16);
  if (entryCount === 0xffff || centralDirectoryOffset === 0xffffffff) {
    throw new Error(`ZIP64 profiles are not supported: ${profilePath}`);
  }

  const entries = new Map();
  let offset = centralDirectoryOffset;
  for (let index = 0; index < entryCount; index += 1) {
    expectSignature(bytes, offset, 0x02014b50, 'central directory', profilePath);
    const flags = bytes.readUInt16LE(offset + 8);
    const compressionMethod = bytes.readUInt16LE(offset + 10);
    const expectedCrc = bytes.readUInt32LE(offset + 16);
    const compressedSize = bytes.readUInt32LE(offset + 20);
    const uncompressedSize = bytes.readUInt32LE(offset + 24);
    const nameLength = bytes.readUInt16LE(offset + 28);
    const extraLength = bytes.readUInt16LE(offset + 30);
    const commentLength = bytes.readUInt16LE(offset + 32);
    const localHeaderOffset = bytes.readUInt32LE(offset + 42);
    const nameStart = offset + 46;
    const name = bytes.subarray(nameStart, nameStart + nameLength).toString('utf8');
    if ((flags & 1) !== 0) throw new Error(`Encrypted profile entry is not supported: ${name}`);

    expectSignature(bytes, localHeaderOffset, 0x04034b50, `local entry ${name}`, profilePath);
    const localNameLength = bytes.readUInt16LE(localHeaderOffset + 26);
    const localExtraLength = bytes.readUInt16LE(localHeaderOffset + 28);
    const dataOffset = localHeaderOffset + 30 + localNameLength + localExtraLength;
    const dataEnd = dataOffset + compressedSize;
    if (dataOffset < 0 || dataEnd > bytes.length) {
      throw new Error(`Profile entry points outside the archive: ${name}`);
    }
    const compressed = bytes.subarray(dataOffset, dataEnd);
    const content = compressionMethod === 0
      ? Buffer.from(compressed)
      : compressionMethod === 8
        ? inflateRawSync(compressed)
        : throwUnsupportedCompression(name, compressionMethod);
    if (content.length !== uncompressedSize) {
      throw new Error(`Unexpected uncompressed size for profile entry ${name}.`);
    }
    if (crc32(content) !== expectedCrc) {
      throw new Error(`CRC mismatch for profile entry ${name}.`);
    }
    if (!name.endsWith('/')) entries.set(name, content);
    offset = nameStart + nameLength + extraLength + commentLength;
  }
  return entries;
}

function findEndOfCentralDirectory(bytes, profilePath) {
  const minimumOffset = Math.max(0, bytes.length - 65_557);
  for (let offset = bytes.length - 22; offset >= minimumOffset; offset -= 1) {
    if (bytes.readUInt32LE(offset) === 0x06054b50) return offset;
  }
  throw new Error(`Unable to locate ZIP central directory in ${profilePath}.`);
}

function expectSignature(bytes, offset, expected, label, profilePath) {
  if (offset < 0 || offset + 4 > bytes.length || bytes.readUInt32LE(offset) !== expected) {
    throw new Error(`Invalid ${label} ZIP signature in ${profilePath}.`);
  }
}

function throwUnsupportedCompression(name, method) {
  throw new Error(`Unsupported ZIP compression method ${method} for profile entry ${name}.`);
}

const crcTable = createCrcTable();

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

function yamlScalar(source, key) {
  const escapedKey = key.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const rawValue = source.match(new RegExp(`^${escapedKey}:\\s*(.+?)\\s*$`, 'm'))?.[1];
  if (rawValue === undefined) return null;
  return rawValue.replace(/^(['"])(.*)\1$/, '$2');
}

export function defaultProfilePath(root) {
  return path.join(root, 'package', 'profiles', 'DefaultProfile70.lp5');
}
