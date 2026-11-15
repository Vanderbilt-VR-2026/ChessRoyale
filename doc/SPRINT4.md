# Sprint 4

Sprint 4 improves multiplayer presence and makes the match easier to read. The main addition in this update is a visible, animated opponent avatar that works between a Quest build and a player running in the Unity Editor.

The other sprint notes are [Sprint 1](SPRINT1.md), [Sprint 2](SPRINT2.md), and [Sprint 3](SPRINT3.md). The [project README](../README.md) describes the game as it is now. A recording of the avatar in a match is [sprint-4-avatar.mp4](sprint-4-avatar.mp4).

## Multiplayer opponent avatar

Each network player now uses the free **Minimal Poly - Free** humanoid. A player does not see their own model, but the other player sees it across the chessboard. The earlier procedural monkey remains as a fallback if the humanoid prefab cannot load.

Photon Fusion shares the opponent's:

- horizontal headset position and viewing direction;
- left- and right-controller positions and rotations;
- walking speed; and
- left- and right-hand grab state.

The avatar uses the package's idle and walking clips. A small built-in two-bone arm solver moves its arms toward the tracked controllers, and its fingers curl while the player is holding an interactable object. This does not require Final IK, Animation Rigging, or another paid package.

Avatar height is intentionally fixed. Headset movement across the room still moves the model and looking around still turns its head, but headset height and crouching do not resize or stretch the avatar. The current network head height is 1.55 metres above the player root.

Runtime fallback geometry uses a URP-compatible material so missing built-in shaders do not appear as purple boxes.

## Match status

During a network match, the clock panel now says **Your turn** or **Opponent's turn** instead of repeating the active piece color. Checkmate, draw, timeout, and other finished-game messages are unchanged.

## Testing with one Quest

1. Build and install the latest APK on the Quest. The avatar changes alter the networked player layout, so both players must use a matching build.
2. Open the same project revision in Unity and enter Play Mode.
3. Create a multiplayer room on either device, then join its four-letter code from the other device.
4. Confirm that each side sees the other player's humanoid, but not its own.
5. Move sideways, look around, move both controllers, grab a chess piece, and release it. The remote avatar should walk, turn its head, follow both hands, close the grabbing hand, and reopen it on release.
6. Change the headset's vertical position. The remote avatar should remain at the fixed standing height without stretching its head or neck.

If the Console reports `FailedToCreateInstance` for the network player, rebuild Fusion's prefab table from **Tools > Fusion > Rebuild Prefab Table**, then rebuild the APK.

## Asset source

The humanoid and animation files come from **Minimal Poly - Free** by Yuuki Tsuji (Unity Asset Store package 308063) under the Standard Unity Asset Store EULA.
