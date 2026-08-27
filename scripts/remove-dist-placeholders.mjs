import { readdir, rm, rmdir, stat } from 'node:fs/promises';
import path from 'node:path';

const placeholders = [
  path.join(process.cwd(), 'dist', 'actionicons', '.gitkeep'),
  path.join(process.cwd(), 'dist', 'actionsymbols', '.gitkeep')
];

for (const placeholder of placeholders) {
  await rm(placeholder, { force: true });
}

async function pruneEmptyDirectories(directory) {
  try {
    const entries = await readdir(directory, { withFileTypes: true });

    for (const entry of entries) {
      if (entry.isDirectory()) {
        await pruneEmptyDirectories(path.join(directory, entry.name));
      }
    }

    const remainingEntries = await readdir(directory);
    if (remainingEntries.length === 0) {
      await rmdir(directory);
    }
  } catch (error) {
    if (error.code !== 'ENOENT' && error.code !== 'ENOTEMPTY') {
      throw error;
    }
  }
}

const distPath = path.join(process.cwd(), 'dist');

try {
  await stat(distPath);
  await pruneEmptyDirectories(distPath);
} catch (error) {
  if (error.code !== 'ENOENT') {
    throw error;
  }
}
