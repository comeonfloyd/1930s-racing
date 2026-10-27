#!/usr/bin/env python3
"""
Turns the chosen Commons photos (fetched by fetch.py) into the game's pictures: cropped to the
card's shape, toned to one sepia so photos from different archives sit together, and saved to
game/art/photos/<kind>/<id>.jpg. The author and licence of each go to
game/data/photo-credits.json, which the game's Picture credits list reads.

    python3 tools/photos/pick.py <fetch out dir> tools/photos/choices.json
    python3 tools/photos/pick.py <fetch out dir> --sheet <kind> sheet.jpg   # contact sheet to choose from

choices.json: {"drivers": {"tazio-nuvolari": 0, ...}, "cars": {...}, ...}: the candidate number
for each, as numbered on the contact sheet. An optional "focus" maps "<kind>/<id>" to the
vertical centre of the crop (0 top, 1 bottom) for photos where the face is not near the top.
"""
import json
import os
import sys

from PIL import Image, ImageDraw, ImageOps

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
# Portraits are 4:5, cars and circuits 8:5, like the cards that show them.
SIZES = {"drivers": (240, 300), "staff": (240, 300), "cars": (480, 300), "circuits": (480, 300)}
DARK, LIGHT = (38, 28, 18), (243, 230, 200)


def tone(img):
    """Greyscale, contrast stretched, then mapped from dark brown to cream."""
    grey = ImageOps.autocontrast(img.convert("L"), cutoff=1)
    return ImageOps.colorize(grey, DARK, LIGHT, mid=(150, 122, 88))


def crop(img, size, focus):
    w, h = size
    scale = max(w / img.width, h / img.height)
    img = img.resize((max(w, round(img.width * scale)), max(h, round(img.height * scale))), Image.LANCZOS)
    x = (img.width - w) // 2
    y = int(min(max(focus * img.height - h / 2, 0), img.height - h))
    return img.crop((x, y, x + w, y + h))


def sheet(out_dir, kind, path):
    manifest = json.load(open(os.path.join(out_dir, "manifest.json")))
    rows = [(key, files) for key, files in sorted(manifest.get(kind, {}).items()) if files]
    cell = 150
    cols = max(len(f) for _, f in rows) if rows else 1
    board = Image.new("RGB", (220 + cols * cell, len(rows) * cell), "white")
    draw = ImageDraw.Draw(board)
    for r, (key, files) in enumerate(rows):
        draw.text((6, r * cell + 6), key, fill="black")
        for c, f in enumerate(files):
            try:
                im = Image.open(os.path.join(out_dir, f["file"])).convert("RGB")
            except OSError:
                continue
            im.thumbnail((cell - 8, cell - 22))
            board.paste(im, (220 + c * cell, r * cell + 4))
            draw.text((220 + c * cell, r * cell + cell - 16), f"{c} {f.get('date', '')[:10]}", fill="black")
    board.save(path, quality=80)


def main():
    out_dir = sys.argv[1]
    if sys.argv[2] == "--sheet":
        sheet(out_dir, sys.argv[3], sys.argv[4])
        return
    choices = json.load(open(sys.argv[2]))
    manifest = json.load(open(os.path.join(out_dir, "manifest.json")))
    focus = choices.get("focus", {})
    credits = []
    for kind, picks in choices.items():
        if kind not in SIZES:
            continue
        dest = os.path.join(ROOT, "game", "art", "photos", kind)
        os.makedirs(dest, exist_ok=True)
        for key, index in sorted(picks.items()):
            f = manifest[kind][key][index]
            img = Image.open(os.path.join(out_dir, f["file"])).convert("RGB")
            default = 0.38 if kind in ("drivers", "staff") else 0.5
            crop(tone(img), SIZES[kind], focus.get(f"{kind}/{key}", default)).save(os.path.join(dest, key + ".jpg"), quality=86)
            credits.append({
                "picture": f"{kind}/{key}", "title": f["title"].removeprefix("File:"), "author": f["artist"] or "Unknown",
                "licence": f["license"], "source": f["page"],
            })
    with open(os.path.join(ROOT, "game", "data", "photo-credits.json"), "w") as w:
        json.dump(credits, w, indent=1, ensure_ascii=False)
    print(len(credits), "pictures")


if __name__ == "__main__":
    main()
