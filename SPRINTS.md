# Sprint writeups

The [project README](README.md) describes the game as it is now. Each sprint's notes are here.

## Sprint 1

Sprint 1 established the first playable Meta Quest version of Chess Royale. This preserves the milestone documentation from [PR #7](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/7) without carrying its APK into the current branch.

### Goal

Build the Unity and OpenXR foundation, place the player inside an oversized chess environment, and demonstrate two VR interaction methods.

### Completed work

- Configured Unity XR, OpenXR, and Quest controller support.
- Created a playable scene with the user positioned in the middle of an oversized chess board.
- Added all 32 human-scale chess pieces using the Chess Mega Set models.
- Added near grabbing, distance ray dragging, square snapping, and a board-reset control.
- Built the original scene inside a 3D Sci-Fi Kit arena with project lighting.
- Added [SETUP.md](SETUP.md) with Unity, asset-import, editor-testing, and Quest build instructions.

### Demo

[Watch the Sprint 1 VR demo](https://github.com/Vanderbilt-VR-2026/ChessRoyale/blob/e1083a7cb03dbb5c3c5eaa15d480841bae2802ed/sprint1.mov).

### What changed after Sprint 1

Sprint 2 replaced the original arena presentation with a clean black-and-white board made from 64 independent squares. It also added stable piece placement, turn enforcement, legal moves, move hints, captures, complete chess outcomes, castling, en passant, and pawn promotion.

## Sprint 2

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

## Sprint 3

The Quest build for this sprint is [chessRoyale.apk](chessRoyale.apk).

### Design

#### Start screen

The first thing the player sees is the hall, standing at an empty board, with a floating Chess Royale panel. Starting in the room says this is a calm, formal match. Next they choose Single Player or Multiplayer, then a time control. The pieces come onto the board and the clock starts.

#### Interactions

Pieces still use the toolkit's default Near-Far interactor, so a hand can grab a piece up close or with a ray from far away. Direct grab is the better match, because the board is within reach and a far ray on a crowded board picks up the wrong piece. The ray stays for menus only.

Hands replace the controller models. Menus and the result screen use pointer hands with a ray. During the match the hands are gloves and the ray is hidden.

#### Networking

Single Player is local. Multiplayer is online through Photon. Two players join a room and share one hall, the moves, and the chess clock.
