using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Editor
{
    public static class WizardChessDraftBuilder
    {
        private const string OriginalScene = "Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity";
        private const string VisualFolder = "Assets/ChessRoyale/CustomAssets/WizardChess";

        [MenuItem("Tools/Chess Royale/Build Wizard Chess Draft")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before building the draft.");
            if (!File.Exists(OriginalScene)) throw new FileNotFoundException("Build the original VR scene first.", OriginalScene);
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            string folder = AssetDatabase.GetSubFolders(VisualFolder)
                .OrderByDescending(p => p, StringComparer.Ordinal)
                .FirstOrDefault(p => p.Contains("/Extracted_") && LoadVisuals(p).ContainsKey("Board"));
            if (folder == null) folder = WizardChessAssetPreview.GenerateGroupedVisuals();
            Dictionary<string, GameObject> visuals = LoadVisuals(folder);
            if (!visuals.ContainsKey("Board")) throw new InvalidOperationException("Expected a grouped board visual prefab.");
            foreach (GameObject prefab in visuals.Values)
                if (prefab.GetComponentsInChildren<Component>(true).Any(c => c == null ||
                    !(c is Transform || c is MeshFilter || c is MeshRenderer)))
                    throw new InvalidOperationException("Visual prefab contains non-visual components: " + prefab.name);

            string draftPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ChessRoyale/Scenes/ChessRoyaleWizardDraft.unity");
            if (!AssetDatabase.CopyAsset(OriginalScene, draftPath)) throw new IOException("Could not copy the original scene.");
            Scene draft = EditorSceneManager.OpenScene(draftPath, OpenSceneMode.Single);
            ChessPieceInteractable[] pieces = draft.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<ChessPieceInteractable>(true)).ToArray();
            if (pieces.Length != 32) throw new InvalidOperationException("The original scene must contain 32 interactive pieces.");
            foreach (ChessPieceInteractable piece in pieces)
            {
                bool black = piece.name.StartsWith("Black ", StringComparison.Ordinal);
                int file = Mathf.Clamp(Mathf.RoundToInt((piece.transform.position.x + 7f) / 2f), 0, 7);
                // The GLB's white side occupies positive Z; the original VR scene uses negative Z.
                // Pair rear/front rows by spatial location, keeping the file order for this draft.
                bool rear = black ? piece.transform.position.z > 8f : piece.transform.position.z < -4f;
                int sourceRank = black ? (rear ? 1 : 2) : (rear ? 8 : 7);
                string key = $"{(black ? "Black" : "White")}_{(char)('A' + file)}{sourceRank}";
                if (!visuals.TryGetValue(key, out GameObject prefab))
                {
                    Debug.LogWarning("Wizard Chess draft: missing grouped visual " + key + ". Keeping the original visual for " + piece.name + ".");
                    continue;
                }
                Bounds oldBounds = BoundsOf(piece.gameObject);
                HideRenderers(piece.gameObject);
                GameObject visual = InstantiateVisual(prefab, piece.transform);
                Bounds bounds = BoundsOf(visual);
                float scale = oldBounds.size.y / Mathf.Max(bounds.size.y, 0.001f);
                // Fit visual children only; interaction root scales and collider geometry remain unchanged.
                visual.transform.localScale *= scale;
                bounds = BoundsOf(visual);
                visual.transform.position += new Vector3(piece.transform.position.x - bounds.center.x,
                    oldBounds.min.y - bounds.min.y, piece.transform.position.z - bounds.center.z);
            }

            GameObject board = draft.GetRootGameObjects().FirstOrDefault(r => r.name.StartsWith("GIANT CHESS BOARD", StringComparison.Ordinal));
            if (board == null) throw new InvalidOperationException("Original board root was not found.");
            HideRenderers(board);
            GameObject squares = draft.GetRootGameObjects().FirstOrDefault(r => r.name == "64 Playable Board Squares");
            if (squares != null) HideRenderers(squares);
            GameObject boardVisual = InstantiateVisual(visuals["Board"], board.transform);
            Bounds boardBounds = BoundsOf(boardVisual);
            boardVisual.transform.localScale *= Mathf.Min(16f / boardBounds.size.x, 16f / boardBounds.size.z);
            boardBounds = BoundsOf(boardVisual);
            boardVisual.transform.position += new Vector3(-boardBounds.center.x, 0.421f - boardBounds.max.y, 2f - boardBounds.center.z);

            EditorSceneManager.MarkSceneDirty(draft);
            if (!EditorSceneManager.SaveScene(draft, draftPath)) throw new IOException("Could not save draft scene.");
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(draftPath);
            Debug.Log($"Wizard Chess draft ready: {draftPath}. Visuals: {folder}. Press Play to test. Original scene and build settings are unchanged.");
        }

        private static Dictionary<string, GameObject> LoadVisuals(string folder)
        {
            var result = new Dictionary<string, GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string name = Path.GetFileNameWithoutExtension(path);
                string key = null;
                if (name == "Wizard_Chessboard_Visual") key = "Board";
                else
                {
                    string[] tokens = name.Split('_');
                    if (tokens.Length >= 4 && (tokens[0] == "White" || tokens[0] == "Black") && tokens[1] == "Position"
                        && tokens[2].Length == 2 && tokens[2][0] >= 'A' && tokens[2][0] <= 'H'
                        && (tokens[0] == "White" ? tokens[2][1] == '7' || tokens[2][1] == '8' : tokens[2][1] == '1' || tokens[2][1] == '2'))
                        key = tokens[0] + "_" + tokens[2];
                }
                if (key != null) result[key] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return result;
        }

        private static GameObject InstantiateVisual(GameObject prefab, Transform parent)
        {
            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent.gameObject.scene);
            visual.name = "Wizard Draft Visual — " + prefab.name;
            visual.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;
            visual.transform.SetParent(parent, true);
            return visual;
        }

        private static void HideRenderers(GameObject root)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        }

        private static Bounds BoundsOf(GameObject root)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No visual bounds found: " + root.name);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }
    }
}
