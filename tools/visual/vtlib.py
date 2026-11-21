"""AI3-visual: tiny image helpers for the visual report (stdlib only; Pillow/numpy are used
only as an optional speed-up when they happen to be importable - nothing is installed).

Image model: (w, h, rgb) where rgb is a bytearray of w*h*3 bytes (8-bit RGB, alpha dropped
by compositing over white).
"""
import struct
import zlib

try:  # optional fast path
    from PIL import Image as _PIL  # type: ignore
except Exception:  # pragma: no cover
    _PIL = None


# ---------------------------------------------------------------- PNG decode / encode

def _paeth(a, b, c):
    p = a + b - c
    pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
    if pa <= pb and pa <= pc:
        return a
    return b if pb <= pc else c


def _read_png_stdlib(path):
    data = open(path, 'rb').read()
    if data[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError('not a PNG: %s' % path)
    pos, idat, w = 8, [], 0
    h = depth = ctype = interlace = 0
    plte = None
    trns = None
    while pos < len(data):
        n, = struct.unpack('>I', data[pos:pos + 4])
        typ = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + n]
        pos += 12 + n
        if typ == b'IHDR':
            w, h, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', body)
        elif typ == b'PLTE':
            plte = body
        elif typ == b'tRNS':
            trns = body
        elif typ == b'IDAT':
            idat.append(body)
        elif typ == b'IEND':
            break
    if depth != 8 or interlace:
        raise ValueError('unsupported PNG (depth %d interlace %d): %s' % (depth, interlace, path))
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw = zlib.decompress(b''.join(idat))
    stride = w * ch
    out = bytearray(h * stride)
    prev = bytearray(stride)
    i = 0
    for y in range(h):
        f = raw[i]
        line = bytearray(raw[i + 1:i + 1 + stride])
        i += 1 + stride
        if f == 1:
            for x in range(ch, stride):
                line[x] = (line[x] + line[x - ch]) & 255
        elif f == 2:
            for x in range(stride):
                line[x] = (line[x] + prev[x]) & 255
        elif f == 3:
            for x in range(stride):
                left = line[x - ch] if x >= ch else 0
                line[x] = (line[x] + ((left + prev[x]) >> 1)) & 255
        elif f == 4:
            for x in range(stride):
                a = line[x - ch] if x >= ch else 0
                c = prev[x - ch] if x >= ch else 0
                line[x] = (line[x] + _paeth(a, prev[x], c)) & 255
        out[y * stride:(y + 1) * stride] = line
        prev = line
    rgb = bytearray(w * h * 3)
    if ctype == 2:
        rgb[:] = out
    elif ctype == 6:
        for p in range(w * h):
            r, g, b, a = out[p * 4:p * 4 + 4]
            rgb[p * 3] = (r * a + 255 * (255 - a)) // 255
            rgb[p * 3 + 1] = (g * a + 255 * (255 - a)) // 255
            rgb[p * 3 + 2] = (b * a + 255 * (255 - a)) // 255
    elif ctype == 0:
        for p in range(w * h):
            rgb[p * 3] = rgb[p * 3 + 1] = rgb[p * 3 + 2] = out[p]
    elif ctype == 4:
        for p in range(w * h):
            v, a = out[p * 2], out[p * 2 + 1]
            rgb[p * 3] = rgb[p * 3 + 1] = rgb[p * 3 + 2] = (v * a + 255 * (255 - a)) // 255
    elif ctype == 3:
        for p in range(w * h):
            k = out[p]
            rgb[p * 3:p * 3 + 3] = plte[k * 3:k * 3 + 3]
    return w, h, rgb


def read_png(path):
    """(w, h, rgb bytearray). Accepts PNG (stdlib) or anything Pillow reads when available."""
    if _PIL is not None:
        im = _PIL.open(path)
        if im.mode in ('RGBA', 'LA', 'P'):
            im = im.convert('RGBA')
            bg = _PIL.new('RGBA', im.size, (255, 255, 255, 255))
            bg.alpha_composite(im)
            im = bg
        im = im.convert('RGB')
        return im.size[0], im.size[1], bytearray(im.tobytes())
    return _read_png_stdlib(path)


def write_png(path, w, h, rgb):
    if _PIL is not None:
        _PIL.frombytes('RGB', (w, h), bytes(rgb)).save(path)
        return
    raw = bytearray()
    stride = w * 3
    for y in range(h):
        raw.append(0)
        raw += rgb[y * stride:(y + 1) * stride]

    def chunk(t, b):
        c = zlib.crc32(t + b) & 0xffffffff
        return struct.pack('>I', len(b)) + t + b + struct.pack('>I', c)
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(bytes(raw), 6)) + chunk(b'IEND', b'')
    open(path, 'wb').write(png)


# ---------------------------------------------------------------- geometry

def crop(img, x, y, cw, ch):
    w, h, rgb = img
    x, y = max(0, int(x)), max(0, int(y))
    cw, ch = max(1, min(int(cw), w - x)), max(1, min(int(ch), h - y))
    out = bytearray(cw * ch * 3)
    for r in range(ch):
        s = ((y + r) * w + x) * 3
        out[r * cw * 3:(r + 1) * cw * 3] = rgb[s:s + cw * 3]
    return cw, ch, out


def resize(img, nw, nh):
    """Box-filter downscale / nearest upscale to nw x nh."""
    w, h, rgb = img
    if _PIL is not None:
        im = _PIL.frombytes('RGB', (w, h), bytes(rgb)).resize((nw, nh), _PIL.BOX if nw <= w else _PIL.BILINEAR)
        return nw, nh, bytearray(im.tobytes())
    out = bytearray(nw * nh * 3)
    for oy in range(nh):
        y0 = oy * h // nh
        y1 = max(y0 + 1, (oy + 1) * h // nh)
        for ox in range(nw):
            x0 = ox * w // nw
            x1 = max(x0 + 1, (ox + 1) * w // nw)
            sr = sg = sb = n = 0
            for yy in range(y0, y1, max(1, (y1 - y0) // 3)):
                base = yy * w
                for xx in range(x0, x1, max(1, (x1 - x0) // 3)):
                    p = (base + xx) * 3
                    sr += rgb[p]
                    sg += rgb[p + 1]
                    sb += rgb[p + 2]
                    n += 1
            o = (oy * nw + ox) * 3
            out[o] = sr // n
            out[o + 1] = sg // n
            out[o + 2] = sb // n
    return nw, nh, out


def gray(img):
    w, h, rgb = img
    return [(rgb[i] * 299 + rgb[i + 1] * 587 + rgb[i + 2] * 114) // 1000 for i in range(0, w * h * 3, 3)]
