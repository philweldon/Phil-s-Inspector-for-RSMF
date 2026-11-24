#!/usr/bin/env python3
"""Draws the Store listing logo: the app's brick under a magnifying glass with a tick, on the same
dark background as the listing pictures. Writes store-logo-300.png (the Store's 1:1 app tile icon),
store-logo-150.png and store-logo-71.png (the smaller Store logo sizes) and store-logo-1080.png (a larger master).
Usage: make-logo.py <out folder>     (needs Pillow)"""
import importlib.util, math, os, sys
from PIL import Image, ImageDraw, ImageFilter

S = 1200
here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("make_icon", os.path.join(here, "..", "..", "tools", "make-icon.py"))
make_icon = importlib.util.module_from_spec(spec); spec.loader.exec_module(make_icon)

def layer(): return Image.new("RGBA", (S, S), (0, 0, 0, 0))

def draw():
    top, bottom = (17, 28, 44), (40, 64, 96)
    img = Image.new("RGB", (S, S)); d = ImageDraw.Draw(img)
    for y in range(S):
        t = y / (S - 1)
        d.line([(0, y), (S, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    img = img.convert("RGBA")
    glow = layer(); ImageDraw.Draw(glow).ellipse((170, 250, 950, 1010), fill=(226, 52, 48, 120))
    img.alpha_composite(glow.filter(ImageFilter.GaussianBlur(120)))
    brick = make_icon.draw().resize((840, 840), Image.LANCZOS)
    img.alpha_composite(brick, (110, 110))
    # magnifying glass, lower right
    cx, cy, r, ring = 800, 790, 215, 46
    shadow = layer(); sd = ImageDraw.Draw(shadow)
    sd.ellipse((cx - r, cy - r + 16, cx + r, cy + r + 16), outline=(0, 0, 0, 150), width=ring)
    img.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(18)))
    glass = layer(); g = ImageDraw.Draw(glass)
    k = math.sqrt(0.5)
    hx0, hy0 = cx + (r - 6) * k, cy + (r - 6) * k
    hx1, hy1 = cx + (r + 190) * k, cy + (r + 190) * k
    g.line([(hx0, hy0), (hx1, hy1)], fill=(236, 241, 248, 255), width=78)
    g.ellipse((hx1 - 39, hy1 - 39, hx1 + 39, hy1 + 39), fill=(236, 241, 248, 255))
    g.ellipse((cx - r, cy - r, cx + r, cy + r), fill=(255, 255, 255, 235))
    g.ellipse((cx - r + ring, cy - r + ring, cx + r - ring, cy + r - ring), fill=(232, 244, 236, 255))
    tick = [(cx - 92, cy + 4), (cx - 26, cy + 70), (cx + 100, cy - 66)]
    g.line(tick, fill=(34, 160, 84, 255), width=50, joint="curve")
    for px, py in (tick[0], tick[2]):
        g.ellipse((px - 25, py - 25, px + 25, py + 25), fill=(34, 160, 84, 255))
    img.alpha_composite(glass)
    return img.convert("RGB")

if __name__ == "__main__":
    out = sys.argv[1]; os.makedirs(out, exist_ok=True)
    base = draw()
    base.resize((1080, 1080), Image.LANCZOS).save(os.path.join(out, "store-logo-1080.png"), "PNG", optimize=True)
    for n in (300, 150, 71):
        base.resize((n, n), Image.LANCZOS).save(os.path.join(out, f"store-logo-{n}.png"), "PNG", optimize=True)
