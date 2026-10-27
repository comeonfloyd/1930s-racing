#!/usr/bin/env python3
"""
Collects candidate period photos from Wikimedia Commons for the game's menus: drivers, staff,
cars and circuits. Only freely licensed files are kept (public domain, CC0, CC BY, CC BY-SA),
with author, licence and date written to manifest.json for the credits screen.

Commons is blocked from the cloud container, so this runs on Alex's Mac:
    python3 tools/photos/fetch.py queries.json out/
queries.json: {"drivers": {id: name}, "staff": {id: name}, "cars": [name], "circuits": {id: [query]}}
Thumbnails (640 px wide) go to out/<kind>/<id>/<n>.jpg. Pick the keepers with pick.py.
"""
import html
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

UA = "1930s-racing-art-tools/1.0 (personal game project; apersidskiy@icloud.com)"
COMMONS = "https://commons.wikimedia.org/w/api.php"
WIKIDATA = "https://www.wikidata.org/w/api.php"
FREE = re.compile(r"^(public domain|pd|cc0|cc[- ]by([- ]sa)?[- ]?\d)", re.I)
# Altered brands back to the real makers, for searching.
REAL = {"Alfa Rossa": "Alfa Romeo", "Bugetti": "Bugatti", "Masarelli": "Maserati", "Mercator-Benz": "Mercedes-Benz",
        "Auto Unitas": "Auto Union", "Millard": "Miller", "Duesenhoff": "Duesenberg"}


def get(url, params):
    url = url + "?" + urllib.parse.urlencode({**params, "format": "json"})
    for attempt in range(4):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=30) as r:
                time.sleep(0.15)
                return json.load(r)
        except Exception as e:  # noqa: BLE001 - retry anything, give up after a few
            print("  retry", e, file=sys.stderr)
            time.sleep(2 ** attempt)
    return {}


def wikidata_images(name):
    found = get(WIKIDATA, {"action": "wbsearchentities", "search": name, "language": "en", "limit": 5, "type": "item"})
    for hit in found.get("search", []):
        ent = get(WIKIDATA, {"action": "wbgetentities", "ids": hit["id"], "props": "claims"})
        claims = ent.get("entities", {}).get(hit["id"], {}).get("claims", {})
        human = any(c["mainsnak"].get("datavalue", {}).get("value", {}).get("id") == "Q5" for c in claims.get("P31", []))
        if human:
            return ["File:" + c["mainsnak"]["datavalue"]["value"] for c in claims.get("P18", []) if "datavalue" in c["mainsnak"]]
    return []


def commons_search(query, limit):
    found = get(COMMONS, {"action": "query", "list": "search", "srsearch": query + " filetype:bitmap", "srnamespace": 6, "srlimit": limit})
    return [h["title"] for h in found.get("query", {}).get("search", [])]


def clean(text):
    return html.unescape(re.sub(r"<[^>]+>", "", text or "")).strip()


def info(titles):
    out = {}
    for i in range(0, len(titles), 20):
        res = get(COMMONS, {"action": "query", "titles": "|".join(titles[i:i + 20]), "prop": "imageinfo",
                            "iiprop": "url|extmetadata|size", "iiurlwidth": 640})
        for page in res.get("query", {}).get("pages", {}).values():
            ii = (page.get("imageinfo") or [None])[0]
            if not ii:
                continue
            meta = ii.get("extmetadata", {})
            m = lambda k: clean(meta.get(k, {}).get("value", ""))  # noqa: E731
            out[page["title"]] = {
                "title": page["title"], "page": ii.get("descriptionurl"), "thumb": ii.get("thumburl"),
                "width": ii.get("width"), "height": ii.get("height"),
                "license": m("LicenseShortName"), "artist": m("Artist"), "credit": m("Credit"),
                "date": m("DateTimeOriginal"), "description": m("ImageDescription")[:300],
            }
    return out


def collect(kind, key, titles, out_dir, manifest):
    titles = list(dict.fromkeys(titles))
    files = []
    for n, f in enumerate(v for v in info(titles).values() if FREE.match(v["license"] or "") and v["thumb"]):
        path = f"{kind}/{key}/{n}.jpg"
        os.makedirs(os.path.join(out_dir, kind, key), exist_ok=True)
        try:
            req = urllib.request.Request(f["thumb"], headers={"User-Agent": UA})
            with urllib.request.urlopen(req, timeout=60) as r, open(os.path.join(out_dir, path), "wb") as w:
                w.write(r.read())
            time.sleep(0.1)
        except Exception as e:  # noqa: BLE001
            print("  skip", f["title"], e, file=sys.stderr)
            continue
        files.append({**f, "file": path})
    manifest.setdefault(kind, {})[key] = files
    print(f"{kind}/{key}: {len(files)} of {len(titles)}")


def main():
    queries = json.load(open(sys.argv[1]))
    out_dir = sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    mpath = os.path.join(out_dir, "manifest.json")
    manifest = json.load(open(mpath)) if os.path.exists(mpath) else {}
    jobs = []
    for kind in ("drivers", "staff"):
        for key, name in queries.get(kind, {}).items():
            jobs.append((kind, key, lambda name=name: wikidata_images(name) + commons_search(f'"{name}"', 5)))
    for name in queries.get("cars", []):
        real = name
        for alt, r in REAL.items():
            real = real.replace(alt, r)
        key = re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")
        jobs.append(("cars", key, lambda real=real: commons_search(real + " 193", 6) + commons_search(real, 4)))
    for key, qs in queries.get("circuits", {}).items():
        jobs.append(("circuits", key, lambda qs=qs: [t for q in qs for t in commons_search(q, 5)]))
    for kind, key, titles in jobs:
        if key in manifest.get(kind, {}):
            continue
        collect(kind, key, titles(), out_dir, manifest)
        json.dump(manifest, open(mpath, "w"), indent=1, ensure_ascii=False)


if __name__ == "__main__":
    main()
