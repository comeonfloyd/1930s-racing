# Design summary

The full, living design plan is a shared doc: https://claude.ai/artifact/YZFxuTnqVJoJ4RC5rzD8WL. This file records the decisions it is built on, as of 26 Oct 2026.

## Decisions

| Topic | Decision |
| --- | --- |
| Platform | Desktop, Godot 4 with C# (Windows, macOS, Linux) |
| Drivers and events | Real names |
| Team and maker brands | Slightly changed but recognisable: Alfa Rossa (Alfa Romeo), Bugetti (Bugatti), Masarelli (Maserati), Mercator-Benz (Mercedes-Benz), Auto Unitas (Auto Union) |
| Danger | Drivers can die; fatal crashes come from the sim, never scripted; a setting turns them into career-ending injuries |
| Race view | 3D view of each circuit (painted ground, trees, houses and stands stood up as models, procedural 3D cars, newsreel camera that drops behind the followed car), with the flat painted map one button away |
| Other art | AI-generated posters, portraits and backgrounds (needs Steam's AI-content disclosure) |
| MVP span | Full career, 1930 to 1939, all three rule eras |
| Player's team | Take over a real team; Alfa Rossa and Bugetti first. A fictional own team comes after the MVP |
| Release | Personal project; aim for Steam if it turns out well |

## MVP venues

Monaco, Montlhéry, Monza, Nürburgring, Spa-Francorchamps and Indianapolis first, then Reims, Pescara, AVUS, Bremgarten, Tripoli and Brooklands.

## Milestones

1. Skeleton: Godot 4 C# project, data for 6 circuits and the 1930 calendar, a calendar screen. *(done)*
2. Race sim headless: one race, the full grid, lap chart output. *(done)*
3. Animated race view for one circuit (Monza), driven by the sim. *(in progress)*
4. Team and car building screens, R&D queue, money, travel map with entry choice; 1930 season playable.
5. Seasons 1931 to 1939: rule changes, driver careers, deaths, works teams; the other 6 MVP venues.
6. Balance pass and playtest.
