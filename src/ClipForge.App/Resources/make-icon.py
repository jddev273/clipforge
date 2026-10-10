#!/usr/bin/env python3
"""Generates ClipForge's app icon: a flat clipboard motif, drawn programmatically
so every size is crisp (hand-tuned geometry per size, no blurry downscales).

Design: vivid-blue clipboard board (reads on dark taskbars), dark clip,
white paper with text lines. 16/32/48/256px packed as PNG-compressed ICO.
Run: python3 make-icon.py  ->  writes app.ico next to this script.
"""
import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

BOARD = (79, 107, 255, 255)    # vivid blue — visible on dark taskbars
CLIP = (43, 43, 61, 255)       # dark slate clip
PAPER = (255, 255, 255, 255)   # white paper
LINE = (154, 166, 232, 255)    # muted periwinkle text lines


def draw(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    u = size / 256.0

    def rr(box, r, fill):
        x0, y0, x1, y1 = box[0] * u, box[1] * u, box[2] * u, box[3] * u
        # clamp radius so tiny rects don't collapse (PIL requires x0+r+1 <= x1-r-1)
        r = max(0, min(round(r * u), int(min(x1 - x0, y1 - y0) / 2) - 1))
        if r <= 0:
            d.rectangle([x0, y0, x1, y1], fill=fill)
        else:
            d.rounded_rectangle([x0, y0, x1, y1], radius=r, fill=fill)

    def hline(x1, y, x2, w):
        d.line([x1 * u, y * u, x2 * u, y * u], fill=LINE,
               width=max(1, round(w * u)))

    # board
    rr((52, 70, 204, 232), 26, BOARD)
    # clip
    rr((100, 40, 156, 100), 12, CLIP)
    if size >= 48:
        rr((116, 58, 140, 86), 8, BOARD)  # clip finger-hole shows board color
    # paper
    rr((74, 104, 182, 210), 8, PAPER)
    # text lines (fewer + chunkier at tray sizes)
    if size >= 48:
        for y in (134, 160, 186):
            hline(92, y, 164, 9)
    else:
        for y in (140, 172):
            hline(92, y, 164, 14)
    return img


def make_ico(images, path: Path) -> None:
    """Pack (size, Image) list as a PNG-compressed multi-image ICO."""
    png_datas = []
    for _, im in images:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        png_datas.append(buf.getvalue())
    count = len(images)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = b""
    for (size, _), data in zip(images, png_datas):
        w = 0 if size >= 256 else size  # 0 encodes 256 in ICO
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    path.write_bytes(header + entries + b"".join(png_datas))


if __name__ == "__main__":
    out = Path(__file__).with_name("app.ico")
    sizes = [16, 32, 48, 256]
    images = [(s, draw(s)) for s in sizes]
    make_ico(images, out)
    # preview sheet for eyeballing
    sheet = Image.new("RGBA", (256 * len(sizes) + 40, 300), (30, 30, 40, 255))
    dd = ImageDraw.Draw(sheet)
    for i, (s, im) in enumerate(images):
        x = 20 + i * 256
        dd.text((x, 8), f"{s}px", fill=(255, 255, 255, 255))
        sheet.alpha_composite(im.resize((s, s)) if s != im.width else im, (x, 40))
    sheet.save(out.with_name("preview.png"))
    print(f"wrote {out} ({out.stat().st_size} bytes)")
