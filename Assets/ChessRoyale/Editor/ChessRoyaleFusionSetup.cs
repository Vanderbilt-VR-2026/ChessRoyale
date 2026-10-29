#if UNITY_EDITOR
using System;
using System.IO;
using ChessRoyale.Networking;
using Fusion;
using Fusion.Editor;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Editor
{
    public static class ChessRoyaleFusionSetup
    {
        private const string XrRigPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        private const string PlayerPrefabPath = "Assets/ChessRoyale/Prefabs/Networking/ChessNetworkPlayer.prefab";
        private const string GameStatePrefabPath = "Assets/ChessRoyale/Prefabs/Networking/ChessNetworkGameState.prefab";
        private const string ChessScenePath = "Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity";
        private const string RoomObjectName = "Chess Fusion Room";

        [MenuItem("Tools/Chess Royale/Configure Photon Fusion")]
        public static void Configure()
        {
            EnsureFolder("Assets/ChessRoyale/Prefabs");
            EnsureFolder("Assets/ChessRoyale/Prefabs/Networking");
            ConfigurePlayerPrefab();
            ConfigureGameStatePrefab();
            ConfigureChessScene();
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            AssetDatabase.SaveAssets();

            if (!ChessFusionAppSettings.HasFusionAppId())
            {
                Debug.LogError(ChessFusionAppSettings.MissingAppIdMessage);
            }

            Debug.Log(
                "Chess Royale Photon Fusion foundation is configured. " +
                "Shared Mode rooms are limited to 2 players. Offline chess is unchanged until a room is created or joined.");
        }

        private static void ConfigurePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(XrRigPath) == null)
                throw new FileNotFoundException($"Required XR rig prefab is missing: {XrRigPath}");

            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) != null;
            GameObject root = PrefabUtility.LoadPrefabContents(exists ? PlayerPrefabPath : XrRigPath);
            try
            {
                if (root.GetComponent<NetworkObject>() == null)
                    root.AddComponent<NetworkObject>();

                ChessFusionPlayer player = root.GetComponent<ChessFusionPlayer>();
                if (player == null)
                    player = root.AddComponent<ChessFusionPlayer>();

                XROrigin origin = root.GetComponent<XROrigin>();
                if (origin == null)
                    origin = root.GetComponentInChildren<XROrigin>(true);
                if (origin == null)
                    throw new InvalidOperationException("The network player prefab needs an XR Origin.");

                Camera camera = origin.Camera != null ? origin.Camera : root.GetComponentInChildren<Camera>(true);
                SerializedObject serializedPlayer = new SerializedObject(player);
                serializedPlayer.FindProperty("xrOrigin").objectReferenceValue = origin;
                serializedPlayer.FindProperty("xrCamera").objectReferenceValue = camera;
                serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

                new NetworkObjectBaker().Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureGameStatePrefab()
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(GameStatePrefabPath) != null;
            if (!exists)
            {
                GameObject created = new GameObject("ChessNetworkGameState");
                try
                {
                    created.AddComponent<NetworkObject>();
                    PrefabUtility.SaveAsPrefabAsset(created, GameStatePrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }

            GameObject root = PrefabUtility.LoadPrefabContents(GameStatePrefabPath);
            try
            {
                if (root.GetComponent<NetworkObject>() == null)
                    root.AddComponent<NetworkObject>();

                new NetworkObjectBaker().Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root, GameStatePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureChessScene()
        {
            Scene previousActiveScene = SceneManager.GetActiveScene();
            Scene chessScene = default;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path == ChessScenePath)
                    chessScene = scene;
            }

            bool openedScene = !chessScene.IsValid();
            if (openedScene)
                chessScene = EditorSceneManager.OpenScene(ChessScenePath, OpenSceneMode.Additive);

            try
            {
                ChessFusionRoomManager[] managers = Resources.FindObjectsOfTypeAll<ChessFusionRoomManager>();
                ChessFusionRoomManager keeper = null;
                foreach (ChessFusionRoomManager manager in managers)
                {
                    if (manager == null || !manager.gameObject.scene.IsValid() || manager.gameObject.scene != chessScene)
                        continue;

                    if (keeper == null)
                    {
                        keeper = manager;
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(manager.gameObject);
                }

                if (keeper == null)
                {
                    GameObject roomObject = new GameObject(RoomObjectName);
                    SceneManager.MoveGameObjectToScene(roomObject, chessScene);
                    keeper = roomObject.AddComponent<ChessFusionRoomManager>();
                }
                else if (keeper.gameObject.name != RoomObjectName)
                {
                    keeper.gameObject.name = RoomObjectName;
                }

                GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
                NetworkObject playerNetworkObject = playerPrefab != null ? playerPrefab.GetComponent<NetworkObject>() : null;
                if (playerNetworkObject == null)
                    throw new InvalidOperationException("The Chess Network Player prefab needs a baked NetworkObject.");

                SerializedObject serializedManager = new SerializedObject(keeper);
                serializedManager.FindProperty("playerPrefab").objectReferenceValue = playerNetworkObject;
                serializedManager.ApplyModifiedPropertiesWithoutUndo();

                EditorSceneManager.MarkSceneDirty(chessScene);
                EditorSceneManager.SaveScene(chessScene);
            }
            finally
            {
                if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
                    SceneManager.SetActiveScene(previousActiveScene);
                if (openedScene && chessScene.IsValid())
                    EditorSceneManager.CloseScene(chessScene, true);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folderName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
                throw new InvalidOperationException($"Cannot create folder '{path}'.");

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
#endif
