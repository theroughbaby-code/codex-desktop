import { copyFile, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises';
import path from 'node:path';

const root = process.cwd();
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const catalog = JSON.parse(await readFile(path.join(root, 'src-csharp', 'ShortcutCatalog.json'), 'utf8'));
const profileName = 'A1B2C3D4E5F6478890ABCDEF12345678';
const workspaceName = 'B1C2D3E4F5A6478890ABCDEF12345678';
const profileActionId = '$@Generic___@ProfileAction___C1D2E3F4A5B6478890ABCDEF12345678';
const profileBuildRoot = path.join(root, '.profile-build');
const profileBuildDirectory = path.join(profileBuildRoot, 'DefaultProfile70');
const profileOutputDirectory = path.join(root, 'package', 'profiles');
const profileOutputPath = path.join(profileOutputDirectory, 'DefaultProfile70.lp5');
const namespaceName = 'Loupedeck.CodexDesktopPlugin';
const crcTable = createCrcTable();

assertInsideRoot(profileBuildRoot);
assertInsideRoot(profileOutputPath);
await rm(profileBuildRoot, { recursive: true, force: true });
await mkdir(path.join(profileBuildDirectory, 'metadata'), { recursive: true });
await mkdir(profileOutputDirectory, { recursive: true });

const assignedActions = [
  profileCommand('OpenCodexCommand'),
  profileCommand('UsageStatusCommand'),
  profileActionId,
  profileCommand('OpenTerminalCommand'),
  ...catalog.map((action) => profileCommand(action.className)),
];

if (assignedActions.length !== 62 || new Set(assignedActions).size !== assignedActions.length) {
  throw new Error(`Expected 62 unique test actions, found ${assignedActions.length}.`);
}

const pageNames = [
  'App and Chat',
  'Navigation I',
  'Navigation II',
  'Navigation and Panels',
  'Panels and General',
  'General',
  'Chat Index',
];

const pages = pageNames.map((displayName, pageIndex) => ({
  '$type': 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutPage7, LoupedeckService',
  name: `D${String(pageIndex + 1).padStart(2, '0')}2E3F4A5B6478890ABCDEF12345678`,
  displayName,
  description: 'Codex Desktop action test page.',
  controls: assignedActions.slice(pageIndex * 9, pageIndex * 9 + 9).map((pressAction, controlId) => ({
    '$type': 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutControl7, LoupedeckService',
    controlId,
    pressAction,
    rotateAction: null,
  })),
}));

const applicationInfo = {
  '$type': 'Loupedeck.Service.SupportedApplicationInfo, LoupedeckService',
  name: '@_codexdesktop',
  displayName: 'Codex Desktop',
  description: 'Codex Desktop test profile for Logitech MX Creative Keypad.',
  deviceType: 'Loupedeck70',
  nativePluginName: 'CodexDesktop',
  hasNativePlugin: true,
  processOrBundleName: 'ChatGPT',
  modes: [
    {
      '$type': 'Loupedeck.Service.ApplicationMode, LoupedeckService',
      name: 'main',
      parentModeName: null,
      displayName: 'Main',
    },
  ],
  defaultProfileName: profileName,
  isEnabled: true,
};

const profileInfo = {
  '$type': 'Loupedeck.Service.ApplicationProfile, LoupedeckService',
  name: profileName,
  profileFlags: 'None',
  displayName: 'Codex Desktop Test Profile',
  description: 'Seven pages containing every Codex Desktop plugin action.',
  deviceType: 'Loupedeck70',
  applicationName: '@_codexdesktop',
  nativePluginName: 'CodexDesktop',
  hasNativePlugin: true,
  additionalNativePluginNames: ['DefaultWin'],
  lastModifiedTimeUtc: '2026-09-01T00:00:00.0000000Z',
  profileSettings: {
    '$type': 'Loupedeck.DictionaryNoCase`1[[System.String, System.Private.CoreLib]], PluginApi',
  },
  actionImages90: null,
  actionImages60: null,
  wheelImages: null,
  actionColors: null,
  layout: {
    '$type': 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayout7, LoupedeckService',
    deviceType: 'Loupedeck70',
    profileFlags: 'None',
    layoutModes: [
      {
        '$type': 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutMode7, LoupedeckService',
        deviceType: 'Loupedeck70',
        modeName: 'main',
        parentModeName: null,
        actions: null,
        dynamicButtonPages: null,
        dynamicEncoderPages: null,
        workspaces: [
          {
            '$type': 'Loupedeck.Service.Devices.Loupedeck7Devices.ProfileLayoutWorkspace7, LoupedeckService',
            name: workspaceName,
            displayName: 'All Actions',
            description: 'Swipe between seven pages to test every action.',
            pressPages: pages,
            rotatePages: [],
          },
        ],
        homeWorkspaceName: workspaceName,
      },
    ],
    folderPages: [],
  },
  macroCommands: [],
  macroAdjustments: [],
  profileCommands: [],
  profileAdjustments: [],
  conversionHistory: '',
  packageName: null,
  packageVersion: null,
  profileActions: [
    {
      '$type': 'Loupedeck.Service.ApplicationProfileCommand, LoupedeckService',
      isCommand: true,
      name: profileActionId,
      templateActionName: '$CodexDesktop___SendPrompt',
      actionParameters: {
        '$type': 'Loupedeck.ActionEditorActionParameters, PluginApi',
        parameters: {
          '$type': 'Loupedeck.StringDictionaryNoCase, PluginApi',
          prompt: 'Reply with: Codex Desktop profile test successful.',
        },
        count: 1,
      },
      displayName: 'Custom Prompt test',
      description: 'Types the complete test prompt and presses Enter.',
      groupName: 'Chat',
      superGroupName: 'codexdesktop',
      isProfileAction: true,
      isMultiState: false,
      isResetCommand: false,
      adjustmentName: null,
      states: null,
    },
  ],
};

await writeJson('ApplicationInfo.json', applicationInfo);
await writeJson('ProfileInfo.json', profileInfo);
await writeJson(path.join('metadata', 'ProfilePreview.json'), { buttonPages: [], encoderPages: [] });
await writeJson(path.join('metadata', 'AdvancedInfo.json'), { additionalPluginNames: ['CodexDesktop'] });
await writeFile(
  path.join(profileBuildDirectory, 'metadata', 'LoupedeckPackage.yaml'),
  `type: Profile5\nname: ${profileName}\ndisplayName: Codex Desktop Test Profile\nversion: ${packageJson.version}\n`,
);
await copyFile(
  path.join(root, 'package', 'metadata', 'Icon256x256.png'),
  path.join(profileBuildDirectory, 'ApplicationIcon.png'),
);

await rm(profileOutputPath, { force: true });
await writeZipFromDirectory(profileBuildDirectory, profileOutputPath);
await rm(profileBuildRoot, { recursive: true, force: true });

console.log(`Generated ${profileOutputPath} with ${pages.length} pages and ${assignedActions.length} actions.`);

function profileCommand(className) {
  return `$CodexDesktop___${namespaceName}.${className}`;
}

function assertInsideRoot(targetPath) {
  const relative = path.relative(root, targetPath);
  if (!relative || relative.startsWith('..') || path.isAbsolute(relative)) {
    throw new Error(`Refusing to write outside the project: ${targetPath}`);
  }
}

async function writeJson(relativePath, value) {
  await writeFile(path.join(profileBuildDirectory, relativePath), `${JSON.stringify(value, null, 2)}\n`);
}

async function listFiles(directory, baseDirectory = directory) {
  const entries = await readdir(directory, { withFileTypes: true });
  const files = [];

  for (const entry of entries) {
    const entryPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...await listFiles(entryPath, baseDirectory));
    } else if (entry.isFile()) {
      files.push({
        absolutePath: entryPath,
        archivePath: path.relative(baseDirectory, entryPath).replaceAll('\\', '/'),
      });
    }
  }

  return files.sort((left, right) => left.archivePath.localeCompare(right.archivePath));
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
    localHeader.writeUInt32LE(crc, 14);
    localHeader.writeUInt32LE(data.length, 18);
    localHeader.writeUInt32LE(data.length, 22);
    localHeader.writeUInt16LE(name.length, 26);
    chunks.push(localHeader, name, data);
    centralDirectory.push({ name, crc, size: data.length, offset });
    offset += localHeader.length + name.length + data.length;
  }

  const centralStart = offset;
  for (const record of centralDirectory) {
    const header = Buffer.alloc(46);
    header.writeUInt32LE(0x02014b50, 0);
    header.writeUInt16LE(20, 4);
    header.writeUInt16LE(20, 6);
    header.writeUInt16LE(0x0800, 8);
    header.writeUInt32LE(record.crc, 16);
    header.writeUInt32LE(record.size, 20);
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
