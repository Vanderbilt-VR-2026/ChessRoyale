# Sprint writeups

The [project README](README.md) describes the game as it is now. Each sprint's notes are here.

## Sprint 1

Sprint 1 put a player on a Quest inside an oversized board and proved the pieces could be grabbed. The milestone notes from [PR #7](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/7) are kept here. That pull request's APK is not on this branch.

### Scene and pieces

The player stands in the middle of the board. All 32 pieces are human-scale Chess Mega Set models. The scene uses the 3D Sci-Fi Kit arena and the project lighting. [SETUP.md](SETUP.md) covers the Unity version, asset import, and a Quest build.

Included: Unity XR, OpenXR, and Quest controller profiles. Near grab, a distance ray, square snapping, and a board reset.

Still open at the end of the sprint: a checkered board, legal chess, and multiplayer.

### Demo

[Watch the Sprint 1 VR demo](https://github.com/Vanderbilt-VR-2026/ChessRoyale/blob/e1083a7cb03dbb5c3c5eaa15d480841bae2802ed/sprint1.mov).

### In progress

The arena presentation was the placeholder court.

### Next

Replace the arena with a clean board and add turns, legal moves, and the rest of the rules.

## Sprint 2

Sprint 2 made the board a real chess game and gave the player a hall to stand in. Pieces stay on squares, only legal moves count, and the player can walk the board.

### Board and rules

The player grabs a piece and drops it on a glowing square. Illegal drops go back.

Included: 64 separate squares, stable placement, turns, captures, hints, check, checkmate, stalemate, castling, en passant, promotion, and the draw rules.

Still open at the end of the sprint: an online opponent.

### Hall, clock, and menu

The player starts from a VR menu and can teleport across the board without leaving it. A chess clock is in the scene.

Included: the hall, lighting, board confinement, the main menu, and teleportation limited to the board.

### Demo

[Watch the Sprint 2 demo video](https://github.com/Vanderbilt-VR-2026/ChessRoyale/releases/tag/sprint-2-demo).

### Contribution log

| Team member | Work completed this sprint |
|---|---|
| RuoHan Chen | Migrated and populated the GitHub Kanban board; authored the product design requirements; built stable VR piece placement and snapping; implemented legal moves, turns, captures, move hints, complete chess rules, special moves, promotion, and correct board orientation. |
| Derrick Seo | Worked on the blender and soundtracks. |
| Sarina Liu | Added Background and Timer. Adjusted the lighting. Recorded the demo. |
| Feiyuan Yang | Added VR teleportation limited to the board so players can move around without falling off; rebuilt the VR main menu (START/QUIT) with controller-ray interaction on the new board and set it as the first build scene; merged the main menu and teleportation onto the complete-rules stack; tested teleport boundaries and the main menu on Meta Quest 3. |

### Pull requests

1. [Checkered board and clean environment](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/17)
2. [Stable VR chess-piece placement](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/23)
3. [Legal moves, turns, hints, and captures](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/24)
4. [Complete chess rules and promotion](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/25)
5. [Sprint 2 documentation and contribution log](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/26)

### In progress

Online play was not in this sprint. The clock ran as a local scene clock.

### Next

Connect two headsets through a room code and keep one shared board.

## Sprint 3

Sprint 3 splits local chess from online chess and gives every screen a way back to the lobby. The Quest build is [chessRoyale.apk](chessRoyale.apk).

### Design

#### Start screen

The first thing the player sees is the hall, standing at an empty board, with a floating Chess Royale panel. Starting in the room says this is a calm, formal match. Next they choose Single Player or Multiplayer, then a time control. The pieces come onto the board and the clock starts.

#### Interactions

Pieces still use the toolkit's default Near-Far interactor, so a hand can grab a piece up close or with a ray from far away. Direct grab is the better match, because the board is within reach and a far ray on a crowded board picks up the wrong piece. The ray stays for menus only.

Hands replace the controller models. Menus and the result screen use pointer hands with a ray. During the match the hands are gloves and the ray is hidden.

### Rooms

From Multiplayer the player chooses Join or Create. Create shows a four-letter code. Join types that code on the panel keyboard. A code that is not found stays on the join screen.

Included: four-letter codes, the in-panel A–Z keyboard, and Lobby from the wait, the match, and the join screen.

Still open: a time-control choice before the clock starts.

### Clock and the end of the match

The clock is hidden until both players are seated. When the game ends the panel says You Win, You Lose, or Draw. Rematch starts only after both agree. Return to Lobby goes back to Join or Create.

Included: a shared clock, win and loss on time, and a disconnect forfeit after the match has started.

Still open: choosing 5 minutes, 10 minutes, or untimed from the start screen.

### Team contributions

| Team Member | Contributions |
|---|---|
| RuoHan Chen | Implemented multiplayer networking using Photon Fusion, including creating and joining game rooms, and worked on networking fixes and testing. |
| Derrick Seo | Worked on background music, gameplay and UI sound effects, and product-sense documentation. |
| Sarina Liu | Worked on multiplayer networking testing and troubleshooting and updated the GitHub project board. |
| Feiyuan Yang | Updated Sprint 3 documentation and helped test and troubleshoot multiplayer networking. |

### In progress

Direct grab for pieces, with the ray kept for menus. Hands that switch between a pointer and a glove as the screen changes. A time-control choice on the start screen.

### Next

Limit the ray to menus, add the time presets, and keep the shared match in step when a player drops briefly and comes back.
