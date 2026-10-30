# Sprint 1

Sprint 1 established the first playable Meta Quest version of Chess Royale. This page preserves the milestone documentation from [PR #7](https://github.com/Vanderbilt-VR-2026/ChessRoyale/pull/7) without carrying its APK into the current branch.

## Goal

Build the Unity and OpenXR foundation, place the player inside an oversized chess environment, and demonstrate two VR interaction methods.

## Completed work

- Configured Unity XR, OpenXR, and Quest controller support.
- Created a playable scene with the user positioned in the middle of an oversized chess board.
- Added all 32 human-scale chess pieces using the Chess Mega Set models.
- Added near grabbing, distance ray dragging, square snapping, and a board-reset control.
- Built the original scene inside a 3D Sci-Fi Kit arena with project lighting.
- Added [SETUP.md](SETUP.md) with Unity, asset-import, editor-testing, and Quest build instructions.

## Demo

[Watch the Sprint 1 VR demo](https://github.com/Vanderbilt-VR-2026/ChessRoyale/blob/e1083a7cb03dbb5c3c5eaa15d480841bae2802ed/sprint1.mov).

## What changed after Sprint 1

Sprint 2 replaced the original arena presentation with a clean black-and-white board made from 64 independent squares. It also added stable piece placement, turn enforcement, legal moves, move hints, captures, complete chess outcomes, castling, en passant, and pawn promotion.
