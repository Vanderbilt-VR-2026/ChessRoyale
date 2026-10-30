# Chess Royale VR Setup

This guide takes a clean checkout from missing local assets to a playable OpenXR scene and a Meta Quest Android build.

## What you need

- Unity Hub
- Unity Editor **6000.3.23f1** with these modules:
  - Android Build Support
  - Android SDK & NDK Tools
  - OpenJDK
- A Unity ID that owns these free Asset Store packages:
  - [3D SciFi Kit Starter Kit](https://assetstore.unity.com/packages/3d/environments/3d-scifi-kit-starter-kit-92152)
  - [Chess Mega Set FREE VERSION](https://assetstore.unity.com/packages/3d/props/chess-mega-set-free-version-287294)
- Git LFS
- For a Quest build: a developer-enabled Meta Quest and a USB data cable

The repository includes the Unity XR packages and configuration. It does **not** include the raw Asset Store source files because this is a public repository and those files remain subject to their Asset Store licenses.

## 1. Clone and open the project

```bash
git lfs install
git clone https://github.com/Vanderbilt-VR-2026/ChessRoyale.git
cd ChessRoyale
```

Open the directory in Unity Hub with Unity **6000.3.23f1**. Let Unity finish package resolution and script compilation before continuing.

## 2. Import the third-party assets

In Unity:

1. Open **Window > Package Management > My Assets**.
2. Find **3D SciFi Kit Starter Kit**, select **Download**, then **Import**.
3. Find **Chess Mega Set FREE VERSION**, select **Download**, then **Import**.
4. Keep every file selected in both import dialogs.

After import, these paths must exist:

```text
Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD/
Assets/Chess MEGA-pack/
```

Do not force-add these directories to Git. They are intentionally listed in `.gitignore`; every contributor should obtain them through their own Unity Asset Store account.

## 3. Generate or refresh the scene

Choose **Tools > Chess Royale > Build VR Scene**.

The builder:

- configures OpenXR for desktop and Android;
- enables Oculus Touch and Meta Quest Touch Plus controller profiles;
- creates the sci-fi arena and giant chess board;
- creates 32 human-scale chess pieces;
- adds near grabbing, distance ray dragging, board-square snapping, and reset behavior;
- places the XR player at the center of the board; and
- makes `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity` the build scene.

If the menu reports a missing prefab, reimport both Asset Store packages and run the builder again.

## 4. Test in the Unity Editor

1. Open `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity`.
2. Connect and start an OpenXR-compatible headset runtime if one is available.
3. Press **Play**.
4. Confirm the headset view starts in the middle of the board.

Controls:

- **Grip near a piece:** grab and move it directly.
- **Point the ray and hold Trigger:** drag a piece from a distance.
- **Release:** snap the piece to the nearest board square.
- **Select the RESET pedestal:** restore every piece to its starting square.

The Game view can render without a headset, but tracked controllers require an active XR runtime or device.

## 5. Build and run on Meta Quest

1. In Unity Hub, confirm the Android modules listed under **What you need** are installed for Unity 6000.3.23f1.
2. Enable Developer Mode for the Quest and connect it to the computer by USB.
3. Put on the headset and accept the USB debugging prompt.
4. In Unity, open **File > Build Profiles**.
5. Add or select the **Android** profile and choose **Switch Platform**.
6. Confirm `Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity` is enabled in the scene list.
7. Choose **Build and Run** and save the output as `ChessRoyale.apk`.
8. After installation, launch **Chess Royale VR** from the headset's developer or unknown-sources applications.

The project is configured with package identifier `com.vanderbiltvr.chessroyale`, Android API level 29 minimum, ARM64, and OpenXR.
