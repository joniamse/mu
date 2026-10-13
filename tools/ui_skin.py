"""Generates the modern skin of the classic interface ([Modern] ModernSkin).

Each texture keeps the size and layout of the classic one it replaces (the game's code positions
and slices them), so every classic window and dialog that uses them changes look without code
changes: message boxes, the 190-wide panel frames (character, storage, trade, Chaos Machine,
NPC shop...), their buttons and divider lines. Colours follow the modern HUD (Data/UI/theme.rcss).

Textures are drawn at up to 4x the classic resolution; byte 0 of each file holds that factor, and
the game reports the texture in classic pixels (see SkinTexelScale in GlobalBitmap.cpp).

Output: OZT / OZJ files in <data>/Interface/Modern; PNG previews with --preview.

    python tools/ui_skin.py client-src/MuMain/src/bin/Data [--preview DIR]
"""

from __future__ import annotations

import argparse
import io
import pathlib
import struct

from PIL import Image, ImageDraw, ImageFont

SS = 8  # drawing resolution, in classic pixels; textures come out at SCALE (see texel_scale)
SCALE = 1  # the texture's resolution, in classic pixels (set per texture by build())
MAX_TEXTURE = 1024  # the game's largest texture side

SURFACE = (13, 20, 30)          # panel body, like #0d141e
HEADER_TOP = (31, 45, 66)       # #1f2d42
HEADER_BOTTOM = (17, 26, 39)    # #111a27
LINE = (37, 50, 70)             # #253246
ACCENT = (111, 155, 209)        # #6f9bd1
OUTLINE = (4, 6, 10)
TEXT = (196, 210, 228)
GREEN = (79, 216, 166)
RED = (255, 143, 143)

FONT_PATH = pathlib.Path(__file__).resolve().parent.parent / "client-src/MuMain/src/bin/fonts/LiberationSans-Bold.ttf"


def font(size):
    return ImageFont.truetype(str(FONT_PATH), size * SS)


def canvas(width, height, color=(0, 0, 0, 0)):
    return Image.new("RGBA", (width * SS, height * SS), color)


def finish(image, width, height):
    return image.resize((width * SCALE, height * SCALE), Image.LANCZOS)


def next_pow2(value):
    result = 1
    while result < value:
        result *= 2
    return result


def texel_scale(width, height):
    """The finest power of two (up to 4x) whose texture still fits: the game pads textures to powers
    of two and measures them in classic pixels, so the factor must keep that padding exact."""
    scale = 4
    while scale > 1 and max(next_pow2(width), next_pow2(height)) * scale > MAX_TEXTURE:
        scale //= 2
    return scale


def vgradient(draw, box, top, bottom):
    x0, y0, x1, y1 = box
    for y in range(int(y0), int(y1)):
        t = (y - y0) / max(1, (y1 - y0 - 1))
        color = tuple(int(a + (b - a) * t) for a, b in zip(top, bottom)) + (255,)
        draw.line([(x0, y), (x1 - 1, y)], fill=color)


def s(v):
    return v * SS


# ---- Message boxes (230 wide: top 67, middle 15 per extra line, bottom 50) ---------------------

def msgbox_back():
    """The body, stretched under every dialog and classic panel: a flat surface with faint grain."""
    width, height = 225, 512
    img = Image.new("RGB", (width, height), SURFACE)
    noise = Image.effect_noise((width, height), 6).convert("L")
    grain = Image.merge("RGB", [noise.point(lambda v, c=c: max(0, min(255, c + (v - 128) // 16))) for c in SURFACE])
    return Image.blend(img, grain, 0.5).resize((width * SCALE, height * SCALE), Image.BICUBIC)


def side_bands(draw, width, y0, y1, left=4, right=8):
    """The frame's edges: the body texture stops short of them (MSGBOX_BACK_BLANK_*)."""
    draw.rectangle([0, s(y0), s(left) - 1, s(y1) - 1], fill=SURFACE + (255,))
    draw.rectangle([s(width - right), s(y0), s(width) - 1, s(y1) - 1], fill=SURFACE + (255,))
    draw.rectangle([0, s(y0), s(1) - 1, s(y1) - 1], fill=LINE + (255,))
    draw.rectangle([s(width - 1), s(y0), s(width) - 1, s(y1) - 1], fill=LINE + (255,))


def msgbox_top(title_bar=False):
    width, height = 230, 67
    img = canvas(width, height)
    draw = ImageDraw.Draw(img)
    band = 26 if title_bar else 22
    vgradient(draw, (0, 0, s(width), s(band)), HEADER_TOP, HEADER_BOTTOM)
    side_bands(draw, width, band, height)
    draw.rectangle([0, 0, s(width) - 1, s(1) - 1], fill=ACCENT + (255,))
    draw.rectangle([0, 0, s(1) - 1, s(band)], fill=LINE + (255,))
    draw.rectangle([s(width - 1), 0, s(width) - 1, s(band)], fill=LINE + (255,))
    draw.rectangle([s(6), s(band) - SS, s(width - 6), s(band) - 1], fill=ACCENT + (90,))
    return finish(img, width, height)


def msgbox_middle():
    width, height = 230, 15
    img = canvas(width, height)
    side_bands(ImageDraw.Draw(img), width, 0, height)
    return finish(img, width, height)


def msgbox_bottom():
    width, height = 230, 50
    img = canvas(width, height)
    draw = ImageDraw.Draw(img)
    side_bands(draw, width, 0, height - 8)
    draw.rectangle([0, s(height - 9), s(width) - 1, s(height) - 1], fill=SURFACE + (255,))
    draw.rectangle([0, s(height - 1), s(width) - 1, s(height) - 1], fill=LINE + (255,))
    draw.rectangle([0, s(height - 9), s(1) - 1, s(height)], fill=LINE + (255,))
    draw.rectangle([s(width - 1), s(height - 9), s(width) - 1, s(height)], fill=LINE + (255,))
    return finish(img, width, height)


# ---- Panel frames (190 x 429: top 64, sides 21 x 320, bottom 45) -------------------------------

def close_glyph(draw, cx, cy, size=5):
    w = int(1.6 * SS)
    for dx, dy in ((size, size), (size, -size)):
        draw.line([(s(cx) - s(dx), s(cy) - s(dy)), (s(cx) + s(dx), s(cy) + s(dy))], fill=TEXT + (255,), width=w)


def panel_top(name_field=False):
    width, height = 190, 64
    img = canvas(width, height)
    draw = ImageDraw.Draw(img)
    band = 36
    vgradient(draw, (0, 0, s(width), s(band)), HEADER_TOP, HEADER_BOTTOM)
    draw.rectangle([0, 0, s(width) - 1, s(1) - 1], fill=ACCENT + (255,))
    draw.rectangle([0, s(band) - SS, s(width) - 1, s(band) - 1], fill=ACCENT + (70,))
    for x in (0, width - 1):
        draw.rectangle([s(x), 0, s(x + 1) - 1, s(height) - 1], fill=LINE + (255,))
    # The close button of the shared frame: hit box 13 x 12 at (169, 7).
    draw.rounded_rectangle([s(168), s(5), s(184), s(21)], radius=s(3), outline=LINE + (255,), width=SS)
    close_glyph(draw, 176, 13, 3.6)
    if name_field:
        draw.rounded_rectangle([s(48), s(8), s(162), s(24)], radius=s(3), fill=(3, 5, 10, 230),
                               outline=LINE + (255,), width=SS)
    return finish(img, width, height)


def panel_side(left):
    width, height = 21, 320
    img = canvas(width, height)
    draw = ImageDraw.Draw(img)
    x = 0 if left else width - 1
    draw.rectangle([s(x), 0, s(x + 1) - 1, s(height) - 1], fill=LINE + (255,))
    return finish(img, width, height)


def panel_bottom():
    width, height = 190, 45
    img = canvas(width, height)
    draw = ImageDraw.Draw(img)
    for x in (0, width - 1):
        draw.rectangle([s(x), 0, s(x + 1) - 1, s(height) - 1], fill=LINE + (255,))
    draw.rectangle([0, s(height - 1), s(width) - 1, s(height) - 1], fill=LINE + (255,))
    draw.rectangle([s(8), s(4), s(width - 8), s(4) + SS - 1], fill=LINE + (200,))
    return finish(img, width, height)


# ---- Buttons: 3 states stacked (up, hover, down) -----------------------------------------------

STATE_STYLE = [
    ((29, 42, 60), (14, 21, 31), (47, 64, 88)),       # up
    ((38, 54, 77), (18, 27, 40), (156, 194, 238)),    # hover
    ((12, 18, 27), (25, 37, 53), (111, 155, 209)),    # down
]


def button_canvas(width, height, states=3):
    """The states of a button, stacked, at drawing resolution."""
    frame = height // states
    img = canvas(width, height)
    draw = ImageDraw.Draw(img)
    for i in range(states):
        top, bottom, border = STATE_STYLE[min(i, 2)] if states == 3 else STATE_STYLE[0 if i == 0 else 1]
        y0 = i * frame
        box = [s(1), s(y0 + 1), s(width - 1) - 1, s(y0 + frame - 1) - 1]
        mask = Image.new("L", img.size, 0)
        ImageDraw.Draw(mask).rounded_rectangle(box, radius=s(4), fill=255)
        fill = canvas(width, height)
        vgradient(ImageDraw.Draw(fill), (0, s(y0), s(width), s(y0 + frame)), top, bottom)
        img.paste(fill, (0, 0), mask)
        draw.rounded_rectangle(box, radius=s(4), outline=border + (255,), width=SS)
    return img


def button_strip(width, height, states=3):
    """An empty button: the game draws its text over it."""
    return finish(button_canvas(width, height, states), width, height)


def labelled_button(width, height, label, color):
    """A 3-state button with its label centred (drawn with anchor 'mm')."""
    frame = height // 3
    img = button_canvas(width, height)
    draw = ImageDraw.Draw(img)
    f = font(9.5)
    for i in range(3):
        offset = s(1) if i == 2 else 0
        draw.text((s(width) / 2, s(i * frame) + s(frame) / 2 + offset), label, font=f, anchor="mm",
                  fill=color + (255,))
    return finish(img, width, height)


def exit_button():
    width, height = 36, 58
    img = button_canvas(width, height, states=2)
    draw = ImageDraw.Draw(img)
    for i in range(2):
        close_glyph(draw, width / 2, i * 29 + 14.5, 5)
    return finish(img, width, height)


# ---- Lines ---------------------------------------------------------------------------------------

def message_line():
    width, height = 223, 21
    img = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    for x in range(width):
        t = 1 - abs(x - width / 2) / (width / 2)
        img.putpixel((x, height // 2), ACCENT + (int(200 * min(1, t * 2.5)),))
    return img.resize((width * SCALE, height * SCALE), Image.BICUBIC)


def separate_line():
    return Image.new("RGB", (205 * SCALE, 2 * SCALE), (28, 39, 54))


# name -> (classic width, classic height, maker)
TEXTURES = {
    "newui_msgbox_back.jpg": (225, 512, msgbox_back),
    "newui_msgbox_top.tga": (230, 67, msgbox_top),
    "newui_Message_03.tga": (230, 67, lambda: msgbox_top(title_bar=True)),
    "newui_msgbox_middle.tga": (230, 15, msgbox_middle),
    "newui_msgbox_bottom.tga": (230, 50, msgbox_bottom),
    "newui_item_back01.tga": (190, 64, panel_top),
    "newui_item_back04.tga": (190, 64, lambda: panel_top(name_field=True)),
    "newui_item_back02-L.tga": (21, 320, lambda: panel_side(True)),
    "newui_item_back02-R.tga": (21, 320, lambda: panel_side(False)),
    "newui_item_back03.tga": (190, 45, panel_bottom),
    "newui_button_ok.tga": (54, 90, lambda: labelled_button(54, 90, "Aceptar", GREEN)),
    "newui_button_cancel.tga": (54, 90, lambda: labelled_button(54, 90, "Cancelar", RED)),
    "newui_button_close.tga": (54, 90, lambda: labelled_button(54, 90, "Cerrar", TEXT)),
    "newui_btn_empty.tga": (108, 87, lambda: button_strip(108, 87)),
    "newui_btn_empty_small.tga": (64, 87, lambda: button_strip(64, 87)),
    "newui_btn_empty_big.tga": (180, 87, lambda: button_strip(180, 87)),
    "newui_btn_empty_very_small.tga": (54, 69, lambda: button_strip(54, 69)),
    "newui_exit_00.tga": (36, 58, exit_button),
    "newui_Message_Line.tga": (223, 21, message_line),
    "newui_separate_line.jpg": (205, 2, separate_line),
}


def build():
    """name -> (image, scale)."""
    global SCALE
    result = {}
    for name, (width, height, maker) in TEXTURES.items():
        SCALE = texel_scale(width, height)
        image = maker()
        assert image.size == (width * SCALE, height * SCALE), (name, image.size)
        result[name] = (image, SCALE)
    SCALE = 1
    return result


def write(name: str, image: Image.Image, scale: int, folder: pathlib.Path):
    stem, ext = name.rsplit(".", 1)
    if ext == "jpg":
        buffer = io.BytesIO()
        image.convert("RGB").save(buffer, "JPEG", quality=95)
        # .ozj: 24 bytes the loader skips (the first: the texel scale), then the JPEG.
        (folder / f"{stem}.OZJ").write_bytes(bytes([scale]) + bytes(23) + buffer.getvalue())
    else:
        width, height = image.size
        header = bytes([scale, 0, 2, 0]) + struct.pack("<BBBHHBHHHHBB", 0, 0, 2, 0, 0, 0, 0, 0, width, height, 32, 8)
        pixels = image.convert("RGBA").transpose(Image.FLIP_TOP_BOTTOM).tobytes("raw", "BGRA")
        (folder / f"{stem}.OZT").write_bytes(header + pixels)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("data", type=pathlib.Path, help="the client's Data folder")
    parser.add_argument("--preview", type=pathlib.Path, help="also write PNG previews here")
    args = parser.parse_args()

    out = args.data / "Interface" / "Modern"
    out.mkdir(parents=True, exist_ok=True)
    textures = build()
    for name, (image, scale) in textures.items():
        write(name, image, scale, out)
        print(f"{name}: {image.size[0]}x{image.size[1]} ({scale}x)")
    if args.preview:
        args.preview.mkdir(parents=True, exist_ok=True)
        for name, (image, _) in textures.items():
            image.save(args.preview / (name.rsplit(".", 1)[0] + ".png"))


if __name__ == "__main__":
    main()
