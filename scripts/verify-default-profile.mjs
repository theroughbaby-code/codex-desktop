import { spawnSync } from 'node:child_process';
import path from 'node:path';

const profilePath = path.join(process.cwd(), 'package', 'profiles', 'DefaultProfile70.lp5');
const applicationInfo = readJsonEntry('ApplicationInfo.json');
const profileInfo = readJsonEntry('ProfileInfo.json');

const errors = [];
expect(applicationInfo.name === '@_codexdesktop', 'application name must be @_codexdesktop');
expect(applicationInfo.deviceType === 'Loupedeck70', 'application profile must target MX Creative Keypad');
expect(applicationInfo.nativePluginName === 'CodexDesktop', 'application must use the CodexDesktop native plugin');
expect(applicationInfo.hasNativePlugin === true, 'application must declare its native plugin');
expect(applicationInfo.processOrBundleName === 'ChatGPT', 'application must bind to the Codex desktop process');
expect(profileInfo.name === applicationInfo.defaultProfileName, 'default profile ID must match ProfileInfo');
expect(profileInfo.applicationName === applicationInfo.name, 'profile must reference the Codex application');
expect(profileInfo.nativePluginName === 'CodexDesktop', 'profile must use the CodexDesktop native plugin');
expect(profileInfo.deviceType === 'Loupedeck70', 'profile must target MX Creative Keypad');

const controls = profileInfo.layout?.layoutModes
  ?.flatMap((mode) => mode.workspaces ?? [])
  .flatMap((workspace) => workspace.pressPages ?? [])
  .flatMap((page) => page.controls ?? []) ?? [];

expect(controls.length > 0, 'default profile must contain assigned controls');
for (const control of controls) {
  expect(
    typeof control.pressAction === 'string' && control.pressAction.startsWith('$CodexDesktop___'),
    `control ${control.controlId} must reference a CodexDesktop action`,
  );
}

if (errors.length) {
  throw new Error(`Default profile verification failed:\n- ${errors.join('\n- ')}`);
}

console.log(`Default application profile verified with ${controls.length} assigned Codex actions.`);

function readJsonEntry(entryName) {
  const result = spawnSync('tar', ['-xOf', profilePath, entryName], { encoding: 'utf8' });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`Unable to read ${entryName} from ${profilePath}: ${result.stderr.trim()}`);
  }
  return JSON.parse(result.stdout);
}

function expect(condition, message) {
  if (!condition) {
    errors.push(message);
  }
}
