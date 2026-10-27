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
    def __init__(self, files, exclude=None, snap=20, links=(), highways=None):
        """highways, if given, keeps only ways of those highway types (the big files hold every path)."""
        self.ways, self.adj = [], {}
        for f in files:
            for e in json.load(open(os.path.join(HERE, "osm", f)))["elements"]:
                if e["type"] != "way" or e.get("tags", {}).get("area") == "yes":
                    continue
                if highways and e.get("tags", {}).get("highway") not in highways:
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


ROADS = {"motorway", "trunk", "primary", "secondary", "tertiary", "unclassified", "residential",
         "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link", "raceway"}


def reims():
    # The roads are straight between the three corners, so the lap is drawn through them
    # directly; routing through the village streets wanders.
    thillois, gueux, garenne = [3.9500, 49.2603], [3.9062, 49.2528], [3.9200, 49.2688]
    pts = [thillois, [3.9400, 49.2576], [3.9300, 49.2550], [3.9215, 49.2526], [3.9150, 49.2527], [3.9080, 49.2527],
           gueux, [3.9055, 49.2555], [3.9060, 49.2580], [3.9100, 49.2610], [3.9150, 49.2650], [3.9185, 49.2672],
           garenne, [3.9300, 49.2660], [3.9400, 49.2632]]
    return dict(
        loop=densify(pts), clockwise=False, start=[3.9300, 49.2550],
        note="Reims-Gueux as raced before 1952, drawn along the roads in OpenStreetMap: the pits on the Gueux road, through the village, up to La Garenne, along the Soissons road to the Thillois hairpin and back.",
        corners=[["Gueux", *gueux], ["La Garenne", *garenne], ["Thillois", *thillois]])


def densify(pts, step=0.0005):
    """Straight lines between the points, cut into short pieces for convert.py."""
    out = []
    for a, b in zip(pts, pts[1:] + pts[:1]):
        n = max(1, round(math.hypot(b[0] - a[0], b[1] - a[1]) / step))
        out += [[a[0] + (b[0] - a[0]) * i / n, a[1] + (b[1] - a[1]) * i / n] for i in range(n)]
    return out


def pescara():
    g = Graph(["pescara.json"], highways=ROADS)
    start, spoltore, cappelle = [14.1990, 42.4830], [14.1400, 42.4560], [14.1050, 42.4660]
    montesilvano, marina = [14.1270, 42.4960], [14.1450, 42.5200]
    wps = [start, [14.1960, 42.4720], [14.1600, 42.4610], spoltore, cappelle, montesilvano, marina]
    return dict(
        loop=g.route(wps), clockwise=True, samples=200,
        note="Circuito di Pescara, routed along today's roads from OpenStreetMap: from the seafront up into the hills through Spoltore and Cappelle sul Tavo, down the long Montesilvano straight and back along the coast. Approximate: some 1930s roads have since been rebuilt.",
        corners=[["Spoltore", *spoltore], ["Cappelle", *cappelle], ["Montesilvano straight", *montesilvano], ["Montesilvano", *marina]])


def avus():
    # The two carriageways of the motorway lie a few metres apart, too close to tell apart in the
    # drawing: drive down one and back up a copy set off to the side, joined by the two curves.
    g = Graph(["avus.json"], highways={"motorway"})
    north, south = [13.2745, 52.4985], [13.1985, 52.4345]
    line = g.route([north, [13.2350, 52.4670], south], closed=False)
    gap = 0.005  # set off about 450 m to the south-east
    back = [[lon + gap, lat - gap * 0.6] for lon, lat in reversed(line)]
    def bend(a, b, out):
        mid = [(a[0] + b[0]) / 2 + out[0], (a[1] + b[1]) / 2 + out[1]]
        return [[(1 - t) ** 2 * a[0] + 2 * t * (1 - t) * mid[0] + t * t * b[0],
                 (1 - t) ** 2 * a[1] + 2 * t * (1 - t) * mid[1] + t * t * b[1]] for t in [i / 8 for i in range(1, 8)]]
    loop = line + bend(line[-1], back[0], [-0.0012, -0.0012]) + back + bend(back[-1], line[0], [0.0012, 0.0012])
    return dict(
        loop=loop, clockwise=False, start=[13.2450, 52.4750], rotate=54,
        note="AVUS, Berlin, traced from the motorway in OpenStreetMap: two straights of nine kilometres joined by the south curve and the north curve, which was banked in 1937. The carriageways are drawn further apart than they are, so both can be seen.",
        corners=[["Nordkurve", 13.2770, 52.5005], ["Südkurve", 13.1960, 52.4325]])


def bremgarten():
    # The forest tracks in OpenStreetMap do not join up into the old lap, and the motorway now
    # cuts across it, so the lap is drawn through points along its course in the Bremgartenwald.
    forsthaus, eymatt, glasbrunnen = [7.4180, 46.9615], [7.3870, 46.9688], [7.4130, 46.9702]
    pts = [forsthaus, [7.4100, 46.9603], [7.4020, 46.9622], [7.3950, 46.9638], [7.3900, 46.9662], eymatt,
           [7.3892, 46.9703], [7.3950, 46.9708], [7.4020, 46.9694], [7.4085, 46.9706], glasbrunnen,
           [7.4190, 46.9690], [7.4232, 46.9668], [7.4226, 46.9638], [7.4204, 46.9620]]
    return dict(
        loop=smooth(pts), clockwise=False, start=forsthaus,
        credit="Drawn from period maps",
        note="Bremgarten, Bern: a fast loop through the Bremgartenwald with hardly a straight, drawn through points along its course. The motorway now cuts across where it ran.",
        corners=[["Forsthaus", *forsthaus], ["Eymatt", *eymatt], ["Glasbrunnen", *glasbrunnen]])


def smooth(pts, steps=10):
    """A closed Catmull-Rom curve through the points, so bends come out round."""
    loop = []
    for i in range(len(pts)):
        p0, p1, p2, p3 = (pts[(i + k) % len(pts)] for k in (-1, 0, 1, 2))
        for t in [j / steps for j in range(steps)]:
            loop.append([0.5 * (2 * p1[d] + (-p0[d] + p2[d]) * t + (2 * p0[d] - 5 * p1[d] + 4 * p2[d] - p3[d]) * t * t
                                + (-p0[d] + 3 * p1[d] - 3 * p2[d] + p3[d]) * t ** 3) for d in (0, 1)])
    return loop


def tripoli():
    # The circuit ran round the Mellaha salt lake, where Mitiga airport is now. Today's streets
    # round the airport do not follow it, so the lap is drawn round the lake's edge, along the
    # coast road in the north.
    pts = [[13.2625, 32.9075], [13.2700, 32.9085], [13.2820, 32.9095], [13.2900, 32.9085], [13.3000, 32.9040],
           [13.3100, 32.9000], [13.3195, 32.8970], [13.3185, 32.8920], [13.3160, 32.8865], [13.3050, 32.8845],
           [13.2930, 32.8870], [13.2800, 32.8890], [13.2700, 32.8905], [13.2620, 32.8925], [13.2600, 32.8970],
           [13.2610, 32.9030]]
    return dict(
        loop=smooth(pts), clockwise=True, start=[13.2820, 32.9095],
        credit="Drawn from period maps",
        note="Mellaha, Tripoli: a very fast lap round the Mellaha salt lake, where Mitiga airport is now, drawn round the lake's edge with the coast road in the north.",
        corners=[["Mellaha", 13.2900, 32.9085], ["Tagiura bend", 13.3190, 32.8945], ["Lake side", 13.2930, 32.8870]])


def brooklands():
    # Almost none of the outer circuit is in OpenStreetMap, so the lap is drawn through points
    # along its known course: the Members' banking in the north-east, the Railway straight beside
    # the line to the north-west, the Byfleet banking over the River Wey in the south, and back
    # past the Fork.
    pts = [[-0.4615, 51.3568], [-0.4625, 51.3590], [-0.4655, 51.3597], [-0.4700, 51.3572], [-0.4760, 51.3530],
           [-0.4790, 51.3490], [-0.4772, 51.3455], [-0.4712, 51.3440], [-0.4657, 51.3462], [-0.4630, 51.3500],
           [-0.4612, 51.3535]]
    return dict(
        loop=smooth(pts), clockwise=False, start=[-0.4630, 51.3500],
        credit="Drawn from period maps",
        note="Brooklands outer circuit, drawn through its known course: most of it is gone or not mapped. Members' banking, the Railway straight, the Byfleet banking and the Fork.",
        corners=[["Members' banking", -0.4635, 51.3593], ["Railway straight", -0.4730, 51.3552],
                 ["Byfleet banking", -0.4760, 51.3450], ["The Fork", -0.4625, 51.3510]])


def lemans():
    # The 1932 Sarthe lap on today's roads and track: Dunlop, the Esses, Tertre Rouge, the
    # Hunaudières straight to Mulsanne, Indianapolis and Arnage, then Maison Blanche. The run
    # from Maison Blanche to the pits was straight in the 1930s, so the later chicanes are
    # bridged, and the road from Arnage joins Maison Blanche where the track now has its own link.
    g = Graph(["lemans.json"], highways=ROADS | {"raceway"},
              exclude=r"Chicane (Daytona|Michelin|Ford|Maison Blanche|du Raccordement)|Pit|Circuit (Bugatti|Alain|C)|Porsche|Corvette|Karting|Garage Bleu|Raccordement|Chemin aux|Moto|Safety|Long lap|Virage du Pont",
              links=[[(0.2090683, 47.9322813), (0.2096724, 47.9390959)], [(0.2089944, 47.9464183), (0.2074582, 47.9483033)]])
    wps = [[0.2076, 47.9505], "Courbe Dunlop", "Esses de la Forêt", "Tertre Rouge", [0.2300, 47.9530], [0.2380, 47.9300],
           "Virage de Mulsanne", [0.2300, 47.9180], "Virage d'Indianapolis", "Virage d'Arnage", (0.2096724, 47.9390959),
           (0.2089944, 47.9464183), (0.2074582, 47.9483033)]
    return dict(
        loop=g.route(wps), clockwise=True,
        note="The Sarthe circuit as run from 1932, traced from OpenStreetMap: Tertre Rouge, the Hunaudières straight, Mulsanne, Arnage and Maison Blanche, without the later chicanes.",
        corners=corners(g, [["Tertre Rouge", "Tertre Rouge"], ["Hunaudières", [0.2300, 47.9530]],
                            ["Mulsanne", "Virage de Mulsanne"], ["Arnage", "Virage d'Arnage"],
                            ["Maison Blanche", "Maison Blanche"]]))


BUILDERS = dict(nurburgring=nurburgring, monza=monza, montlhery=montlhery, indianapolis=indianapolis, spa=spa, monaco=monaco,
                reims=reims, pescara=pescara, avus=avus, bremgarten=bremgarten, tripoli=tripoli, brooklands=brooklands,
                lemans=lemans)

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
