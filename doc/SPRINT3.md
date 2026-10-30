# Sprint 3

Sprint 3 splits local chess from online chess and gives every screen a way back to the lobby. The Quest build is [chessRoyale.apk](../chessRoyale.apk).

The other sprint notes are [Sprint 1](SPRINT1.md) and [Sprint 2](SPRINT2.md). The [project README](../README.md) describes the game as it is now.

## Design

### Start screen

The first thing the player sees is the hall, standing at an empty board, with a floating Chess Royale panel. Starting in the room says this is a calm, formal match. Next they choose Single Player or Multiplayer, then a time control. The pieces come onto the board and the clock starts.

### Interactions

Pieces still use the toolkit's default Near-Far interactor, so a hand can grab a piece up close or with a ray from far away. Direct grab is the better match, because the board is within reach and a far ray on a crowded board picks up the wrong piece. The ray stays for menus only.

Hands replace the controller models. Menus and the result screen use pointer hands with a ray. During the match the hands are gloves and the ray is hidden.

## Rooms

From Multiplayer the player chooses Join or Create. Create shows a four-letter code. Join types that code on the panel keyboard. A code that is not found stays on the join screen.

Included: four-letter codes, the in-panel A–Z keyboard, and Lobby from the wait, the match, and the join screen.

Still open: a time-control choice before the clock starts.

## Clock and the end of the match

The clock is hidden until both players are seated. When the game ends the panel says You Win, You Lose, or Draw. Rematch starts only after both agree. Return to Lobby goes back to Join or Create.

Included: a shared clock, win and loss on time, and a disconnect forfeit after the match has started.

Still open: choosing 5 minutes, 10 minutes, or untimed from the start screen.

## Team contributions

| Team Member | Contributions |
|---|---|
| RuoHan Chen | Implemented multiplayer networking using Photon Fusion, including creating and joining game rooms, and worked on networking fixes and testing. |
| Sarina Liu | Worked on multiplayer networking testing and troubleshooting and updated the GitHub project board. |
| Feiyuan Yang | Updated Sprint 3 documentation and helped test and troubleshoot multiplayer networking. |

## In progress

Direct grab for pieces, with the ray kept for menus. Hands that switch between a pointer and a glove as the screen changes. A time-control choice on the start screen.

## Next

Limit the ray to menus, add the time presets, and keep the shared match in step when a player drops briefly and comes back.

## Demo

[Watch the Sprint 3 demo](https://youtu.be/OnBdmAllzGQ).
