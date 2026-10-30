using System.IO;
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

            try
            {
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
                target.Release();
                Object.DestroyImmediate(target);
            }

            AssetDatabase.Refresh();
            Debug.Log($"ChessRoyale: saved headset-camera preview to {OutputPath}");
        }
    }
}
