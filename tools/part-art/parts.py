#!/usr/bin/env python3
"""
Draws the car designer's part illustrations as 1930s blueprints: white line work on cyanotype
blue, a title block, and a small detail view. One SVG per choice in CarDesign.cs, rendered to
PNG with Chromium through Playwright for Node (render.js), so the lettering font is embedded.

    python3 tools/part-art/parts.py game/art/parts [name ...]

Writes <group>-<choice>.png (e.g. engine-straight8.png, frame-tubular.png) at 1280x800, twice the
drawing's size so it stays sharp when shown large or on a high-DPI screen, and a thumbnail of the same
name in thumbs/: no lettering and much heavier lines, so it still reads at the size of a button.
Everything is drawn from code here, so the art can be changed without anyone drawing.
"""
import base64
import math
import os
import sys

W, H = 640, 400
INK = "#eaf2ff"
FAINT = "rgba(234,242,255,0.35)"
HERE = os.path.dirname(os.path.abspath(__file__))
# Thumbnails leave out the lettering, grid and title block and draw every line heavier.
THUMB = False


def sw(w, stroke):
    """A stroke width: thumbnails thicken the ink lines a lot and the faint ones less."""
    return w + (1.2 if stroke == FAINT else 4.5) if THUMB else w


# ---------------------------------------------------------------- helpers

def path(d, w=2.0, fill="none", stroke=INK, extra=""):
    return f'<path d="{d}" fill="{fill}" stroke="{stroke}" stroke-width="{sw(w, stroke)}" stroke-linejoin="round" stroke-linecap="round" {extra}/>'


def line(x1, y1, x2, y2, w=2.0, stroke=INK, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    return f'<line x1="{x1:.1f}" y1="{y1:.1f}" x2="{x2:.1f}" y2="{y2:.1f}" stroke="{stroke}" stroke-width="{sw(w, stroke)}" stroke-linecap="round"{d}/>'


def rect(x, y, w, h, r=0, width=2.0, fill="none", stroke=INK, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    return f'<rect x="{x:.1f}" y="{y:.1f}" width="{w:.1f}" height="{h:.1f}" rx="{r}" fill="{fill}" stroke="{stroke}" stroke-width="{sw(width, stroke)}"{d}/>'


def circle(cx, cy, r, width=2.0, fill="none", stroke=INK, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ""
    return f'<circle cx="{cx:.1f}" cy="{cy:.1f}" r="{r:.1f}" fill="{fill}" stroke="{stroke}" stroke-width="{sw(width, stroke)}"{d}/>'


def text(x, y, s, size=15, anchor="start", fill=INK, rotate=None):
    if THUMB:
        return ""
    t = f' transform="rotate({rotate} {x} {y})"' if rotate is not None else ""
    return f'<text x="{x}" y="{y}" font-size="{size}" text-anchor="{anchor}" fill="{fill}"{t}>{s}</text>'


def hatch(x, y, w, h, gap=7, width=1.0):
    """Section hatching at 45 degrees, clipped to the rectangle."""
    cid = f"h{abs(hash((x, y, w, h))) % 10**8}"
    out = [f'<clipPath id="{cid}"><rect x="{x}" y="{y}" width="{w}" height="{h}"/></clipPath><g clip-path="url(#{cid})">']
    k = -h
    while k < w:
        out.append(line(x + k, y + h, x + k + h, y, width, FAINT))
        k += gap
    out.append("</g>")
    return "".join(out)


def callout(x, y, tx, ty, label, anchor="start"):
    """A leader line from a point on the drawing to its hand-lettered label."""
    if THUMB:
        return ""
    return (circle(x, y, 2.5, 1.2, INK) + line(x, y, tx, ty, 1.0, FAINT)
            + text(tx + (5 if anchor == "start" else -5), ty + 6, label, 18, anchor))


def dimension(x1, x2, y, label):
    """A horizontal dimension line with arrow ticks."""
    if THUMB:
        return ""
    return (line(x1, y, x2, y, 1.0, FAINT) + line(x1, y - 6, x1, y + 6, 1.0, FAINT) + line(x2, y - 6, x2, y + 6, 1.0, FAINT)
            + path(f"M{x1 + 8},{y - 3} L{x1},{y} L{x1 + 8},{y + 3}", 1.0, stroke=FAINT)
            + path(f"M{x2 - 8},{y - 3} L{x2},{y} L{x2 - 8},{y + 3}", 1.0, stroke=FAINT)
            + text((x1 + x2) / 2, y - 8, label, 16, "middle", FAINT))


def wheel(cx, cy, r, spokes=True, width=2.0):
    """A wire wheel seen from the side: tyre, rim, knock-off hub and crossed spokes."""
    out = [circle(cx, cy, r, width), circle(cx, cy, r * 0.78, 1.4), circle(cx, cy, r * 0.16, 1.6)]
    if spokes:
        for i in range(24):
            a = i * math.pi / 12
            a2 = a + 0.5
            out.append(line(cx + math.cos(a) * r * 0.14, cy + math.sin(a) * r * 0.14,
                            cx + math.cos(a2) * r * 0.76, cy + math.sin(a2) * r * 0.76, 0.6, FAINT))
    # Knock-off spinner ears.
    for a in (0, math.pi):
        out.append(line(cx + math.cos(a) * r * 0.16, cy, cx + math.cos(a) * r * 0.32, cy - 3, 1.4))
    return "".join(out)


def tyre_front(cx, cy, w, h):
    """A wheel seen from the front: a narrow tall rounded tyre with the rim line."""
    return rect(cx - w / 2, cy - h / 2, w, h, w / 2.2, 2) + line(cx, cy - h / 2 + 6, cx, cy + h / 2 - 6, 0.8, FAINT)


def sheet(title, sub, number, body, inset=""):
    """The paper: blue ground, grid, border, title block, and the drawing itself."""
    font = base64.b64encode(open(os.path.join(HERE, "ArchitectsDaughter-Regular.ttf"), "rb").read()).decode()
    grid = []
    for g in [] if THUMB else range(0, max(W, H) + 1, 16):
        major = g % 80 == 0
        width, colour = (0.9, "rgba(234,242,255,0.13)") if major else (0.5, "rgba(234,242,255,0.07)")
        if g <= W:
            grid.append(line(g, 0, g, H, width, colour))
        if g <= H:
            grid.append(line(0, g, W, g, width, colour))
    block = "" if THUMB else (
        rect(W - 276, H - 70, 260, 54, 0, 1.4) + line(W - 276, H - 44, W - 16, H - 44, 1.0)
        + line(W - 74, H - 44, W - 74, H - 16, 1.0)
        + text(W - 268, H - 50, title, 20)
        + text(W - 268, H - 24, sub, 15, fill=FAINT)
        + text(W - 67, H - 24, number, 15, fill=FAINT))
    border = rect(10, 10, W - 20, H - 20, 0, 1.6) + ("" if THUMB else rect(16, 16, W - 32, H - 32, 0, 0.7, stroke=FAINT))
    return f"""<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}">
<defs>
<style>@font-face{{font-family:Draft;src:url(data:font/ttf;base64,{font})}} text{{font-family:Draft}}</style>
<radialGradient id="paper" cx="45%" cy="40%" r="80%"><stop offset="0" stop-color="#25578f"/><stop offset="0.7" stop-color="#1b4677"/><stop offset="1" stop-color="#132f53"/></radialGradient>
<filter id="ink"><feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="4"/><feColorMatrix values="0 0 0 0 1  0 0 0 0 1  0 0 0 0 1  0 0 0 0.05 0"/><feComposite in2="SourceGraphic" operator="in"/></filter>
</defs>
<rect width="{W}" height="{H}" fill="url(#paper)"/>
{''.join(grid)}
<rect width="{W}" height="{H}" filter="url(#ink)"/>
{border}
<g>{body}{inset}{block}</g>
</svg>"""


def inset_box(x, y, w, h, label, content):
    return rect(x, y, w, h, 0, 1.0, stroke=FAINT, dash="4 3") + text(x + 6, y + 17, label, 14, fill=FAINT) + content


# ---------------------------------------------------------------- engines

def engine(cyl_per_bank, vee, name, number):
    """Side elevation of a twin-cam engine: block, cam covers, plugs, exhaust stubs into a manifold."""
    n = cyl_per_bank
    x0, x1 = 120, 470
    pitch = (x1 - x0) / n
    out = []
    # Crankcase and sump.
    out.append(path(f"M{x0 - 20},210 L{x1 + 20},210 L{x1 + 10},262 L{x1 - 30},300 L{x0 + 10},300 L{x0 - 14},262 Z"))
    out.append(line(x0 - 18, 232, x1 + 16, 232, 1.0, FAINT))
    for i in range(int((x1 - x0) / 22)):
        out.append(line(x0 + 14 + i * 22, 300, x0 + 14 + i * 22, 308, 1.4))  # sump cooling fins
    out.append(line(x0 + 8, 308, x1 - 28, 308, 1.4))
    # Block, with one bore per cylinder shown dashed.
    out.append(rect(x0, 140, x1 - x0, 70, 3))
    for i in range(n):
        cx = x0 + pitch * (i + 0.5)
        out.append(rect(cx - pitch * 0.36, 148, pitch * 0.72, 56, 2, 1.0, stroke=FAINT, dash="5 4"))
    # Head and cam cover(s): a V shows the near bank's cover over the far one.
    if vee:
        out.append(path(f"M{x0 - 6},140 L{x0 + 4},104 L{x1 - 4},104 L{x1 + 6},140", 1.4, stroke=FAINT))
    out.append(rect(x0 - 4, 118, x1 - x0 + 8, 22, 2))
    out.append(rect(x0 + 6, 92, x1 - x0 - 12, 26, 12))
    for i in range(n + 1):
        out.append(circle(x0 + 14 + i * (x1 - x0 - 28) / n, 105, 2.4, 1.2))  # cover studs
    # Plugs and leads.
    for i in range(n):
        cx = x0 + pitch * (i + 0.5)
        out.append(rect(cx - 3, 128, 6, 10, 1, 1.2))
        out.append(path(f"M{cx},128 C{cx},110 {cx + 10},84 {x0 + 40 + i * 6},70", 0.9, stroke=FAINT))
    out.append(rect(x0 + 20, 60, 40 + n * 6, 12, 5, 1.4))  # plug lead tube
    # Exhaust: a stub per cylinder sweeping down into the manifold.
    for i in range(n):
        cx = x0 + pitch * (i + 0.5)
        out.append(path(f"M{cx - 5},210 C{cx - 5},236 {cx + 12},240 {cx + 24},246", 2.4))
    out.append(path(f"M{x0 + 10},246 L{x1 + 50},246 L{x1 + 104},252", 6, stroke="rgba(234,242,255,0.9)"))
    # Timing case and flywheel.
    out.append(path(f"M{x0},120 L{x0 - 36},128 L{x0 - 40},226 L{x0 - 20},248", 2))
    out.append(circle(x0 - 26, 176, 16, 1.4))
    out.append(path(f"M{x1 + 20},150 L{x1 + 34},150 L{x1 + 34},262 L{x1 + 10},262", 2))  # bell housing
    out.append(path(f"M{x1 + 34},164 A46,46 0 0 1 {x1 + 34},252", 2))  # flywheel rim
    out.append(circle(x1 + 34, 208, 7, 1.4))
    out.append(dimension(x0, x1, 322, f"{n} cylinders{' per bank' if vee else ''} in line"))
    # Front view inset: one tall cylinder, or two banks in a V.
    ix, iy = 512, 32
    if vee:
        def bank(sgn):
            return (f'<g transform="rotate({sgn * 30} {ix + 55} {iy + 112})">' + rect(ix + 45, iy + 34, 20, 66, 3, 1.4)
                    + rect(ix + 41, iy + 26, 28, 10, 3, 1.2) + "</g>")
        inset = bank(-1) + bank(1) + circle(ix + 55, iy + 112, 13, 1.4)
    else:
        inset = rect(ix + 44, iy + 32, 22, 68, 3, 1.4) + rect(ix + 40, iy + 24, 30, 10, 3, 1.2) + circle(ix + 55, iy + 112, 13, 1.4)
    out.append(callout(x0 + 120, 104, x0 + 150, 34, "twin overhead camshafts"))
    # Lifted clear of the dimension line and the title block.
    drawing = f'<g transform="translate(0,-18)">{"".join(out[:-2])}</g>' + "".join(out[-2:])
    return sheet(name.upper(), "Engine · side elevation", number, drawing,
                 inset_box(ix, iy, 110, 150, "FRONT", inset))


# ---------------------------------------------------------------- supercharging

def roots(cx, cy, scale=1.0, label=True):
    """A Roots blower in section: the casing and its two figure-of-eight rotors."""
    s = scale
    out = [path(f"M{cx - 70 * s},{cy - 50 * s} a{50 * s},{50 * s} 0 0 1 {50 * s},-{0}"
                f" L{cx + 20 * s},{cy - 50 * s} a{50 * s},{50 * s} 0 0 1 {0},{100 * s} L{cx - 20 * s},{cy + 50 * s}"
                f" a{50 * s},{50 * s} 0 0 1 0,-{100 * s} Z", 0.01)]
    # Casing: two overlapping circles' outer outline.
    r = 46 * s
    dx = 30 * s
    out = [path(f"M{cx - dx},{cy - r} L{cx + dx},{cy - r} A{r},{r} 0 0 1 {cx + dx},{cy + r} L{cx - dx},{cy + r} A{r},{r} 0 0 1 {cx - dx},{cy - r} Z", 2.2)]
    out.append(path(f"M{cx - dx - r - 8 * s},{cy - r - 8 * s} L{cx + dx + r + 8 * s},{cy - r - 8 * s} L{cx + dx + r + 8 * s},{cy + r + 8 * s} L{cx - dx - r - 8 * s},{cy + r + 8 * s} Z", 1.0, stroke=FAINT))
    for k, (ox, rot) in enumerate(((-dx, 0), (dx, 90))):
        lobe = 40 * s
        d = []
        for i in range(73):
            t = i / 72 * 2 * math.pi
            rr = lobe * (0.4 + 0.6 * abs(math.cos(t)) ** 2.4)
            a = t + math.radians(rot)
            d.append(f"{'M' if i == 0 else 'L'}{cx + ox + math.cos(a) * rr:.1f},{cy + math.sin(a) * rr:.1f}")
        out.append(path(" ".join(d) + " Z", 1.8))
        out.append(circle(cx + ox, cy, 6 * s, 1.4))
    if label:
        out.append(path(f"M{cx},{cy + r + 40 * s} L{cx},{cy + r + 12 * s}", 1.4) + path(f"M{cx - 5},{cy + r + 20 * s} L{cx},{cy + r + 12 * s} L{cx + 5},{cy + r + 20 * s}", 1.4))
        out.append(path(f"M{cx},{cy - r - 12 * s} L{cx},{cy - r - 40 * s}", 1.4) + path(f"M{cx - 5},{cy - r - 32 * s} L{cx},{cy - r - 40 * s} L{cx + 5},{cy - r - 32 * s}", 1.4))
    return "".join(out)


def carburettor(x, y):
    """An updraught carburettor: float chamber, body and bell-mouth."""
    return (rect(x, y, 34, 60, 4) + rect(x + 34, y + 14, 22, 30, 3, 1.4) + path(f"M{x + 4},{y + 60} L{x - 6},{y + 84} L{x + 40},{y + 84} L{x + 30},{y + 60}", 1.8)
            + line(x + 17, y - 26, x + 17, y, 3) + circle(x + 45, y + 29, 5, 1.2))


def supercharger(kind, number):
    out = []
    if kind == "none":
        # Inlet manifold along the block with two carburettors under it.
        out.append(rect(120, 120, 380, 60, 3, 1.4, stroke=FAINT, dash="6 4"))
        out.append(text(310, 156, "engine block", 17, "middle", FAINT))
        out.append(path("M150,180 L150,200 L470,200 L470,180", 4))
        for x in (200, 380):
            out.append(carburettor(x, 214))
        out.append(callout(234, 260, 110, 326, "twin updraught carburettors"))
        out.append(callout(470, 200, 488, 262, "inlet manifold"))
        return sheet("UNSUPERCHARGED", "Induction · elevation", number, "".join(out))
    if kind == "single":
        out.append(roots(250, 196, 1.3))
        out.append(text(250, 104, "to the engine", 17, "middle", FAINT))
        out.append(text(250, 312, "air and fuel from the carburettor", 17, "middle", FAINT))
        out.append(callout(211, 180, 90, 150, "rotor"))
        out.append(callout(340, 150, 440, 116, "Roots-type blower"))
        out.append(callout(310, 210, 440, 250, "geared to the crank"))
        return sheet("SUPERCHARGED", "Roots blower · section", number, "".join(out))
    # Two stages: a large first blower feeding a smaller second one.
    out.append(roots(190, 210, 1.05, label=False))
    out.append(roots(430, 170, 0.8, label=False))
    out.append(path("M190,152 C190,90 330,80 430,124", 8, stroke="rgba(234,242,255,0.85)"))
    out.append(path("M430,216 C430,260 480,290 540,300", 6, stroke="rgba(234,242,255,0.85)"))
    out.append(path("M190,330 L190,268", 1.4) + path("M185,276 L190,268 L195,276", 1.4))
    out.append(callout(300, 96, 300, 40, "first stage feeds the second"))
    out.append(text(200, 340, "air in", 17, "start", FAINT))
    out.append(callout(520, 290, 440, 236, "to the engine"))
    return sheet("TWO-STAGE", "Supercharging · section", number, "".join(out))


# ---------------------------------------------------------------- frames

def frame(kind, number):
    """Plan view of the chassis frame, front to the left."""
    out = []
    x0, x1 = 70, 560
    top, bot = 140, 260

    def rail(y, kick):
        return f"M{x0},{y} L{x0 + 120},{y} C{x0 + 160},{y} {x0 + 170},{y + kick} {x0 + 210},{y + kick} L{x1 - 40},{y + kick} L{x1},{y + kick}"
    if kind == "tubular":
        for y, kick in ((top, -8), (bot, 8)):
            out.append(path(rail(y - 5, kick), 2) + path(rail(y + 5, kick), 2))
        xs = [x0 + 30, x0 + 180, x0 + 320, x1 - 20]
        for x in xs:
            out.append(line(x - 5, top + 2, x - 5, bot - 2, 2) + line(x + 5, top + 2, x + 5, bot - 2, 2))
        for a, b in ((xs[1], xs[2]), (xs[2], xs[3])):
            out.append(line(a, top + 4, b, bot - 4, 1.6) + line(a, bot - 4, b, top + 4, 1.6))
        inset = circle(560, 82, 22, 2) + circle(560, 82, 17, 1.2)
        out.append(callout(x0 + 260, top - 3, x0 + 240, 70, "drawn steel tube"))
        out.append(callout(x0 + 390, 200, x0 + 420, 300, "cross-bracing"))
        sub = "Tubular frame · plan"
        title = "TUBULAR FRAME"
    else:
        for y, kick in ((top, -8), (bot, 8)):
            out.append(path(rail(y - 7, kick), 2) + path(rail(y + 7, kick), 2))
        for x in (x0 + 30, x0 + 200, x0 + 340, x1 - 20):
            out.append(rect(x - 6, top + 8, 12, bot - top - 16, 1, 1.8))
        if kind == "boxed":
            # Boxed side members: hatched section and lightening holes.
            for i in range(9):
                out.append(circle(x0 + 230 + i * 30, top - 8, 4, 1.2))
                out.append(circle(x0 + 230 + i * 30, bot + 8, 4, 1.2))
            inset = rect(538, 60, 44, 48, 0, 2) + hatch(540, 62, 40, 44) + rect(546, 68, 28, 32, 0, 1.4, fill="#1d4878")
            out.append(callout(x0 + 290, top - 8, x0 + 150, 70, "boxed side members, drilled"))
            sub = "Boxed frame · plan"
            title = "BOXED LADDER FRAME"
        else:
            inset = path("M542,62 L582,62 L582,68 L550,68 L550,102 L582,102 L582,108 L542,108 Z", 2) + hatch(542, 62, 8, 46, 5)
            out.append(callout(x0 + 290, top - 8, x0 + 150, 70, "pressed-steel channel side members"))
            sub = "Ladder frame · plan"
            title = "LADDER FRAME"
    # Engine and axle positions in ghost lines.
    out.append(rect(x0 + 50, 168, 170, 64, 6, 1.0, stroke=FAINT, dash="6 4"))
    out.append(text(x0 + 135, 205, "engine", 16, "middle", FAINT))
    for x in (x0 + 30, x1 - 60):
        out.append(line(x, 100, x, 300, 1.0, FAINT, "10 4 2 4"))
    out.append(dimension(x0 + 30, x1 - 60, 316, "wheelbase"))
    return sheet(title, sub, number, "".join(out), inset_box(520, 32, 80, 96, "SECTION", inset))


# ---------------------------------------------------------------- suspension

def suspension(kind, number):
    """Front elevation of the front axle (and for all-independent, the rear swing axles too)."""
    out = []
    cy = 230
    if kind == "beam":
        out.append(tyre_front(140, cy, 34, 140) + tyre_front(500, cy, 34, 140))
        out.append(path(f"M160,{cy} L200,{cy} L230,{cy + 18} L410,{cy + 18} L440,{cy} L480,{cy}", 6))
        for x in (230, 410):
            # Semi-elliptic leaf spring stack seen end on, with its shackle to the frame.
            for i in range(6):
                out.append(rect(x - 24 + i * 2, cy - 8 - i * 6, 48 - i * 4, 5, 2, 1.2))
            out.append(rect(x - 14, cy - 86, 28, 36, 0, 1.6) + hatch(x - 14, cy - 86, 28, 36, 6))
            out.append(line(x, cy - 50, x, cy - 42, 2))
        out.append(rect(170, cy - 120, 300, 18, 2, 1.0, stroke=FAINT, dash="6 4"))
        out.append(callout(320, cy + 18, 110, 330, "one solid axle beam"))
        out.append(callout(230, cy - 30, 70, 90, "leaf springs"))
        return sheet("BEAM AXLES", "Front axle · elevation", number, "".join(out))
    # Double wishbones with coil springs, each wheel on its own.
    def corner(xw, side, cyy, h=140):
        o = [tyre_front(xw, cyy, 34, h)]
        xi = xw + side * 22
        xf = 320 - side * 90
        o.append(path(f"M{xi},{cyy - 30} L{xf},{cyy - 44} M{xi},{cyy + 28} L{xf},{cyy + 34}", 3))
        o.append(line(xi, cyy - 34, xi, cyy + 32, 4))  # upright
        for y in (cyy - 44, cyy + 34):
            o.append(circle(xf, y, 4, 1.4))
        # Coil spring between the lower arm and the frame.
        sx = (xi + xf) / 2
        coils = " ".join(f"L{sx + (8 if i % 2 else -8)},{cyy + 26 - i * 9}" for i in range(1, 10))
        o.append(path(f"M{sx},{cyy + 30} {coils}", 1.6))
        return "".join(o)
    out.append(corner(140, 1, cy) + corner(500, -1, cy))
    out.append(rect(230, cy - 70, 180, 140, 4, 1.6) + hatch(230, cy - 70, 180, 140, 10))
    out.append(callout(190, cy - 37, 120, 80, "wishbones"))
    out.append(callout(200, cy + 8, 120, 340, "coil spring"))
    title, sub, inset = "INDEPENDENT FRONT", "Front axle · elevation", ""
    if kind == "all":
        # Rear swing axles in the inset: two half-shafts pivoting at the differential.
        ix, iy = 476, 30
        diff = circle(ix + 70, iy + 70, 14, 1.4)
        inset = inset_box(ix, iy, 140, 110, "REAR", diff + tyre_front(ix + 18, iy + 70, 14, 60) + tyre_front(ix + 122, iy + 70, 14, 60)
                          + line(ix + 26, iy + 76, ix + 58, iy + 70, 3) + line(ix + 114, iy + 76, ix + 82, iy + 70, 3))
        title, sub = "ALL-INDEPENDENT", "Swing axles at the rear"
    return sheet(title, sub, number, "".join(out), inset)


# ---------------------------------------------------------------- brakes

def brakes(kind, number):
    """A brake drum in section: shoes, linings, and how they are pushed apart."""
    cx, cy, r = 250, 200, 120
    out = [circle(cx, cy, r + 10, 1.2, stroke=FAINT), circle(cx, cy, r, 3)]
    for i in range(36):
        a = i * math.pi / 18
        out.append(line(cx + math.cos(a) * (r + 10), cy + math.sin(a) * (r + 10), cx + math.cos(a) * (r + 22), cy + math.sin(a) * (r + 22), 1.4))  # cooling fins
    # Two shoes with their linings.
    for side in (-1, 1):
        a0, a1 = math.radians(-70), math.radians(70)
        if side < 0:
            a0, a1 = math.radians(110), math.radians(250)
        p = []
        for i in range(25):
            a = a0 + (a1 - a0) * i / 24
            p.append(f"{'M' if i == 0 else 'L'}{cx + math.cos(a) * (r - 6):.1f},{cy + math.sin(a) * (r - 6):.1f}")
        for i in range(24, -1, -1):
            a = a0 + (a1 - a0) * i / 24
            p.append(f"L{cx + math.cos(a) * (r - 22):.1f},{cy + math.sin(a) * (r - 22):.1f}")
        out.append(path(" ".join(p) + " Z", 1.8))
        q = []
        for i in range(25):
            a = a0 + (a1 - a0) * i / 24
            q.append(f"{'M' if i == 0 else 'L'}{cx + math.cos(a) * (r - 30):.1f},{cy + math.sin(a) * (r - 30):.1f}")
        out.append(path(" ".join(q), 3))
    out.append(circle(cx, cy + 92, 7, 1.6))  # anchor pin
    out.append(path(f"M{cx - 60},{cy + 40} C{cx - 30},{cy + 70} {cx + 30},{cy + 70} {cx + 60},{cy + 40}", 1.4, stroke=FAINT))  # return spring
    out.append(circle(cx, cy, 26, 1.6) + circle(cx, cy, 8, 1.4))
    if kind == "mechanical":
        out.append(rect(cx - 12, cy - 112, 24, 18, 3, 1.8))  # cam
        out.append(path(f"M{cx},{cy - 103} L{cx + 160},{cy - 150} L{cx + 300},{cy - 150}", 2))
        out.append(path(f"M{cx + 300},{cy - 150} L{cx + 316},{cy - 100}", 2.4))
        out.append(circle(cx + 316, cy - 100, 5, 1.4))
        out.append(callout(cx, cy - 103, 40, 40, "cam pushes the shoes apart"))
        out.append(callout(cx + 230, cy - 150, 420, 130, "cables to the pedal"))
        return sheet("MECHANICAL BRAKES", "Brake drum · section", number, "".join(out))
    out.append(rect(cx - 26, cy - 112, 52, 18, 4, 1.8) + rect(cx - 30, cy - 108, 6, 10, 1, 1.4) + rect(cx + 24, cy - 108, 6, 10, 1, 1.4))
    out.append(path(f"M{cx},{cy - 112} C{cx},{cy - 160} {cx + 120},{cy - 160} {cx + 240},{cy - 150}", 1.6))
    out.append(rect(cx + 240, cy - 166, 80, 30, 5, 2) + rect(cx + 256, cy - 180, 30, 14, 3, 1.4))
    out.append(callout(cx + 4, cy - 104, 410, 230, "wheel cylinder"))
    out.append(callout(cx + 280, cy - 151, 420, 120, "master cylinder"))
    return sheet("HYDRAULIC BRAKES", "Brake drum · section", number, "".join(out))


# ---------------------------------------------------------------- bodies

def body(kind, number):
    """Side elevation of the whole car."""
    out = []
    ground = 300
    fw, rw, r = 170, 450, 52
    if kind == "open":
        r = 46
        out.append(path(f"M86,{ground - 52} C84,{ground - 96} 100,{ground - 106} 130,{ground - 106} L350,{ground - 108}"
                        f" C362,{ground - 136} 396,{ground - 138} 404,{ground - 110} L500,{ground - 98}"
                        f" C548,{ground - 92} 578,{ground - 74} 592,{ground - 52} L560,{ground - 40} L500,{ground - 36}"
                        f" L400,{ground - 34} L230,{ground - 32} L110,{ground - 34} Z", 2.4))
        out.append(rect(88, ground - 98, 14, 42, 4, 1.6))  # radiator grille
        for i in range(5):
            out.append(line(92 + i * 2, ground - 94, 92 + i * 2, ground - 60, 0.6, FAINT))
        out.append(path(f"M352,{ground - 110} C356,{ground - 128} 372,{ground - 134} 384,{ground - 134}", 1.6))  # screen
        out.append(circle(378, ground - 120, 12, 1.4, stroke=FAINT))  # steering wheel
        for i in range(8):
            out.append(line(170 + i * 18, ground - 100, 178 + i * 18, ground - 76, 1.0, FAINT))  # bonnet louvres
        out.append(wheel(fw, ground - r, r) + wheel(rw, ground - r, r))
        out.append(path(f"M{rw + 50},{ground - 46} L{rw + 120},{ground - 54}", 4))  # tail pipe
        out.append(callout(300, ground - 107, 310, 70, "narrow body, wheels in the air"))
        title, sub = "OPEN-WHEEL BODY", "Side elevation"
    else:
        out.append(path(f"M60,{ground - 52} C60,{ground - 120} 120,{ground - 150} 220,{ground - 150} L340,{ground - 150}"
                        f" C370,{ground - 186} 410,{ground - 188} 430,{ground - 156}"
                        f" C520,{ground - 150} 590,{ground - 110} 610,{ground - 60} L600,{ground - 24} L80,{ground - 24} Z", 2.4))
        out.append(path(f"M346,{ground - 150} C360,{ground - 172} 384,{ground - 178} 402,{ground - 172}", 1.6))  # cockpit
        out.append(path(f"M60,{ground - 76} C64,{ground - 86} 72,{ground - 92} 82,{ground - 94}", 1.4))  # air intake
        for cx in (fw, rw):
            # Spats cover the wheels: draw the wheels as hidden lines.
            out.append(circle(cx, ground - r, r, 1.2, stroke=FAINT, dash="6 4"))
            out.append(path(f"M{cx - r - 10},{ground - 24} C{cx - r - 6},{ground - 70} {cx + r + 6},{ground - 70} {cx + r + 10},{ground - 24}", 1.4))
        out.append(path(f"M480,{ground - 140} L520,{ground - 184} L540,{ground - 182} L532,{ground - 122}", 1.8))  # tail fin
        for i in range(12):
            out.append(line(100 + i * 40, ground - 155 + abs(6 - i) * 3, 80 + i * 40, ground - 175 + abs(6 - i) * 3, 0.8, FAINT))  # airflow
        out.append(callout(300, ground - 150, 230, 70, "enclosed wheels, smooth tail"))
        title, sub = "STREAMLINED BODY", "Streamliner · elevation"
    out.append(line(40, ground, 600, ground, 1.0, FAINT))
    out.append(dimension(fw, rw, ground + 20, "wheelbase"))
    return sheet(title, sub, number, "".join(out))


# ---------------------------------------------------------------- output

DRAWINGS = {
    "engine-straight6": lambda: engine(6, False, "Straight-6", "E-6"),
    "engine-straight8": lambda: engine(8, False, "Straight-8", "E-8"),
    "engine-v8": lambda: engine(4, True, "V8", "E-V8"),
    "engine-v12": lambda: engine(6, True, "V12", "E-V12"),
    "engine-v16": lambda: engine(8, True, "V16", "E-V16"),
    "supercharging-none": lambda: supercharger("none", "S-0"),
    "supercharging-single": lambda: supercharger("single", "S-1"),
    "supercharging-twostage": lambda: supercharger("two", "S-2"),
    "frame-ladder": lambda: frame("ladder", "F-1"),
    "frame-boxedladder": lambda: frame("boxed", "F-2"),
    "frame-tubular": lambda: frame("tubular", "F-3"),
    "suspension-beamaxles": lambda: suspension("beam", "R-1"),
    "suspension-independentfront": lambda: suspension("front", "R-2"),
    "suspension-allindependent": lambda: suspension("all", "R-3"),
    "brakes-mechanical": lambda: brakes("mechanical", "B-1"),
    "brakes-hydraulic": lambda: brakes("hydraulic", "B-2"),
    "body-openwheel": lambda: body("open", "C-1"),
    "body-streamlined": lambda: body("stream", "C-2"),
}


def main():
    global THUMB
    out_dir = sys.argv[1] if len(sys.argv) > 1 else "game/art/parts"
    only = sys.argv[2:]
    render = os.path.join(HERE, "render.js")
    # Chromium draws the SVGs (with the embedded lettering font) to PNG: the sheets at twice their
    # size, the thumbnails at their own.
    for thumb, svg_dir, png_dir, scale in ((False, "svg", out_dir, 2), (True, "svg-thumbs", os.path.join(out_dir, "thumbs"), 1)):
        THUMB = thumb
        svg_dir = os.path.join(HERE, svg_dir)
        os.makedirs(svg_dir, exist_ok=True)
        for name, draw in DRAWINGS.items():
            if not only or name in only:
                open(os.path.join(svg_dir, name + ".svg"), "w").write(draw())
        os.system(f'node "{render}" "{svg_dir}" "{png_dir}" {W} {H} {scale}')


if __name__ == "__main__":
    main()
