import { mkdir, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { deflateSync } from 'node:zlib';

const root = process.cwd();
const outputDirectory = path.join(root, 'src-csharp', 'resources', 'generated');
const logicalSize = 80;
const supersampling = 4;
const rasterSize = logicalSize * supersampling;
const crcTable = createCrcTable();

const colors = {
  transparent: [0, 0, 0, 0],
  dark: [17, 23, 20, 255],
  light: [245, 247, 243, 255],
  green: [45, 201, 118, 255],
  amber: [241, 177, 52, 255],
  red: [238, 68, 68, 255],
};

const images = [
  ['ApprovalApproveIdle.png', () => renderApproval('approve', false)],
  ['ApprovalApprovePending.png', () => renderApproval('approve', true)],
  ['ApprovalAlwaysIdle.png', () => renderApproval('always', false)],
  ['ApprovalAlwaysPending.png', () => renderApproval('always', true)],
  ['ApprovalDenyIdle.png', () => renderApproval('deny', false)],
  ['ApprovalDenyPending.png', () => renderApproval('deny', true)],
  ['StopThinkingIdle.png', () => renderStop(false)],
  ['StopThinkingActive.png', () => renderStop(true)],
];

await mkdir(outputDirectory, { recursive: true });
for (const [fileName, render] of images) {
  await writeFile(path.join(outputDirectory, fileName), encodePng(render()));
}

console.log(`Generated ${images.length} runtime state images in ${outputDirectory}`);

function renderApproval(decision, pending) {
  const surface = createSurface();
  const accent = decision === 'approve'
    ? colors.green
    : decision === 'always'
      ? colors.amber
      : colors.red;

  drawStateCircle(surface, pending, accent);

  if (decision === 'deny') {
    drawGlyphLine(surface, 28, 28, 52, 52, pending);
    drawGlyphLine(surface, 52, 28, 28, 52, pending);
  } else if (decision === 'always') {
    drawCheck(surface, 34, 35, 0.72, pending);
    drawCheck(surface, 46, 46, 0.72, pending);
  } else {
    drawCheck(surface, 40, 40, 1, pending);
  }

  return downsample(surface);
}

function renderStop(active) {
  const surface = createSurface();
  drawStateCircle(surface, active, colors.red);
  fillRoundedRectangle(surface, 30, 30, 20, 20, 3, colors.dark);
  fillRoundedRectangle(surface, 34, 34, 12, 12, 1.5, colors.light);
  return downsample(surface);
}

function drawStateCircle(surface, active, accent) {
  if (active) {
    fillCircle(surface, 40, 40, 29, accent);
    strokeCircle(surface, 40, 40, 27, 7, colors.dark);
    return;
  }

  strokeCircle(surface, 40, 40, 27, 8, colors.dark);
  strokeCircle(surface, 40, 40, 27, 3, colors.light);
}

function drawCheck(surface, centerX, centerY, scale, active) {
  const points = [
    [centerX - 14 * scale, centerY],
    [centerX - 4 * scale, centerY + 10 * scale],
    [centerX + 15 * scale, centerY - 12 * scale],
  ];
  drawGlyphLine(surface, ...points[0], ...points[1], active);
  drawGlyphLine(surface, ...points[1], ...points[2], active);
}

function drawGlyphLine(surface, x1, y1, x2, y2, active) {
  if (active) {
    drawRoundedLine(surface, x1, y1, x2, y2, 4.5, colors.dark);
    return;
  }

  drawRoundedLine(surface, x1, y1, x2, y2, 7, colors.dark);
  drawRoundedLine(surface, x1, y1, x2, y2, 3, colors.light);
}

function createSurface() {
  const data = new Uint8Array(rasterSize * rasterSize * 4);
  for (let offset = 0; offset < data.length; offset += 4) {
    data.set(colors.transparent, offset);
  }
  return data;
}

function fillCircle(surface, centerX, centerY, radius, color) {
  paint(surface, (x, y) => Math.hypot(x - centerX, y - centerY) <= radius, color);
}

function strokeCircle(surface, centerX, centerY, radius, width, color) {
  const halfWidth = width / 2;
  paint(
    surface,
    (x, y) => Math.abs(Math.hypot(x - centerX, y - centerY) - radius) <= halfWidth,
    color,
  );
}

function drawRoundedLine(surface, x1, y1, x2, y2, width, color) {
  const radius = width / 2;
  const dx = x2 - x1;
  const dy = y2 - y1;
  const lengthSquared = dx * dx + dy * dy;
  paint(surface, (x, y) => {
    const projection = lengthSquared === 0
      ? 0
      : Math.max(0, Math.min(1, ((x - x1) * dx + (y - y1) * dy) / lengthSquared));
    const closestX = x1 + projection * dx;
    const closestY = y1 + projection * dy;
    return Math.hypot(x - closestX, y - closestY) <= radius;
  }, color);
}

function fillRoundedRectangle(surface, x, y, width, height, radius, color) {
  const left = x;
  const right = x + width;
  const top = y;
  const bottom = y + height;
  paint(surface, (pointX, pointY) => {
    const closestX = Math.max(left + radius, Math.min(right - radius, pointX));
    const closestY = Math.max(top + radius, Math.min(bottom - radius, pointY));
    return Math.hypot(pointX - closestX, pointY - closestY) <= radius;
  }, color);
}

function paint(surface, predicate, color) {
  for (let pixelY = 0; pixelY < rasterSize; pixelY += 1) {
    const y = (pixelY + 0.5) / supersampling;
    for (let pixelX = 0; pixelX < rasterSize; pixelX += 1) {
      const x = (pixelX + 0.5) / supersampling;
      if (!predicate(x, y)) {
        continue;
      }

      const offset = (pixelY * rasterSize + pixelX) * 4;
      surface.set(color, offset);
    }
  }
}

function downsample(surface) {
  const result = Buffer.alloc(logicalSize * logicalSize * 4);
  const sampleCount = supersampling * supersampling;

  for (let y = 0; y < logicalSize; y += 1) {
    for (let x = 0; x < logicalSize; x += 1) {
      let alpha = 0;
      let red = 0;
      let green = 0;
      let blue = 0;

      for (let sampleY = 0; sampleY < supersampling; sampleY += 1) {
        for (let sampleX = 0; sampleX < supersampling; sampleX += 1) {
          const sourceX = x * supersampling + sampleX;
          const sourceY = y * supersampling + sampleY;
          const sourceOffset = (sourceY * rasterSize + sourceX) * 4;
          const sampleAlpha = surface[sourceOffset + 3];
          alpha += sampleAlpha;
          red += surface[sourceOffset] * sampleAlpha;
          green += surface[sourceOffset + 1] * sampleAlpha;
          blue += surface[sourceOffset + 2] * sampleAlpha;
        }
      }

      const targetOffset = (y * logicalSize + x) * 4;
      result[targetOffset + 3] = Math.round(alpha / sampleCount);
      if (alpha > 0) {
        result[targetOffset] = Math.round(red / alpha);
        result[targetOffset + 1] = Math.round(green / alpha);
        result[targetOffset + 2] = Math.round(blue / alpha);
      }
    }
  }

  return result;
}

function encodePng(rgba) {
  const scanlines = Buffer.alloc((logicalSize * 4 + 1) * logicalSize);
  for (let y = 0; y < logicalSize; y += 1) {
    const targetOffset = y * (logicalSize * 4 + 1);
    scanlines[targetOffset] = 0;
    rgba.copy(scanlines, targetOffset + 1, y * logicalSize * 4, (y + 1) * logicalSize * 4);
  }

  const header = Buffer.alloc(13);
  header.writeUInt32BE(logicalSize, 0);
  header.writeUInt32BE(logicalSize, 4);
  header[8] = 8;
  header[9] = 6;
  header[10] = 0;
  header[11] = 0;
  header[12] = 0;

  return Buffer.concat([
    Buffer.from([137, 80, 78, 71, 13, 10, 26, 10]),
    pngChunk('IHDR', header),
    pngChunk('IDAT', deflateSync(scanlines, { level: 9 })),
    pngChunk('IEND', Buffer.alloc(0)),
  ]);
}

function pngChunk(type, data) {
  const typeBuffer = Buffer.from(type, 'ascii');
  const chunk = Buffer.alloc(12 + data.length);
  chunk.writeUInt32BE(data.length, 0);
  typeBuffer.copy(chunk, 4);
  data.copy(chunk, 8);
  chunk.writeUInt32BE(crc32(Buffer.concat([typeBuffer, data])), 8 + data.length);
  return chunk;
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
  for (let value = 0; value < 256; value += 1) {
    let current = value;
    for (let bit = 0; bit < 8; bit += 1) {
      current = current & 1 ? 0xedb88320 ^ (current >>> 1) : current >>> 1;
    }
    table[value] = current >>> 0;
  }
  return table;
}
