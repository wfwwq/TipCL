// Tiny PNG encoder (RGBA, no dependencies) used by the hello_wait tool and the /image route.

import { deflateSync } from 'node:zlib';

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (const b of buf) c = CRC_TABLE[(c ^ b) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const td = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(td));
  return Buffer.concat([len, td, crc]);
}

/**
 * Encode RGBA pixels. `pixel(x, y)` returns [r, g, b, a].
 * @param {number} width @param {number} height @param {(x: number, y: number) => number[]} pixel
 */
export function encodePng(width, height, pixel) {
  const raw = Buffer.alloc((width * 4 + 1) * height);
  let o = 0;
  for (let y = 0; y < height; y++) {
    raw[o++] = 0; // filter: none
    for (let x = 0; x < width; x++) {
      const [r, g, b, a = 255] = pixel(x, y);
      raw[o++] = r; raw[o++] = g; raw[o++] = b; raw[o++] = a;
    }
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', deflateSync(raw)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/** A gradient card with a disc whose size shows `fraction` (0..1), tinted by a hex color. */
export function badge(fraction = 1, hex = '#4f8cff', size = 96) {
  const m = /^#?([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/i.exec(hex) ?? [0, '4f', '8c', 'ff'];
  const [r, g, b] = [1, 2, 3].map((i) => parseInt(m[i], 16));
  const radius = (size / 2 - 6) * Math.max(0.15, Math.min(1, fraction));
  return encodePng(size, size, (x, y) => {
    const dx = x - size / 2, dy = y - size / 2;
    if (dx * dx + dy * dy <= radius * radius) return [r, g, b, 255];
    const t = y / size;
    return [Math.round(24 + 30 * t), Math.round(24 + 20 * t), Math.round(32 + 40 * t), 255];
  });
}
