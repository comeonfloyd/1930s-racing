"""Paint the ground under each circuit: game/art/tracks/<circuit>.jpg.

The race view draws this picture under the cars instead of plain shapes. Everything is
generated from the track file (game/data/tracks/<circuit>.json) and a few lines of scenery
per circuit below, so the road in the picture is exactly the road the cars follow.

It also writes game/data/scenery/<circuit>.json: every tree, house, boat and grandstand in the
picture, so the 3D race view can stand them up where they were painted.

Needs only numpy and Pillow.  Usage, from the repo root:
    python3 tools/track-art/paint.py            # all circuits
    python3 tools/track-art/paint.py monza      # one
"""
import json, math, os, sys

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
TRACKS = os.path.join(ROOT, "game", "data", "tracks")
OUT = os.path.join(ROOT, "game", "art", "tracks")
SCENERY_OUT = os.path.join(ROOT, "game", "data", "scenery")

H = 2000
W = int(H * 1.4)
ROAD = 0.016      # road width as a share of the picture height; TrackView uses the same
VERGE = 0.026

# Scenery per circuit: how much forest, the field colours, towns, the road surface.
SCENES = {
    "monza": dict(forest=0.55, fields="park", surface="tarmac",
                  note="the royal park: woods, lawns and avenues"),
    "monaco": dict(forest=0.0, fields="garden", surface="tarmac", town=True,
                   # The harbour and the open sea beyond it, in drawing units (x across, y down).
                   sea=[(0.405, 0.52), (0.5, 0.505), (0.6, 0.49), (0.68, 0.42), (0.76, 0.3), (0.84, 0.17),
                        (0.93, 0.1), (1.0, 0.07), (1.0, 1.0), (0.405, 1.0)],
                   boats=[(0.44, 0.6, 0.53, 0.88), (0.6, 0.7, 0.9, 0.95)]),
    "spa": dict(forest=0.5, fields="farm", surface="tarmac",
                villages=["La Source", "Malmedy", "Stavelot", "Burnenville"]),
    "nurburgring": dict(forest=0.62, fields="farm", surface="tarmac", villages=["Döttinger Höhe", "Adenauer Forst"]),
    "montlhery": dict(forest=0.4, fields="farm", surface="tarmac", villages=["Bruyères"]),
    "indianapolis": dict(forest=0.08, fields="prairie", surface="brick"),
    "reims": dict(forest=0.04, fields="farm", surface="tarmac", villages=["Gueux", "Thillois"],
                  note="open Champagne wheat fields"),
    "pescara": dict(forest=0.12, fields="farm", surface="tarmac", villages=["Spoltore", "Cappelle", "Montesilvano"],
                    # The Adriatic, beyond the coast road.
                    sea=[(0.5, 0.0), (0.58, 0.08), (0.65, 0.2), (0.71, 0.32), (0.77, 0.44), (0.84, 0.55), (0.9, 0.64),
                         (1.0, 0.72), (1.0, 0.0)]),
    "avus": dict(forest=0.6, fields="park", surface="tarmac", note="the Grunewald on both sides of the motorway"),
    "bremgarten": dict(forest=0.72, fields="farm", surface="tarmac", note="deep in the Bremgartenwald"),
    "tripoli": dict(forest=0.02, fields="desert", surface="tarmac",
                    # The Mediterranean to the north and the Mellaha salt lake inside the lap.
                    sea=[(0.0, 0.0), (1.0, 0.0), (1.0, 0.33), (0.92, 0.42), (0.74, 0.33), (0.57, 0.2), (0.385, 0.13),
                         (0.19, 0.16), (0.0, 0.2)],
                    lake=0.8),
    "brooklands": dict(forest=0.14, fields="park", surface="concrete", note="the airfield inside the banked oval"),
    "lemans": dict(forest=0.45, fields="farm", surface="tarmac", villages=["Mulsanne", "Arnage"],
                   note="Sarthe pine woods and farmland on public roads"),
}

FIELD_COLOURS = {
    "farm": [(118, 140, 72), (140, 152, 80), (168, 160, 92), (104, 128, 66), (186, 170, 104), (128, 146, 84), (150, 128, 82)],
    "park": [(104, 134, 70), (116, 144, 76), (96, 126, 64), (126, 150, 82)],
    "garden": [(122, 136, 84), (136, 138, 92), (110, 124, 74)],
    "prairie": [(132, 150, 82), (144, 156, 88), (124, 144, 78), (156, 160, 96)],
    "desert": [(196, 176, 130), (206, 186, 140), (186, 166, 120), (214, 196, 150), (178, 160, 114)],
}


# ---------------------------------------------------------------- the track, as the game sees it

def smooth(points, steps=16):
    """Closed Catmull-Rom spline, the same formula as TrackShape.Smooth in the game."""
    n, out = len(points), []
    for i in range(n):
        p0, p1, p2, p3 = (np.array(points[(i + k) % n]) for k in (-1, 0, 1, 2))
        for s in range(steps):
            t = s / steps
            out.append(0.5 * (2 * p1 + (p2 - p0) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (3 * p1 - p0 - 3 * p2 + p3) * t ** 3))
    return np.array(out)


def px(p):
    return (float(p[0]) * W, float(p[1]) * H)


# Things painted into the picture, for the 3D view: filled in by paint() for one circuit at a time.
SCENERY = {}


def keep(kind, *values):
    SCENERY.setdefault(kind, []).append([round(float(v), 4) for v in values])


# ---------------------------------------------------------------- noise and masks

def noise(rng, cells, smooth_px=0):
    """Value noise: a random grid of about `cells` across, scaled up smoothly to the picture."""
    gw, gh = max(2, int(cells)), max(2, int(cells / 1.4))
    grid = Image.fromarray((rng.random((gh, gw)) * 255).astype(np.uint8))
    img = grid.resize((W, H), Image.BICUBIC)
    if smooth_px:
        img = img.filter(ImageFilter.GaussianBlur(smooth_px))
    return np.asarray(img, dtype=np.float32) / 255


def fbm(rng, base, octaves=4):
    total, amp, norm = 0, 1.0, 0
    for o in range(octaves):
        total = total + noise(rng, base * 2 ** o) * amp
        norm += amp
        amp *= 0.5
    return total / norm


def line_mask(lines, width, closed=True):
    img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(img)
    for pts in lines:
        q = [px(p) for p in pts] + ([px(pts[0])] if closed else [])
        d.line(q, fill=255, width=int(width), joint="curve")
        for p in q:
            r = width / 2
            d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=255)
    return img


def near(mask_img, radius):
    """1 near the mask, fading to 0 about `radius` pixels away."""
    m = mask_img.filter(ImageFilter.GaussianBlur(radius))
    a = np.asarray(m, dtype=np.float32) / 255
    return np.clip(a * 3, 0, 1)


def rect_mask(rects):
    img = Image.new("L", (W, H), 0)
    d = ImageDraw.Draw(img)
    for x, y, w, h in rects:
        d.rectangle([x * W, y * H, (x + w) * W, (y + h) * H], fill=255)
    return img


# ---------------------------------------------------------------- painting

def fields(rng, palette):
    """A patchwork of fields with a slight furrow texture."""
    gw, gh = 700, 500
    n = 70
    seeds = rng.random((n, 2)) * [gw, gh]
    yy, xx = np.mgrid[0:gh, 0:gw].astype(np.float32)
    # Warp the grid a little so field edges wander like old hedgerows.
    wx = np.asarray(Image.fromarray((rng.random((12, 16)) * 255).astype(np.uint8)).resize((gw, gh), Image.BICUBIC), np.float32) / 255
    wy = np.asarray(Image.fromarray((rng.random((12, 16)) * 255).astype(np.uint8)).resize((gw, gh), Image.BICUBIC), np.float32) / 255
    xx += (wx - 0.5) * 40
    yy += (wy - 0.5) * 40
    best = np.full((gh, gw), np.inf, np.float32)
    owner = np.zeros((gh, gw), np.uint8)
    for k, (sx, sy) in enumerate(seeds):
        d = (xx - sx) ** 2 + (yy - sy) ** 2
        closer = d < best
        best[closer] = d[closer]
        owner[closer] = k
    owner = np.asarray(Image.fromarray(owner).resize((W, H), Image.NEAREST).filter(ImageFilter.ModeFilter(9)))
    colours = np.array([palette[rng.integers(len(palette))] for _ in range(n)], dtype=np.float32)
    img = colours[owner]
    angle = rng.random(n) * math.pi
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    a = angle[owner]
    furrow = np.sin((xx * np.cos(a) + yy * np.sin(a)) * 0.35)
    img *= (1 + 0.035 * furrow)[..., None]
    # Hedges along field edges.
    edge = np.zeros((H, W), bool)
    edge[1:, :] |= owner[1:, :] != owner[:-1, :]
    edge[:, 1:] |= owner[:, 1:] != owner[:, :-1]
    hedge = Image.fromarray((edge * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(5)).filter(ImageFilter.GaussianBlur(1.5))
    h = np.asarray(hedge, dtype=np.float32)[..., None] / 255
    img = img * (1 - h * 0.55) + np.array([60, 82, 44], np.float32) * h * 0.55
    return img


def tint(img, rng, amount, base):
    v = fbm(rng, base, 3)[..., None]
    return img * (1 - amount / 2 + amount * v)


def crowns(draw, rng, mask, count, size, dark=(42, 64, 34), light=(88, 118, 60)):
    """Tree crowns, each with a shadow to the lower right and a lit upper left."""
    ys, xs = np.nonzero(mask)
    if len(xs) == 0:
        return
    pick = rng.integers(len(xs), size=count)
    order = np.argsort(ys[pick])
    for i in pick[order]:
        x, y = xs[i], ys[i]
        r = size * (0.6 + rng.random() * 0.8)
        draw.ellipse([x - r + r * 0.5, y - r + r * 0.6, x + r + r * 0.5, y + r + r * 0.6], fill=(26, 36, 22, 90))
        g = rng.random()
        base = tuple(int(dark[k] + (light[k] - dark[k]) * g * 0.6) for k in range(3))
        draw.ellipse([x - r, y - r, x + r, y + r], fill=base + (255,))
        keep("trees", x / W, y / H, r / H, g)
        hi = tuple(min(255, int(c * 1.25)) for c in base)
        draw.ellipse([x - r * 0.75, y - r * 0.8, x + r * 0.2, y + r * 0.1], fill=hi + (150,))


def houses(draw, rng, centre, count, spread, avoid):
    cx, cy = centre
    for _ in range(count):
        x = cx + rng.normal() * spread
        y = cy + rng.normal() * spread * 0.8
        if not (0 <= x < W and 0 <= y < H) or avoid[int(y), int(x)]:
            continue
        w, h = 14 + rng.random() * 22, 10 + rng.random() * 14
        if rng.random() < 0.5:
            w, h = h, w
        roof = [(168, 84, 58), (150, 72, 52), (122, 108, 98), (180, 104, 70)][rng.integers(4)]
        draw.rectangle([x + 4, y + 4, x + w + 4, y + h + 4], fill=(30, 30, 26, 110))
        draw.rectangle([x, y, x + w, y + h], fill=roof + (255,))
        keep("houses", x / W, y / H, w / W, h / H, *roof)
        if w > h:
            draw.line([x, y + h / 2, x + w, y + h / 2], fill=tuple(int(c * 0.75) for c in roof) + (255,), width=2)
        else:
            draw.line([x + w / 2, y, x + w / 2, y + h], fill=tuple(int(c * 0.75) for c in roof) + (255,), width=2)


def town(draw, rng, avoid):
    """Blocks of houses climbing the hill behind the harbour, with streets and gardens between."""
    block = 120
    for by in range(0, H, block):
        for bx in range(0, W, block):
            if rng.random() < 0.15:
                continue  # a garden or a square
            step = 24 + rng.integers(0, 10)
            palette = [(204, 132, 92), (190, 112, 80), (222, 186, 140), (214, 160, 112), (232, 214, 182)]
            for gy in range(by + 10, by + block - 10, step):
                for gx in range(bx + 10, bx + block - 10, step):
                    x, y = gx + rng.random() * 3, gy + rng.random() * 3
                    if avoid[min(H - 1, int(y + 8)), min(W - 1, int(x + 8))] or rng.random() < 0.08:
                        continue
                    w, h = step - 3, step - 3
                    roof = palette[rng.integers(len(palette))]
                    draw.rectangle([x + 4, y + 5, x + w + 4, y + h + 5], fill=(40, 34, 28, 110))
                    draw.rectangle([x, y, x + w, y + h], fill=roof + (255,))
                    keep("houses", x / W, y / H, w / W, h / H, *roof)
                    draw.line([x, y + h / 2, x + w, y + h / 2], fill=tuple(int(c * 0.8) for c in roof) + (255,), width=2)


def boats(draw, rng, areas, water):
    """Yachts and launches moored in the harbour and out at sea."""
    for x0, y0, x1, y1 in areas:
        for _ in range(40):
            x, y = (x0 + rng.random() * (x1 - x0)) * W, (y0 + rng.random() * (y1 - y0)) * H
            if not water[min(H - 1, int(y)), min(W - 1, int(x))]:
                continue
            L, B = 18 + rng.random() * 26, 6 + rng.random() * 5
            hull = [(x, y - B / 2), (x + L * 0.75, y - B / 2), (x + L, y), (x + L * 0.75, y + B / 2), (x, y + B / 2)]
            draw.polygon([(a + 3, b + 3) for a, b in hull], fill=(20, 30, 40, 90))
            draw.polygon(hull, fill=(238, 234, 222, 255), outline=(120, 110, 96, 255))
            keep("boats", x / W, y / H, L / W, B / H)
            draw.rectangle([x + L * 0.25, y - B / 4, x + L * 0.55, y + B / 4], fill=(170, 130, 90, 255))


def stands(draw, rects):
    for x, y, w, h in rects:
        x0, y0, x1, y1 = x * W, y * H, (x + w) * W, (y + h) * H
        draw.rectangle([x0 + 8, y0 + 8, x1 + 8, y1 + 8], fill=(30, 28, 24, 140))
        draw.rectangle([x0, y0, x1, y1], fill=(118, 86, 58, 255))
        # Roof planks across the short side.
        horizontal = (x1 - x0) > (y1 - y0)
        n = int((x1 - x0 if horizontal else y1 - y0) / 9)
        for k in range(n):
            t = k / max(1, n)
            if horizontal:
                xx = x0 + (x1 - x0) * t
                draw.line([xx, y0, xx, y1], fill=(96, 68, 46, 255), width=2)
            else:
                yy = y0 + (y1 - y0) * t
                draw.line([x0, yy, x1, yy], fill=(96, 68, 46, 255), width=2)
        draw.rectangle([x0, y0, x1, y1], outline=(70, 50, 34, 255), width=3)


def bales(draw, road, rng):
    """Straw bales on the outside of the tighter corners."""
    n = len(road)
    for i in range(0, n, 3):
        a, b, c = road[i - 6], road[i], road[(i + 6) % n]
        v1, v2 = b - a, c - b
        cross = v1[0] * v2[1] - v1[1] * v2[0]
        turn = math.atan2(cross, float(v1 @ v2))
        if abs(turn) < 0.18:
            continue
        t = v2 / (np.linalg.norm(v2) + 1e-9)
        nrm = np.array([t[1], -t[0]]) * (1 if turn < 0 else -1)
        p = np.array(px(b)) + nrm * H * (VERGE / 2 + 0.004)
        draw.rectangle([p[0] - 5, p[1] - 3, p[0] + 5, p[1] + 3], fill=(214, 186, 104, 255), outline=(150, 124, 60, 255))
        keep("bales", p[0] / W, p[1] / H, math.atan2(t[1], t[0]))


def road_surface(img, mask, surface, rng):
    m = np.asarray(mask, dtype=np.float32)[..., None] / 255
    grain = noise(rng, 900)[..., None]
    if surface == "brick":
        base = np.array([132, 78, 58], np.float32)
        yy, xx = np.mgrid[0:H, 0:W]
        joints = ((xx % 9 == 0) | (yy % 5 == 0))[..., None]
        colour = base * (0.9 + 0.2 * grain) - joints * 22
    elif surface == "concrete":
        base = np.array([150, 146, 136], np.float32)
        yy, xx = np.mgrid[0:H, 0:W]
        slabs = ((xx % 40 == 0) | (yy % 40 == 0))[..., None]
        colour = base * (0.9 + 0.18 * grain) - slabs * 14
    else:
        base = np.array([78, 76, 72], np.float32)
        colour = base * (0.85 + 0.3 * grain)
    return img * (1 - m) + colour * m


def paint(cid):
    track = json.load(open(os.path.join(TRACKS, cid + ".json"), encoding="utf-8"))
    scene = SCENES.get(cid, dict(forest=0.3, fields="farm", surface="tarmac"))
    rng = np.random.default_rng(sum(map(ord, cid)))
    SCENERY.clear()
    road = smooth(track["points"])
    unused = [np.array(u) for u in track.get("unused", [])]

    road_img = line_mask([road], ROAD * H)
    verge_img = line_mask([road], VERGE * H)
    unused_img = line_mask(unused, ROAD * H, closed=False) if unused else Image.new("L", (W, H), 0)
    water_img = rect_mask(track.get("water", []) if "sea" not in scene else [])
    if "sea" in scene:
        ImageDraw.Draw(water_img).polygon([(x * W, y * H) for x, y in scene["sea"]], fill=255)
        if "lake" in scene:  # a lake inside the lap: the lap shrunk towards its middle
            cx, cy = np.mean(road, axis=0)
            k = scene["lake"]
            ImageDraw.Draw(water_img).polygon([((cx + (x - cx) * k) * W, (cy + (y - cy) * k) * H) for x, y in road], fill=255)
        water_img = water_img.filter(ImageFilter.GaussianBlur(3)).point(lambda v: 255 if v > 128 else 0)
    stand_img = rect_mask(track.get("grandstands", []))
    water = np.asarray(water_img) > 0

    # Ground: fields, then soft light and shade over everything.
    img = fields(rng, FIELD_COLOURS[scene["fields"]])
    img = tint(img, rng, 0.22, 3)
    img *= (0.94 + 0.12 * noise(rng, 500))[..., None]

    # Water, with a lighter shallow edge.
    if water.any():
        wn = fbm(rng, 30, 3)
        sea = np.stack([52 + 20 * wn, 92 + 26 * wn, 128 + 30 * wn], axis=-1)
        shore = np.asarray(water_img.filter(ImageFilter.MinFilter(9)).filter(ImageFilter.GaussianBlur(6)), np.float32)[..., None] / 255
        img = np.where(water[..., None], sea * (0.82 + 0.18 * shore) + (1 - shore) * np.array([40, 46, 34]), img)

    canvas = Image.fromarray(np.clip(img, 0, 255).astype(np.uint8)).convert("RGBA")
    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)

    keep_clear = (near(verge_img, 14) > 0.15) | water | (np.asarray(stand_img) > 0) | (np.asarray(unused_img) > 0)

    # Woods: big masses from low-frequency noise, kept off the road.
    if scene["forest"] > 0:
        wood = fbm(rng, 6, 4)
        threshold = np.quantile(wood, 1 - scene["forest"])
        forest = (wood > threshold) & ~keep_clear
        floor = Image.fromarray((forest * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3))
        f = np.asarray(floor, np.float32)[..., None] / 255
        base = np.asarray(canvas, np.float32)[..., :3]
        canvas = Image.fromarray(np.clip(base * (1 - f) + np.array([44, 62, 36]) * f, 0, 255).astype(np.uint8)).convert("RGBA")
        crowns(draw, rng, forest, int(forest.sum() / 260), 13)
    # Single trees and rows along the fields.
    lone = (~keep_clear) & (rng.random((H, W)) < 0.00012)
    crowns(draw, rng, lone, int(lone.sum()), 10)
    # Avenues of trees beside the road, as on most period circuits.
    side = (near(verge_img, 26) > 0.08) & (near(verge_img, 12) < 0.3) & ~water
    crowns(draw, rng, side, int(side.sum() / 900), 8)

    if scene.get("town"):
        town(draw, rng, keep_clear | water)
    if scene.get("boats"):
        boats(draw, rng, scene["boats"], water)
    for name in scene.get("villages", []):
        corner = next((c for c in track.get("corners", []) if c[0] == name), None)
        if corner:
            houses(draw, rng, px(corner[1:]), 70, 70, keep_clear)

    canvas = Image.alpha_composite(canvas, layer)

    # The road: a soft shadow, the dusty verge, then the surface.
    shadow = verge_img.filter(ImageFilter.GaussianBlur(10))
    shadow = ImageChops.offset(shadow, 6, 8)
    base = np.asarray(canvas, np.float32)[..., :3]
    s = np.asarray(shadow, np.float32)[..., None] / 255
    base = base * (1 - 0.35 * s)
    v = np.asarray(verge_img.filter(ImageFilter.GaussianBlur(2)), np.float32)[..., None] / 255
    dust = np.array([176, 160, 122], np.float32) * (0.9 + 0.2 * noise(rng, 700))[..., None]
    base = base * (1 - v) + dust * v
    if unused:
        u = np.asarray(unused_img.filter(ImageFilter.GaussianBlur(1)), np.float32)[..., None] / 255
        base = base * (1 - u) + np.array([168, 164, 152], np.float32) * u
    base = road_surface(base, road_img.filter(ImageFilter.GaussianBlur(1)), scene["surface"], rng)
    canvas = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8)).convert("RGBA")

    layer = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    stands(draw, track.get("grandstands", []))
    bales(draw, road, rng)
    start_line(draw, road)
    canvas = Image.alpha_composite(canvas, layer).convert("RGB")

    return grade(canvas, rng)


def start_line(draw, road):
    """A chequered band across the road at the start."""
    p, q = np.array(px(road[0])), np.array(px(road[2]))
    t = (q - p) / (np.linalg.norm(q - p) + 1e-9)
    nrm = np.array([-t[1], t[0]])
    half = ROAD * H / 2
    cells = 6
    size = 2 * half / cells
    for row in range(2):
        for k in range(cells):
            a = p + nrm * (-half + k * size) + t * row * size
            corners = [a, a + nrm * size, a + nrm * size + t * size, a + t * size]
            colour = (235, 232, 222, 255) if (k + row) % 2 == 0 else (30, 30, 30, 255)
            draw.polygon([tuple(c) for c in corners], fill=colour)


def grade(img, rng):
    """Period look: warm, slightly faded colours, a soft vignette and paper grain."""
    a = np.asarray(img, np.float32) / 255
    a = a * 0.92 + 0.04
    warm = np.array([1.04, 1.0, 0.9], np.float32)
    a = a * warm
    yy, xx = np.mgrid[0:H, 0:W].astype(np.float32)
    r = np.sqrt(((xx / W - 0.5) * 1.2) ** 2 + (yy / H - 0.5) ** 2)
    a *= (1 - 0.28 * np.clip(r - 0.35, 0, 1))[..., None]
    a *= (0.97 + 0.06 * noise(rng, 1400))[..., None]
    return Image.fromarray(np.clip(a * 255, 0, 255).astype(np.uint8))


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for cid in sys.argv[1:] or sorted(f[:-5] for f in os.listdir(TRACKS) if f.endswith(".json")):
        image = paint(cid)
        path = os.path.join(OUT, cid + ".jpg")
        image.save(path, quality=86, optimize=True)
        os.makedirs(SCENERY_OUT, exist_ok=True)
        with open(os.path.join(SCENERY_OUT, cid + ".json"), "w", encoding="utf-8") as f:
            json.dump({"note": "Written by tools/track-art/paint.py: what is painted where, in drawing units.",
                       **{k: SCENERY.get(k, []) for k in ("trees", "houses", "boats", "bales")}},
                      f, separators=(",", ":"))
        print(f"{cid}: {os.path.getsize(path) // 1024} KB, {len(SCENERY.get('trees', []))} trees")
