# Generates the Split Signal mark from exact geometry (run: py -3 make_mark.py)
import math
CX, CY, SW, GAP, STEP, DOT = 128, 204, 28, 21, 24, 18
RADII = (44, 88, 132)

def pt(r, deg):
    a = math.radians(deg)
    return CX + r * math.cos(a), CY - r * math.sin(a)

def half(side, RADII=RADII):
    # side -1 = left, +1 = right. Each arc runs from 45° off vertical to just short of the centre gap.
    out = []
    for r in RADII:
        end = math.degrees(math.acos(side * GAP / r))      # where x = CX ± GAP
        start = 90 - side * 45
        x1, y1 = pt(r, start); x2, y2 = pt(r, end)
        sweep = 1 if side < 0 else 0
        out.append(f'M{x1:.1f} {y1:.1f} A{r} {r} 0 0 {sweep} {x2:.1f} {y2:.1f}')
    return ' '.join(out)

def dot(side):
    x = CX + side * 6                                         # small gap between the dot halves
    sweep = 0 if side < 0 else 1
    return f'M{x} {CY - DOT} A{DOT} {DOT} 0 0 {sweep} {x} {CY + DOT} Z'

def mark(left, right, tile=None, scale=1.0, radii=RADII, sw=SW, rx=56):
    t = f'translate({128 - 128 * scale:.2f} {128 - 128 * scale:.2f}) scale({scale})'
    bg = f'<rect width="256" height="256" rx="{rx}" fill="{tile}"/>' if tile else ''
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">
  {bg}<g transform="{t}">
    <path d="{half(-1, radii)}" fill="none" stroke="{left}" stroke-width="{sw}" stroke-linecap="round"/>
    <path d="{dot(-1)}" fill="{left}"/>
    <g transform="translate(0 -{STEP})">
      <path d="{half(1, radii)}" fill="none" stroke="{right}" stroke-width="{sw}" stroke-linecap="round"/>
      <path d="{dot(1)}" fill="{right}"/>
    </g>
  </g>
</svg>
'''

INK, ACCENT, TILE = '#0F172A', '#F59E0B', '#0F172A'
open('symbol-black.svg', 'w').write(mark('#000', '#000'))
open('symbol-color.svg', 'w').write(mark(INK, ACCENT))
open('app-icon.svg', 'w').write(mark('#FFFFFF', ACCENT, tile=TILE, scale=0.78))
print('ok')
# Small-size cut for 16-24 px: two heavier arcs, drawn larger on the tile.
open('app-icon-small.svg', 'w').write(mark('#FFFFFF', ACCENT, tile=TILE, scale=0.95, radii=(60, 120), sw=40, rx=40))
