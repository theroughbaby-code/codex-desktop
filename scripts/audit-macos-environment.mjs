import { execFile } from 'node:child_process';
import { constants } from 'node:fs';
import { access, mkdir, readdir, stat, writeFile } from 'node:fs/promises';
import { homedir } from 'node:os';
import path from 'node:path';
import { promisify } from 'node:util';

const execFileAsync = promisify(execFile);
const root = process.cwd();
const helpRequested = process.argv.includes('--help') || process.argv.includes('-h');

if (helpRequested) {
  console.log(`Usage: npm run audit:mac -- [--output-dir <directory>]

Collects a read-only macOS environment report for the Codex Desktop Logitech plugin.
The default output directory is ./output/macos-audit.`);
  process.exit(0);
}

if (process.platform !== 'darwin') {
  console.error(`Mac environment audit requires macOS. Current platform: ${process.platform}.`);
  console.error('Run this command from the project folder on the Mac used for plugin testing.');
  process.exit(1);
}

const outputDirectory = path.resolve(readArgument('--output-dir') ?? path.join(root, 'output', 'macos-audit'));
const generatedAt = new Date();
const reportStem = `codex-desktop-macos-audit-${formatTimestamp(generatedAt)}`;

console.log('Collecting macOS, ChatGPT, Codex, and Logitech environment details...');

const applicationPaths = await findRelevantApplications();
const applications = [];
for (const applicationPath of applicationPaths) {
  applications.push(await inspectApplication(applicationPath));
}

const codexPath = await findFirstCommand(['codex']);
const dotnetPath = await findFirstCommand(['dotnet']);
const logiPluginToolPaths = unique([
  ...await findCommands(['LogiPluginTool', 'logiplugintool']),
  ...await spotlight('kMDItemFSName == "LogiPluginTool"cd || kMDItemFSName == "logiplugintool"cd'),
]);
const pluginApiPaths = unique(await spotlight('kMDItemFSName == "PluginApi.dll"cd'));

const pluginDirectories = [
  path.join(homedir(), 'Library', 'Application Support', 'Logi', 'LogiPluginService', 'Plugins'),
  path.join('/Library', 'Application Support', 'Logi', 'LogiPluginService', 'Plugins'),
];
const logDirectories = [
  path.join(homedir(), 'Library', 'Application Support', 'Logi', 'LogiPluginService', 'Logs'),
  path.join('/Library', 'Application Support', 'Logi', 'LogiPluginService', 'Logs'),
];

const report = {
  generatedAt: generatedAt.toISOString(),
  machine: {
    platform: process.platform,
    architecture: process.arch,
    productVersion: await commandValue('/usr/bin/sw_vers', ['-productVersion']),
    buildVersion: await commandValue('/usr/bin/sw_vers', ['-buildVersion']),
    hardwareArchitecture: await commandValue('/usr/bin/uname', ['-m']),
    hardwareModel: await commandValue('/usr/sbin/sysctl', ['-n', 'hw.model']),
  },
  developmentRuntime: {
    nodePath: process.execPath,
    nodeVersion: process.version,
    dotnetPath,
    dotnetInfo: dotnetPath ? await commandOutput(dotnetPath, ['--info'], 20_000) : null,
  },
  codex: {
    executablePath: codexPath,
    version: codexPath ? await commandOutput(codexPath, ['--version']) : null,
    appCommandHelp: codexPath ? await commandOutput(codexPath, ['app', '--help']) : null,
  },
  applications,
  runningProcesses: await relevantProcesses(),
  logitech: {
    pluginToolPaths: logiPluginToolPaths,
    pluginToolProbe: await inspectExecutables(logiPluginToolPaths, ['--help']),
    pluginApiFiles: await inspectFiles(pluginApiPaths),
    pluginDirectories: await inspectDirectories(pluginDirectories),
    logDirectories: await inspectDirectories(logDirectories, { recentFiles: 12 }),
    profileFiles: await findFiles(
      path.join(homedir(), 'Library', 'Application Support', 'Logi'),
      (filePath) => filePath.toLowerCase().endsWith('.lp5'),
      7,
      50,
    ),
  },
  manualChecks: [
    'Keep ChatGPT and Logi Options+ open, then rerun the audit so their exact process names are captured.',
    'In System Settings > Privacy & Security > Accessibility, record whether Logi Plugin Service is listed and enabled.',
    'In Options+, export a minimal ChatGPT application profile so its macOS application matcher can be inspected.',
    'Install a generated sample plugin and confirm that Adapt to App switches to its profile when ChatGPT is foreground.',
  ],
};

await mkdir(outputDirectory, { recursive: true });
const jsonPath = path.join(outputDirectory, `${reportStem}.json`);
const markdownPath = path.join(outputDirectory, `${reportStem}.md`);
await writeFile(jsonPath, `${JSON.stringify(report, null, 2)}\n`);
await writeFile(markdownPath, renderMarkdown(report));

console.log('Mac environment audit complete.');
console.log(`JSON: ${jsonPath}`);
console.log(`Summary: ${markdownPath}`);

function readArgument(name) {
  const index = process.argv.indexOf(name);
  if (index < 0) {
    return null;
  }

  const value = process.argv[index + 1];
  if (!value || value.startsWith('--')) {
    throw new Error(`${name} requires a value.`);
  }

  return value;
}

function formatTimestamp(value) {
  return value.toISOString().replaceAll(':', '-').replace(/\.\d{3}Z$/, 'Z');
}

async function run(file, args = [], timeout = 10_000) {
  try {
    const result = await execFileAsync(file, args, {
      encoding: 'utf8',
      maxBuffer: 4 * 1024 * 1024,
      timeout,
    });
    return {
      ok: true,
      stdout: result.stdout.trim(),
      stderr: result.stderr.trim(),
    };
  } catch (error) {
    return {
      ok: false,
      code: error.code ?? null,
      signal: error.signal ?? null,
      stdout: String(error.stdout ?? '').trim(),
      stderr: String(error.stderr ?? error.message ?? '').trim(),
    };
  }
}

async function commandValue(file, args) {
  const result = await run(file, args);
  return result.ok ? result.stdout : null;
}

async function commandOutput(file, args, timeout) {
  const result = await run(file, args, timeout);
  return {
    ok: result.ok,
    output: [result.stdout, result.stderr].filter(Boolean).join('\n'),
    code: result.code ?? null,
  };
}

async function findFirstCommand(names) {
  return (await findCommands(names))[0] ?? null;
}

async function findCommands(names) {
  const paths = [];
  for (const name of names) {
    const result = await run('/usr/bin/which', [name]);
    if (result.ok && result.stdout) {
      paths.push(...result.stdout.split(/\r?\n/).filter(Boolean));
    }
  }
  return unique(paths);
}

async function spotlight(query) {
  const result = await run('/usr/bin/mdfind', [query], 20_000);
  return result.ok ? result.stdout.split(/\r?\n/).filter(Boolean) : [];
}

async function findRelevantApplications() {
  const paths = [];
  for (const directory of ['/Applications', path.join(homedir(), 'Applications')]) {
    try {
      const entries = await readdir(directory, { withFileTypes: true });
      for (const entry of entries) {
        if (entry.isDirectory() && entry.name.endsWith('.app') && isRelevantApplicationName(entry.name)) {
          paths.push(path.join(directory, entry.name));
        }
      }
    } catch {
      // The user-level Applications directory is optional.
    }
  }

  const spotlightApps = await spotlight(
    'kMDItemContentType == "com.apple.application-bundle" && '
      + '(kMDItemFSName == "*ChatGPT*.app"cd || kMDItemFSName == "*Codex*.app"cd || '
      + 'kMDItemFSName == "*Logi*.app"cd || kMDItemFSName == "*Loupedeck*.app"cd)',
  );
  paths.push(...spotlightApps.filter((item) => item.endsWith('.app')));

  return unique(paths).sort((left, right) => left.localeCompare(right));
}

function isRelevantApplicationName(name) {
  return /(chatgpt|codex|logi|loupedeck)/i.test(name);
}

async function inspectApplication(applicationPath) {
  const infoPlistPath = path.join(applicationPath, 'Contents', 'Info.plist');
  let info = {};

  if (await exists(infoPlistPath)) {
    const plist = await run('/usr/bin/plutil', ['-convert', 'json', '-o', '-', infoPlistPath]);
    if (plist.ok) {
      try {
        info = JSON.parse(plist.stdout);
      } catch {
        info = {};
      }
    }
  }

  const executableName = info.CFBundleExecutable ?? null;
  const executablePath = executableName
    ? path.join(applicationPath, 'Contents', 'MacOS', executableName)
    : null;
  const urlSchemes = (info.CFBundleURLTypes ?? [])
    .flatMap((entry) => entry.CFBundleURLSchemes ?? [])
    .filter((value) => typeof value === 'string');
  const scriptingDefinitions = await findFiles(
    path.join(applicationPath, 'Contents'),
    (filePath) => filePath.toLowerCase().endsWith('.sdef'),
    6,
    25,
  );

  return {
    path: applicationPath,
    bundleIdentifier: info.CFBundleIdentifier ?? null,
    bundleName: info.CFBundleName ?? null,
    displayName: info.CFBundleDisplayName ?? null,
    version: info.CFBundleShortVersionString ?? null,
    build: info.CFBundleVersion ?? null,
    executablePath,
    executableFormat: executablePath && await exists(executablePath)
      ? await commandValue('/usr/bin/file', [executablePath])
      : null,
    urlSchemes,
    appleScriptEnabled: info.NSAppleScriptEnabled ?? false,
    scriptingDefinitions,
    signing: await signingDetails(applicationPath),
  };
}

async function signingDetails(applicationPath) {
  const result = await run('/usr/bin/codesign', ['-dv', '--verbose=4', applicationPath]);
  const output = [result.stdout, result.stderr].filter(Boolean).join('\n');
  return {
    ok: result.ok,
    identifier: matchLine(output, 'Identifier'),
    teamIdentifier: matchLine(output, 'TeamIdentifier'),
    authorities: output
      .split(/\r?\n/)
      .filter((line) => line.startsWith('Authority='))
      .map((line) => line.slice('Authority='.length)),
  };
}

function matchLine(output, key) {
  const prefix = `${key}=`;
  const line = output.split(/\r?\n/).find((entry) => entry.startsWith(prefix));
  return line ? line.slice(prefix.length) : null;
}

async function relevantProcesses() {
  const result = await run('/bin/ps', ['-axo', 'pid=,ppid=,comm=,args=']);
  if (!result.ok) {
    return [];
  }

  return result.stdout
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => /(chatgpt|codex|logiplugin|logi options|loupedeck)/i.test(line));
}

async function inspectExecutables(paths, probeArguments) {
  const results = [];
  for (const filePath of paths) {
    results.push({
      path: filePath,
      format: await commandValue('/usr/bin/file', [filePath]),
      probe: await commandOutput(filePath, probeArguments),
    });
  }
  return results;
}

async function inspectFiles(paths) {
  const results = [];
  for (const filePath of paths) {
    const fileStat = await safeStat(filePath);
    results.push({
      path: filePath,
      size: fileStat?.size ?? null,
      modifiedAt: fileStat?.mtime?.toISOString() ?? null,
      format: await commandValue('/usr/bin/file', [filePath]),
      sha256: await sha256(filePath),
    });
  }
  return results;
}

async function inspectDirectories(paths, options = {}) {
  const results = [];
  for (const directoryPath of paths) {
    if (!await exists(directoryPath)) {
      results.push({ path: directoryPath, exists: false, entries: [] });
      continue;
    }

    const entries = await directoryEntries(directoryPath, options.recentFiles ?? 100);
    results.push({ path: directoryPath, exists: true, entries });
  }
  return results;
}

async function directoryEntries(directoryPath, limit) {
  const entries = await readdir(directoryPath, { withFileTypes: true });
  const details = [];
  for (const entry of entries) {
    const entryPath = path.join(directoryPath, entry.name);
    const entryStat = await safeStat(entryPath);
    details.push({
      name: entry.name,
      type: entry.isDirectory() ? 'directory' : 'file',
      modifiedAt: entryStat?.mtime?.toISOString() ?? null,
    });
  }

  return details
    .sort((left, right) => String(right.modifiedAt).localeCompare(String(left.modifiedAt)))
    .slice(0, limit);
}

async function sha256(filePath) {
  const result = await run('/usr/bin/shasum', ['-a', '256', filePath], 20_000);
  return result.ok ? result.stdout.split(/\s+/)[0] : null;
}

async function findFiles(directoryPath, predicate, maxDepth, limit, depth = 0, results = []) {
  if (depth > maxDepth || results.length >= limit || !await exists(directoryPath)) {
    return results;
  }

  let entries;
  try {
    entries = await readdir(directoryPath, { withFileTypes: true });
  } catch {
    return results;
  }

  for (const entry of entries) {
    if (results.length >= limit) {
      break;
    }

    const entryPath = path.join(directoryPath, entry.name);
    if (entry.isFile() && predicate(entryPath)) {
      results.push(entryPath);
    } else if (entry.isDirectory() && !entry.isSymbolicLink()) {
      await findFiles(entryPath, predicate, maxDepth, limit, depth + 1, results);
    }
  }

  return results;
}

async function exists(filePath) {
  try {
    await access(filePath, constants.F_OK);
    return true;
  } catch {
    return false;
  }
}

async function safeStat(filePath) {
  try {
    return await stat(filePath);
  } catch {
    return null;
  }
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}

function renderMarkdown(report) {
  const chatGptApps = report.applications.filter((app) => /(chatgpt|codex|openai)/i.test(
    `${app.path} ${app.bundleIdentifier ?? ''}`,
  ));
  const logiApps = report.applications.filter((app) => /(logi|loupedeck)/i.test(
    `${app.path} ${app.bundleIdentifier ?? ''}`,
  ));

  return `# Codex Desktop macOS Audit

Generated: ${report.generatedAt}

## Machine

- macOS: ${report.machine.productVersion ?? 'not detected'} (${report.machine.buildVersion ?? 'unknown build'})
- Architecture: ${report.machine.hardwareArchitecture ?? report.machine.architecture}
- Hardware: ${report.machine.hardwareModel ?? 'not detected'}
- Node: ${report.developmentRuntime.nodeVersion} at \`${escapeMarkdown(report.developmentRuntime.nodePath)}\`
- .NET: ${report.developmentRuntime.dotnetPath ? `\`${escapeMarkdown(report.developmentRuntime.dotnetPath)}\`` : 'not found'}

## Codex

- Executable: ${report.codex.executablePath ? `\`${escapeMarkdown(report.codex.executablePath)}\`` : 'not found'}
- Version: ${singleLine(report.codex.version?.output) ?? 'not detected'}

## ChatGPT and Codex Applications

${applicationTable(chatGptApps)}

## Logitech Applications

${applicationTable(logiApps)}

## Logitech Plugin Runtime

- LogiPluginTool: ${report.logitech.pluginToolPaths.length ? report.logitech.pluginToolPaths.map(code).join(', ') : 'not found'}
- PluginApi.dll: ${report.logitech.pluginApiFiles.length ? report.logitech.pluginApiFiles.map((item) => code(item.path)).join(', ') : 'not found'}
- Running matching processes: ${report.runningProcesses.length}

## Running Processes

${report.runningProcesses.length ? report.runningProcesses.map((line) => `- \`${escapeMarkdown(line)}\``).join('\n') : 'No matching processes were detected.'}

## Manual Checks Still Required

${report.manualChecks.map((item, index) => `${index + 1}. ${item}`).join('\n')}
`;
}

function applicationTable(applications) {
  if (!applications.length) {
    return 'No matching applications were detected.';
  }

  const rows = applications.map((app) => [
    app.displayName ?? app.bundleName ?? path.basename(app.path),
    app.bundleIdentifier ?? 'unknown',
    app.version ?? 'unknown',
    app.executableFormat ?? 'unknown',
    app.scriptingDefinitions.length ? 'yes' : 'no',
  ]);

  return [
    '| Application | Bundle ID | Version | Executable | AppleScript dictionary |',
    '| --- | --- | --- | --- | --- |',
    ...rows.map((row) => `| ${row.map(escapeTableCell).join(' | ')} |`),
  ].join('\n');
}

function escapeTableCell(value) {
  return String(value).replaceAll('|', '\\|').replaceAll('\n', ' ');
}

function escapeMarkdown(value) {
  return String(value).replaceAll('`', '\\`');
}

function code(value) {
  return `\`${escapeMarkdown(value)}\``;
}

function singleLine(value) {
  return value ? String(value).split(/\r?\n/)[0] : null;
}
