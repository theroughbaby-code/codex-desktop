import path from 'node:path';
import { verifyPluginIcon } from './lib/plugin-icon-validation.mjs';

const iconPath = path.join(process.cwd(), 'package', 'metadata', 'Icon256x256.png');
const result = await verifyPluginIcon(iconPath);
console.log(
  `Plugin icon verified: ${result.width}x${result.height} canvas, ${result.artworkWidth}x${result.artworkHeight} centered artwork.`,
);
