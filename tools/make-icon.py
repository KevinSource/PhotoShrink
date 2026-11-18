"""Draws the PhotoShrink icon at each size (supersampled) and writes a multi-size .ico.

Usage: python tools/make-icon.py PhotoShrink/PhotoShrink.ico   (needs Pillow: pip install pillow)
Small sizes (<= 24 px) are drawn with shorter, bolder arrows so they stay readable in menus.
"""
from PIL import Image, ImageDraw
import sys, math

SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SS = 8  # supersampling factor

BLUE_TOP = (88, 166, 237)
BLUE_BOT = (178, 219, 250)
HILL_BACK = (76, 160, 92)
HILL_FRONT = (46, 125, 64)
SUN = (255, 200, 40)
FRAME = (255, 255, 255)
EDGE = (40, 70, 110)
ARROW = (235, 105, 30)

def draw(size):
    S = size * SS
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    small = size <= 24
    # Photo rectangle: centred, inset so the corner arrows fit.
    inset = S * (0.19 if not small else 0.20)
    x0, y0, x1, y1 = inset, inset + S * 0.05, S - inset, S - inset - S * 0.05
    border = max(S * 0.04, SS * (1.0 if not small else 0.6))
    edge = max(S * 0.025, SS * 1.0)
    r = S * 0.04
    # Outer edge + white frame.
    d.rounded_rectangle([x0, y0, x1, y1], r, fill=EDGE)
    d.rounded_rectangle([x0 + edge, y0 + edge, x1 - edge, y1 - edge], r * 0.7, fill=FRAME)
    # Picture area.
    px0, py0, px1, py1 = x0 + edge + border, y0 + edge + border, x1 - edge - border, y1 - edge - border
    pic = Image.new("RGBA", (int(px1 - px0), int(py1 - py0)))
    pw, ph = pic.size
    pd = ImageDraw.Draw(pic)
    for yy in range(ph):  # sky gradient
        t = yy / max(1, ph - 1)
        c = tuple(int(BLUE_TOP[i] + (BLUE_BOT[i] - BLUE_TOP[i]) * t) for i in range(3))
        pd.line([(0, yy), (pw, yy)], fill=c)
    # Sun.
    sr = ph * 0.16
    pd.ellipse([pw * 0.70 - sr, ph * 0.30 - sr, pw * 0.70 + sr, ph * 0.30 + sr], fill=SUN)
    # Hills.
    pd.ellipse([-pw * 0.35, ph * 0.55, pw * 0.75, ph * 1.65], fill=HILL_BACK)
    pd.ellipse([pw * 0.25, ph * 0.62, pw * 1.40, ph * 1.75], fill=HILL_FRONT)
    img.paste(pic, (int(px0), int(py0)), pic)

    # Corner arrows pointing inward at the photo's corners.
    w = max(S * 0.07, SS * 1.8)          # shaft width
    head = max(S * 0.13, SS * (3.6 if not small else 4.2))        # head size
    corners = [((0, 0), (x0, y0)), ((S, 0), (x1, y0)), ((0, S), (x0, y1)), ((S, S), (x1, y1))]
    for (ox, oy), (tx, ty) in corners:
        # Arrow goes from near the outer corner toward the photo corner, stopping just short.
        sx = ox + (tx - ox) * (0.25 if not small else 0.55)
        sy = oy + (ty - oy) * (0.25 if not small else 0.55)
        ex = tx - (tx - ox) * 0.10
        ey = ty - (ty - oy) * 0.10
        ang = math.atan2(ey - sy, ex - sx)
        bx, by = ex - math.cos(ang) * head * 0.85, ey - math.sin(ang) * head * 0.85
        d.line([(sx, sy), (bx, by)], fill=ARROW, width=int(w))
        left = (ex - math.cos(ang - 0.6) * head, ey - math.sin(ang - 0.6) * head)
        right = (ex - math.cos(ang + 0.6) * head, ey - math.sin(ang + 0.6) * head)
        d.polygon([(ex, ey), left, right], fill=ARROW)
    return img.resize((size, size), Image.LANCZOS)

frames = {s: draw(s) for s in SIZES}
out = sys.argv[1] if len(sys.argv) > 1 else 'PhotoShrink.ico'
frames[256].save(out, format="ICO", sizes=[(s, s) for s in SIZES],
                 append_images=[frames[s] for s in SIZES if s != 256])
