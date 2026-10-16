# Chess Royale

Chess Royale is a Meta Quest VR chess prototype built with Unity and OpenXR. The current scene places the player in the middle of a large black-and-white board with human-sized chess pieces that can be grabbed nearby or dragged from a distance.

Project history: [Sprint 1 summary and VR demo](SPRINT1.md).

## Current state

The current Sprint 2 stack includes:

- A clean board made from 64 independent `ChessBoardSquare` objects.
- Human-sized chess pieces using the Chess Mega Set asset models.
- Near grabbing with the controller grip and distance dragging with the controller ray.
- Upright piece handling, board-elevation recovery, and automatic square-center snapping.
- Turn enforcement, legal-move validation, captures, and king-safety checks.
- Boundary-glow hints around valid destination squares.
- Check, checkmate, stalemate, repetition, fifty-move, and insufficient-material draws.
- Castling, en passant, and in-world pawn-promotion selection.

This stack currently builds directly into `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity`. A start screen, end screen, sound, teleportation, and networked multiplayer are not part of this branch stack yet.

## VR controls

| Action | Input |
|---|---|
| Grab a nearby piece | Hold **Grip** near the piece |
| Drag a piece from a distance | Point the controller ray and hold **Trigger** |
| Place a piece | Release over a valid square |
| View possible moves | Select or grab a piece to show glowing square boundaries |
| Choose a promotion piece | Point at the in-world promotion option and select it |
| Reset the board | Select the reset control in the scene |

Invalid moves are rejected and return the piece to its previous square. Captured pieces are removed without physically knocking other pieces over.

## Setup and Quest build

See [SETUP.md](SETUP.md) for the full environment, Asset Store import, Unity scene-generation, editor-testing, and Meta Quest build instructions.

Quick start:

1. Install Git LFS and clone the repository.
2. Open the project with Unity **6000.3.23f1** and Android Build Support.
3. Import the required Asset Store packages through **Window > Package Management > My Assets**.
4. Choose **Tools > Chess Royale > Build VR Scene**.
5. Open `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity` and test it in Play Mode.
6. Use **File > Build Profiles > Android > Build and Run** to install it on a developer-enabled Meta Quest.

The separately licensed Asset Store source folders and generated Unity/build directories are intentionally excluded from Git.

## Team roles

| Team member | Major | Role |
|---|---|---|
| RuoHan Chen | CS | GitHub Manager |
| Derrick Seo | CS | Communications Director |
| Sarina Liu | CS | Design Director |
| Feiyuan Yang | CS | Asset Manager |

## Sprint 2 contribution log

| Team member | Work completed this sprint |
|---|---|
| RuoHan Chen | Migrated and populated the GitHub Kanban board; authored the product design requirements; built stable VR piece placement and snapping; implemented legal moves, turns, captures, move hints, complete chess rules, special moves, promotion, and correct board orientation. |
| Derrick Seo | |
| Sarina Liu | |
| Feiyuan Yang | Added VR teleportation limited to the board so players can move around without falling off; rebuilt the VR main menu (START/QUIT) with controller-ray interaction on the new board and set it as the first build scene; merged the main menu and teleportation onto the complete-rules stack; tested teleport boundaries and the main menu on Meta Quest 3. |

## AI usage

OpenAI ChatGPT and Codex were used under human direction to help:

- organize product requirements and the GitHub Kanban board;
- prototype and refine Unity C# gameplay code;
- investigate editor, build, and VR interaction issues;
- prepare documentation and organize the stacked pull requests.

AI-generated suggestions are reviewed through the pull-request workflow and should be black-box tested in Unity and on Meta Quest before merge. AI is used as a development aid; it is not an in-game feature and does not make chess moves for the player.

Claude was also used as a step-by-step guide for configuring the XR Interaction Toolkit (world-space menu UI, controller ray interaction, teleportation areas and layers), explaining Git branching and merge-conflict resolution, troubleshooting Quest USB debugging, and drafting PR and README text. Start and end screen layouts were designed by the team, and AI helped write their code. All changes were configured, tested on Meta Quest 3, and debugged by the team.

## Pull-request stack

Review the current work in dependency order:

1. [Checkered board and clean environment](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/17)
2. [Stable VR chess-piece placement](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/23)
3. [Legal moves, turns, hints, and captures](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/24)
4. [Complete chess rules and promotion](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/25)
5. [Sprint 2 documentation and contribution log](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/26)
