"""Draw the top-down race car sprite: game/art/car-body.png and game/art/car-detail.png.

The body is painted in light greys so the game can tint it in each team's racing colour;
wheels, driver, exhaust and shadow-free details sit in a second, untinted layer on top.
The car points right (+x).  Usage: python3 tools/track-art/car.py
"""
import os

from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
OUT = os.path.join(ROOT, "game", "art")
S = 4                      # draw big, shrink for smooth edges
W, H = 64 * S, 28 * S


def body():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = H / 2
    # Cigar-shaped body: pointed tail on the left, long bonnet to a radiator on the right.
    outline = [(3 * S, cy), (10 * S, cy - 5 * S), (26 * S, cy - 7 * S), (44 * S, cy - 6 * S),
               (56 * S, cy - 5 * S), (59 * S, cy - 4 * S), (59 * S, cy + 4 * S), (56 * S, cy + 5 * S),
               (44 * S, cy + 6 * S), (26 * S, cy + 7 * S), (10 * S, cy + 5 * S)]
    d.polygon(outline, fill=(226, 226, 226, 255))
    # Light from the upper left: a highlight along the spine, shade on the lower flank.
    d.polygon([(10 * S, cy - 1 * S), (26 * S, cy - 3 * S), (54 * S, cy - 2.5 * S), (54 * S, cy - 0.5 * S), (26 * S, cy - 0.5 * S)],
              fill=(255, 255, 255, 255))
    d.polygon([(10 * S, cy + 3 * S), (26 * S, cy + 5 * S), (56 * S, cy + 4 * S), (56 * S, cy + 5 * S),
               (26 * S, cy + 7 * S), (10 * S, cy + 5 * S)], fill=(176, 176, 176, 255))
    # Bonnet louvres and the strap across it.
    for x in range(36, 52, 3):
        d.line([(x * S, cy - 3 * S), (x * S, cy - 1.5 * S)], fill=(190, 190, 190, 255), width=S)
        d.line([(x * S, cy + 1.5 * S), (x * S, cy + 3 * S)], fill=(160, 160, 160, 255), width=S)
    d.line([(46 * S, cy - 6 * S), (46 * S, cy + 6 * S)], fill=(150, 150, 150, 255), width=S)
    return img.resize((W // S, H // S), Image.LANCZOS)


def detail():
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    cy = H / 2
    # Wire wheels outside the body, with the axles showing.
    d.line([(52 * S, cy - 10 * S), (52 * S, cy + 10 * S)], fill=(40, 40, 40, 255), width=int(1.2 * S))
    d.line([(16 * S, cy - 10 * S), (16 * S, cy + 10 * S)], fill=(40, 40, 40, 255), width=int(1.2 * S))
    for x in (16, 52):
        for side in (-1, 1):
            y = cy + side * 10.2 * S
            d.rounded_rectangle([(x - 5) * S, y - 2.6 * S, (x + 5) * S, y + 2.6 * S], radius=S, fill=(24, 22, 20, 255))
            d.rectangle([(x - 1.2) * S, y - 1.4 * S, (x + 1.2) * S, y + 1.4 * S], fill=(170, 160, 140, 255))
            for k in (-3.5, -1.5, 2.5, 4):
                d.line([((x + k) * S, y - 2.4 * S), ((x + k) * S, y + 2.4 * S)], fill=(60, 56, 50, 255), width=S // 2)
    # Radiator grille at the nose.
    d.rectangle([58 * S, cy - 3 * S, 60 * S, cy + 3 * S], fill=(60, 58, 54, 255))
    for k in range(-2, 3):
        d.line([(58 * S, cy + k * S), (60 * S, cy + k * S)], fill=(130, 126, 118, 255), width=S // 2)
    # Exhaust pipe down the right-hand side.
    d.line([(34 * S, cy + 7.4 * S), (6 * S, cy + 7.4 * S)], fill=(200, 196, 184, 255), width=int(1.4 * S))
    d.line([(34 * S, cy + 7 * S), (6 * S, cy + 7 * S)], fill=(240, 238, 230, 255), width=S // 2)
    # Cockpit, the driver's leather helmet and white scarf, and a small aero screen.
    d.ellipse([19 * S, cy - 5 * S, 31 * S, cy + 5 * S], fill=(46, 36, 30, 255))
    d.ellipse([21.5 * S, cy - 3.4 * S, 28.5 * S, cy + 3.4 * S], fill=(112, 74, 46, 255))
    d.ellipse([23 * S, cy - 2.2 * S, 26 * S, cy + 0.2 * S], fill=(150, 104, 66, 255))
    d.line([(21 * S, cy + 2.6 * S), (16 * S, cy + 3.4 * S)], fill=(240, 236, 226, 255), width=S)
    d.line([(31 * S, cy - 3 * S), (32.5 * S, cy), (31 * S, cy + 3 * S)], fill=(210, 220, 225, 200), width=S)
    # Racing number roundel on the tail.
    d.ellipse([7 * S, cy - 2.6 * S, 12.2 * S, cy + 2.6 * S], fill=(245, 242, 232, 255), outline=(40, 40, 40, 255), width=S // 2)
    return img.resize((W // S, H // S), Image.LANCZOS)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    body().save(os.path.join(OUT, "car-body.png"))
    detail().save(os.path.join(OUT, "car-detail.png"))
    print("car-body.png, car-detail.png")
