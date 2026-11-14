# Palace home screen

The MainMenu scene contains editable TMP labels and Unity UI buttons over a static palace background. The artwork is a 2D illustration; the existing gameplay palace is unchanged. MainMenuPresentation uses a screen overlay on desktop and preserves the tracked world canvas when an XR device is active. A 16:9 composition fits inside other aspect ratios without cropping.

Font: Cormorant Garamond Medium, instantiated at weight 500 from the Google Fonts variable font. See Font-LICENSE.txt (SIL OFL).

Background: PalaceBackground.png, generated with the built-in image_gen tool from the approved concept. Final prompt:

> Edit this approved mature Chess Royale menu image into the production background plate for the same game. Remove ALL text and ALL UI: remove CHESS ROYALE heading, SINGLE PLAYER and MULTIPLAYER buttons including their rectangles/borders/fills, and QUIT text. Seamlessly reconstruct the plain muted dark burgundy palace wall behind them. Preserve the exact composition, framing, warm natural lighting, realistic travertine columns, arches, window, black and ivory chessboard and realistic ivory and walnut chess pieces on right. Keep the left 40 percent quiet dark burgundy negative space for live Unity UI to be layered later. No text, no typography, no logos, no buttons, no borders. Landscape 16:9 high quality production background. Do not add objects.

Editor commands under Chess Royale can reapply the design, or validate the running home screen and invoke the saved single-player button event. Validation checks the three active buttons, persistent callbacks and label overflow. Desktop Play mode was visually checked and the single-player event loaded ChessRoyaleVR. Physical headset interaction and multiplayer matchmaking were not exercised.
