import { rm } from 'node:fs/promises';
import path from 'node:path';

const placeholders = [
  path.join(process.cwd(), 'dist', 'actionicons', '.gitkeep'),
  path.join(process.cwd(), 'dist', 'actionsymbols', '.gitkeep')
];

for (const placeholder of placeholders) {
  await rm(placeholder, { force: true });
}
