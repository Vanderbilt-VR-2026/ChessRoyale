#if UNITY_EDITOR
using System;
using System.IO;
using ChessRoyale.Networking;
using Fusion;
using Fusion.Editor;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Editor
{
    // Builds everything online play needs, following the class Fusion + XRI tutorial:
    // - ChessNetworkPlayer.prefab: a lightweight tracked monkey avatar with explicit pose replication.
    // - ChessNetworkGameState.prefab: the shared seats/board/clock object, as a master client object.
    // - FusionNetworkRunner.prefab: NetworkRunner + NetworkEvents + scene manager + Photon Voice.
    // - The "Fusion Network Manager" object in ChessRoyaleVR, with all of the above assigned.
    // Safe to run more than once.
    public static class ChessRoyaleFusionSetup
    {
        private const string PrefabFolder = "Assets/ChessRoyale/Prefabs/Networking";
        private const string PlayerPrefabPath = PrefabFolder + "/ChessNetworkPlayer.prefab";
        private const string GameStatePrefabPath = PrefabFolder + "/ChessNetworkGameState.prefab";
        private const string RunnerPrefabPath = PrefabFolder + "/FusionNetworkRunner.prefab";
        private const string SpeakerPrefabPath = PrefabFolder + "/ChessVoiceSpeaker.prefab";
        private const string ChessScenePath = "Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity";
        private const string ManagerObjectName = "Fusion Network Manager";
        private const string LobbyObjectName = "Chess Fusion Lobby";

        [MenuItem("Tools/Chess Royale/Configure Photon Fusion")]
        public static void Configure()
        {
            EnsureFolder("Assets/ChessRoyale/Prefabs");
            EnsureFolder(PrefabFolder);
            ConfigurePlayerPrefab();
            ConfigureGameStatePrefab();
            GameObject speaker = ConfigureSpeakerPrefab();
            ConfigureRunnerPrefab(speaker);
            NetworkProjectConfigUtilities.RebuildPrefabTable();
            ConfigureChessScene();
            AssetDatabase.SaveAssets();

            if (!ChessFusionAppSettings.HasFusionAppId())
                Debug.LogError(ChessFusionAppSettings.MissingAppIdMessage);
            if (!ChessFusionAppSettings.HasVoiceAppId())
                Debug.LogWarning("Chess Royale voice chat is off: no App Id Voice in " + ChessFusionAppSettings.SettingsAssetPath + ".");

            Debug.Log(
                "Chess Royale Photon Fusion is configured: avatar, shared game state (master client object), " +
                "Fusion Network Runner with voice, and the Fusion Network Manager in ChessRoyaleVR.");
        }

        // ---------- Avatar ----------

        private static void ConfigurePlayerPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (existing != null && existing.GetComponentInChildren<XROrigin>(true) == null && existing.GetComponent<ChessFusionPlayer>() != null)
                return;

            // The old player prefab was a whole second XR rig. Replace it with an avatar.
            if (existing != null)
                AssetDatabase.DeleteAsset(PlayerPrefabPath);

            GameObject root = new GameObject("ChessNetworkPlayer");
            try
            {
                NetworkObject networkObject = root.AddComponent<NetworkObject>();
                root.AddComponent<NetworkTransform>();
                ChessFusionPlayer player = root.AddComponent<ChessFusionPlayer>();

                Transform head = AddTrackedPart(root.transform, "Head");
                AddVisual(head, "Head Visual", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.22f, 0.24f, 0.24f));
                AddVisual(head, "Visor", PrimitiveType.Cube, new Vector3(0f, 0.02f, 0.1f), new Vector3(0.17f, 0.06f, 0.06f));

                Transform leftHand = AddTrackedPart(root.transform, "Left Hand");
                // ChessPlayerHands builds the same gloved hand the local player sees under any "...Controller Visual".
                new GameObject("Left Controller Visual").transform.SetParent(leftHand, false);

                Transform rightHand = AddTrackedPart(root.transform, "Right Hand");
                new GameObject("Right Controller Visual").transform.SetParent(rightHand, false);

                Transform body = AddTrackedPart(root.transform, "Body");
                AddVisual(body, "Body Visual", PrimitiveType.Capsule, Vector3.zero, new Vector3(0.4f, 0.45f, 0.3f));

                SerializedObject serializedPlayer = new SerializedObject(player);
                serializedPlayer.FindProperty("head").objectReferenceValue = head;
                serializedPlayer.FindProperty("leftHand").objectReferenceValue = leftHand;
                serializedPlayer.FindProperty("rightHand").objectReferenceValue = rightHand;
                serializedPlayer.FindProperty("body").objectReferenceValue = body;
                serializedPlayer.ApplyModifiedPropertiesWithoutUndo();

                // Like the tutorial's Network Player: the avatar leaves with its owner, and nobody else can take it.
                networkObject.Flags = (networkObject.Flags | NetworkObjectFlags.DestroyWhenStateAuthorityLeaves)
                    & ~NetworkObjectFlags.MasterClientObject
                    & ~NetworkObjectFlags.AllowStateAuthorityOverride;

                new NetworkObjectBaker().Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static Transform AddTrackedPart(Transform parent, string partName)
        {
            GameObject part = new GameObject(partName);
            part.transform.SetParent(parent, false);
            return part.transform;
        }

        private static void AddVisual(Transform parent, string visualName, PrimitiveType primitive, Vector3 localPosition, Vector3 localScale)
        {
            GameObject visual = GameObject.CreatePrimitive(primitive);
            visual.name = visualName;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localScale = localScale;
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
                UnityEngine.Object.DestroyImmediate(collider);
        }

        // ---------- Shared game state ----------

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
                NetworkObject networkObject = root.GetComponent<NetworkObject>();
                if (networkObject == null)
                    networkObject = root.AddComponent<NetworkObject>();
                if (root.GetComponent<ChessFusionLobbyState>() == null)
                    root.AddComponent<ChessFusionLobbyState>();
                if (root.GetComponent<ChessNetworkGameState>() == null)
                    root.AddComponent<ChessNetworkGameState>();

                // Same flags as the tutorial's lobby controller: always owned by the current master client,
                // and NOT destroyed when the room creator leaves.
                networkObject.Flags = (networkObject.Flags | NetworkObjectFlags.MasterClientObject)
                    & ~NetworkObjectFlags.DestroyWhenStateAuthorityLeaves;

                new NetworkObjectBaker().Bake(root);
                PrefabUtility.SaveAsPrefabAsset(root, GameStatePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ---------- Runner + voice ----------

        private static GameObject ConfigureSpeakerPrefab()
        {
            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(SpeakerPrefabPath);
            if (existing != null && existing.GetComponent<Speaker>() != null)
                return existing;

            GameObject root = new GameObject("ChessVoiceSpeaker");
            try
            {
                AudioSource source = root.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f; // Room-wide voice, like the tutorial.
                root.AddComponent<Speaker>();
                return PrefabUtility.SaveAsPrefabAsset(root, SpeakerPrefabPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureRunnerPrefab(GameObject speakerPrefab)
        {
            bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPrefabPath) != null;
            if (!exists)
            {
                GameObject created = new GameObject("Fusion Network Runner");
                try
                {
                    created.AddComponent<NetworkRunner>();
                    PrefabUtility.SaveAsPrefabAsset(created, RunnerPrefabPath);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(created);
                }
            }

            GameObject root = PrefabUtility.LoadPrefabContents(RunnerPrefabPath);
            try
            {
                if (root.GetComponent<NetworkRunner>() == null)
                    root.AddComponent<NetworkRunner>();
                if (root.GetComponent<NetworkEvents>() == null)
                    root.AddComponent<NetworkEvents>();
                if (root.GetComponent<NetworkSceneManagerDefault>() == null)
                    root.AddComponent<NetworkSceneManagerDefault>();
                if (root.GetComponent<NetworkObjectProviderDefault>() == null)
                    root.AddComponent<NetworkObjectProviderDefault>();

                Recorder recorder = root.GetComponent<Recorder>();
                if (recorder == null)
                    recorder = root.AddComponent<Recorder>();
                FusionVoiceClient voice = root.GetComponent<FusionVoiceClient>();
                if (voice == null)
                    voice = root.AddComponent<FusionVoiceClient>();
                if (root.GetComponent<VoiceLogger>() == null)
                    root.AddComponent<VoiceLogger>();

                SerializedObject serializedVoice = new SerializedObject(voice);
                SetBool(serializedVoice, "usePrimaryRecorder", true);
                SetObject(serializedVoice, "primaryRecorder", recorder);
                SetObject(serializedVoice, "speakerPrefab", speakerPrefab);
                serializedVoice.ApplyModifiedPropertiesWithoutUndo();
                voice.UseFusionAppSettings = true;

                PrefabUtility.SaveAsPrefabAsset(root, RunnerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void SetBool(SerializedObject target, string propertyName, bool value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"Chess Royale setup: '{propertyName}' not found on {target.targetObject.GetType().Name}; set it in the Inspector.");
                return;
            }

            property.boolValue = value;
        }

        private static void SetObject(SerializedObject target, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogWarning($"Chess Royale setup: '{propertyName}' not found on {target.targetObject.GetType().Name}; set it in the Inspector.");
                return;
            }

            property.objectReferenceValue = value;
        }

        // ---------- Scene ----------

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
                    GameObject managerObject = new GameObject(ManagerObjectName);
                    SceneManager.MoveGameObjectToScene(managerObject, chessScene);
                    keeper = managerObject.AddComponent<ChessFusionRoomManager>();
                }
                else if (keeper.gameObject.name != ManagerObjectName)
                {
                    keeper.gameObject.name = ManagerObjectName;
                }

                NetworkObject playerNetworkObject = LoadNetworkObject(PlayerPrefabPath, "Chess Network Player");
                NetworkObject gameStateNetworkObject = LoadNetworkObject(GameStatePrefabPath, "Chess Network Game State");
                GameObject runnerObject = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerPrefabPath);
                NetworkRunner runnerPrefab = runnerObject != null ? runnerObject.GetComponent<NetworkRunner>() : null;
                if (runnerPrefab == null)
                    throw new InvalidOperationException("The Fusion Network Runner prefab needs a NetworkRunner.");

                Transform whiteSpawn = GetOrCreateSpawn(chessScene, "White Player Spawn", new Vector3(0f, 0.42f, -8f), Quaternion.identity);
                Transform blackSpawn = GetOrCreateSpawn(chessScene, "Black Player Spawn", new Vector3(0f, 0.42f, 12f), Quaternion.Euler(0f, 180f, 0f));
                EnsureLobby(chessScene);

                SerializedObject serializedManager = new SerializedObject(keeper);
                serializedManager.FindProperty("runnerPrefab").objectReferenceValue = runnerPrefab;
                serializedManager.FindProperty("playerPrefab").objectReferenceValue = playerNetworkObject;
                serializedManager.FindProperty("gameStatePrefab").objectReferenceValue = gameStateNetworkObject;
                serializedManager.FindProperty("whiteSpawn").objectReferenceValue = whiteSpawn;
                serializedManager.FindProperty("blackSpawn").objectReferenceValue = blackSpawn;
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

        private static NetworkObject LoadNetworkObject(string path, string label)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            NetworkObject networkObject = prefab != null ? prefab.GetComponent<NetworkObject>() : null;
            if (networkObject == null)
                throw new InvalidOperationException($"The {label} prefab needs a baked NetworkObject ({path}).");
            return networkObject;
        }

        private static Transform GetOrCreateSpawn(Scene scene, string objectName, Vector3 position, Quaternion rotation)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                    return root.transform;
            }

            GameObject spawn = new GameObject(objectName);
            SceneManager.MoveGameObjectToScene(spawn, scene);
            spawn.transform.SetPositionAndRotation(position, rotation);
            return spawn.transform;
        }

        private static void EnsureLobby(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.GetComponent<ChessFusionLobbyUI>() != null)
                    return;
            }

            GameObject lobby = new GameObject(LobbyObjectName);
            SceneManager.MoveGameObjectToScene(lobby, scene);
            lobby.AddComponent<ChessFusionLobbyUI>();
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
