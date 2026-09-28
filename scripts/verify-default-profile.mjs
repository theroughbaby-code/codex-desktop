import { readFile } from 'node:fs/promises';
import path from 'node:path';
import {
  defaultProfilePath,
  verifyDefaultProfile,
} from './lib/default-profile-validation.mjs';

const root = process.cwd();
const packageJson = JSON.parse(await readFile(path.join(root, 'package.json'), 'utf8'));
const profilePath = defaultProfilePath(root);
const result = verifyDefaultProfile(profilePath, String(packageJson.version));

console.log(
  `Default application profile verified with ${result.controlCount} curated actions at version ${result.internalProfileVersion}.`,
);
