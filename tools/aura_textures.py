"""Generates the textures of the VIP auras ([Modern] VipAuras), which classic MU doesn't have.

They are glow textures for additive blending: white light on black, coloured in game. The
client loads them from Data/Effect/Modern (see GameLogic/Vip/VipAura.cpp):

  aura_sigil  a rotating magic circle: rings, a hexagram and rune marks (Platino, Diamante)
  aura_halo   a thin glowing ring, floated over the head (Diamante)
  aura_beam   a soft vertical light, stacked into a column of light (Diamante)
  aura_orb    a bright orb with a soft corona, for the orbiting lights (Platino, Diamante)

With --kenney, it also converts the textures it uses from Kenney's Particle Pack (CC0, public
domain: https://kenney.nl/assets/particle-pack), the folder "PNG (Black background)" of the
downloaded zip, to aura_<name> (see KENNEY below).

    python tools/aura_textures.py client-src/MuMain/src/bin/Data [--kenney "DIR"] [--preview DIR]
"""

from __future__ import annotations

import argparse
import io
import math
import pathlib

from PIL import Image, ImageChops, ImageDraw, ImageFilter

SIZE = 256
SS = 4


def blank():
    return Image.new("L", (SIZE * SS, SIZE * SS), 0)


def finish(image, glow=6.0, core=1.0):
    """Adds a soft glow around the lines and scales down."""
    small = image.resize((SIZE, SIZE), Image.LANCZOS)
    halo = small.filter(ImageFilter.GaussianBlur(glow))
    result = ImageChops.add(small.point(lambda v: int(v * core)), halo.point(lambda v: min(255, int(v * 1.6))))
    return result


def ring(draw, radius, width, value=255):
    c = SIZE * SS / 2
    r = radius * SIZE * SS
    draw.ellipse((c - r, c - r, c + r, c + r), outline=value, width=int(width * SS))


def sigil():
    img = blank()
    draw = ImageDraw.Draw(img)
    c = SIZE * SS / 2
    ring(draw, 0.47, 3)
    ring(draw, 0.43, 1.5, 200)
    ring(draw, 0.30, 2)
    ring(draw, 0.12, 2, 220)
    # Hexagram inside the middle ring.
    r = 0.30 * SIZE * SS
    for start in (0.0, math.pi / 3):
        points = [(c + r * math.sin(start + k * 2 * math.pi / 3), c - r * math.cos(start + k * 2 * math.pi / 3)) for k in range(3)]
        draw.polygon(points, outline=230, width=int(1.6 * SS))
    # Rune marks between the outer rings: alternating short bars and dots.
    for k in range(36):
        a = k * 2 * math.pi / 36
        r0, r1 = 0.435 * SIZE * SS, 0.465 * SIZE * SS
        if k % 3 == 0:
            m = (r0 + r1) / 2
            x, y = c + m * math.cos(a), c + m * math.sin(a)
            d = 1.6 * SS
            draw.ellipse((x - d, y - d, x + d, y + d), fill=255)
        else:
            draw.line([(c + r0 * math.cos(a), c + r0 * math.sin(a)), (c + r1 * math.cos(a + 0.05), c + r1 * math.sin(a + 0.05))],
                      fill=180, width=int(1.2 * SS))
    # Six small circles on the hexagram's points.
    for k in range(6):
        a = k * math.pi / 3
        x, y = c + r * math.sin(a), c - r * math.cos(a)
        d = 0.03 * SIZE * SS
        draw.ellipse((x - d, y - d, x + d, y + d), outline=255, width=int(1.4 * SS))
    return finish(img, glow=5)


def halo():
    img = blank()
    draw = ImageDraw.Draw(img)
    ring(draw, 0.40, 5)
    ring(draw, 0.34, 1.5, 150)
    return finish(img, glow=8)


def beam():
    """A tall soft ellipse: brightest along a vertical line, fading to the sides and the ends."""
    img = Image.new("L", (SIZE, SIZE), 0)
    pixels = img.load()
    for y in range(SIZE):
        fy = (y - SIZE / 2) / (SIZE / 2)
        for x in range(SIZE):
            fx = (x - SIZE / 2) / (SIZE / 2)
            v = math.exp(-(fx * fx) / 0.03) * max(0.0, 1 - fy * fy) ** 1.5
            pixels[x, y] = int(255 * v)
    return img


def orb():
    img = Image.new("L", (SIZE, SIZE), 0)
    pixels = img.load()
    for y in range(SIZE):
        for x in range(SIZE):
            d = math.hypot(x - SIZE / 2, y - SIZE / 2) / (SIZE / 2)
            v = math.exp(-d * d / 0.01) + 0.45 * math.exp(-d * d / 0.12)
            pixels[x, y] = min(255, int(255 * v))
    return img


# Kenney Particle Pack textures -> aura_<name>, scaled to 256 x 256.
KENNEY = {
    "star_06": "star_soft",     # rising sparkles (Oro)
    "star_07": "star_sharp",    # rising sparkles (Platino)
    "star_08": "star_burst",    # rising sparkles (Diamante)
    "star_09": "orb_flare",     # the core of the orbiting lights
    "magic_02": "runes",        # rune circle on the ground (Oro)
    "twirl_02": "vortex",       # spinning vortex on the ground (Platino)
    "twirl_03": "vortex_wide",  # spinning vortex on the ground (Diamante)
    "trace_07": "pillar",       # the column of light (Diamante)
    "spark_06": "bolt",         # the lightning strike (Diamante)
    "light_01": "flash",        # the ground flash of a strike (Diamante)
}


def kenney(folder: pathlib.Path):
    result = {}
    for source, name in KENNEY.items():
        image = Image.open(folder / f"{source}.png").convert("L").resize((SIZE, SIZE), Image.LANCZOS)
        result[f"aura_{name}"] = image
    return result


def write_ozj(image: Image.Image, path: pathlib.Path):
    buffer = io.BytesIO()
    image.convert("RGB").save(buffer, "JPEG", quality=95)
    # .ozj: 24 bytes the loader skips, then the JPEG.
    path.write_bytes(bytes(24) + buffer.getvalue())


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("data", type=pathlib.Path, help="the client's Data folder")
    parser.add_argument("--kenney", type=pathlib.Path, help='the "PNG (Black background)" folder of the Kenney Particle Pack')
    parser.add_argument("--preview", type=pathlib.Path, help="also write PNG previews here")
    args = parser.parse_args()

    out = args.data / "Effect" / "Modern"
    out.mkdir(parents=True, exist_ok=True)
    textures = {"aura_sigil": sigil(), "aura_halo": halo(), "aura_beam": beam(), "aura_orb": orb()}
    if args.kenney:
        textures.update(kenney(args.kenney))
    for name, image in textures.items():
        write_ozj(image, out / f"{name}.OZJ")
        print(f"{name}: {image.size[0]}x{image.size[1]}")
        if args.preview:
            args.preview.mkdir(parents=True, exist_ok=True)
            image.save(args.preview / f"{name}.png")


if __name__ == "__main__":
    main()
