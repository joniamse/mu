"""Generates the HD mouse cursors of the client ([Modern] HdCursor).

The classic cursors are 32x32 textures drawn 24 units wide; these are drawn at 8x and scaled down
to 64x64 (the talk cursor is a 2x2 atlas of 64x64 frames, like the original), so they stay sharp
at any window size. Every cursor points from its top-left corner, like the classic ones (the game
draws them 2 units up-left of the mouse).

Output: OZT files (the game's TGA wrapper) in <out>/Interface/HD, plus PNG previews with --preview.

    python tools/cursor_hd.py client-src/MuMain/src/bin/Data [--preview DIR]
"""

from __future__ import annotations

import argparse
import math
import pathlib
import struct

from PIL import Image, ImageChops, ImageDraw, ImageFilter

SIZE = 64
SS = 8  # supersampling
CANVAS = SIZE * SS
TIP = (0.05, 0.05)

# Palette: steel with a gold rim, like the modern HUD.
OUTLINE = (6, 9, 14, 255)
RIM = (214, 176, 92, 255)
STEEL_TOP = (240, 244, 250)
STEEL_BOTTOM = (112, 128, 150)


def px(point):
    return (point[0] * CANVAS, point[1] * CANVAS)


def polygon_mask(points):
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    ImageDraw.Draw(mask).polygon([px(p) for p in points], fill=255)
    return mask


def ellipse_mask(center, radius):
    mask = Image.new("L", (CANVAS, CANVAS), 0)
    cx, cy = px(center)
    r = radius * CANVAS
    ImageDraw.Draw(mask).ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)
    return mask


def union(*masks):
    result = masks[0]
    for mask in masks[1:]:
        result = ImageChops.lighter(result, mask)
    return result


def grow(mask, pixels):
    size = int(pixels * SS) * 2 + 1
    return mask.filter(ImageFilter.MaxFilter(size)) if size > 1 else mask


def shrink(mask, pixels):
    size = int(pixels * SS) * 2 + 1
    return mask.filter(ImageFilter.MinFilter(size)) if size > 1 else mask


def gradient(top, bottom, diagonal=True):
    """An RGBA image going from `top` (top-left) to `bottom` (bottom-right)."""
    line = Image.new("RGBA", (CANVAS * 2, 1))
    for x in range(CANVAS * 2):
        t = x / (CANVAS * 2 - 1)
        line.putpixel((x, 0), tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,))
    if not diagonal:
        column = line.resize((1, CANVAS)).transpose(Image.TRANSPOSE)
        return column.resize((CANVAS, CANVAS))
    big = line.resize((CANVAS * 2, CANVAS * 2)).rotate(-45, resample=Image.BICUBIC)
    offset = CANVAS // 2
    return big.crop((offset, offset, offset + CANVAS, offset + CANVAS))


def solid(color):
    return Image.new("RGBA", (CANVAS, CANVAS), color if len(color) == 4 else color + (255,))


class Layer:
    """Shapes painted from back to front, each with a dark outline; one soft shadow under all."""

    def __init__(self):
        self.image = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        self.silhouette = Image.new("L", (CANVAS, CANVAS), 0)

    def paint(self, mask, fill, rim=None, outline=1.4, rim_width=0.9):
        outer = grow(mask, outline)
        self.image.paste(solid(OUTLINE), (0, 0), outer)
        if rim is not None:
            self.image.paste(solid(rim), (0, 0), mask)
            inner = shrink(mask, rim_width)
            self.image.paste(fill, (0, 0), inner)
        else:
            self.image.paste(fill, (0, 0), mask)
        self.silhouette = ImageChops.lighter(self.silhouette, outer)

    def gloss(self, mask, strength=70):
        """A light sheen over the upper-left half of a shape."""
        sheen = Image.new("L", (CANVAS, CANVAS), 0)
        ImageDraw.Draw(sheen).polygon([(0, 0), (CANVAS * 0.9, 0), (0, CANVAS * 0.9)], fill=strength)
        sheen = ImageChops.multiply(sheen, shrink(mask, 1.6))
        self.image.paste(solid((255, 255, 255, 255)), (0, 0), sheen)

    def finish(self):
        shadow = self.silhouette.filter(ImageFilter.GaussianBlur(2.2 * SS))
        shadow = shadow.point(lambda v: int(v * 0.55))
        result = Image.new("RGBA", (CANVAS, CANVAS), (0, 0, 0, 0))
        offset = int(1.6 * SS)
        result.paste(solid((0, 0, 0, 255)), (offset, offset * 2), shadow)
        result.alpha_composite(self.image)
        return result.resize((SIZE, SIZE), Image.LANCZOS)


# ---- Shapes --------------------------------------------------------------------------------

ARROW = [(0.05, 0.05), (0.05, 0.80), (0.24, 0.63), (0.37, 0.93), (0.50, 0.87), (0.37, 0.59), (0.62, 0.59)]


def scaled(points, factor, origin=TIP):
    return [(origin[0] + (x - origin[0]) * factor, origin[1] + (y - origin[1]) * factor) for x, y in points]


def along(t, side, start=TIP):
    """A point on the diagonal from the tip to the bottom-right: t along it, side across it."""
    d = 1 / math.sqrt(2)
    return (start[0] + t * d - side * d, start[1] + t * d + side * d)


def arrow(layer, fill_top=STEEL_TOP, fill_bottom=STEEL_BOTTOM, factor=1.0):
    mask = polygon_mask(scaled(ARROW, factor))
    layer.paint(mask, gradient(fill_top, fill_bottom), rim=RIM)
    layer.gloss(mask, 45)


def badge(layer, top, bottom, symbol):
    center, radius = (0.72, 0.72), 0.21
    mask = ellipse_mask(center, radius)
    layer.paint(mask, gradient(top, bottom, diagonal=False), rim=RIM)
    layer.gloss(mask, 50)
    draw = ImageDraw.Draw(layer.image)
    symbol(draw, px(center), radius * CANVAS)


def sword(layer, blade_top, blade_bottom, outline_color=None):
    blade = polygon_mask([along(0, 0), along(0.62, -0.055), along(0.62, 0.055)])
    guard = polygon_mask([along(0.60, -0.19), along(0.67, -0.19), along(0.67, 0.19), along(0.60, 0.19)])
    grip = polygon_mask([along(0.67, -0.032), along(0.88, -0.032), along(0.88, 0.032), along(0.67, 0.032)])
    pommel = ellipse_mask(along(0.92, 0), 0.055)
    if outline_color is not None:
        glow = grow(blade, 2.4).filter(ImageFilter.GaussianBlur(1.5 * SS))
        layer.image.paste(solid(outline_color), (0, 0), glow)
    layer.paint(grip, gradient((92, 60, 38), (40, 24, 14)))
    layer.paint(blade, gradient(blade_top, blade_bottom), rim=(250, 250, 255, 255), rim_width=0.7)
    layer.gloss(blade, 60)
    layer.paint(guard, gradient((255, 220, 130), (150, 104, 36)))
    layer.paint(pommel, gradient((255, 220, 130), (150, 104, 36)))


def hammer(layer):
    handle = polygon_mask([along(0.18, -0.035), along(0.95, -0.035), along(0.95, 0.035), along(0.18, 0.035)])
    head = polygon_mask([along(0.04, -0.20), along(0.26, -0.20), along(0.26, 0.20), along(0.04, 0.20)])
    band = polygon_mask([along(0.27, -0.05), along(0.33, -0.05), along(0.33, 0.05), along(0.27, 0.05)])
    layer.paint(handle, gradient((150, 100, 60), (70, 42, 22)))
    layer.paint(head, gradient(STEEL_TOP, (90, 104, 124)), rim=RIM)
    layer.gloss(head, 60)
    layer.paint(band, gradient((255, 220, 130), (150, 104, 36)))


def forbidden(layer):
    center, radius = (0.42, 0.42), 0.34
    ring = ImageChops.subtract(ellipse_mask(center, radius), ellipse_mask(center, radius - 0.09))
    slash = polygon_mask([along(0.2, -0.05, start=(0.08, 0.08)), along(0.2, 0.05, start=(0.08, 0.08)),
                          along(0.76, 0.05, start=(0.08, 0.08)), along(0.76, -0.05, start=(0.08, 0.08))])
    sign = union(ring, ImageChops.multiply(slash, ellipse_mask(center, radius - 0.02)))
    layer.paint(sign, gradient((255, 110, 100), (170, 20, 20)))
    layer.gloss(sign, 50)


# ---- Badge symbols (drawn on the supersampled canvas) ------------------------------------------

def symbol_pick(draw, center, r):
    cx, cy = center
    w = r * 0.18
    draw.line([(cx, cy - r * 0.5), (cx, cy + r * 0.25)], fill=(255, 255, 255, 255), width=int(w))
    draw.polygon([(cx - r * 0.42, cy), (cx + r * 0.42, cy), (cx, cy + r * 0.5)], fill=(255, 255, 255, 255))


def symbol_sit(draw, center, r):
    cx, cy = center
    draw.polygon([(cx - r * 0.5, cy - r * 0.3), (cx + r * 0.5, cy - r * 0.3), (cx, cy + r * 0.45)],
                 fill=(255, 255, 255, 255))


def symbol_lean(draw, center, r):
    cx, cy = center
    w = int(r * 0.16)
    draw.line([(cx + r * 0.4, cy - r * 0.5), (cx + r * 0.4, cy + r * 0.5)], fill=(255, 255, 255, 255), width=w)
    draw.line([(cx - r * 0.5, cy), (cx + r * 0.15, cy)], fill=(255, 255, 255, 255), width=w)
    draw.polygon([(cx + r * 0.3, cy), (cx + r * 0.02, cy - r * 0.3), (cx + r * 0.02, cy + r * 0.3)],
                 fill=(255, 255, 255, 255))


def talk_frame(dots):
    layer = Layer()
    arrow(layer, factor=0.82)
    bubble = union(
        polygon_mask([(0.40, 0.42), (0.95, 0.42), (0.95, 0.80), (0.62, 0.80), (0.50, 0.94), (0.52, 0.80),
                      (0.40, 0.80)]))
    layer.paint(bubble, gradient((252, 252, 255), (196, 208, 226), diagonal=False), rim=RIM)
    draw = ImageDraw.Draw(layer.image)
    for i in range(3):
        cx, cy = px((0.53 + i * 0.145, 0.61))
        r = 0.045 * CANVAS
        color = (52, 84, 130, 255) if i < dots else (150, 166, 190, 255)
        draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=color)
    return layer.finish()


def build():
    cursors = {}

    layer = Layer()
    arrow(layer)
    cursors["Cursor"] = layer.finish()

    layer = Layer()
    arrow(layer, (180, 205, 236), (70, 92, 124), factor=0.9)
    cursors["CursorPush"] = layer.finish()

    layer = Layer()
    sword(layer, (250, 252, 255), (130, 146, 168))
    cursors["CursorAttack"] = layer.finish()

    layer = Layer()
    sword(layer, (255, 200, 190), (190, 50, 40), outline_color=(255, 40, 30, 200))
    cursors["CursorAttack2"] = layer.finish()

    layer = Layer()
    arrow(layer)
    badge(layer, (120, 230, 140), (30, 130, 60), symbol_pick)
    cursors["CursorGet"] = layer.finish()

    layer = Layer()
    hammer(layer)
    cursors["CursorRepair"] = layer.finish()

    layer = Layer()
    arrow(layer)
    badge(layer, (130, 180, 240), (40, 80, 150), symbol_lean)
    cursors["CursorLeanAgainst"] = layer.finish()

    layer = Layer()
    arrow(layer)
    badge(layer, (130, 180, 240), (40, 80, 150), symbol_sit)
    cursors["CursorSitDown"] = layer.finish()

    layer = Layer()
    forbidden(layer)
    cursors["CursorDontMove"] = layer.finish()

    # 2x2 atlas, the order the game plays it in (ping-pong): top-left, top-right, bottom-left,
    # bottom-right.
    atlas = Image.new("RGBA", (SIZE * 2, SIZE * 2), (0, 0, 0, 0))
    for index, dots in enumerate((1, 2, 3, 0)):
        atlas.paste(talk_frame(dots), ((index % 2) * SIZE, (index // 2) * SIZE))
    cursors["CursorTalk"] = atlas
    return cursors


def write_ozt(image: Image.Image, path: pathlib.Path):
    """The game's .ozt: 4 bytes, then an uncompressed 32-bit TGA, bottom row first."""
    width, height = image.size
    header = bytes([0, 0, 2, 0]) + struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, width, height, 32, 8)
    pixels = image.transpose(Image.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
    path.write_bytes(header + pixels)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("data", type=pathlib.Path, help="the client's Data folder")
    parser.add_argument("--preview", type=pathlib.Path, help="also write enlarged PNG previews here")
    args = parser.parse_args()

    out = args.data / "Interface" / "HD"
    out.mkdir(parents=True, exist_ok=True)
    cursors = build()
    for name, image in cursors.items():
        write_ozt(image, out / f"{name}.ozt")
        print(f"{name}: {image.size[0]}x{image.size[1]}")

    if args.preview:
        args.preview.mkdir(parents=True, exist_ok=True)
        sheet = Image.new("RGBA", (len(cursors) * (SIZE * 2 + 16), SIZE * 2), (60, 72, 60, 255))
        x = 0
        for name, image in cursors.items():
            image.save(args.preview / f"{name}.png")
            frame = image.crop((0, 0, SIZE, SIZE)) if image.size[0] > SIZE else image
            sheet.alpha_composite(frame.resize((SIZE * 2, SIZE * 2), Image.LANCZOS), (x, 0))
            x += SIZE * 2 + 16
        sheet.save(args.preview / "sheet.png")


if __name__ == "__main__":
    main()
