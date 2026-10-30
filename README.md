# Chess Royale

Chess Royale is a Meta Quest VR chess prototype built with Unity and OpenXR. The current scene places the player in the middle of a large black-and-white board with human-sized chess pieces that can be grabbed nearby or dragged from a distance.

Sprint writeups live in [SPRINTS.md](SPRINTS.md): [Sprint 1](SPRINTS.md#sprint-1), [Sprint 2](SPRINTS.md#sprint-2), and [Sprint 3](SPRINTS.md#sprint-3).

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
| Disconnect immediately counts as a win for the other player | This is the simplest rule to implement. On Quest, short Wi-Fi drops and headset sleep are common, so a real player can lose a good game by accident. | Give a short reconnect window (for example, 30 seconds) before awarding the win. |
| Fixed 10-minute clock for online games | One hard-coded value is the default. The [Sprint 3 writeup](SPRINTS.md#sprint-3) calls for a time-control choice on the start screen. | Add two or three presets (for example, 5, 10, and untimed). |
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

Claude was used to help write the `ChessAudio` sound system and to draft the product-sense section above; the audio was chosen, placed, and tested by the team, and the product-sense recommendations were reviewed and edited by the team.
