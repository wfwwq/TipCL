"""AI3-visual: crop.py <in.png> <x> <y> <w> <h> <out.png> (used by shoot-mockups.ps1)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import vtlib  # noqa: E402


def main(argv):
    if len(argv) != 7:
        print('usage: crop.py in.png x y w h out.png')
        return 2
    src, x, y, w, h, dst = argv[1], float(argv[2]), float(argv[3]), float(argv[4]), float(argv[5]), argv[6]
    img = vtlib.read_png(src)
    out = vtlib.crop(img, round(x), round(y), round(w), round(h))
    vtlib.write_png(dst, *out)
    print('cropped %s -> %s (%dx%d)' % (os.path.basename(src), os.path.basename(dst), out[0], out[1]))
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
