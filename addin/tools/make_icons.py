"""Generates the ribbon icons (16/32 px PNG) without any imaging library.
Run from the addin folder:  python tools/make_icons.py"""
import math, os, struct, zlib

OUT = os.path.join(os.path.dirname(__file__), "..", "src", "CAD2Revit", "Resources")
BLUE, WHITE, AMBER = (32, 96, 176), (255, 255, 255), (245, 180, 40)


def png(path, px, n):
    raw = b"".join(b"\x00" + b"".join(bytes(px[y * n + x]) for x in range(n)) for y in range(n))
    def chunk(t, d):
        return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    data = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", n, n, 8, 6, 0, 0, 0)) + \
        chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(data)


def render(n, shapes):
    """shapes: list of (inside(x, y) -> bool, rgb). 4x4 supersampling."""
    px = []
    ss = 4
    for y in range(n):
        for x in range(n):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    u, v = (x + (sx + .5) / ss) / n, (y + (sy + .5) / ss) / n
                    col = None
                    for inside, rgb in shapes:
                        if inside(u, v):
                            col = rgb
                    if col:
                        acc[0] += col[0]; acc[1] += col[1]; acc[2] += col[2]; acc[3] += 1
            a = acc[3] / (ss * ss)
            px.append((int(acc[0] / acc[3]) if acc[3] else 0, int(acc[1] / acc[3]) if acc[3] else 0,
                       int(acc[2] / acc[3]) if acc[3] else 0, int(a * 255)))
    return px


def rrect(x0, y0, x1, y1, r):
    def f(u, v):
        cx, cy = min(max(u, x0 + r), x1 - r), min(max(v, y0 + r), y1 - r)
        return (u - cx) ** 2 + (v - cy) ** 2 <= r * r and x0 <= u <= x1 and y0 <= v <= y1
    return f


def rect(x0, y0, x1, y1):
    return lambda u, v: x0 <= u <= x1 and y0 <= v <= y1


def circle(cx, cy, r):
    return lambda u, v: (u - cx) ** 2 + (v - cy) ** 2 <= r * r


def ring(cx, cy, r0, r1):
    return lambda u, v: r0 * r0 <= (u - cx) ** 2 + (v - cy) ** 2 <= r1 * r1


bg = (rrect(0.03, 0.03, 0.97, 0.97, 0.18), BLUE)
list_icon = [bg] + [s for i in range(3) for s in (
    (rect(0.2, 0.24 + i * 0.2, 0.32, 0.36 + i * 0.2), AMBER),
    (rect(0.4, 0.26 + i * 0.2, 0.8, 0.34 + i * 0.2), WHITE))]
place_icon = [bg,
              (ring(0.5, 0.5, 0.2, 0.3), WHITE),
              (rect(0.47, 0.1, 0.53, 0.9), WHITE), (rect(0.1, 0.47, 0.9, 0.53), WHITE),
              (circle(0.5, 0.5, 0.11), AMBER)]

def gear_teeth():
    out = []
    for i in range(8):
        a = i * math.pi / 4
        cx, cy = 0.5 + 0.3 * math.cos(a), 0.5 + 0.3 * math.sin(a)
        out.append((circle(cx, cy, 0.075), WHITE))
    return out


settings_icon = [bg] + gear_teeth() + [(circle(0.5, 0.5, 0.27), WHITE), (circle(0.5, 0.5, 0.11), BLUE)]
help_icon = [bg, (circle(0.5, 0.5, 0.36), WHITE),
             (rect(0.45, 0.43, 0.55, 0.72), BLUE), (circle(0.5, 0.32, 0.06), AMBER)]

for name, shapes in (("ListBlocks", list_icon), ("PlaceFamilies", place_icon),
                     ("Settings", settings_icon), ("Help", help_icon)):
    for n in (16, 32):
        png(os.path.join(OUT, "%s%d.png" % (name, n)), render(n, shapes), n)
print("icons written to", os.path.abspath(OUT))
