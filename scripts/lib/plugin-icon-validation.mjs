import { readFile } from 'node:fs/promises';
import { inflateSync } from 'node:zlib';

const pngSignature = Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]);

export async function verifyPluginIcon(iconPath) {
  const bytes = await readFile(iconPath);
  if (bytes.length < pngSignature.length || !bytes.subarray(0, 8).equals(pngSignature)) {
    throw new Error(`Plugin icon is not a PNG: ${iconPath}`);
  }

  let offset = 8;
  let width;
  let height;
  let bitDepth;
  let colorType;
  let interlaceMethod;
  const imageChunks = [];
  while (offset + 12 <= bytes.length) {
    const length = bytes.readUInt32BE(offset);
    const type = bytes.subarray(offset + 4, offset + 8).toString('ascii');
    const dataStart = offset + 8;
    const dataEnd = dataStart + length;
    if (dataEnd + 4 > bytes.length) throw new Error(`Plugin icon has a truncated ${type} chunk.`);
    const data = bytes.subarray(dataStart, dataEnd);
    if (type === 'IHDR') {
      width = data.readUInt32BE(0);
      height = data.readUInt32BE(4);
      bitDepth = data[8];
      colorType = data[9];
      interlaceMethod = data[12];
    } else if (type === 'IDAT') {
      imageChunks.push(data);
    } else if (type === 'IEND') {
      break;
    }
    offset = dataEnd + 4;
  }

  if (width !== 256 || height !== 256) {
    throw new Error(`Plugin icon must be 256x256, found ${width ?? '?'}x${height ?? '?'}.`);
  }
  if (bitDepth !== 8 || colorType !== 6 || interlaceMethod !== 0) {
    throw new Error('Plugin icon must be a non-interlaced 8-bit RGBA PNG.');
  }
  if (imageChunks.length === 0) throw new Error('Plugin icon contains no image data.');

  const bytesPerPixel = 4;
  const stride = width * bytesPerPixel;
  const inflated = inflateSync(Buffer.concat(imageChunks));
  if (inflated.length !== (stride + 1) * height) {
    throw new Error('Plugin icon has an unexpected decoded data length.');
  }

  const pixels = Buffer.alloc(stride * height);
  let sourceOffset = 0;
  for (let y = 0; y < height; y += 1) {
    const filter = inflated[sourceOffset];
    sourceOffset += 1;
    const rowOffset = y * stride;
    for (let x = 0; x < stride; x += 1) {
      const raw = inflated[sourceOffset + x];
      const left = x >= bytesPerPixel ? pixels[rowOffset + x - bytesPerPixel] : 0;
      const up = y > 0 ? pixels[rowOffset - stride + x] : 0;
      const upLeft = y > 0 && x >= bytesPerPixel
        ? pixels[rowOffset - stride + x - bytesPerPixel]
        : 0;
      const predictor = filter === 0
        ? 0
        : filter === 1
          ? left
          : filter === 2
            ? up
            : filter === 3
              ? Math.floor((left + up) / 2)
              : filter === 4
                ? paeth(left, up, upLeft)
                : throwUnsupportedFilter(filter);
      pixels[rowOffset + x] = (raw + predictor) & 0xff;
    }
    sourceOffset += stride;
  }

  let minX = width;
  let minY = height;
  let maxX = -1;
  let maxY = -1;
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      if (pixels[(y * width + x) * bytesPerPixel + 3] === 0) continue;
      minX = Math.min(minX, x);
      minY = Math.min(minY, y);
      maxX = Math.max(maxX, x);
      maxY = Math.max(maxY, y);
    }
  }
  if (maxX < minX || maxY < minY) throw new Error('Plugin icon is fully transparent.');

  const artworkWidth = maxX - minX + 1;
  const artworkHeight = maxY - minY + 1;
  const centerX = (minX + maxX) / 2;
  const centerY = (minY + maxY) / 2;
  if (artworkWidth > 192 || artworkHeight > 192) {
    throw new Error(
      `Plugin icon artwork must fit within 192x192; alpha bounds are ${artworkWidth}x${artworkHeight}.`,
    );
  }
  if (Math.abs(centerX - 127.5) > 1 || Math.abs(centerY - 127.5) > 1) {
    throw new Error(
      `Plugin icon artwork must be centered; alpha bounds center is ${centerX},${centerY}.`,
    );
  }

  return { width, height, minX, minY, maxX, maxY, artworkWidth, artworkHeight };
}

function paeth(left, up, upLeft) {
  const estimate = left + up - upLeft;
  const leftDistance = Math.abs(estimate - left);
  const upDistance = Math.abs(estimate - up);
  const diagonalDistance = Math.abs(estimate - upLeft);
  if (leftDistance <= upDistance && leftDistance <= diagonalDistance) return left;
  if (upDistance <= diagonalDistance) return up;
  return upLeft;
}

function throwUnsupportedFilter(filter) {
  throw new Error(`Plugin icon uses unsupported PNG filter ${filter}.`);
}
