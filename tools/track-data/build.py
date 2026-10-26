"""Build game/data/tracks/*.json from real map data.

OpenStreetMap raceways and roads (tools/track-data/osm, © OpenStreetMap
contributors, ODbL) are joined into a graph, and each 1930s lap is routed through
it via a list of waypoints in driving order. Monaco comes from the
bacinger/f1-circuits GeoJSON (MIT) with the post-war swimming-pool section taken
out. convert.py then fits each loop into the game's drawing box.

Usage: python3 tools/track-data/build.py   (from the repo root)
"""
import heapq, json, math, os, re, sys

sys.path.insert(0, os.path.dirname(__file__))
from convert import convert, dumps

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "game", "data", "tracks")


def dist(a, b):
    """Metres between two [lon, lat] points."""
    k = math.cos(math.radians((a[1] + b[1]) / 2))
    return math.hypot((a[0] - b[0]) * 111320 * k, (a[1] - b[1]) * 110540)


class Graph:
    def __init__(self, files, exclude=None, snap=20, links=()):
        self.ways, self.adj = [], {}
        for f in files:
            for e in json.load(open(os.path.join(HERE, "osm", f)))["elements"]:
                if e["type"] != "way" or e.get("tags", {}).get("area") == "yes":
                    continue
                name = e["tags"].get("name") or ""
                if exclude and re.search(exclude, name):
                    continue
                pts = [(round(p["lon"], 7), round(p["lat"], 7)) for p in e["geometry"]]
                self.ways.append((name, pts))
                for a, b in zip(pts, pts[1:]):
                    self.link(a, b, dist(a, b))
        # Ways that meet without sharing a node (raceway next to public road)
        ends = [p for _, pts in self.ways for p in (pts[0], pts[-1])]
        for i, a in enumerate(ends):
            for b in ends[i + 1:]:
                if a != b and dist(a, b) < snap:
                    self.link(a, b, dist(a, b))
        for a, b in links:  # gaps where a later chicane replaced the 1930s road
            self.link(tuple(a), tuple(b), dist(a, b))

    def link(self, a, b, w):
        self.adj.setdefault(a, {})[b] = w
        self.adj.setdefault(b, {})[a] = w

    def locate(self, wp):
        if isinstance(wp, str):
            pts = [p for name, ps in self.ways if name == wp for p in ps]
            if not pts:
                raise SystemExit(f"no way named {wp!r}")
            mid = (sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts))
            return min(pts, key=lambda n: dist(n, mid))
        return min(self.adj, key=lambda n: dist(n, wp))

    def route(self, waypoints, closed=True):
        nodes = [self.locate(w) for w in waypoints]
        legs = list(zip(nodes, nodes[1:] + nodes[:1])) if closed else list(zip(nodes, nodes[1:]))
        used, path = set(), [nodes[0]]
        for a, b in legs:
            leg = self.shortest(a, b, used)
            used.update(zip(leg, leg[1:]))
            used.update(zip(leg[1:], leg))
            path += leg[1:]
        return [list(p) for p in (path[:-1] if closed else path)]

    def shortest(self, a, b, used):
        best, prev, queue = {a: 0}, {}, [(0, a)]
        while queue:
            d, n = heapq.heappop(queue)
            if n == b:
                break
            if d > best[n]:
                continue
            for m, w in self.adj[n].items():
                # Already driven this lap: avoid, so out-and-back roads use both carriageways
                nd = d + w * (25 if (n, m) in used else 1)
                if nd < best.get(m, 1e18):
                    best[m], prev[m] = nd, n
                    heapq.heappush(queue, (nd, m))
        if b not in prev:
            raise SystemExit(f"no route {a} -> {b}")
        leg = [b]
        while leg[-1] != a:
            leg.append(prev[leg[-1]])
        return leg[::-1]

    def centre(self, name):
        pts = [p for n, ps in self.ways if n == name for p in ps]
        return [sum(p[0] for p in pts) / len(pts), sum(p[1] for p in pts) / len(pts)]


def corners(graph, names):
    """[label, way name or [lon, lat]] -> [label, lon, lat] at that corner."""
    out = []
    for label, where in names:
        p = graph.centre(where) if isinstance(where, str) else where
        out.append([label] + p)
    return out


def nurburgring():
    g = Graph(["nurburgring.json"], exclude=r"Sprint|Boxengasse|Müllenbach|Rallycross|GP|Variante|Anbindung zum")
    wps = ["T13", "Hatzenbach", "Flugplatz", "Aremberg", "Adenauer Forst", "Kallenhard", "Bergwerk",
           "Hohe Acht", "Brünnchen", "Pflanzgarten", "Schwalbenschwanz", "Galgenkopf",
           "Döttinger Höhe", "Antoniusbuche", "Tiergarten"]
    return dict(
        loop=g.route(wps), clockwise=True, samples=200,
        note="Nürburgring Nordschleife, traced from OpenStreetMap. The 1930s start and pits stood where the modern GP circuit begins.",
        corners=corners(g, [["Hatzenbach", "Hatzenbach"], ["Flugplatz", "Flugplatz"], ["Adenauer Forst", "Adenauer Forst"],
                            ["Bergwerk", "Bergwerk"], ["Karussell", "Karussell"], ["Hohe Acht", "Hohe Acht"],
                            ["Brünnchen", "Brünnchen"], ["Pflanzgarten", "Pflanzgarten"], ["Döttinger Höhe", "Döttinger Höhe"]]))


def monza():
    g = Graph(["monza.json"], exclude=r"Variante|Sopraelevata|Raccordo|Pirelli|Pit Lane|anello|Tondo", snap=90,
              links=[[(9.2859173, 45.6222108), (9.2849918, 45.6209513)]])  # bridge the chicanes straight
    wps = ["Rettifilo di partenza", "Curva Biassono", "Lesmo 1", "Lesmo 2", "Curva del Serraglio",
           "Curva Vialone", [9.2850, 45.6160], "Curva Alboreto"]
    return dict(
        loop=g.route(wps), clockwise=True,
        note="Monza road course (pista stradale), traced from OpenStreetMap without the post-war chicanes. The south curve is today's Parabolica.",
        corners=corners(g, [["Curva Grande", "Curva Biassono"], ["Lesmo", "Lesmo 1"], ["Vialone", "Curva Vialone"],
                            ["Curva Sud", "Curva Alboreto"]]))


def montlhery():
    g = Graph(["montlhery.json"], exclude=r"Espace Plus")
    bowl_top, bowl_east, bowl_se = [2.2440, 48.6278], [2.2522, 48.6255], [2.2468, 48.6232]
    wps = [bowl_top, bowl_east, bowl_se, [2.2300, 48.6190], "Virage du Gendarme", "Virage de la Forêt",
           "Épingle des Bruyères", [2.2070, 48.6205], [2.2250, 48.6200], "Virage de la Ferme", "Épingle du Faye"]
    return dict(
        loop=g.route(wps), clockwise=True,
        unused=[g.route([bowl_se, [2.2400, 48.6222], [2.2366, 48.6245], [2.2400, 48.6270], bowl_top], closed=False)],
        note="Linas-Montlhéry 12.5 km circuit, traced from OpenStreetMap: the eastern half of the banked bowl, then the road loop out to the Bruyères hairpin and back past the Faye hairpin.",
        corners=corners(g, [["Speed bowl", [2.2445, 48.6250]], ["Ferme", "Virage de la Ferme"], ["Gendarme", "Virage du Gendarme"],
                            ["Bruyères", "Épingle des Bruyères"], ["Faye", "Épingle du Faye"]]))


def indianapolis():
    g = Graph(["indianapolis.json"], exclude=r"Pit|Dirt|Warmup|Formula|Alternate|Escape")
    wps = ["Indianapolis Motor Speedway - Main Straight", "Indianapolis Motor Speedway - Turn 1",
           "Indianapolis Motor Speedway - Turn 2", "Indianapolis Motor Speedway - Turn 3",
           "Indianapolis Motor Speedway - Turn 4"]
    return dict(
        loop=g.route(wps), clockwise=False,
        note="Indianapolis Motor Speedway oval, traced from OpenStreetMap: 2.5 miles, four quarter-mile turns, run anticlockwise.",
        corners=corners(g, [[f"Turn {i}", f"Indianapolis Motor Speedway - Turn {i}"] for i in range(1, 5)]))


def spa():
    g = Graph(["spa-raceway.json", "spa-roads.json"], exclude=r"Kart|Pit Lane|Moto|Chicane|Bruxelles|Double Gauche|Fagnes|Campus|Speaker|Paul Frère|Rivage|Malmedy", snap=40,
              links=[[(5.9769802, 50.4298564), (5.9776006, 50.4290244)],   # Les Combes onto the Burnenville road
                     [(5.9583018, 50.4303509), (5.9639606, 50.4334236)]])  # old road from Stavelot up to Blanchimont
    combes, burnenville, malmedy = [5.9776, 50.4290], [6.0015, 50.4250], [5.9930, 50.4170]
    masta, stavelot = [5.9790, 50.4140], [5.9475, 50.4040]
    wps = ["La Source", "Raidillon", "Kemmel", combes, burnenville, malmedy, masta, stavelot,
           [5.9500, 50.4100], [5.9560, 50.4250], "Blanchimont"]
    return dict(
        loop=g.route(wps), clockwise=True,
        note="Spa-Francorchamps 1930s road triangle, traced from OpenStreetMap public roads: Eau Rouge and Les Combes, down to Burnenville and the Malmedy corner, the Masta straight, the Stavelot hairpin and back up through Blanchimont.",
        corners=corners(g, [["La Source", "La Source"], ["Eau Rouge", "Raidillon"], ["Les Combes", combes],
                            ["Burnenville", burnenville], ["Malmedy", malmedy], ["Masta", masta],
                            ["Stavelot", stavelot], ["Blanchimont", "Blanchimont"]]))


def monaco():
    f = json.load(open(os.path.join(HERE, "f1-circuits", "monaco.geojson")))["features"][0]
    c = f["geometry"]["coordinates"]
    # 1930: from Tabac straight down the quay to the Gasometer hairpin; the pool came in 1973
    loop = c[:72] + c[98:]
    start = c[138]
    return dict(
        loop=loop, start=start, clockwise=True,
        waterBox=[[0.4, 0.52, 0.2, 0.4], [0.64, 0.6, 0.36, 0.4]],  # the harbour and the open sea
        credit="Outline from bacinger/f1-circuits",
        note="Monaco street circuit from the f1-circuits outline (MIT), with the post-war swimming-pool section replaced by the 1930s run along the quay to the Gasometer hairpin.",
        corners=[["Sainte Dévote", *c[150]], ["Casino", *c[10]], ["Station", *c[20]], ["Portier", *c[40]],
                 ["Tunnel", *c[47]], ["Tabac", *c[66]], ["Gasometer", *c[104]]])


BUILDERS = dict(nurburgring=nurburgring, monza=monza, montlhery=montlhery, indianapolis=indianapolis, spa=spa, monaco=monaco)

if __name__ == "__main__":
    for cid in sys.argv[1:] or BUILDERS:
        spec = BUILDERS[cid]()
        spec.setdefault("start", spec["loop"][0])
        spec["circuitId"] = cid
        spec.setdefault("credit", "Map data © OpenStreetMap contributors")
        out, metres = convert(spec)
        path = os.path.join(OUT, cid + ".json")
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(dumps(out))
        print(f"{cid}: {metres / 1000:.2f} km, {len(out['points'])} points")
