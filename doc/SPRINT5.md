# Sprint 5

Sprint 5 adds host-configurable multiplayer matches without replacing the existing in-world VR lobby. The host chooses the rules and arena before creating a room, while the guest reviews the synchronized settings and presses **Ready** before play begins.

The other sprint notes are [Sprint 1](SPRINT1.md), [Sprint 2](SPRINT2.md), [Sprint 3](SPRINT3.md), and [Sprint 4](SPRINT4.md). The complete feature design is [Multiplayer Match Configuration and Chess Variants](../docs/designs/multiplayer-chess-variants.md).

## Match configuration

The host can configure:

- **Standard** or **Chess960** starting positions;
- **Three-check** on or off;
- **Podium & Overview** on or off; and
- the existing **White**, **Black**, or **Random** seat preference.

The configuration is owned by the network authority and cannot be edited after the room is created. The guest sees the same settings as a read-only summary and accepts their exact revision with the **Ready** button. Seat occupancy, readiness, and the active-match phase are tracked separately so a connection change cannot accidentally start or reset a game.

Chaos mode remains reserved for a later sprint. Its simultaneous movement, cooldown, and piece-weight rules are not selectable yet.

## Chess960

Chess960 uses one of the 960 legal starting positions. Both sides receive the same mirrored back rank, bishops begin on opposite colors, and the king begins between the rooks.

Castling uses Chess960 rules and completes as one authoritative action, including positions where only one piece moves or the king and rook exchange squares. Every rematch keeps the selected match settings but generates a different Chess960 position.

## Three-check

When Three-check is enabled, the network authority counts checks delivered by each player. Delivering a third check wins the game. The replicated counters appear beside the existing match status, and the result screen identifies a Three-check victory.

## Arena options

With **Podium & Overview** enabled, both players receive podiums and each player independently controls their own Overview view.

With it disabled, generated podiums are removed, Overview is unavailable, and four teleport areas are placed around the board for ground-level play. Players continue to grab pieces directly in VR.

## Rematches and compatibility

Rematches preserve the host's configuration. Chess960 rematches generate a new position, while Standard rematches reset to the normal layout. A round sequence is included in network actions so a delayed move or promotion request from the previous game is rejected.

This sprint changes the Photon Fusion network state. The Unity Editor and Quest must run builds made from the same revision. Rebuild and reinstall the APK before testing this work with a headset.

## Black-box verification

The project compiled in Unity 6.3 and opened a Photon room in Play Mode. The existing `ABCD` host flow displayed one waiting panel with the room code, synchronized settings, and assigned color. No missing-reference, null-reference, network-spawn, or failed-prefab-instance errors appeared during that flow.

A complete two-player Quest/Editor check is still required for guest Ready, live Chess960 castling, Three-check victory, ground teleporting, and rematch synchronization.
