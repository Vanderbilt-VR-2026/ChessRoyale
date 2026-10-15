# ChessRoyale

ChessRoyale is a VR chess game for Meta Quest built in Unity. Players stand on a giant chessboard with human-sized pieces. They pick up and move pieces with the Quest controllers and play a full game of chess against an opponent.


## Team Roles

| Team Member | Major(s) | Role |
|---|---|---|
| RuoHan Chen | CS | GitHub Manager |
| Derrick Seo | CS | Communications Director |
| Sarina Liu | CS | Design Director |
| Feiyuan Yang | CS | Asset Manager |

## Getting Started

1. Open the project in Unity 6.
2. Open `Assets/ChessRoyale/Scenes/MainMenu.unity`.
3. In **File → Build Profiles**, make sure the scene list is:
   - `MainMenu` (index 0)
   - `ChessRoyaleVR` (index 1)
4. Switch the platform to **Android**, connect a Quest with Developer Mode and USB debugging enabled, and click **Build and Run**.

A prebuilt `ChessRoyale.apk` is stored through Git LFS for sideload testing.

## Game Flow

```
Main Menu
├── START GAME   → ChessRoyaleVR (single player)
├── MULTIPLAYER  → online two-player match (in development)
└── QUIT         → exit the app
```

- **Single player:** Play chess in VR on your own. Available now.
- **Multiplayer:** Two Quest players join the same game online and play against each other, one on each side of the board. Networking is still in development.

## VR Controls

| Action | Input |
|---|---|
| Select a menu button | Point the controller ray at the button and press **Trigger** |
| Grab a nearby piece | **Grip** (Near Grab) |
| Drag a piece from a distance | **Trigger** (Ray Drag) |
| Teleport | Push the **thumbstick forward** to aim the arc, release to teleport |

---

## Sprint Summaries

### Sprint 1

**Goal:** Build the VR foundation and a playable giant chess scene on Quest.

**Completed**
- **XR foundation** (#1): OpenXR and XR Interaction Toolkit setup with Quest controller support.
- **Giant interactive VR chess scene** (#2): Human-sized chess pieces, a sci-fi arena, Near Grab and Ray Drag interaction, and a reset pedestal.
- **Setup and build guide** (#3): Instructions for opening, building, and sideloading the project.
- **Wizard Chess visual draft** (#5): An alternative visual style in a separate draft scene that keeps the existing VR interaction system.
- **VR home screen and timer** (#6):
  - A world-space main menu with START GAME, MULTIPLAYER, and QUIT, selectable with the controller ray.
  - START GAME loads the chess scene. QUIT exits the app.
  - Countdown timer shown on two canvases, one facing each player.
- **Sprint 1 build and demo** (#7): Android APK, a headset demo video, and a progress summary.

**Problems solved**
- **White screen on Quest:** The first menu used a Screen Space Overlay canvas, which only renders on a flat screen and is invisible in a headset. We rebuilt it as a world-space canvas in front of the player.
- **Menu buttons not clickable in VR:** Standard UI raycasting only works with a mouse. We switched to the Tracked Device Graphic Raycaster and the XR UI Input Module so the controller rays can press buttons.
- **Two cameras in the menu scene:** Adding the XR rig next to the original camera caused a duplicate Audio Listener and conflicting views. The menu now uses only the XR rig's camera.
- **Player falling in the menu scene:** The XR rig's gravity pulled the player down because the menu scene has no floor. Locomotion is disabled in the menu.
- **Mirrored timer text:** The timer canvas was facing away from the player. We rotated it and mirrored a second canvas for the opposing player.

### Sprint 2 (in progress)

**Goal:** Turn the scene into real chess with proper rules, timing, and movement.

**Completed / in review**
- **Checkered board, menu, and chess clocks** (#8): Addressable board squares, a VR start/loading menu, and opposing player chess clocks based on #6.
- **Stable piece placement and captures** (#9): Pieces stay upright, drops snap to square centers, invalid drops are rejected, and captures and board reset are handled.
- **Legal chess moves, highlights, and promotion** (#10): Turn order and legal moves, including check, checkmate, stalemate, castling, en passant, and draws. Valid squares are highlighted, the player chooses a promotion piece in-world, and the clock updates after each move.
- **Single-player game logic** (#15): Chess rules and end-of-game logic.
- **End screen** (in progress): An end-of-game screen shown when a match finishes.
- **Music and sound effects** (in progress): Background music and SFX for piece moves, captures, and game events.
- **VR teleportation, single player** (#16):
  - The player can teleport within the white side of the board and the open area behind it.
  - Teleporting does not interfere with grabbing or dragging pieces.
- **Clean checkered board environment** (#17): Removes the arena decorations, rebuilds the board from 64 `ChessBoardSquare` objects with A1–H8 coordinates, and centers the player on the board.

**Problems solved**
- **Teleport ray blocked by the board:** The board and pieces have colliders that stopped the teleport ray before it reached the floor. We put the teleport area on its own `Teleport` layer and set the teleport rays to only detect that layer.
- **Teleport area pushing pieces:** The teleport area's collider was touching the pieces' rigidbodies. It now excludes all physics collisions, so only raycasts detect it.

### Next Steps
- Review and merge the open pull requests into `main`, one at a time and in dependency order.
- Multiplayer: connect the MULTIPLAYER button to networking so two Quest players can join one game.
- Add the black-side teleport area and enable each player's area based on their color.
- Decide what happens when a player's clock reaches 0:00 (loss, prompt, or return to menu).
- Finish the end screen and connect it to the game logic.
- Create a list of needed sound effects and add music and SFX to the game.
- Record a longer demo video showing completed features, problems solved, and plans for the next sprint.
