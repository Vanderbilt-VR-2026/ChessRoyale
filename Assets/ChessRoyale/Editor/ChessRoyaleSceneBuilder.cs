using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace ChessRoyale.Editor
{
    public static class ChessRoyaleSceneBuilder
    {
        private const string ScenePath = "Assets/ChessRoyale/Scenes/ChessRoyaleVR.unity";
        private const float BoardCenterZ = 2f;
        private const float BoardTop = 0.42f;
        private const float SquareSize = 2f;
        private const float BoardWidth = 16f;

        private static readonly string ChessRoot = "Assets/Chess MEGA-pack";
        private static readonly string SciFiRoot = "Assets/Creepy_Cat/3D Scifi Kit Starter Kit_HD";
        private static readonly string XrRigPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        private static Material whiteMaterial;
        private static Material blackMaterial;
        private static Material cyanMaterial;
        private static Material magentaMaterial;
        private static Material sciFiMaterial;
        private static Material boardLightMaterial;
        private static Material boardDarkMaterial;

        [MenuItem("Tools/Chess Royale/Build VR Scene")]
        public static void BuildScene()
        {
            ValidateRequiredAssets();
            ConfigureOpenXR();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMaterials();
            CreateLighting();
            CreateSciFiArena();
            CreateBoard();
            CreatePieces();
            CreateResetPedestal();
            CreateXRPlayer();
            CreateInstructions();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"ChessRoyale: built playable VR scene at {ScenePath}");
        }

        private static void ValidateRequiredAssets()
        {
            string[] required =
            {
                $"{ChessRoot}/prefabs/boards/LowPolyConcrete.prefab",
                $"{ChessRoot}/prefabs/pieces/LowPoly1/kingLowPoly1.prefab",
                $"{SciFiRoot}/Prefabs/Floors/Room_Floor_3x3.prefab",
                XrRigPath,
            };

            string missing = required.FirstOrDefault(path => AssetDatabase.LoadAssetAtPath<GameObject>(path) == null);
            if (missing != null)
                throw new FileNotFoundException($"Required imported asset is missing: {missing}");
        }

        private static void ConfigureOpenXR()
        {
            ConfigureLoader(BuildTargetGroup.Standalone);
            ConfigureLoader(BuildTargetGroup.Android);

            EnableFeature<OculusTouchControllerProfile>(BuildTargetGroup.Standalone);
            EnableFeature<MetaQuestTouchPlusControllerProfile>(BuildTargetGroup.Standalone);
            EnableFeature<OculusTouchControllerProfile>(BuildTargetGroup.Android);
            EnableFeature<MetaQuestTouchPlusControllerProfile>(BuildTargetGroup.Android);
            EnableFeature<MetaQuestFeature>(BuildTargetGroup.Android);

            PlayerSettings.productName = "Chess Royale VR";
            PlayerSettings.companyName = "Vanderbilt VR";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.vanderbiltvr.chessroyale");
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.runInBackground = true;
            ConfigureInteractionLayers();
        }

        private static void ConfigureInteractionLayers()
        {
            UnityEngine.Object settings = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(
                "Assets/XRI/Settings/Resources/InteractionLayerSettings.asset");
            if (settings == null)
                return;

            SerializedObject serialized = new SerializedObject(settings);
            SerializedProperty layerNames = serialized.FindProperty("m_LayerNames");
            if (layerNames != null && layerNames.isArray && layerNames.arraySize > 31)
            {
                layerNames.GetArrayElementAtIndex(31).stringValue = "Teleport";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
        }

        private static void ConfigureLoader(BuildTargetGroup group)
        {
            const string settingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
            XRGeneralSettingsPerBuildTarget perBuildTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
            if (perBuildTarget == null)
            {
                perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perBuildTarget, settingsPath);
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, perBuildTarget, true);
            }

            if (!perBuildTarget.HasManagerSettingsForBuildTarget(group))
                perBuildTarget.CreateDefaultManagerSettingsForBuildTarget(group);

            XRGeneralSettings general = perBuildTarget.SettingsForBuildTarget(group);

            general.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(general.Manager, typeof(OpenXRLoader).FullName, group);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(general.Manager);
        }

        private static void EnableFeature<T>(BuildTargetGroup group) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            T feature = settings != null ? settings.GetFeature<T>() : null;
            if (feature == null)
                return;

            feature.enabled = true;
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(settings);
        }

        private static void CreateMaterials()
        {
            whiteMaterial = CreateMaterial("RoyalWhite", new Color(0.78f, 0.9f, 1f), new Color(0.06f, 0.35f, 0.6f));
            blackMaterial = CreateMaterial("RoyalBlack", new Color(0.08f, 0.04f, 0.13f), new Color(0.55f, 0.04f, 0.75f));
            cyanMaterial = CreateMaterial("NeonCyan", new Color(0.03f, 0.32f, 0.42f), new Color(0f, 1.3f, 2f));
            magentaMaterial = CreateMaterial("NeonMagenta", new Color(0.35f, 0.02f, 0.25f), new Color(1.5f, 0f, 0.9f));
            sciFiMaterial = CreateMaterial("SciFiGunmetal", new Color(0.055f, 0.075f, 0.11f), new Color(0.01f, 0.08f, 0.12f));
            boardLightMaterial = CreateMaterial("BoardSilver", new Color(0.42f, 0.5f, 0.58f), new Color(0.02f, 0.08f, 0.12f));
            boardDarkMaterial = CreateMaterial("BoardObsidian", new Color(0.025f, 0.035f, 0.06f), new Color(0.08f, 0.01f, 0.12f));
        }

        private static Material CreateMaterial(string name, Color baseColor, Color emission)
        {
            string path = $"Assets/ChessRoyale/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_Color", baseColor);
            material.SetFloat("_Metallic", 0.75f);
            material.SetFloat("_Smoothness", 0.82f);
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.17f, 0.26f);
            RenderSettings.ambientEquatorColor = new Color(0.035f, 0.06f, 0.11f);
            RenderSettings.ambientGroundColor = new Color(0.01f, 0.015f, 0.025f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.015f, 0.025f, 0.055f);
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;

            GameObject keyLight = new GameObject("Key Light");
            Light directional = keyLight.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.intensity = 1.15f;
            directional.color = new Color(0.72f, 0.84f, 1f);
            keyLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);

            CreatePointLight("Cyan Arena Light", new Vector3(-7f, 5f, 2f), new Color(0f, 0.8f, 1f));
            CreatePointLight("Magenta Arena Light", new Vector3(7f, 5f, 2f), new Color(1f, 0.05f, 0.65f));
        }

        private static void CreatePointLight(string name, Vector3 position, Color color)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 22f;
            light.intensity = 850f;
            light.color = color;
        }

        private static void CreateSciFiArena()
        {
            GameObject arena = new GameObject("SCI-FI ARENA — Creepy Cat Asset Pack");
            GameObject floorPrefab = LoadPrefab($"{SciFiRoot}/Prefabs/Floors/Room_Floor_3x3.prefab");
            GameObject wallPrefab = LoadPrefab($"{SciFiRoot}/Prefabs/Walls/Wall_Simple_01_Long.prefab");
            GameObject columnPrefab = LoadPrefab($"{SciFiRoot}/Your_Hown_Prefabs/Simple_Column_01.prefab");

            GameObject floor = InstantiatePrefab(floorPrefab, arena.transform, "SciFi Floor");
            floor.transform.position = new Vector3(0f, 0f, BoardCenterZ);
            FitObjectToXZ(floor, 30f, 30f);
            ApplyMaterial(floor, sciFiMaterial);

            CreateWall(wallPrefab, arena.transform, new Vector3(0f, 3f, 16f), Quaternion.identity, new Vector3(5f, 2f, 1f));
            CreateWall(wallPrefab, arena.transform, new Vector3(-15f, 3f, 2f), Quaternion.Euler(0f, 90f, 0f), new Vector3(5f, 2f, 1f));
            CreateWall(wallPrefab, arena.transform, new Vector3(15f, 3f, 2f), Quaternion.Euler(0f, 90f, 0f), new Vector3(5f, 2f, 1f));

            foreach (Vector3 position in new[]
                     {
                         new Vector3(-11f, 0f, -8f), new Vector3(11f, 0f, -8f),
                         new Vector3(-11f, 0f, 12f), new Vector3(11f, 0f, 12f),
                     })
            {
                GameObject column = InstantiatePrefab(columnPrefab, arena.transform, "SciFi Column");
                column.transform.position = position;
                column.transform.localScale *= 1.6f;
                ApplyMaterial(column, cyanMaterial);
            }

            GameObject groundCollider = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundCollider.name = "Arena Floor Collider";
            groundCollider.transform.SetParent(arena.transform);
            groundCollider.transform.position = new Vector3(0f, -0.12f, BoardCenterZ);
            groundCollider.transform.localScale = new Vector3(32f, 0.2f, 32f);
            UnityEngine.Object.DestroyImmediate(groundCollider.GetComponent<MeshRenderer>());
        }

        private static void CreateWall(GameObject prefab, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            GameObject wall = InstantiatePrefab(prefab, parent, "SciFi Wall");
            wall.transform.SetPositionAndRotation(position, rotation);
            wall.transform.localScale = Vector3.Scale(wall.transform.localScale, scale);
            ApplyMaterial(wall, sciFiMaterial);
        }

        private static void CreateBoard()
        {
            GameObject boardPrefab = LoadPrefab($"{ChessRoot}/prefabs/boards/LowPolyConcrete.prefab");
            GameObject board = InstantiatePrefab(boardPrefab, null, "GIANT CHESS BOARD — Chess Mega Set");
            board.transform.position = new Vector3(0f, BoardTop, BoardCenterZ);
            FitObjectToXZ(board, BoardWidth, BoardWidth);
            PlaceBoundsBottomAt(board, BoardTop - 0.2f);
            ApplyMaterial(board, sciFiMaterial);

            GameObject squares = new GameObject("64 Playable Board Squares");
            squares.transform.SetParent(board.transform.parent, true);
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    GameObject square = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    square.name = $"Square {char.ConvertFromUtf32('A' + file)}{rank + 1}";
                    square.transform.SetParent(squares.transform, true);
                    square.transform.position = new Vector3(-7f + file * SquareSize, BoardTop - 0.06f, -5f + rank * SquareSize);
                    square.transform.localScale = new Vector3(1.94f, 0.12f, 1.94f);
                    square.GetComponent<Renderer>().sharedMaterial = (file + rank) % 2 == 0 ? boardLightMaterial : boardDarkMaterial;
                }
            }

            Bounds bounds = CalculateBounds(board);
            BoxCollider collider = board.GetComponent<BoxCollider>();
            if (collider == null)
                collider = board.AddComponent<BoxCollider>();
            float scale = Mathf.Max(0.0001f, board.transform.lossyScale.x);
            collider.center = board.transform.InverseTransformPoint(bounds.center);
            collider.size = new Vector3(bounds.size.x / scale, Mathf.Max(bounds.size.y / scale, 0.08f), bounds.size.z / scale);
        }

        private static void CreatePieces()
        {
            GameObject piecesRoot = new GameObject("HUMAN-SIZED INTERACTIVE CHESS PIECES");
            string[] order = { "rook", "knight", "bishop", "queen", "king", "bishop", "knight", "rook" };

            for (int file = 0; file < 8; file++)
            {
                float x = -7f + file * SquareSize;
                CreatePiece(order[file], false, new Vector3(x, BoardTop, -5f), piecesRoot.transform, file);
                CreatePiece("pawn", false, new Vector3(x, BoardTop, -3f), piecesRoot.transform, file);
                CreatePiece("pawn", true, new Vector3(x, BoardTop, 7f), piecesRoot.transform, file);
                CreatePiece(order[file], true, new Vector3(x, BoardTop, 9f), piecesRoot.transform, file);
            }
        }

        private static void CreatePiece(string type, bool black, Vector3 boardPosition, Transform parent, int file)
        {
            string suffix = black ? " 1" : string.Empty;
            string path = $"{ChessRoot}/prefabs/pieces/LowPoly1/{type}LowPoly1{suffix}.prefab";
            GameObject prefab = LoadPrefab(path);
            GameObject piece = InstantiatePrefab(prefab, parent, $"{(black ? "Black" : "White")} {char.ToUpperInvariant(type[0]) + type.Substring(1)} {file + 1}");

            piece.transform.position = boardPosition;
            piece.transform.rotation = Quaternion.Euler(0f, black ? 180f : 0f, 0f);
            FitObjectToHeight(piece, TargetHeight(type));
            PlaceBoundsBottomAt(piece, BoardTop + 0.03f);

            foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>())
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = black ? blackMaterial : whiteMaterial;
                renderer.sharedMaterials = materials;
            }

            Bounds bounds = CalculateBounds(piece);
            float scale = Mathf.Max(0.0001f, piece.transform.lossyScale.x);
            CapsuleCollider collider = piece.GetComponent<CapsuleCollider>();
            if (collider == null)
                collider = piece.AddComponent<CapsuleCollider>();
            collider.center = piece.transform.InverseTransformPoint(bounds.center);
            collider.height = bounds.size.y / scale;
            collider.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.42f / scale;

            Rigidbody body = piece.GetComponent<Rigidbody>();
            if (body == null)
                body = piece.AddComponent<Rigidbody>();
            body.mass = 18f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearDamping = 1.5f;
            body.angularDamping = 3f;

            XRGrabInteractable grab = piece.GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = piece.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.trackPosition = true;
            grab.trackRotation = true;
            grab.throwOnDetach = false;

            piece.AddComponent<ChessPieceInteractable>();
        }

        private static float TargetHeight(string type)
        {
            switch (type)
            {
                case "king": return 2.35f;
                case "queen": return 2.15f;
                case "bishop": return 1.95f;
                case "knight": return 1.85f;
                case "rook": return 1.65f;
                default: return 1.45f;
            }
        }

        private static void CreateResetPedestal()
        {
            GameObject pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "RESET BOARD — Grab or Ray Select";
            pedestal.transform.position = new Vector3(-10.5f, 0.65f, -7.5f);
            pedestal.transform.localScale = new Vector3(1.2f, 0.65f, 1.2f);
            pedestal.GetComponent<Renderer>().sharedMaterial = magentaMaterial;
            pedestal.AddComponent<XRSimpleInteractable>();
            pedestal.AddComponent<ChessBoardReset>();

            GameObject label = new GameObject("Reset Label");
            label.transform.SetParent(pedestal.transform, false);
            label.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            label.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh text = label.AddComponent<TextMesh>();
            text.text = "RESET";
            text.alignment = TextAlignment.Center;
            text.anchor = TextAnchor.MiddleCenter;
            text.characterSize = 0.24f;
            text.fontSize = 48;
            text.color = Color.white;
        }

        private static void CreateXRPlayer()
        {
            GameObject interactionManager = new GameObject("XR Interaction Manager");
            interactionManager.AddComponent<XRInteractionManager>();

            GameObject rigPrefab = LoadPrefab(XrRigPath);
            GameObject rig = InstantiatePrefab(rigPrefab, null, "XR Origin — Near Grab + Ray Drag");
            // Start with the tracking origin standing on the center of the board.
            // The XR Origin prefab supplies the user's eye-height camera offset.
            rig.transform.position = new Vector3(0f, BoardTop, BoardCenterZ);
            rig.transform.rotation = Quaternion.identity;

            Camera camera = rig.GetComponentInChildren<Camera>(true);
            if (camera != null)
            {
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.005f, 0.008f, 0.02f);
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 150f;
            }
        }

        private static void CreateInstructions()
        {
            GameObject sign = new GameObject("VR Instructions");
            sign.transform.position = new Vector3(0f, 5.25f, 14.8f);
            sign.transform.rotation = Quaternion.identity;
            TextMesh text = sign.AddComponent<TextMesh>();
            text.text = "CHESS ROYALE\nGRIP: NEAR GRAB   •   TRIGGER: RAY DRAG\nSELECT RESET PEDESTAL TO RESTART";
            text.alignment = TextAlignment.Center;
            text.anchor = TextAnchor.MiddleCenter;
            text.characterSize = 0.11f;
            text.fontSize = 64;
            text.color = new Color(0.55f, 0.92f, 1f);
        }

        private static GameObject LoadPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                throw new FileNotFoundException($"Prefab not found: {path}");
            return prefab;
        }

        private static GameObject InstantiatePrefab(GameObject prefab, Transform parent, string name)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.name = name;
            if (parent != null)
                instance.transform.SetParent(parent, true);
            return instance;
        }

        private static void FitObjectToHeight(GameObject target, float height)
        {
            Bounds bounds = CalculateBounds(target);
            float scale = height / Mathf.Max(0.001f, bounds.size.y);
            target.transform.localScale *= scale;
        }

        private static void ApplyMaterial(GameObject target, Material material)
        {
            foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = material;
                renderer.sharedMaterials = materials;
            }
        }

        private static void FitObjectToXZ(GameObject target, float width, float depth)
        {
            Bounds bounds = CalculateBounds(target);
            float scale = Mathf.Min(width / Mathf.Max(0.001f, bounds.size.x), depth / Mathf.Max(0.001f, bounds.size.z));
            target.transform.localScale *= scale;
        }

        private static void PlaceBoundsBottomAt(GameObject target, float y)
        {
            Bounds bounds = CalculateBounds(target);
            target.transform.position += Vector3.up * (y - bounds.min.y);
        }

        private static Bounds CalculateBounds(GameObject target)
        {
            Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(target.transform.position, Vector3.one);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
