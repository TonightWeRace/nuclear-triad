"""Draws the Nuclear Triad mark: three screens seen from the cockpit, a rectangle with a trapezoid
on each side, an artificial horizon cutting straight across all three, and a bare HUD around it.

The centre screen is a 3:2 rectangle. Each side screen is turned in toward the viewer, so it shows
as a trapezoid whose outer edge is taller than its inner one; the gaps between them stand for the
bezels. The proportions are abstract rather than a projection of a real rig: the side screens are
drawn narrow and steep so the mark stays compact enough to fill a square icon. The horizon is
banked a little and runs unbroken across both seams, which is the whole job of the plugin. On the
centre screen a HUD follows it: two pitch-ladder rungs above and two below, and an airspeed and an
altitude tape above it, all banked with it and cut out of the screen the same way.

Writes SVGs to branding/: the bare mark in ink and in white, and a rounded-square app icon, yellow
screens on ink or ink screens on yellow. With --png it also writes PNGs to branding/png/. Headless
Edge (profile in .tools/edge-profile) draws everything 1024 px wide, and smaller sizes are averaged
down from that, because Edge's screenshots quietly crop at small window sizes and scale factors.
The 64, 32 and 16 px icons are redrawn on whole pixels first (PIXEL_ICONS) so they stay sharp,
without the HUD, which needs about 256 px.

Python 3, standard library only: python tools/logo.py [--png]
"""
import base64
import math
import os
import struct
import subprocess
import sys
import zlib

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'branding')
EDGE = r'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'

INK, WHITE, YELLOW = '#111111', '#FFFFFF', '#FFD100'  # safety yellow, near ANSI Z535 / Pantone 109 C
UNIT = 1000           # SVG units per centre-screen width; every size below is in centre-screen widths
CENTRE_H = 2 / 3      # the centre screen's height (3:2)
GAP = 0.10            # between screens
SIDE_W = 0.42         # a side screen's apparent width
FLARE = 1.6           # a side screen's outer edge over its inner edge
CORNER = 0.04         # corner radius of every screen
MARGIN = 0.06         # around the bare mark, as a share of its width
ICON_FILL = 0.88      # how much of the icon's width the screens span
ICON_ROUND = 0.22     # the icon's corner radius, as a share of its side
HORIZON_BANK = 8      # degrees; the horizon rises to the right, as in a gentle right bank
HORIZON_CUT = 0.045   # the width of the cut the horizon makes through the screens
# A bare HUD on the centre screen, banked with the horizon and cut out like it, but thinner: a pitch
# ladder of two rungs above the horizon and two below, and above the horizon a tape either side,
# square to the horizon, for airspeed and altitude.
LADDER_STEPS = (0.09, 0.18)    # from the horizon to each rung, on both sides of it
LADDER_RUNG = 0.115            # each rung runs this far either side of the middle
LADDER_CUT = 0.022             # the width of every HUD line
# from the middle to each tape, along the horizon: it clears the rungs' ends by the gap between rungs
TAPE_X = LADDER_RUNG + LADDER_STEPS[0] - LADDER_CUT / 2
TAPE_SPAN = (0.05, 0.25)       # how far above the horizon each tape starts and ends

PNG_WIDTH = 1024      # Edge's render; a multiple of every smaller size
ICON_SIZES = (256,)
# Small icons fitted to whole pixels: side screen width, gap, centre width and height, the side
# screens' outer height, and the horizon's cut, all in px. Gaps and the cut get relatively wider
# here so they stay visible; at 16 px there's no room for the horizon, so it's left out.
PIXEL_ICONS = {64: (12, 2, 28, 18, 30, 2), 32: (6, 1, 14, 10, 16, 2), 16: (3, 1, 6, 4, 6, 0)}


def screens():
    """The left, centre and right screens as polygons in SVG units (y down), centred on 0."""
    w, h = UNIT / 2, UNIT * CENTRE_H / 2
    x0, x1 = w + GAP * UNIT, w + (GAP + SIDE_W) * UNIT
    right = [(x0, -h), (x1, -h * FLARE), (x1, h * FLARE), (x0, h)]
    left = [(-x, y) for x, y in reversed(right)]
    return [left, [(-w, -h), (w, -h), (w, h), (-w, h)], right]


def pixel_screens(size):
    """The screens for a PIXEL_ICONS size, in px from the icon's top-left corner."""
    side, gap, cw, ch, outer, _ = PIXEL_ICONS[size]
    x0, c, o = (size - 2 * side - 2 * gap - cw) / 2, (size - ch) / 2, (size - outer) / 2
    left = [(x0, o), (x0 + side, c), (x0 + side, size - c), (x0, size - o)]
    right = [(size - x, y) for x, y in reversed(left)]
    x1 = x0 + side + gap
    return [left, [(x1, c), (x1 + cw, c), (x1 + cw, size - c), (x1, size - c)], right]


def cut(poly, width, centre):
    """The parts of a screen above and below the horizon, a band width wide through centre, banked
    HORIZON_BANK degrees. Each part is (points, on_cut), on_cut marking the corners the cut made."""
    a = math.radians(HORIZON_BANK)
    n = (math.sin(a), math.cos(a))          # the band's normal, pointing down the page

    def outside(p, side):                   # <= 0 on that side of the band
        return width / 2 - side * ((p[0] - centre[0]) * n[0] + (p[1] - centre[1]) * n[1])

    parts = []
    for side in (-1, 1):                    # above the band, then below it
        pts, on_cut = [], []
        for i, p in enumerate(poly):
            q = poly[(i + 1) % len(poly)]
            fp, fq = outside(p, side), outside(q, side)
            if fp <= 0:
                pts.append(p)
                on_cut.append(False)
            if fp * fq < 0:
                t = fp / (fp - fq)
                pts.append((p[0] + (q[0] - p[0]) * t, p[1] + (q[1] - p[1]) * t))
                on_cut.append(True)
        parts.append((pts, on_cut))
    return parts


def rounded(poly, radii, seen=None):
    """A closed path through poly, each corner rounded to its radius; 0 leaves it sharp. The points
    the path runs through go into seen, if given."""
    d = []
    for i, (p, r) in enumerate(zip(poly, radii)):
        if not r:
            d.append(f'{"L" if d else "M"}{num(p[0])} {num(p[1])}')
            if seen is not None:
                seen.append(p)
            continue
        prev, nxt = poly[i - 1], poly[(i + 1) % len(poly)]
        a, b = (prev[0] - p[0], prev[1] - p[1]), (nxt[0] - p[0], nxt[1] - p[1])
        la, lb = math.hypot(*a), math.hypot(*b)
        a, b = (a[0] / la, a[1] / la), (b[0] / lb, b[1] / lb)
        half = math.acos(max(-1.0, min(1.0, a[0] * b[0] + a[1] * b[1]))) / 2
        t = min(r / math.tan(half), la / 2, lb / 2)       # from the corner to where the arc starts
        start, end = (p[0] + a[0] * t, p[1] + a[1] * t), (p[0] + b[0] * t, p[1] + b[1] * t)
        sweep = 1 if a[1] * b[0] - a[0] * b[1] > 0 else 0   # clockwise turn on screen
        d.append(f'{"L" if d else "M"}{num(start[0])} {num(start[1])}'
                 f'A{num(t * math.tan(half))} {num(t * math.tan(half))} 0 0 {sweep} {num(end[0])} {num(end[1])}')
        if seen is not None:
            seen += [start, end]
    return ''.join(d) + 'Z'


def num(x):
    s = f'{x:.1f}'.rstrip('0').rstrip('.')
    return '0' if s == '-0' else s


def outline():
    """The mark's path data, and every point the path runs through."""
    parts = [part for poly in screens() for part in cut(poly, HORIZON_CUT * UNIT, (0, 0))]
    d, seen = [], []
    for pts, on_cut in parts:
        d.append(rounded(pts, [0 if c else CORNER * UNIT for c in on_cut], seen))
    d += ['M' + 'L'.join(f'{num(x)} {num(y)}' for x, y in bar) + 'Z' for bar in ladder()]
    return ''.join(d), seen


def ladder():
    """The HUD's lines, as rectangles inside the centre screen; drawn with the even-odd rule, they
    come out as holes."""
    a = math.radians(HORIZON_BANK)
    along, down = (math.cos(a), -math.sin(a)), (math.sin(a), math.cos(a))
    rung, half = LADDER_RUNG * UNIT, LADDER_CUT * UNIT / 2
    # each line as (from, to) along the horizon and (from, to) down from it, with the horizon at 0
    lines = [((-rung, rung), (step * UNIT - half, step * UNIT + half))
             for step in [-s for s in LADDER_STEPS] + list(LADDER_STEPS)]
    lines += [((x * TAPE_X * UNIT - half, x * TAPE_X * UNIT + half), (-TAPE_SPAN[1] * UNIT, -TAPE_SPAN[0] * UNIT))
              for x in (-1, 1)]
    return [[(u * along[0] + v * down[0], u * along[1] + v * down[1]) for u, v in
             ((u0, v0), (u1, v0), (u1, v1), (u0, v1))] for (u0, u1), (v0, v1) in lines]


def pixel_icon(size, tile, screen):
    """The icon redrawn for a PIXEL_ICONS size, with every screen edge on a whole pixel."""
    polys, width = pixel_screens(size), PIXEL_ICONS[size][-1]
    if width:
        polys = [pts for poly in polys for pts, _ in cut(poly, width, (size / 2, size / 2))]
    d = ''.join('M' + 'L'.join(f'{num(x)} {num(y)}' for x, y in p) + 'Z' for p in polys)
    return svg(f'<rect width="{size}" height="{size}" rx="{num(ICON_ROUND * size)}" fill="{tile}"/>'
               f'<path fill="{screen}" d="{d}"/>', (0, 0, size, size))


def svg(body, box):
    x0, y0, x1, y1 = box
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{num(x0)} {num(y0)} {num(x1 - x0)} {num(y1 - y0)}" '
            f'role="img" aria-label="Nuclear Triad"><title>Nuclear Triad</title>{body}</svg>\n')


def designs():
    """Every file as name -> (svg text, its viewBox, the bounds of what it draws, and the icon's
    pixel-fitted sizes as {px: (svg text, viewBox, bounds)}). Boxes are (left, top, right, bottom)."""
    d, seen = outline()
    xs, ys = [x for x, _ in seen], [y for _, y in seen]
    drawn = (min(xs), min(ys), max(xs), max(ys))
    m = (drawn[2] - drawn[0]) * MARGIN
    view = (drawn[0] - m, drawn[1] - m, drawn[2] + m, drawn[3] + m)
    s = (drawn[2] - drawn[0]) / ICON_FILL / 2
    square = (-s, -s, s, s)

    def icon(tile, screen):
        body = (f'<rect x="{num(-s)}" y="{num(-s)}" width="{num(2 * s)}" height="{num(2 * s)}" '
                f'rx="{num(ICON_ROUND * 2 * s)}" fill="{tile}"/><path fill="{screen}" fill-rule="evenodd" d="{d}"/>')
        small = {px: (pixel_icon(px, tile, screen), (0, 0, px, px), (0, 0, px, px)) for px in PIXEL_ICONS}
        return svg(body, square), square, square, small

    return {
        'nuclear-triad-mark': (svg(f'<path fill="{INK}" fill-rule="evenodd" d="{d}"/>', view), view, drawn, {}),
        'nuclear-triad-mark-white': (svg(f'<path fill="{WHITE}" fill-rule="evenodd" d="{d}"/>', view), view, drawn, {}),
        'nuclear-triad-icon': icon(INK, YELLOW),
        'nuclear-triad-icon-light': icon(YELLOW, INK),
    }


def render(text, png_path, view, drawn):
    """Edge's render of the SVG on a transparent page, PNG_WIDTH wide, as (width, rows), once it's
    checked that the render holds all of the drawing."""
    scale = PNG_WIDTH / (view[2] - view[0])
    height = round((view[3] - view[1]) * scale)
    subprocess.run([EDGE, '--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1',
                    '--default-background-color=00000000',
                    '--user-data-dir=' + os.path.join(ROOT, '.tools', 'edge-profile'),
                    f'--window-size={PNG_WIDTH},{height}', '--screenshot=' + png_path,
                    'data:image/svg+xml;base64,' + base64.b64encode(text.encode()).decode()],
                   check=True, capture_output=True, timeout=120)
    w, h, rows = read_rgba(png_path)
    if (w, h) != (PNG_WIDTH, height):
        raise SystemExit(f'{png_path}: got {w}x{h}, wanted {PNG_WIDTH}x{height}')
    check_render(png_path, rows, [(v - view[i % 2]) * scale for i, v in enumerate(drawn)])
    return w, rows


def check_render(path, rows, box):
    """Edge crops a screenshot it can't fit without saying so. A whole render has clear corners, and
    its opaque pixels fill box (left, top, right, bottom, in px) to within two pixels."""
    h, w = len(rows), len(rows[0]) // 4
    if any(rows[y][x * 4 + 3] for y in (0, h - 1) for x in (0, w - 1)):
        raise SystemExit(f"{path}: a corner isn't clear, so the render was probably cropped")
    solid = bytes(int(v >= 128) for v in range(256))
    spans = [(y, m.find(1), m.rfind(1)) for y, m in enumerate(r[3::4].translate(solid) for r in rows) if 1 in m]
    got = (min(s[1] for s in spans), spans[0][0], max(s[2] for s in spans) + 1, spans[-1][0] + 1) if spans else None
    if not got or any(abs(g - b) > 2 for g, b in zip(got, box)):
        raise SystemExit(f'{path}: drew {got}, expected about {tuple(round(b) for b in box)}, '
                         'so the render was probably cropped')


def shrink(w, rows, f):
    """Averages f x f blocks of RGBA rows. Colour is weighted by alpha, so clear pixels add none."""
    out = []
    for y0 in range(0, len(rows), f):
        acc = [0] * (w // f * 4)
        for row in rows[y0:y0 + f]:
            for x in range(w):
                i, o, a = x * 4, x // f * 4, row[x * 4 + 3]
                acc[o] += row[i] * a
                acc[o + 1] += row[i + 1] * a
                acc[o + 2] += row[i + 2] * a
                acc[o + 3] += a
        line = bytearray()
        for o in range(0, len(acc), 4):
            a = acc[o + 3]
            line += bytes((round(acc[o] / a), round(acc[o + 1] / a), round(acc[o + 2] / a), round(a / f / f))) if a else bytes(4)
        out.append(bytes(line))
    return out


def read_rgba(path):
    """(width, height, rows of RGBA bytes) from an 8-bit RGBA, non-interlaced PNG, as Edge writes."""
    data, pos, idat = open(path, 'rb').read(), 8, b''
    while pos < len(data):
        length, kind = struct.unpack('>I4s', data[pos:pos + 8])
        body = data[pos + 8:pos + 8 + length]
        if kind == b'IHDR':
            w, h, depth, ctype, _, _, interlace = struct.unpack('>IIBBBBB', body)
            if (depth, ctype, interlace) != (8, 6, 0):
                raise SystemExit(f'{path}: not 8-bit RGBA')
        elif kind == b'IDAT':
            idat += body
        pos += 12 + length
    raw, stride, rows, prev = zlib.decompress(idat), w * 4, [], bytearray(w * 4)
    for y in range(h):
        kind, line = raw[y * (stride + 1)], bytearray(raw[y * (stride + 1) + 1:(y + 1) * (stride + 1)])
        for i in range(stride):
            a, b, c = (line[i - 4] if i >= 4 else 0), prev[i], (prev[i - 4] if i >= 4 else 0)
            if kind == 1:
                line[i] = (line[i] + a) & 255
            elif kind == 2:
                line[i] = (line[i] + b) & 255
            elif kind == 3:
                line[i] = (line[i] + (a + b) // 2) & 255
            elif kind == 4:
                p = a + b - c
                pa, pb, pc = abs(p - a), abs(p - b), abs(p - c)
                line[i] = (line[i] + (a if pa <= pb and pa <= pc else b if pb <= pc else c)) & 255
        rows.append(bytes(line))
        prev = line
    return w, h, rows


def write_rgba(path, w, h, rows):
    def chunk(kind, body):
        return struct.pack('>I', len(body)) + kind + body + struct.pack('>I', zlib.crc32(kind + body))
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0))
                + chunk(b'IDAT', zlib.compress(b''.join(b'\0' + r for r in rows), 9)) + chunk(b'IEND', b''))


def main():
    png = '--png' in sys.argv[1:]
    os.makedirs(os.path.join(OUT, 'png') if png else OUT, exist_ok=True)
    for name, (text, view, drawn, small) in designs().items():
        path = os.path.join(OUT, name + '.svg')
        with open(path, 'w', encoding='utf-8', newline='\n') as f:
            f.write(text)
        print(os.path.relpath(path, ROOT))
        if not png:
            continue
        out = os.path.join(OUT, 'png', f'{name}-{PNG_WIDTH}.png')
        w, rows = render(text, out, view, drawn)
        print('  ' + os.path.relpath(out, ROOT))
        sizes = [(s, rows) for s in ICON_SIZES] if small else []
        for size, (fitted, fview, fdrawn) in sorted(small.items(), reverse=True):
            scratch = os.path.join(OUT, 'png', f'.{name}-{size}-render.png')
            sizes.append((size, render(fitted, scratch, fview, fdrawn)[1]))
            os.remove(scratch)
        for size, big in sizes:
            out = os.path.join(OUT, 'png', f'{name}-{size}.png')
            write_rgba(out, size, size, shrink(PNG_WIDTH, big, PNG_WIDTH // size))
            print('  ' + os.path.relpath(out, ROOT))


if __name__ == '__main__':
    main()
