"""Turn real map geometry into a game track file (game/data/tracks/<id>.json).

Input is a closed loop of [lon, lat] points. The script projects it to metres,
rotates the loop so it starts at the start line, sets the direction of travel,
simplifies it and fits it, north up, into the game's 1.4:1 drawing box.

Usage: python3 convert.py <spec.json> <out.json>

The spec holds: circuitId, note, loop ([lon, lat] list), start ([lon, lat]),
clockwise (bool), optional rotate (degrees), unused (list of [lon, lat] lines),
water/grandstands (rectangles as [lon, lat, lon, lat]) and corners
([name, lon, lat] or [name, lapFraction]).
"""
import json, math, re, sys

ASPECT = 1.4
MARGIN = 0.07


def project(lonlat, origin):
    lon0, lat0 = origin
    k = math.cos(math.radians(lat0))
    return [((lon - lon0) * 111320 * k, -(lat - lat0) * 110540) for lon, lat in lonlat]


def area(pts):
    # Signed area with y pointing down (screen): positive means clockwise on screen.
    return sum(x0 * y1 - x1 * y0 for (x0, y0), (x1, y1) in zip(pts, pts[1:] + pts[:1])) / 2


def rdp(pts, eps):
    if len(pts) < 3:
        return pts
    (x0, y0), (x1, y1) = pts[0], pts[-1]
    dx, dy = x1 - x0, y1 - y0
    norm = math.hypot(dx, dy) or 1e-9
    best, idx = 0, 0
    for i in range(1, len(pts) - 1):
        d = abs(dy * (pts[i][0] - x0) - dx * (pts[i][1] - y0)) / norm
        if d > best:
            best, idx = d, i
    if best <= eps:
        return [pts[0], pts[-1]]
    return rdp(pts[: idx + 1], eps)[:-1] + rdp(pts[idx:], eps)


def resample(pts, step):
    """Even spacing so the game's Catmull-Rom smoothing doesn't bunch up."""
    loop = pts + [pts[0]]
    total = length(pts)
    n = max(8, round(total / step))
    out, seg, run = [], 0, 0.0
    for k in range(n):
        d = total * k / n
        while run + math.dist(loop[seg], loop[seg + 1]) < d:
            run += math.dist(loop[seg], loop[seg + 1])
            seg += 1
        (x0, y0), (x1, y1) = loop[seg], loop[seg + 1]
        f = (d - run) / (math.dist(loop[seg], loop[seg + 1]) or 1)
        out.append((x0 + (x1 - x0) * f, y0 + (y1 - y0) * f))
    return out


def length(pts):
    return sum(math.dist(a, b) for a, b in zip(pts, pts[1:] + pts[:1]))


def convert(spec):
    loop = spec["loop"]
    if loop[0] == loop[-1]:
        loop = loop[:-1]
    origin = loop[0]
    pts = project(loop, origin)

    ang = math.radians(spec.get("rotate", 0))
    rot = lambda p: (p[0] * math.cos(ang) - p[1] * math.sin(ang), p[0] * math.sin(ang) + p[1] * math.cos(ang))
    tf = lambda ll: [rot(p) for p in project(ll, origin)]
    pts = [rot(p) for p in pts]

    if (area(pts) > 0) != spec["clockwise"]:
        pts.reverse()
    s = tf([spec["start"]])[0]
    i0 = min(range(len(pts)), key=lambda i: math.dist(pts[i], s))
    pts = pts[i0:] + pts[:i0]

    metres = length(pts)
    # Keep corners sharp: simplify finely, then resample hairpins and straights evenly.
    far = max(range(len(pts)), key=lambda i: math.dist(pts[0], pts[i]))
    eps = metres / 2500
    simple = rdp(pts[: far + 1], eps)[:-1] + rdp(pts[far:] + [pts[0]], eps)[:-1]
    pts = resample(simple, metres / spec.get("samples", 110))

    xs, ys = [p[0] for p in pts], [p[1] for p in pts]
    w, h = max(xs) - min(xs), max(ys) - min(ys)
    scale = min(ASPECT * (1 - 2 * MARGIN) / w, (1 - 2 * MARGIN) / h)  # box units: height 1
    cx, cy = (max(xs) + min(xs)) / 2, (max(ys) + min(ys)) / 2
    norm = lambda p: [round((ASPECT / 2 + (p[0] - cx) * scale) / ASPECT, 3), round(0.5 + (p[1] - cy) * scale, 3)]

    out = {"circuitId": spec["circuitId"], "note": spec["note"], "points": [norm(p) for p in pts]}
    if spec.get("credit"):
        out["credit"] = spec["credit"]
    # Main grandstand alongside the start line, on the outside of the lap
    a, b = norm(pts[-2]), norm(pts[2])
    side = 1 if spec["clockwise"] else -1
    if abs(b[0] - a[0]) * ASPECT > abs(b[1] - a[1]):  # start straight runs across the drawing
        nx = -side * (1 if b[0] > a[0] else -1)
        x0 = round(min(a[0], b[0]) - 0.03, 3)
        out["grandstands"] = [[x0, round(out["points"][0][1] + nx * 0.035 - 0.0125, 3), round(abs(b[0] - a[0]) + 0.06, 3), 0.025]]
    else:
        ny = side * (1 if b[1] > a[1] else -1)
        y0 = round(min(a[1], b[1]) - 0.04, 3)
        out["grandstands"] = [[round(out["points"][0][0] + ny * 0.03 - 0.009, 3), y0, 0.018, round(abs(b[1] - a[1]) + 0.08, 3)]]
    if spec.get("unused"):
        out["unused"] = [[norm(p) for p in tf(line)] for line in spec["unused"]]
    if spec.get("waterBox"):  # already in drawing units
        out["water"] = spec["waterBox"]
    for key in ("water", "grandstands"):
        if spec.get(key):
            rects = []
            for lon0, lat0, lon1, lat1 in spec[key]:
                a, b = (norm(p) for p in tf([[lon0, lat0], [lon1, lat1]]))
                rects.append([min(a[0], b[0]), min(a[1], b[1]), round(abs(a[0] - b[0]), 3), round(abs(a[1] - b[1]), 3)])
            out[key] = rects
    if spec.get("corners"):
        corners = []
        for c in spec["corners"]:
            if len(c) == 2:  # lap fraction
                name, i = c[0], int(c[1] * len(pts)) % len(pts)
            else:
                name, q = c[0], tf([c[1:]])[0]
                i = min(range(len(pts)), key=lambda j: math.dist(pts[j], q))
            # Label just outside the lap, along the normal of the track there
            (x0, y0), (x1, y1) = pts[i - 3], pts[(i + 3) % len(pts)]
            d = math.hypot(x1 - x0, y1 - y0) or 1
            out_x, out_y = (y1 - y0) / d, -(x1 - x0) / d
            if not spec["clockwise"]:
                out_x, out_y = -out_x, -out_y
            p = (pts[i][0] + out_x * 0.07 / scale, pts[i][1] + out_y * 0.045 / scale)
            corners.append([name] + norm(p))
        out["corners"] = corners
    return out, metres


def dumps(track):
    """Pretty JSON with one point per line, like the hand-written files."""
    text = json.dumps(track, ensure_ascii=False, indent=2)
    return re.sub(r"\[\s+(-?[\d.]+),\s+(-?[\d.]+)\s+\]", r"[\1, \2]", text) + "\n"


if __name__ == "__main__":
    spec = json.load(open(sys.argv[1]))
    out, metres = convert(spec)
    with open(sys.argv[2], "w", encoding="utf-8") as f:
        f.write(dumps(out))
    print(f"{spec['circuitId']}: {metres / 1000:.2f} km, {len(out['points'])} points")
