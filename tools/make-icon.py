#!/usr/bin/env python3
"""Draws the application icon: a small 3-D, translucent red toy brick (generic; no brand marks).
Usage: make-icon.py <out.ico>"""
import struct, sys, io
from PIL import Image, ImageDraw, ImageFilter

S = 1024
SIZES = [16, 20, 24, 32, 40, 48, 64, 256]

def layer():
    return Image.new("RGBA", (S, S), (0, 0, 0, 0))

def poly(img, pts, fill):
    l = layer(); ImageDraw.Draw(l).polygon(pts, fill=fill); return Image.alpha_composite(img, l)

def ell(img, box, fill):
    l = layer(); ImageDraw.Draw(l).ellipse(box, fill=fill); return Image.alpha_composite(img, l)

def line(img, pts, fill, w):
    l = layer(); ImageDraw.Draw(l).line(pts, fill=fill, width=w, joint="curve"); return Image.alpha_composite(img, l)

def draw():
    img = layer()
    dy = 40
    T, R, B, L = (512, 250 + dy), (922, 455 + dy), (512, 660 + dy), (102, 455 + dy)
    h = 250
    L2, B2, R2 = (L[0], L[1] + h), (B[0], B[1] + h), (R[0], R[1] + h)
    back = (T[0], T[1] + h)
    A = 208                                    # body opacity: see-through, but solid enough at 16 px
    # soft shadow under the brick
    sh = layer(); ImageDraw.Draw(sh).ellipse((150, 800, 874, 990), fill=(60, 0, 0, 70))
    img = Image.alpha_composite(img, sh.filter(ImageFilter.GaussianBlur(26)))
    # faces
    img = poly(img, [L, B, B2, L2], (214, 40, 40, A))          # left face
    img = poly(img, [B, R, R2, B2], (160, 18, 22, A + 12))     # right face (in shade)
    img = poly(img, [T, R, B, L], (240, 82, 78, A - 10))       # top face
    # the far edges, seen through the plastic
    for a, b in ((back, L2), (back, R2), (back, T)):
        img = line(img, [a, b], (255, 190, 185, 70), 10)
    # gloss on the left face and bright rims
    img = poly(img, [(L[0] + 40, L[1] + 50), (L[0] + 150, L[1] + 105), (L[0] + 150, L[1] + 290), (L[0] + 40, L[1] + 235)], (255, 255, 255, 46))
    img = line(img, [L, B, R], (255, 205, 200, 190), 14)
    img = line(img, [L, T, R], (255, 225, 220, 150), 10)
    img = line(img, [B, B2], (255, 170, 165, 120), 10)
    img = line(img, [L, L2, B2, R2, R], (110, 8, 12, 200), 12)
    # studs, back to front
    u, v = ((R[0] - T[0]) / 2, (R[1] - T[1]) / 2), ((L[0] - T[0]) / 2, (L[1] - T[1]) / 2)
    rx, ry, sh_ = 118, 59, 62
    for a, b in ((0.5, 0.5), (1.5, 0.5), (0.5, 1.5), (1.5, 1.5)):
        cx, cy = T[0] + a * u[0] + b * v[0], T[1] + a * u[1] + b * v[1]
        side = layer(); d = ImageDraw.Draw(side)
        d.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(178, 22, 26, 235))
        d.rectangle((cx - rx, cy - sh_, cx + rx, cy), fill=(178, 22, 26, 235))
        d.rectangle((cx - rx, cy - sh_, cx - rx * 0.25, cy + ry * 0.2), fill=(216, 44, 44, 235))   # lit half
        mask = layer(); m = ImageDraw.Draw(mask)
        m.ellipse((cx - rx, cy - ry, cx + rx, cy + ry), fill=(255, 255, 255, 255))
        m.rectangle((cx - rx, cy - sh_, cx + rx, cy), fill=(255, 255, 255, 255))
        side.putalpha(Image.composite(side.getchannel("A"), Image.new("L", (S, S), 0), mask.getchannel("A")))
        img = Image.alpha_composite(img, side)
        img = ell(img, (cx - rx, cy - sh_ - ry, cx + rx, cy - sh_ + ry), (248, 104, 98, 240))
        img = ell(img, (cx - rx * 0.72, cy - sh_ - ry * 0.80, cx + rx * 0.30, cy - sh_ + ry * 0.05), (255, 255, 255, 60))
    return img

def write_ico(path, base):
    entries = []
    for n in SIZES:
        im = base.resize((n, n), Image.LANCZOS)
        if n == 256:
            buf = io.BytesIO(); im.save(buf, "PNG", optimize=True); data = buf.getvalue()
        else:                                   # classic 32-bit DIB + empty AND mask, for the widest compatibility
            px = im.tobytes("raw", "BGRA")
            rows = [px[y * n * 4:(y + 1) * n * 4] for y in range(n - 1, -1, -1)]
            mask_row = bytes(((n + 31) // 32) * 4)
            data = struct.pack("<IiiHHIIiiII", 40, n, n * 2, 1, 32, 0, 0, 0, 0, 0, 0) + b"".join(rows) + mask_row * n
        entries.append((n, data))
    out = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    for n, data in entries:
        out += struct.pack("<BBBBHHII", n % 256, n % 256, 0, 0, 1, 32, len(data), offset); offset += len(data)
    with open(path, "wb") as f:
        f.write(out + b"".join(d for _, d in entries))

if __name__ == "__main__":
    write_ico(sys.argv[1], draw())
