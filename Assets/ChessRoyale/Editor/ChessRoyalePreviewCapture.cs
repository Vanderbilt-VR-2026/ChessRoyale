using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChessRoyale.Editor
{
    public static class ChessRoyalePreviewCapture
    {
        private const string ScenePath = "Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity";
        private const string OutputPath = "Assets/ChessRoyale/Documentation/ChessRoyaleVR-HeadsetPreview.png";

        [MenuItem("Tools/Chess Royale/Capture Headset Preview")]
        public static void Capture()
        {
            if (!EditorSceneManager.GetActiveScene().path.Equals(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);

            Camera camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null)
                throw new MissingReferenceException("The Chess Royale XR camera was not found.");

            const int width = 1920;
            const int height = 1080;
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            List<GameObject> hiddenOverlays = new();

            try
            {
                // Keep the proof image readable and demonstrate the legal-move highlight state.
                foreach (string objectName in new[] { "White Clock — North", "White Clock — South", "Black Clock — North", "Black Clock — South", "Game Status" })
                {
                    GameObject overlay = GameObject.Find(objectName);
                    if (overlay != null && overlay.activeSelf)
                    {
                        overlay.SetActive(false);
                        hiddenOverlays.Add(overlay);
                    }
                }
                foreach (BoardSquare square in Object.FindObjectsByType<BoardSquare>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (square.Coordinate == new BoardCoordinate(4, 2) || square.Coordinate == new BoardCoordinate(4, 3))
                        square.SetHighlight(BoardSquareHighlight.LegalMove);
                }

                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;

                Texture2D image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();

                Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
                File.WriteAllBytes(OutputPath, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                foreach (GameObject overlay in hiddenOverlays)
                    overlay.SetActive(true);
                foreach (BoardSquare square in Object.FindObjectsByType<BoardSquare>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    square.SetHighlight(BoardSquareHighlight.None);
                target.Release();
                Object.DestroyImmediate(target);
            }

            AssetDatabase.Refresh();
            Debug.Log($"ChessRoyale: saved headset-camera preview to {OutputPath}");
        }
    }
}
