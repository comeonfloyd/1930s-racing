# Track data

`game/data/tracks/*.json` are generated from real map data. Don't hand-edit them; change `build.py` and rerun:

    python3 tools/track-data/build.py            # every circuit
    python3 tools/track-data/build.py spa        # just one

- `osm/` holds raw Overpass API responses (`out geom`) for each circuit. © OpenStreetMap contributors, available under the [ODbL](https://opendatacommons.org/licenses/odbl/). The game shows that credit under the track drawing.
- `f1-circuits/monaco.geojson` is from [bacinger/f1-circuits](https://github.com/bacinger/f1-circuits) (MIT).
- `build.py` joins the ways into a graph and routes each 1930s lap through a list of waypoints in driving order. Old roads that later chicanes cut are bridged with explicit `links`.
- `convert.py` projects the loop to metres, puts the start line first, fixes the direction of travel, evens out the points and fits it north-up into the 1.4:1 drawing box.

What each lap follows:

| Circuit | Layout |
|---|---|
| Monaco | Today's outline with the 1973 pool section replaced by the run down the quay to the Gasometer hairpin |
| Montlhéry | The eastern half of the banked bowl plus the road loop to the Bruyères hairpin |
| Monza | Road course without the chicanes; the south curve is today's Parabolica |
| Nürburgring | Nordschleife from T13; the 1930s start loop through today's GP area is simplified |
| Spa | The old public-road triangle: Les Combes, Burnenville, Malmedy, Masta, Stavelot, Blanchimont |
| Indianapolis | The oval, anticlockwise |
| Reims | Before 1952, through Gueux village: drawn straight between the corners on the OpenStreetMap roads |
| Pescara | Routed on today's roads: Spoltore, Cappelle sul Tavo, the Montesilvano straight, the coast road |
| AVUS | One motorway carriageway, with the return leg set further apart than it is so both show |
| Bremgarten | Drawn through points in the Bremgartenwald; the forest tracks in OpenStreetMap do not join up |
| Tripoli | Drawn round the Mellaha lake, where Mitiga airport is now |
| Brooklands | Drawn along the outer circuit's known course; almost none of it is mapped |
| Le Mans | The 1932 Sarthe lap on today's roads; the straight from Maison Blanche to the pits bridges the Ford chicane |

The `osm/` files for the newer circuits are trimmed to road ways with only their `highway` and `name` tags. Overpass now refuses requests without a User-Agent header, so send one when fetching.

Lap lengths for the simulation stay in `game/data/circuits.json` (period figures), not from these drawings.
