# Sprint 2

Sprint 2 made the board a real chess game and gave the player a hall to stand in. Pieces stay on squares, only legal moves count, and the player can walk the board.

The other sprint notes are [Sprint 1](SPRINT1.md) and [Sprint 3](SPRINT3.md). The [project README](../README.md) describes the game as it is now.

## Board and rules

The player grabs a piece and drops it on a glowing square. Illegal drops go back.

Included: 64 separate squares, stable placement, turns, captures, hints, check, checkmate, stalemate, castling, en passant, promotion, and the draw rules.

Still open at the end of the sprint: an online opponent.

## Hall, clock, and menu

The player starts from a VR menu and can teleport across the board without leaving it. A chess clock is in the scene.

Included: the hall, lighting, board confinement, the main menu, and teleportation limited to the board.

## Demo

[Watch the Sprint 2 demo video](https://github.com/Vanderbilt-VR-2026/ChessRoyale/releases/tag/sprint-2-demo).

## Contribution log

| Team member | Work completed this sprint |
|---|---|
| RuoHan Chen | Migrated and populated the GitHub Kanban board; authored the product design requirements; built stable VR piece placement and snapping; implemented legal moves, turns, captures, move hints, complete chess rules, special moves, promotion, and correct board orientation. |
| Derrick Seo | Worked on the blender and soundtracks. |
| Sarina Liu | Added Background and Timer. Adjusted the lighting. Recorded the demo. |
| Feiyuan Yang | Added VR teleportation limited to the board so players can move around without falling off; rebuilt the VR main menu (START/QUIT) with controller-ray interaction on the new board and set it as the first build scene; merged the main menu and teleportation onto the complete-rules stack; tested teleport boundaries and the main menu on Meta Quest 3. |

## Pull requests

1. [Checkered board and clean environment](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/17)
2. [Stable VR chess-piece placement](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/23)
3. [Legal moves, turns, hints, and captures](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/24)
4. [Complete chess rules and promotion](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/25)
5. [Sprint 2 documentation and contribution log](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/26)

## In progress

Online play was not in this sprint. The clock ran as a local scene clock.

## Next

Connect two headsets through a room code and keep one shared board.
