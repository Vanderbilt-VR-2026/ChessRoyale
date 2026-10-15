using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Editor
{
    public static class GameOverScreenBuilder
    {
        private const string ManagerName = "Game Manager";

        [MenuItem("Tools/Chess Royale/Add Game Over Screen")]
        public static void AddToActiveScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before adding the game over screen.");

            GameObject manager = AddTo(SceneManager.GetActiveScene());
            Selection.activeGameObject = manager;
        }

        // Batch entry point: Unity -batchmode -executeMethod ChessRoyale.Editor.GameOverScreenBuilder.AddToScenesFromCommandLine -scenes "A.unity;B.unity"
        public static void AddToScenesFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-scenes");
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Pass -scenes \"Assets/A.unity;Assets/B.unity\".");

            foreach (string path in args[index + 1].Split(';'))
            {
                Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                AddTo(scene);
                EditorSceneManager.SaveScene(scene);
                Debug.Log("Game over screen added to " + path);
            }
        }

        private static GameObject AddTo(Scene scene)
        {
            GameObject manager = null;
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == ManagerName)
                    manager = root;

            if (manager == null)
            {
                manager = new GameObject(ManagerName);
                SceneManager.MoveGameObjectToScene(manager, scene);
                Undo.RegisterCreatedObjectUndo(manager, "Add Game Over Screen");
            }

            if (!manager.TryGetComponent(out PlayerHealth _))
                Undo.AddComponent<PlayerHealth>(manager);
            if (!manager.TryGetComponent(out HealthBar _))
                Undo.AddComponent<HealthBar>(manager);
            if (!manager.TryGetComponent(out GameOverWindow window))
                window = Undo.AddComponent<GameOverWindow>(manager);

            Undo.RecordObject(window, "Build Game Over Window");
            window.BuildWindow();
            EditorUtility.SetDirty(window);
            EditorSceneManager.MarkSceneDirty(scene);
            return manager;
        }
    }
}
