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
- Photon Fusion shared-mode multiplayer for two players, with four-letter rooms, White and Black seats, synced moves, a shared chess clock, and a rematch that starts only after both players agree.

This stack builds the VR main menu and `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity`, including board teleportation and networked play. Background music and sound effects are included (see [Audio](#audio)).

## Single player: playing the computer

Choosing **Single Player** opens a setup panel in front of you:

- **Play as:** White, Black, or Random. Playing Black turns you to face the board from Black's side, and the computer moves first.
- **Difficulty:**

| Level | How it plays |
|---|---|
| **Easy** | Looks one move ahead without checking replies, picks among "good enough" moves, and sometimes plays a random one. Grabs free pieces, walks into recaptures. |
| **Medium** | Looks three moves ahead and plays out captures to the end. Spots forks and punishes blunders. |
| **Expert** | Keeps looking deeper (iterative deepening) for about two seconds per move, typically 5 to 8 moves ahead. Plays solid, tactical chess. |

The panel returns after each game with the result, so you can rematch or change level. The setup is remembered between sessions.

**How it works.** `ChessEngine` (`Assets/ChessRoyale/Scripts/AI/`) is a classic alpha-beta chess engine in plain C#: it generates moves, searches ahead while pruning lines that can't beat the best found so far, settles captures before judging a position (quiescence search), and scores positions by material plus piece placement (piece-square tables). `ChessAIPlayer` gives it a copy of the board and runs it on a background thread, so the headset never stutters while it thinks, then plays its move through the same rules code as a human move. The search allocates no memory per position, which avoids garbage-collector pauses on Quest.

**Tested outside Unity.** The move generator matches the published *perft* counts (every legal move sequence to a fixed depth) for six standard test positions, about 6.9 million positions in total, covering castling, en passant and promotion. Search tests confirm it finds checkmates, wins free material, avoids a poisoned pawn, and promotes. In engine-vs-engine games, Medium beat Easy 10-0, and Expert beat Medium 3-0 with 1 draw.

Claude was used to help write the chess engine, the AI player and setup panel, and their tests; the team reviewed and play-tested it on Meta Quest 3.

## Bird's-eye view (Sprint 4, in progress)

In VR, the default player view looks at the board from one side at an angle. Pieces on the far side are smaller and harder to read, and tall pieces can block the ones behind them. The bird's-eye view moves the camera directly above the board so players can **see the whole board and the overall position at once**, which makes it easier to plan moves and spot threats. Tracked in [#69](https://github.com/Vanderbilt-VR-2026/ChessRoyale/issues/69).

**Player view (default).** The normal seated view of the board. A toggle bar at the bottom shows which view is active, with a hint to press **Y** on the left controller to switch.

![Player view with the view toggle at the bottom](docs/images/birds-eye/player-view.png)

**Bird's-eye view.** The camera sits directly above the board. All 64 squares and every piece are visible, with file (a–h) and rank (1–8) labels around the edge. Players can still pick up and move pieces in this view. Pressing **Y** again returns to the player view.

![Bird's-eye view of the full board from above](docs/images/birds-eye/birds-eye-view.png)

**Planned behaviour:**

- Toggle between the two views with **Y** on the left controller or the on-screen toggle.
- The camera moves smoothly between views instead of cutting.
- Grabbing, dragging and placing pieces work the same in both views.

Development happens on the `feature/birds-eye-view` branch and is merged into `sprint-4` through a pull request after review and headset testing.

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

See [SETUP.md](SETUP.md) for the full environment, Asset Store import, Unity scene-generation, editor-testing, and Meta Quest build instructions.

Quick start:

1. Install Git LFS and clone the repository.
2. Open the project with Unity **6000.3.23f1** and Android Build Support.
3. Import the required Asset Store packages through **Window > Package Management > My Assets**.
4. Choose **Tools > Chess Royale > Build VR Scene**, then **Tools > Chess Royale > Configure Photon Fusion** (see [Multiplayer](#multiplayer-photon-fusion)).
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

# Sprint 2 Demo
[Watch the Sprint 2 demo video](https://github.com/Vanderbilt-VR-2026/ChessRoyale/releases/tag/sprint-2-demo)

## Sprint 2 contribution log

| Team member | Work completed this sprint |
|---|---|
| RuoHan Chen | Migrated and populated the GitHub Kanban board; authored the product design requirements; built stable VR piece placement and snapping; implemented legal moves, turns, captures, move hints, complete chess rules, special moves, promotion, and correct board orientation. |
| Derrick Seo | Worked on the blender and soundtracks. |
| Sarina Liu | Added Background and Timer. Adjusted the lighting. Recorded the demo. |
| Feiyuan Yang | Added VR teleportation limited to the board so players can move around without falling off; rebuilt the VR main menu (START/QUIT) with controller-ray interaction on the new board and set it as the first build scene; merged the main menu and teleportation onto the complete-rules stack; tested teleport boundaries and the main menu on Meta Quest 3. |

## Product sense: deliberate design vs. AI default

AI tools tend to produce the "complete, generic" version of a feature: every rule implemented, every state shown as text, every flow covered. This section separates the features that exist because they make Chess Royale a better VR game from the ones that are there mainly because that is what an AI-assisted build produces by default, and says what to do about each.

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
| Spatial sound for piece grabs and illegal moves (new) | On a board this big, sound coming from the piece helps the player notice what happened and where, without reading text. |

### Features that look like AI defaults

| Feature | Why it reads as a default | Recommendation |
|---|---|---|
| Every draw rule (threefold repetition, fifty-move rule, insufficient material) | Rule-complete chess is what an AI produces when asked for "chess." Casual VR sessions almost never reach these. | Keep, since the code already works, but don't spend black-box testing time on them before core moves, captures, and networking are solid. |
| Game state shown as status text ("White to move — check") | Text is the generic way to show state. In VR, text on a panel is easy to miss when you're looking at pieces. | Keep the text, but lead with in-world signals: the red king glow, the check sound, and the turn indicator below. |
| Disconnect immediately counts as a win for the other player | This is the simplest rule to implement. On Quest, short Wi-Fi drops and headset sleep are common, so a real player can lose a good game by accident. | **Done:** a dropped player auto-rejoins, and their seat is held for 30 seconds before the win is awarded (see [Multiplayer](#multiplayer-photon-fusion)). |
| Fixed 10-minute clock for online games | One hard-coded value is the default. The Sprint 3 design calls for a time-control choice on the start screen. | Add two or three presets (for example, 5, 10, and untimed). |
| Many navigation buttons (Leave, Lobby, Return to Lobby) on several screens | Covering every path is thorough, but each screen ends up with similar-looking options. | Black-box test the menus with someone new to the game and remove any button they don't need. |

### Suggested features for Sprint 3

These come from what would make the game more fun to play, not from what is easy to generate:

1. **Turn indicator in the world** (Feiyuan's idea): light up the active player's side of the board, or the edge of the board facing them. This matters more than a text label because players are looking at pieces, not panels.
2. **A raised overview spot**: a teleport point above the board, so players can see the whole position. From ground level, the far side of a giant board is hard to read.
3. **A capture moment**: a short visual effect with the capture sound, so captures feel like the "royale" in the name.
4. **A hint toggle**: experienced players may want to turn the move glow off.

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

## Pull-request stack

Review the current work in dependency order:

1. [Checkered board and clean environment](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/17)
2. [Stable VR chess-piece placement](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/23)
3. [Legal moves, turns, hints, and captures](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/24)
4. [Complete chess rules and promotion](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/25)
5. [Sprint 2 documentation and contribution log](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/26)
