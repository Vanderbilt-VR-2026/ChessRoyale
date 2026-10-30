using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ChessRoyale.Editor
{
    public static class ChessClockMenu
    {
        [MenuItem("Tools/Chess Royale/Add Chess Clock")]
        public static void AddChessClock()
        {
            ChessClock existing = Object.FindFirstObjectByType<ChessClock>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("ChessRoyale: the scene already has a Chess Clock.");
                return;
            }

            GameObject clock = new GameObject("Chess Clock");
            clock.AddComponent<ChessClock>();
            Undo.RegisterCreatedObjectUndo(clock, "Add Chess Clock");
            Selection.activeGameObject = clock;
            EditorSceneManager.MarkSceneDirty(clock.scene);
        }
    }
}
