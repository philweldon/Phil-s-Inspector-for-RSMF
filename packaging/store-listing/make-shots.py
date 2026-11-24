#!/usr/bin/env python3
"""Builds the Microsoft Store listing pictures (1920 x 1080 PNG) from real pictures saved by the app's
self-check (packaging/store-listing/src). Each picture is cropped to leave out the lines that show
folder paths, set unscaled on a plain background, and given a headline. Nothing inside a crop is edited.
Usage: make-shots.py <src folder> <out folder>     (needs Pillow and the Liberation Sans fonts)"""
import importlib.util, os, sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

W, H = 1920, 1080
here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("make_icon", os.path.join(here, "..", "..", "tools", "make-icon.py"))
make_icon = importlib.util.module_from_spec(spec); spec.loader.exec_module(make_icon)
FONT = "/usr/share/fonts/truetype/liberation/LiberationSans-%s.ttf"
def font(style, size): return ImageFont.truetype(FONT % style, size)

def background():
    top, bottom = (17, 28, 44), (36, 58, 86)
    img = Image.new("RGB", (W, H))
    d = ImageDraw.Draw(img)
    for y in range(H):
        t = y / (H - 1)
        d.line([(0, y), (W, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    return img.convert("RGBA")

def card(canvas, shot, x, y, pad=22, radius=18):
    w, h = shot.width + 2 * pad, shot.height + 2 * pad
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle([x, y + 14, x + w, y + h + 14], radius, fill=(0, 0, 0, 150))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(24)))
    ImageDraw.Draw(canvas).rounded_rectangle([x, y, x + w, y + h], radius, fill=(255, 255, 255, 255))
    canvas.paste(shot.convert("RGB"), (x + pad, y + pad))
    return h

def build(out, headline, subline, crops, gap=22, pad=22):
    canvas = background()
    d = ImageDraw.Draw(canvas)
    brick = make_icon.draw().resize((64, 64), Image.LANCZOS)
    canvas.alpha_composite(brick, (70, 46))
    d.text((146, 62), "Phil's Inspector for RSMF files", font=font("Bold", 30), fill=(214, 224, 238))
    hf, sf = font("Bold", 64), font("Regular", 32)
    d.text((W / 2, 170), headline, font=hf, fill=(255, 255, 255), anchor="mm")
    d.text((W / 2, 240), subline, font=sf, fill=(190, 204, 222), anchor="mm")
    total = sum(c.height + 2 * pad for c in crops) + gap * (len(crops) - 1)
    y = 300 + max(0, (H - 300 - 50 - total) // 2)
    for c in crops:
        y += card(canvas, c, (W - c.width - 2 * pad) // 2, y, pad) + gap
    canvas.convert("RGB").save(out, "PNG", optimize=True)

def main(src, out):
    os.makedirs(out, exist_ok=True)
    def crop(name, top, bottom):
        im = Image.open(os.path.join(src, name)); return im.crop((0, top, im.width, bottom))
    build(os.path.join(out, "screenshot-1-findings-by-rule.png"),
          "Findings explained in plain language", "Every finding says what it means and what to do about it.",
          [crop("samples.png", 288, 392), crop("samples.png", 438, 798)])
    build(os.path.join(out, "screenshot-2-chat-viewer.png"),
          "Read each conversation as a chat", "Search, replies, reactions and attachments, in the order they were sent.",
          [crop("viewer.png", 78, 761)])
    build(os.path.join(out, "screenshot-3-folder-scan.png"),
          "Check a whole folder in one pass", "See at a glance which files are clean and which need attention.",
          [crop("main.png", 288, 392), crop("main.png", 438, 798)])
    build(os.path.join(out, "screenshot-4-finding-detail.png"),
          "See exactly where each problem is", "Observed value, location and record for every finding in a file.",
          [crop("findings.png", 78, 761)])

if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
