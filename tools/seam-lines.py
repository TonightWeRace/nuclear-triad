"""Checks the sky grid across the bezel gap in a capture's eye-space seam views.

Every grid line is a great circle, so in an eye-space view (`-seamN-eye-*.png`) it must run
straight through the gap. Grid pixels are found by their colour shift against the local
background; lines by a Hough transform on each side of the gap. Each line that reaches the gap
is followed 80 px into the other side, whose grid pixels near it are binned by signed distance:
a continuous line peaks at 0, a step at its size. A line can also leave the view, run behind
the cockpit, or run past the other screen's lit area; those find nothing to compare.

Python 3, standard library only: python tools/seam-lines.py <eye-view png>...
"""
import math
import struct
import sys
import zlib

GAP = (24, 24, 24)  # what the capture fills the bezel gap with


def read_png(path):
    """8-bit RGB or RGBA, non-interlaced, as rows of (r, g, b)."""
    data = open(path, 'rb').read()
    if data[:8] != b'\x89PNG\r\n\x1a\n': raise ValueError(f'{path}: not a PNG')
    pos, idat = 8, b''
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b'IHDR':
            width, height, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', body)
            if depth != 8 or interlace or ctype not in (2, 6): raise ValueError(f'{path}: unsupported PNG')
        elif kind == b'IDAT':
            idat += body
        pos += 12 + length
    bpp = 3 if ctype == 2 else 4
    raw, stride = zlib.decompress(idat), width * bpp
    rows, prev = [], bytearray(stride)
    for y in range(height):
        f = raw[y * (stride + 1)]
        line = bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a = line[i - bpp] if i >= bpp else 0
            b, c = prev[i], (prev[i - bpp] if i >= bpp else 0)
            if f == 1: line[i] = (line[i] + a) & 255
            elif f == 2: line[i] = (line[i] + b) & 255
            elif f == 3: line[i] = (line[i] + (a + b) // 2) & 255
            elif f == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(line)
        prev = line
    return width, height, [[tuple(r[x * bpp:x * bpp + 3]) for x in range(width)] for r in rows]


def grid_pixels(w, h, px, g0, g1):
    """Pixels shifted toward cyan (meridians) or green (tilted circles) against the background
    3 px either side. A thin line over white cloud mostly shows as a drop in red."""
    out = {}
    for y in range(3, h - 3):
        row = px[y]
        for x in range(3, w - 3):
            if g0 - 4 <= x <= g1 + 4: continue
            p, best = row[x], None
            for a, b in ((px[y - 3][x], px[y + 3][x]), (row[x - 3], row[x + 3])):
                d = tuple(p[i] - (a[i] + b[i]) / 2 for i in range(3))
                cyan, green = d[2] - d[0], d[1] - max(d[0], d[2])
                if best is None or max(cyan, green) > max(best[0], best[1]): best = (cyan, green, d)
            cyan, green, d = best
            if cyan > 45 and d[1] > -15 and cyan >= green: out[(x, y)] = 'cyan'
            elif green > 45: out[(x, y)] = 'green'
    return out


def hough(points, min_votes=30, steps=720):
    trig = [(math.cos(math.pi * t / steps), math.sin(math.pi * t / steps)) for t in range(steps)]
    acc = {}
    for x, y in points:
        for t, (c, s) in enumerate(trig):
            k = (t, int(round(x * c + y * s)))
            acc[k] = acc.get(k, 0) + 1
    peaks = []
    for (t, r), v in sorted(acc.items(), key=lambda kv: -kv[1]):
        if v < min_votes: break
        if not any(abs(t - pt) <= 10 and abs(r - pr) <= 8 for pt, pr in peaks): peaks.append((t, r))
    return [(math.pi * t / steps, r) for t, r in peaks]


def fit(points, theta, rho, tol=1.5):
    """Least-squares line through the points near a Hough line: (normal angle, offset, points)."""
    c, s = math.cos(theta), math.sin(theta)
    near = [(x, y) for x, y in points if abs(x * c + y * s - rho) <= tol]
    if len(near) < 25: return None
    mx, my = sum(x for x, _ in near) / len(near), sum(y for _, y in near) / len(near)
    sxx, syy = sum((x - mx) ** 2 for x, _ in near), sum((y - my) ** 2 for _, y in near)
    sxy = sum((x - mx) * (y - my) for x, y in near)
    n = 0.5 * math.atan2(2 * sxy, sxx - syy) + math.pi / 2
    return n, mx * math.cos(n) + my * math.sin(n), near


def follow(line, others, h, start, step):
    n, rho, _ = line
    c, s = math.cos(n), math.sin(n)
    xs = [start + step * i for i in range(80)]
    if abs(s) < 1e-6: return None, 'runs along the gap'
    if sum(1 for x in xs if 3 <= (rho - x * c) / s < h - 3) < 25: return None, 'leaves the view'
    bins = {}
    for x, y in others:
        if min(xs) <= x <= max(xs):
            d = x * c + y * s - rho
            if abs(d) <= 20: bins[round(d)] = bins.get(round(d), 0) + 1
    if sum(bins.values()) < 8: return None, 'nothing to compare (behind the cockpit, or past the lit area)'
    peak = max(bins, key=bins.get)
    near = {k: v for k, v in bins.items() if abs(k - peak) <= 1}
    offset = sum(k * v for k, v in near.items()) / sum(near.values())
    return offset, f'continues {offset:+.1f} px off ({sum(near.values())} of {sum(bins.values())} px)'


def analyse(path):
    w, h, px = read_png(path)
    gap = [x for x in range(w) if sum(1 for y in range(0, h, 8) if px[y][x] == GAP) > h / 8 * 0.9]
    if not gap: raise ValueError(f'{path}: no bezel gap found; is this an eye-space view?')
    g0, g1 = min(gap), max(gap)
    print(f'{path}: gap x {g0}-{g1}')
    mask, worst = grid_pixels(w, h, px, g0, g1), 0.0
    for colour in ('cyan', 'green'):
        left = [p for p, k in mask.items() if k == colour and p[0] < g0]
        right = [p for p, k in mask.items() if k == colour and p[0] > g1]
        for side, pts, others, start, step, edge in (('left', left, right, g1 + 5, 1, g0), ('right', right, left, g0 - 5, -1, g1)):
            for theta, rho in hough(pts):
                line = fit(pts, theta, rho)
                if not line: continue
                reach = (max if side == 'left' else min)(x for x, _ in line[2])
                if abs(reach - edge) > 40: continue  # doesn't come near the gap
                offset, verdict = follow(line, others, h, start, step)
                if offset is not None: worst = max(worst, abs(offset))
                angle = (math.degrees(line[0]) - 90) % 180
                print(f'  {colour:5} line on the {side:5} at {angle:5.1f} deg ({len(line[2])} px): {verdict}')
    return worst


if __name__ == '__main__':
    if len(sys.argv) < 2: sys.exit(__doc__)
    worst = max(analyse(p) for p in sys.argv[1:])
    print(f'Largest offset across a gap: {worst:.1f} px')
