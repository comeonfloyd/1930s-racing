# 1930s Racing Manager

A desktop management game set in 1930s Grand Prix and Indianapolis racing. You take over a real team (with lightly renamed brands, such as Alfa Rossa and Bugetti), develop its cars and drivers, and choose which races on a map of Europe and the USA are worth the trip.

The design plan lives in [docs/design.md](docs/design.md).

## Layout

| Path | What it is |
| --- | --- |
| `src/RacingSim/` | Game rules and simulation in plain C# with no engine code, so it can be unit tested and run headless |
| `tests/RacingSim.Tests/` | xUnit tests for the sim core |
| `tools/RaceRunner/` | Console app that runs one race headless and prints the result and a lap chart |
| `tools/world-map/` | Paints the sepia travel map (`game/art/map.jpg`) from Natural Earth coastlines |
| `tools/SeasonRunner/` | Console app that plays many seasons headless to check the money balance |
| `tools/track-art/` | Scripts that paint the ground picture for each circuit and draw the car sprite (`game/art/`) |
| `tools/track-data/` | Real map data (OpenStreetMap) and the scripts that turn it into the circuit drawings |
| `game/` | The Godot 4 project (scenes, scripts) |
| `game/data/` | World data as JSON: circuits, team bases, drivers, teams, and one file per season |

## Running

You need the [.NET 8 SDK](https://dotnet.microsoft.com/download) and [Godot 4.3 .NET edition](https://godotengine.org/download).

- Tests: `dotnet test` from the repo root.
- One race in the terminal: `dotnet run --project tools/RaceRunner -- 1930-monza 42` (event id, seed; add `--no-fatal` to turn fatal crashes into injuries).
- Season balance: `dotnet run --project tools/SeasonRunner -- alfa-rossa 300 --develop` (team, number of seasons; `--develop` also orders car upgrades).
- Game: open the `game/` folder in Godot and press Play. The first screen lets you pick Alfa Rossa or Bugetti and enter 1930 races; it shows travel time and cost, and refuses entries the cars cannot reach in time.

When exporting, add `data/*` to the export preset's "Filters to export non-resource files" so the JSON files ship with the game.

## Data notes

Historical dates, lap counts, lap lengths and coordinates are approximate and still need checking against period sources. Money, prestige and driver skills are placeholder game values. Team names are deliberately altered from the real makers' trademarks and can be edited in `game/data/teams.json`.

## Credits

Circuit outlines: map data © OpenStreetMap contributors (ODbL); Monaco outline from bacinger/f1-circuits (MIT). See `tools/track-data/README.md`.

Travel map coastlines, lakes and rivers: Natural Earth (public domain), drawn by `tools/world-map/paint.py`.
