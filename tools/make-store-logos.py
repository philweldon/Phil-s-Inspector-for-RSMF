#!/usr/bin/env python3
"""Draws the Microsoft Store package logos from the same brick as the application icon.
Usage: make-store-logos.py <out folder>     (writes PNG files; needs Pillow)"""
import importlib.util, os, sys
from PIL import Image

here = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("make_icon", os.path.join(here, "make-icon.py"))
make_icon = importlib.util.module_from_spec(spec)
spec.loader.exec_module(make_icon)

def tile(base, size, fill):
    """The brick centred on a transparent square, taking up `fill` of its width."""
    inner = max(1, round(size * fill))
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(base.resize((inner, inner), Image.LANCZOS), ((size - inner) // 2, (size - inner) // 2))
    return out

def main(folder):
    os.makedirs(folder, exist_ok=True)
    base = make_icon.draw()
    def save(name, size, fill):
        tile(base, size, fill).save(os.path.join(folder, name), "PNG", optimize=True)
    for scale in (100, 200, 400):
        save(f"Square44x44Logo.scale-{scale}.png", 44 * scale // 100, 0.86)
        save(f"Square150x150Logo.scale-{scale}.png", 150 * scale // 100, 0.62)
        save(f"StoreLogo.scale-{scale}.png", 50 * scale // 100, 0.90)
    for n in (16, 24, 32, 48, 256):
        save(f"Square44x44Logo.targetsize-{n}.png", n, 1.0)
        save(f"Square44x44Logo.targetsize-{n}_altform-unplated.png", n, 1.0)

if __name__ == "__main__":
    main(sys.argv[1])
