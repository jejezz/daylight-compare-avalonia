#!/usr/bin/env python3
"""Draw Daylight Compare's prop glyph: two overlapping documents, → on the back
one and ← on the front one (compare / copy both ways), Sticker style (white
outline + soft shadow), the way Icons8 "Sticker" glyphs look.

    python3 tool/icon/draw_glyph.py      # writes assets/icon/source_glyph.svg and .png (1024px)

One geometry feeds both files so they never drift. The PNG is drawn with Pillow
(4x supersampled) because render_svg.swift needs macOS. Then run
`python3 tool/icon/generate_icons.py <cat image>` as usual (conventions/icons.md).
"""
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'assets/icon'
SIZE = 1024
SS = 4  # supersampling

OUTLINE = 34        # white sticker edge
RADIUS = 56         # document corner radius
FOLD = 150          # folded corner size

# (left, top, right, bottom) in the 1024 canvas
BACK = (128, 96, 600, 688)
FRONT = (424, 336, 896, 928)

BACK_FILL, BACK_EDGE, BACK_FOLD = '#A9D8FF', '#4A93E8', '#D6ECFF'
FRONT_FILL, FRONT_EDGE, FRONT_FOLD = '#3D9BF5', '#1F6FD1', '#8CC7FF'
BACK_ARROW = '#2F7BE0'
FRONT_ARROW = '#FFFFFF'
EDGE = 10           # coloured line inside the white outline



def arrow_points(cx, cy, length, shaft, head_w, head_l, direction):
    """Solid arrow centred on (cx, cy); direction +1 → right, -1 → left."""
    half = length / 2
    tail, tip = cx - direction * half, cx + direction * half
    neck = tip - direction * head_l
    return [(tail, cy - shaft / 2), (neck, cy - shaft / 2), (neck, cy - head_w / 2), (tip, cy),
            (neck, cy + head_w / 2), (neck, cy + shaft / 2), (tail, cy + shaft / 2)]


BACK_ARROW_PTS = arrow_points(270, 262, 230, 60, 150, 100, +1)
FRONT_ARROW_PTS = arrow_points(640, 545, 300, 68, 176, 120, -1)


def scaled(points, k):
    return [(x * k, y * k) for x, y in points]


def doc_mask(box, grow, k):
    """Mask of a document: rounded rectangle minus the folded corner."""
    l, t, r, b = box
    l, t, r, b = (l - grow) * k, (t - grow) * k, (r + grow) * k, (b + grow) * k
    m = Image.new('L', (SIZE * k, SIZE * k), 0)
    d = ImageDraw.Draw(m)
    d.rounded_rectangle((l, t, r, b), radius=(RADIUS + grow) * k, fill=255)
    f = (FOLD + grow * 0.42) * k
    d.polygon([(r - f, t - 2 * k), (r + 2 * k, t - 2 * k), (r + 2 * k, t + f)], fill=0)
    return m


def fold_points(box, k):
    l, t, r, b = box
    return [((r - FOLD) * k, t * k), ((r - FOLD) * k, (t + FOLD - 18) * k),
            ((r - FOLD + 18) * k, (t + FOLD) * k), (r * k, (t + FOLD) * k)]


def paint(canvas: Image.Image, mask: Image.Image, color):
    layer = Image.new('RGBA', canvas.size, color)
    canvas.paste(layer, (0, 0), mask)


def draw_png() -> Image.Image:
    k = SS
    big = Image.new('RGBA', (SIZE * k, SIZE * k), (0, 0, 0, 0))

    # Shadow under the whole sticker.
    union = Image.new('L', big.size, 0)
    for box in (BACK, FRONT):
        union.paste(255, (0, 0), doc_mask(box, OUTLINE, k))
    shadow = Image.new('RGBA', big.size, (0, 0, 0, 0))
    shadow.paste((10, 20, 60, 90), (0, 14 * k), union)
    shadow = shadow.filter(ImageFilter.GaussianBlur(16 * k))
    big.alpha_composite(shadow)

    for box, fill, edge, fold, arrow, arrow_color in (
        (BACK, BACK_FILL, BACK_EDGE, BACK_FOLD, BACK_ARROW_PTS, BACK_ARROW),
        (FRONT, FRONT_FILL, FRONT_EDGE, FRONT_FOLD, FRONT_ARROW_PTS, FRONT_ARROW),
    ):
        paint(big, doc_mask(box, OUTLINE, k), '#FFFFFF')   # sticker edge
        paint(big, doc_mask(box, 0, k), edge)              # coloured line
        paint(big, doc_mask(box, -EDGE, k), fill)          # body
        d = ImageDraw.Draw(big)
        d.polygon(fold_points(box, k), fill=fold)          # folded corner
        d.line(fold_points(box, k)[:2] + fold_points(box, k)[2:], fill=edge, width=EDGE * k, joint='curve')
        d.polygon(scaled(arrow, k), fill=arrow_color)

    return big.resize((SIZE, SIZE), Image.LANCZOS)


def svg_doc(box, grow, color, radius):
    l, t, r, b = box
    l, t, r, b = l - grow, t - grow, r + grow, b + grow
    f = FOLD + grow * 0.42
    rr = radius
    return (f'<path fill="{color}" d="M{l + rr},{t} H{r - f} L{r},{t + f} V{b - rr} '
            f'Q{r},{b} {r - rr},{b} H{l + rr} Q{l},{b} {l},{b - rr} V{t + rr} Q{l},{t} {l + rr},{t} Z"/>')


def poly(points, color):
    return f'<polygon fill="{color}" points="{" ".join(f"{x:g},{y:g}" for x, y in points)}"/>'


def draw_svg() -> str:
    parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {SIZE} {SIZE}" width="{SIZE}" height="{SIZE}">',
             '<defs><filter id="s" x="-20%" y="-20%" width="140%" height="140%">'
             '<feGaussianBlur stdDeviation="16"/></filter></defs>',
             f'<g filter="url(#s)" opacity="0.35" transform="translate(0 14)" fill="#0A143C">'
             f'{svg_doc(BACK, OUTLINE, "#0A143C", RADIUS + OUTLINE)}{svg_doc(FRONT, OUTLINE, "#0A143C", RADIUS + OUTLINE)}</g>']
    for box, fill, edge, fold, arrow, arrow_color in (
        (BACK, BACK_FILL, BACK_EDGE, BACK_FOLD, BACK_ARROW_PTS, BACK_ARROW),
        (FRONT, FRONT_FILL, FRONT_EDGE, FRONT_FOLD, FRONT_ARROW_PTS, FRONT_ARROW),
    ):
        parts.append(svg_doc(box, OUTLINE, '#FFFFFF', RADIUS + OUTLINE))
        parts.append(svg_doc(box, 0, edge, RADIUS))
        parts.append(svg_doc(box, -EDGE, fill, RADIUS - EDGE))
        parts.append(poly(fold_points(box, 1), fold))
        parts.append(poly(arrow, arrow_color))
    parts.append('</svg>')
    return '\n'.join(parts) + '\n'


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    draw_png().save(OUT / 'source_glyph.png')
    (OUT / 'source_glyph.svg').write_text(draw_svg(), encoding='utf-8')
    print('glyph    assets/icon/source_glyph.png (1024), source_glyph.svg')


if __name__ == '__main__':
    main()
