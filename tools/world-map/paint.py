"""Paints the travel map: an old sepia chart with the eastern USA on the left and western
Europe on the right, the Atlantic between them shortened to a gap the liners cross.

    python3 tools/world-map/paint.py          # writes game/art/map.jpg and game/data/map.json

Coastlines, lakes and rivers come from Natural Earth (public domain), clipped to the two
panels and kept in tools/world-map/data/clipped.json. To refresh them, put the Natural Earth
1:50m GeoJSON files ne_50m_land, ne_50m_lakes and ne_50m_rivers_lake_centerlines in
tools/world-map/data/ and run with --clip first.

map.json tells the game where each panel sits in the picture and which longitudes and
latitudes it covers, so it can put pins on the map (TravelMap.cs reads it).
"""
import json
import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
DATA = os.path.join(HERE, "data")

W, H = 2400, 1200
SS = 2  # supersampling for smooth coasts
MARGIN = 40

# Equirectangular panels, with x stretched by cos(mid latitude) so shapes look right.
PANELS = [
    {"region": "Usa", "title": "UNITED STATES", "lonMin": -90.5, "lonMax": -72.0, "latMin": 33.0, "latMax": 47.0,
     "x": MARGIN, "y": MARGIN, "w": 860, "h": H - 2 * MARGIN},
    {"region": "Europe", "title": "EUROPE", "lonMin": -6.5, "lonMax": 17.5, "latMin": 40.0, "latMax": 55.0,
     "x": 1020, "y": MARGIN, "w": W - 1020 - MARGIN, "h": H - 2 * MARGIN},
]

PAPER = (226, 208, 170)
SEA = (190, 192, 172)
LAND = (232, 214, 172)
INK = (92, 66, 40)
WATER_INK = (96, 110, 112)


def fit_panels():
    """Widens each panel's longitude range so its shape matches its box."""
    for p in PANELS:
        mid = math.radians((p["latMin"] + p["latMax"]) / 2)
        lat_span = p["latMax"] - p["latMin"]
        lon_span = lat_span * (p["w"] / p["h"]) / math.cos(mid)
        centre = (p["lonMin"] + p["lonMax"]) / 2
        p["lonMin"], p["lonMax"] = round(centre - lon_span / 2, 3), round(centre + lon_span / 2, 3)


def project(p, lon, lat):
    x = p["x"] + (lon - p["lonMin"]) / (p["lonMax"] - p["lonMin"]) * p["w"]
    y = p["y"] + (p["latMax"] - lat) / (p["latMax"] - p["latMin"]) * p["h"]
    return x * SS, y * SS


def rings(geometry):
    if geometry["type"] == "Polygon":
        return geometry["coordinates"]
    if geometry["type"] == "MultiPolygon":
        return [r for poly in geometry["coordinates"] for r in poly]
    if geometry["type"] == "LineString":
        return [geometry["coordinates"]]
    if geometry["type"] == "MultiLineString":
        return geometry["coordinates"]
    return []


def clip():
    """Keeps only the shapes that reach into a panel, so the repo holds a small file."""
    fit_panels()
    out = {}
    for name in ["ne_50m_land", "ne_50m_lakes", "ne_50m_rivers_lake_centerlines"]:
        with open(os.path.join(DATA, name + ".geojson")) as f:
            features = json.load(f)["features"]
        kept = []
        for feature in features:
            for ring in rings(feature["geometry"]):
                lons = [c[0] for c in ring]
                lats = [c[1] for c in ring]
                for p in PANELS:
                    if max(lons) >= p["lonMin"] - 3 and min(lons) <= p["lonMax"] + 3 and max(lats) >= p["latMin"] - 3 and min(lats) <= p["latMax"] + 3:
                        kept.append([[round(c[0], 3), round(c[1], 3)] for c in ring])
                        break
        out[name.replace("ne_50m_", "")] = kept
    with open(os.path.join(DATA, "clipped.json"), "w") as f:
        json.dump(out, f, separators=(",", ":"))
    print("clipped:", {k: len(v) for k, v in out.items()})


def font(size):
    for path in ["/usr/share/fonts/truetype/dejavu/DejaVuSerif-Bold.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSerif.ttf"]:
        if os.path.exists(path):
            return ImageFont.truetype(path, size * SS)
    return ImageFont.load_default()


def paper(rng):
    """Old paper: a warm base with blotches and fine grain."""
    base = np.ones((H * SS, W * SS, 3)) * np.array(PAPER, dtype=float)
    blotch = rng.random((H // 12 + 2, W // 12 + 2))
    blotch = Image.fromarray((blotch * 255).astype(np.uint8)).resize((W * SS, H * SS), Image.BICUBIC)
    blotch = np.array(blotch.filter(ImageFilter.GaussianBlur(10 * SS))) / 255.0
    grain = rng.random((H * SS, W * SS))
    base *= (0.93 + 0.1 * blotch)[..., None]
    base *= (0.97 + 0.04 * grain)[..., None]
    return base


def paint():
    fit_panels()
    with open(os.path.join(DATA, "clipped.json")) as f:
        shapes = json.load(f)
    rng = np.random.default_rng(1930)

    img = Image.fromarray(np.clip(paper(rng), 0, 255).astype(np.uint8))
    draw = ImageDraw.Draw(img)

    for p in PANELS:
        box = [p["x"] * SS, p["y"] * SS, (p["x"] + p["w"]) * SS, (p["y"] + p["h"]) * SS]
        layer = Image.new("RGB", (W * SS, H * SS), SEA)
        land_mask = Image.new("L", (W * SS, H * SS), 0)
        mask_draw = ImageDraw.Draw(land_mask)
        for ring in shapes["land"]:
            mask_draw.polygon([project(p, *c) for c in ring], fill=255)
        for ring in shapes["lakes"]:
            mask_draw.polygon([project(p, *c) for c in ring], fill=0)

        # Water lines along the coast, as engraved charts have.
        sea = Image.new("RGB", (W * SS, H * SS), SEA)
        for i, (radius, shade) in enumerate([(10, 0.93), (22, 0.96), (38, 0.985)]):
            # Grow the land by about this radius: blur, then keep everything the blur reached.
            grown = land_mask.filter(ImageFilter.BoxBlur(radius * SS)).point(lambda v: 255 if v > 8 else 0)
            ring_img = Image.new("RGB", sea.size, tuple(int(c * shade) for c in SEA))
            sea.paste(ring_img, mask=grown)
        sea.paste(Image.new("RGB", sea.size, SEA), mask=land_mask)
        layer = sea
        land = Image.new("RGB", layer.size, LAND)
        layer.paste(land, mask=land_mask)

        # Light shading inland from the coast.
        inner = land_mask.filter(ImageFilter.GaussianBlur(14 * SS))
        shade = np.array(layer, dtype=float)
        coast = (np.array(land_mask, dtype=float) - np.array(inner, dtype=float)).clip(0, 255) / 255
        shade -= coast[..., None] * 30
        layer = Image.fromarray(np.clip(shade, 0, 255).astype(np.uint8))

        # Coast outline, rivers and lake shores on top.
        draw = ImageDraw.Draw(layer)
        for ring in shapes["land"]:
            pts = [project(p, *c) for c in ring]
            draw.line(pts + [pts[0]], fill=INK, width=2 * SS, joint="curve")
        for ring in shapes["lakes"]:
            pts = [project(p, *c) for c in ring]
            draw.line(pts + [pts[0]], fill=WATER_INK, width=SS, joint="curve")
        for line in shapes["rivers_lake_centerlines"]:
            draw.line([project(p, *c) for c in line], fill=WATER_INK, width=SS)

        # Graticule every 5 degrees, faint.
        for lon in range(-180, 180, 5):
            if p["lonMin"] < lon < p["lonMax"]:
                x, _ = project(p, lon, 0)
                draw.line([(x, box[1]), (x, box[3])], fill=(150, 128, 96), width=1)
        for lat in range(0, 90, 5):
            if p["latMin"] < lat < p["latMax"]:
                _, y = project(p, 0, lat)
                draw.line([(box[0], y), (box[2], y)], fill=(150, 128, 96), width=1)

        # Only the panel's own box goes onto the sheet.
        panel_mask = Image.new("L", layer.size, 0)
        ImageDraw.Draw(panel_mask).rectangle(box, fill=255)
        img.paste(layer, mask=panel_mask)

    draw = ImageDraw.Draw(img)

    for p in PANELS:
        box = [p["x"] * SS, p["y"] * SS, (p["x"] + p["w"]) * SS, (p["y"] + p["h"]) * SS]
        draw.rectangle(box, outline=INK, width=3 * SS)
        draw.rectangle([box[0] + 8 * SS, box[1] + 8 * SS, box[2] - 8 * SS, box[3] - 8 * SS], outline=INK, width=SS)
        title = font(30)
        tw = draw.textlength(p["title"], font=title)
        tx = box[0] + 30 * SS
        ty = box[3] - 70 * SS
        draw.rectangle([tx - 14 * SS, ty - 8 * SS, tx + tw + 14 * SS, ty + 44 * SS], fill=PAPER, outline=INK, width=2 * SS)
        draw.text((tx, ty), p["title"], font=title, fill=INK)

    # The Atlantic gap between the panels.
    gap_x = (PANELS[0]["x"] + PANELS[0]["w"] + PANELS[1]["x"]) / 2 * SS
    small = font(16)
    for i, text in enumerate(["ATLANTIC", "OCEAN", "", "7 days", "by liner"]):
        tw = draw.textlength(text, font=small)
        draw.text((gap_x - tw / 2, (H / 2 - 70 + i * 26) * SS), text, font=small, fill=INK)
    compass(draw, gap_x, (H - 200) * SS, 34 * SS)

    img = img.resize((W, H), Image.LANCZOS)
    os.makedirs(os.path.join(ROOT, "game", "art"), exist_ok=True)
    img.save(os.path.join(ROOT, "game", "art", "map.jpg"), quality=90)
    with open(os.path.join(ROOT, "game", "data", "map.json"), "w") as f:
        json.dump({
            "comment": "Generated by tools/world-map/paint.py. Panels are pixel boxes in map.jpg and the longitudes and latitudes they show.",
            "width": W, "height": H,
            "panels": [{k: v for k, v in p.items() if k != "title"} for p in PANELS],
        }, f, indent=2)
    print("wrote game/art/map.jpg and game/data/map.json")


def compass(draw, cx, cy, r):
    for angle, length in [(0, 1), (90, 0.6), (180, 1), (270, 0.6)]:
        a = math.radians(angle)
        tip = (cx + math.sin(a) * r * length, cy - math.cos(a) * r * length)
        left = (cx + math.sin(a - 1.4) * r * 0.18, cy - math.cos(a - 1.4) * r * 0.18)
        right = (cx + math.sin(a + 1.4) * r * 0.18, cy - math.cos(a + 1.4) * r * 0.18)
        draw.polygon([tip, left, (cx, cy), right], fill=INK)
    draw.ellipse([cx - r * 0.12, cy - r * 0.12, cx + r * 0.12, cy + r * 0.12], fill=PAPER, outline=INK)
    f = font(14)
    draw.text((cx - draw.textlength("N", font=f) / 2, cy - r - 24 * SS), "N", font=f, fill=INK)


if __name__ == "__main__":
    if "--clip" in sys.argv:
        clip()
    paint()
