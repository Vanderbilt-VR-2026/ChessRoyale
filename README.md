# Chess Royale

Chess Royale is a Meta Quest chess game. Two people share one hall and one giant board, or one person plays both sides.

It is built in Unity **6000.3.23f1** with OpenXR, the XR Interaction Toolkit, and Photon Fusion, for Meta Quest 3.

Sprint writeups live in [doc/SPRINTS.md](doc/SPRINTS.md): [Sprint 1](doc/SPRINT1.md), [Sprint 2](doc/SPRINT2.md), and [Sprint 3](doc/SPRINT3.md).

## Getting started

### In the editor

Open `Assets/ChessRoyale/Scenes/MainMenu.unity`. That scene is first in the build. It has **Single Player** and **Multiplayer**. Asset import and **Build and Run** are in [doc/SETUP.md](doc/SETUP.md).

### On a Quest

The Sprint 3 branch includes [chessRoyale.apk](chessRoyale.apk), the October 29 Quest build. That file does not include the newer networking. Uninstall any older Chess Royale from the headset, copy the APK onto the headset, and install it there.

## How a match goes

**Single player.** Main menu, Single Player. The player moves White, then Black. There is no clock. Leave returns to the main menu. Photon does not start.

**Multiplayer.** Main menu, Multiplayer, then Join or Create. Leave on that screen returns to the main menu. Create shows a four-letter code, and Lobby returns to Join or Create, including while the room is still connecting. Join uses an A–Z keyboard on the panel. A missing room stays on the join screen. The clock appears when both players are in. Lobby on the clock leaves the match. At the end the panel says You Win, You Lose, or Draw, then Rematch or Return to Lobby. Rematch starts only after both players agree.

## What is true today

- A nearby grip and a far ray can both move a piece.
- The clock stays hidden until both players are in the room.
- Rematch waits until both players agree.
- Single player does not open a Photon session.
- If a headset drops mid-game, it tries to rejoin for 25 seconds. The other player holds that seat for 30 seconds.

## The game

The game includes:

- A clean board made from 64 independent `ChessBoardSquare` objects.
- Human-sized chess pieces using the Chess Mega Set asset models.
- Near grabbing with the controller grip and distance dragging with the controller ray.
- Upright piece handling, board-elevation recovery, and automatic square-center snapping.
- Turn enforcement, legal-move validation, captures, and king-safety checks.
- Boundary-glow hints around valid destination squares.
- Check, checkmate, stalemate, repetition, fifty-move, and insufficient-material draws.
- Castling, en passant, and in-world pawn-promotion selection.
- Photon Fusion shared-mode multiplayer for two players, with four-letter rooms, White and Black seats, synced moves, a shared chess clock, and a rematch that starts only after both players agree.

The project includes the VR main menu and `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity`, with board teleportation and networked play. Background music and sound effects are included (see [Audio](#audio)).

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

## Audio

`ChessAudio` (`Assets/ChessRoyale/Scripts/ChessAudio.cs`) plays the background music and sound effects. It creates itself when the game starts and stays alive across the menu and match scenes, so no scene setup is needed.

Sounds are loaded by file name from `Assets/ChessRoyale/Resources/ChessRoyaleAudio/`. To add or replace a sound, drop an `.mp3`, `.wav`, or `.ogg` with the matching name into that folder. Any file that is missing is skipped, and the Console lists which ones on Play.

| File name | Plays when |
|---|---|
| `music_game` | Looping background music (menu and match) |
| `music_menu` | Optional: separate menu music; falls back to `music_game` |
| `sfx_grab` | A piece is picked up (3D, from the piece) |
| `sfx_move` | A move completes, yours or your opponent's |
| `sfx_capture` | A move captures a piece |
| `sfx_check` | The side to move is in check |
| `sfx_invalid` | An illegal move snaps the piece back (3D, from the piece) |
| `sfx_promotion` | A pawn promotion is chosen |
| `sfx_victory` | You win (single player: any checkmate or win on time) |
| `sfx_defeat` | You lose (online) |
| `sfx_draw` | Stalemate or another draw |
| `sfx_new_game` | The board resets or a rematch starts |
| `sfx_opponent_joined` | The second player joins your online room |
| `sfx_ui_click` | Any menu, keyboard, or lobby button is pressed |

Game sounds come from watching `ChessGameController` state (turn, captures, check, result) instead of hooking `TryMove`, so an opponent's move that arrives over Photon sounds the same as a local one. Volumes for music, effects, and UI are on the `ChessAudio` object in Play Mode.

## Multiplayer (Photon Fusion)

Online play follows the structure of the class tutorial project (*Photon Fusion With Unity XR Interaction Toolkit*): Fusion **Shared mode**, a Fusion Network Manager, NetworkEvents, a master-client-owned shared object, NetworkTransform avatars that follow each player's own XR rig, and Photon Voice.

| Tutorial piece | Chess Royale version |
|---|---|
| **Fusion Network Manager** object with a `NetworkRunner` and `NetworkEvents` | `Fusion Network Manager` in `ChessRoyaleVR` (`ChessFusionRoomManager`). Each Create/Join starts a fresh copy of `FusionNetworkRunner.prefab` (runner + NetworkEvents + scene manager + voice). Runners are single-use, so a fresh one per attempt is what makes Leave → Create and reconnecting work. |
| Session name selects the room | A random four-letter room code is the session name. Join opens a letter keyboard. |
| **Lobby controller** spawned by the master with `SharedModeStateAuthMasterClient` and *Is Master Client Object* on, *Destroy When State Authority Leaves* off | `ChessNetworkGameState.prefab` (seats, board snapshot, chess clock, rematch votes). Same flags, so if the room creator leaves, the other player takes over the room instead of losing it. |
| **Network Player** avatar: head and hands with `NetworkTransform`, copied from the local XR rig; the owner hides its own copy | `ChessNetworkPlayer.prefab` (`ChessFusionPlayer`): head, gloved hands, and a body tinted to the player's color. Each headset keeps its own scene XR rig; it is moved to the White or Black side when the match starts. |
| **Photon Voice**: `FusionVoiceClient` + `Recorder` following the Fusion room, speaker prefab for incoming voices | Same, on the runner prefab. The microphone only records after permission and once both connections are up. Voice is off when **App Id Voice** is empty. |
| **Reconnect** on connection failure | If your headset drops mid-game, it automatically rejoins the same room code for up to 25 seconds. The other player's side holds your seat for 30 seconds (shown on the clock) before awarding the win. |

Chess moves are not physics objects: a player's move is sent to the room's authority as a request, checked against the full rules, and the resulting board is published to both players. A request that gets no answer within 4 seconds returns the piece, so the board never locks up.

### One-time multiplayer setup

1. `Assets/Photon/Fusion/Resources/PhotonAppSettings.asset` already has the team's **App Id Fusion**, **Fixed Region** `us`, and **App Version** `chessroyale-1`. Every headset must use builds with the same three values.
2. Optional voice chat: in the [Photon Dashboard](https://dashboard.photonengine.com/), create a **Voice** application and paste its ID into **App Id Voice** in the same asset.
3. Let Unity finish importing the Photon Voice package (it is listed in `Packages/manifest.json` and needs Git installed).
4. Run **Tools > Chess Royale > Configure Photon Fusion** once. It builds the avatar, game-state, runner, and voice-speaker prefabs, wires them to the Fusion Network Manager in `ChessRoyaleVR`, and rebuilds Fusion's prefab table. Commit the resulting prefab and scene changes.
5. Build to every headset. One player taps **Multiplayer > Create** and reads out the code; the other taps **Join** and enters it.

### Testing checklist (two headsets)

| Try | Expect |
|---|---|
| Create on headset A, join on headset B | Both enter the match. A is White, B is Black, and each sees the other's head, hands, and body. |
| Make moves on both sides | Both boards and clocks stay in sync. Illegal or out-of-turn moves snap back. |
| Talk (with a Voice App ID) | Each player hears the other. |
| Mid-game, turn off Wi-Fi on B for about 10 seconds, then back on | A's clock shows the 30-second countdown. B shows "Rejoining room…", then the match resumes with the same board. |
| Mid-game, close the app on A (the room creator) | B keeps the room. After 30 seconds B wins by disconnect. |
| Leave and Create again without restarting the app | A new room works. |

## Setup and Quest build

See [doc/SETUP.md](doc/SETUP.md) for the full environment, Asset Store import, Unity scene-generation, editor-testing, and Meta Quest build instructions.

Quick start:

1. Install Git LFS and clone the repository.
2. Open the project with Unity **6000.3.23f1** and Android Build Support.
3. Import the required Asset Store packages through **Window > Package Management > My Assets**.
4. Choose **Tools > Chess Royale > Build VR Scene**, then **Tools > Chess Royale > Configure Photon Fusion** (see [Multiplayer](#multiplayer-photon-fusion)).
5. Open `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity` and test it in Play Mode.
6. Use **File > Build Profiles > Android > Build and Run** to install it on a developer-enabled Meta Quest.

The separately licensed Asset Store source folders and generated Unity/build directories are intentionally excluded from Git.

## Team

| Team member | Major | Role | Owns |
|---|---|---|---|
| RuoHan Chen | CS | GitHub Manager | Chess rules, piece placement, Photon rooms, and match sync |
| Derrick Seo | CS | Communications Director | Music and sound effects |
| Sarina Liu | CS | Design Director | Hall, lighting, timer, and the Sprint 3 design notes |
| Feiyuan Yang | CS | Asset Manager | Teleportation, the VR main menu, and sprint documentation |

## Product sense

These features are in the game because they make Chess Royale a better fit for VR.

### Features that make sense for this game

| Feature | Why it earns its place |
|---|---|
| Standing on a giant board with human-sized pieces | This is the core idea. It is something only VR can offer; a normal-sized board on a table would just be a worse version of 2D chess. |
| Glowing move hints on squares | Pieces are big and the board is wide, so it is hard to see legal moves from the ground. The glow replaces looking down at a small board and makes the game approachable for people who don't know chess well. |
| Starting inside the hall instead of a flat menu | The first thing the player sees sets the mood. It also means no separate 2D menu to design and maintain. |
| Gloved hands that change between menu and match | Showing a pointer ray only when it is useful keeps the match uncluttered and makes grabbing feel physical. |
| In-world promotion picker | A floating 2D dialog would break immersion; choosing the piece in the world keeps the player in the space. |
| Four-letter room codes with an in-headset letter keyboard | Typing is hard in VR. A short, letters-only code is something a person can read across a room and tap in quickly. |
| Rematch only when both players agree | Prevents one player from resetting the board on the other. |
| Spatial sound for piece grabs and illegal moves | On a board this big, sound coming from the piece helps the player notice what happened and where, without reading text. |

## AI usage

OpenAI ChatGPT and Codex were used under human direction to help:

- organize product requirements and the GitHub Kanban board;
- prototype and refine Unity C# gameplay code;
- investigate editor, build, and VR interaction issues;
- prepare documentation and organize the stacked pull requests.

AI-generated suggestions are reviewed through the pull-request workflow and should be black-box tested in Unity and on Meta Quest before merge. AI is used as a development aid; it is not an in-game feature and does not make chess moves for the player.

Claude was also used as a step-by-step guide for configuring the XR Interaction Toolkit (world-space menu UI, controller ray interaction, teleportation areas and layers), explaining Git branching and merge-conflict resolution, troubleshooting Quest USB debugging, and drafting PR and README text. Start and end screen layouts were designed by the team, and AI helped write their code. All changes were configured, tested on Meta Quest 3, and debugged by the team.

Claude was used to help restructure the Photon Fusion networking to follow the class tutorial (network manager and runner prefab, master-client game state, avatars, voice, reconnect) and to review it for bugs; the team configured, built, and tested it on Meta Quest headsets.

Claude was used to help write the `ChessAudio` sound system and to draft the product-sense section above; the audio was chosen, placed, and tested by the team, and the product-sense recommendations were reviewed and edited by the team.
