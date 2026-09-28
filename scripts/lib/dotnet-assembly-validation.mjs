import { mkdtemp, readFile, rm, stat } from 'node:fs/promises';
import { spawnSync } from 'node:child_process';
import { homedir, tmpdir } from 'node:os';
import path from 'node:path';

export async function verifyPluginAssemblies({ root, assemblies, expectedVersion, requiredTypeNames }) {
  const dotnet = await resolveDotnet();
  const sdkVersion = readDotnetSdkVersion(dotnet);
  const sdkMajor = sdkVersion.match(/^(\d+)\./)?.[1];
  if (!sdkMajor) throw new Error(`Unable to determine the .NET SDK major version from '${sdkVersion}'.`);

  const temporaryDirectory = await mkdtemp(path.join(tmpdir(), 'codex-assembly-inspector-'));
  const outputPath = path.join(temporaryDirectory, 'assemblies.json');
  const inspectorOutputDirectory = path.join(temporaryDirectory, 'bin');
  const projectPath = path.join(root, 'scripts', 'dotnet-assembly-inspector', 'AssemblyMetadataInspector.csproj');

  try {
    const buildResult = spawnSync(dotnet, [
      'build', projectPath,
      '--configuration', 'Release',
      '--verbosity', 'quiet',
      `-p:TargetFramework=net${sdkMajor}.0`,
      `-p:BaseIntermediateOutputPath=${path.join(temporaryDirectory, 'obj')}${path.sep}`,
      '--output', inspectorOutputDirectory,
    ], {
      cwd: root,
      encoding: 'utf8',
      stdio: 'pipe',
    });

    if (buildResult.error) throw buildResult.error;
    if (buildResult.status !== 0) {
      const details = [buildResult.stdout, buildResult.stderr].filter(Boolean).join('\n').trim();
      throw new Error(`Assembly inspector build failed with status ${buildResult.status ?? 1}.${details ? `\n${details}` : ''}`);
    }

    const result = spawnSync(dotnet, [
      path.join(inspectorOutputDirectory, 'AssemblyMetadataInspector.dll'),
      outputPath,
      ...assemblies.map((assembly) => assembly.path),
    ], {
      cwd: root,
      encoding: 'utf8',
      stdio: 'pipe',
    });

    if (result.error) throw result.error;
    if (result.status !== 0) {
      const details = [result.stdout, result.stderr].filter(Boolean).join('\n').trim();
      throw new Error(`Assembly metadata inspection failed with status ${result.status ?? 1}.${details ? `\n${details}` : ''}`);
    }

    const inspections = JSON.parse(await readFile(outputPath, 'utf8'));
    if (!Array.isArray(inspections) || inspections.length !== assemblies.length) {
      throw new Error(`Assembly inspector returned ${inspections?.length ?? 'invalid'} results for ${assemblies.length} assemblies.`);
    }

    for (let index = 0; index < assemblies.length; index += 1) {
      validateInspection(inspections[index], assemblies[index].label);
    }

    return inspections;
  } finally {
    await rm(temporaryDirectory, { recursive: true, force: true });
  }

  function validateInspection(inspection, label) {
    const expectedFourPartVersion = `${expectedVersion}.0`;
    const errors = [];
    if (inspection.assemblyName !== 'CodexDesktopPlugin') {
      errors.push(`assembly name '${inspection.assemblyName ?? 'missing'}' must be 'CodexDesktopPlugin'`);
    }
    if (inspection.assemblyVersion !== expectedFourPartVersion) {
      errors.push(`assembly version '${inspection.assemblyVersion ?? 'missing'}' must be '${expectedFourPartVersion}'`);
    }
    if (inspection.fileVersion !== expectedFourPartVersion) {
      errors.push(`file version '${inspection.fileVersion ?? 'missing'}' must be '${expectedFourPartVersion}'`);
    }
    if (inspection.informationalVersion !== expectedVersion) {
      errors.push(`informational version '${inspection.informationalVersion ?? 'missing'}' must be '${expectedVersion}'`);
    }

    const typeNames = new Set(Array.isArray(inspection.typeNames) ? inspection.typeNames : []);
    const missingTypes = requiredTypeNames.filter((typeName) => !typeNames.has(typeName));
    if (missingTypes.length > 0) errors.push(`missing default-profile action types: ${missingTypes.join(', ')}`);

    if (errors.length > 0) {
      throw new Error(`${label} assembly validation failed:\n- ${errors.join('\n- ')}`);
    }
  }
}

function readDotnetSdkVersion(dotnet) {
  const result = spawnSync(dotnet, ['--version'], { encoding: 'utf8', stdio: 'pipe' });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`Unable to read the .NET SDK version from ${dotnet}: ${result.stderr.trim()}`);
  }
  return result.stdout.trim();
}

async function resolveDotnet() {
  const candidates = unique([
    process.env.DOTNET,
    ...findOnPath(process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'),
    path.join(homedir(), '.dotnet', process.platform === 'win32' ? 'dotnet.exe' : 'dotnet'),
    '/usr/local/share/dotnet/dotnet',
    '/opt/homebrew/bin/dotnet',
  ]);
  for (const candidate of candidates) {
    if (await isFile(candidate)) return candidate;
  }
  throw new Error('dotnet was not found. Install the .NET SDK or set DOTNET before packing.');
}

function findOnPath(command) {
  return (process.env.PATH ?? '')
    .split(path.delimiter)
    .filter(Boolean)
    .map((directory) => path.join(directory, command));
}

async function isFile(filePath) {
  try {
    return (await stat(filePath)).isFile();
  } catch {
    return false;
  }
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}
