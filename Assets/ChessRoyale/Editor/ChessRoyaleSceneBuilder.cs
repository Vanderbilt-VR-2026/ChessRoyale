using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;
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
        private static readonly string ChessRoot = "Assets/Chess MEGA-pack";
        private static readonly string XrRigPath = "Assets/Samples/XR Interaction Toolkit/3.3.2/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        private static Material whiteMaterial;
        private static Material blackMaterial;
        private static Material boardLightMaterial;
        private static Material boardDarkMaterial;
        private static Material hintBoundaryMaterial;

        [MenuItem("Tools/Chess Royale/Build VR Scene")]
        public static void BuildScene()
        {
            ValidateRequiredAssets();
            ConfigureOpenXR();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateMaterials();
            CreateLighting();
            CreateBoard();
            ChessGameController gameController = CreateGameController();
            CreatePieces();
            CreateXRPlayer();
            CreatePromotionPanel(gameController);

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
                $"{ChessRoot}/prefabs/pieces/LowPoly1/kingLowPoly1.prefab",
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
            boardLightMaterial = CreateBoardMaterial("BoardSilver", Color.white);
            boardDarkMaterial = CreateBoardMaterial("BoardObsidian", Color.black);
            hintBoundaryMaterial = CreateMaterial("MoveHintGlow", Color.white, Color.white * 1.5f);
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

        private static Material CreateBoardMaterial(string name, Color color)
        {
            string path = $"Assets/ChessRoyale/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", color);
            material.SetColor("_Color", color);
            material.DisableKeyword("_EMISSION");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CreateLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.22f, 0.22f);
            RenderSettings.fog = false;

            GameObject keyLight = new GameObject("Key Light");
            Light directional = keyLight.AddComponent<Light>();
            directional.type = LightType.Directional;
            directional.intensity = 1.25f;
            directional.color = Color.white;
            keyLight.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
        }

        private static void CreateBoard()
        {
            GameObject squares = new GameObject("CHESS BOARD — 64 Independent Squares");
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    GameObject square = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    square.transform.SetParent(squares.transform, true);
                    square.transform.position = new Vector3(-7f + file * SquareSize, BoardTop + 0.025f, -5f + rank * SquareSize);
                    square.transform.localScale = new Vector3(1.98f, 0.05f, 1.98f);
                    square.GetComponent<Renderer>().sharedMaterial = (file + rank) % 2 == 0 ? boardDarkMaterial : boardLightMaterial;
                    GameObject hintBoundary = CreateHintBoundary(square.transform);
                    square.AddComponent<ChessBoardSquare>().Initialize(file, rank, hintBoundary);
                }
            }
        }

        private static GameObject CreateHintBoundary(Transform square)
        {
            GameObject boundary = new GameObject("Move Hint Boundary");
            boundary.transform.SetParent(square, false);

            CreateHintEdge(boundary.transform, "North", new Vector3(0f, 1.05f, 0.47f), new Vector3(0.96f, 0.65f, 0.035f));
            CreateHintEdge(boundary.transform, "South", new Vector3(0f, 1.05f, -0.47f), new Vector3(0.96f, 0.65f, 0.035f));
            CreateHintEdge(boundary.transform, "East", new Vector3(0.47f, 1.05f, 0f), new Vector3(0.035f, 0.65f, 0.96f));
            CreateHintEdge(boundary.transform, "West", new Vector3(-0.47f, 1.05f, 0f), new Vector3(0.035f, 0.65f, 0.96f));
            boundary.SetActive(false);
            return boundary;
        }

        private static void CreateHintEdge(Transform parent, string name, Vector3 localPosition, Vector3 localScale)
        {
            GameObject edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = name;
            edge.transform.SetParent(parent, false);
            edge.transform.localPosition = localPosition;
            edge.transform.localScale = localScale;
            edge.GetComponent<Renderer>().sharedMaterial = hintBoundaryMaterial;
            UnityEngine.Object.DestroyImmediate(edge.GetComponent<Collider>());
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

        private static ChessGameController CreateGameController()
        {
            GameObject controller = new GameObject("Chess Game Controller — Legal Moves + Turns");
            return controller.AddComponent<ChessGameController>();
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
            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.linearDamping = 1.5f;
            body.angularDamping = 3f;

            XRGrabInteractable grab = piece.GetComponent<XRGrabInteractable>();
            if (grab == null)
                grab = piece.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.trackPosition = true;
            grab.trackRotation = false;
            grab.throwOnDetach = false;

            ChessPieceInteractable interactable = piece.AddComponent<ChessPieceInteractable>();
            interactable.Initialize(
                black ? ChessPieceColor.Black : ChessPieceColor.White,
                (ChessPieceType)Enum.Parse(typeof(ChessPieceType), type, true));

            if (type == "pawn")
                CreatePromotionModels(piece, black);
        }

        private static void CreatePromotionModels(GameObject pawn, bool black)
        {
            GameObject root = new GameObject("Promotion Models");
            root.transform.SetParent(pawn.transform, false);

            string[] promotionTypes = { "queen", "rook", "bishop", "knight" };
            foreach (string type in promotionTypes)
            {
                string suffix = black ? " 1" : string.Empty;
                string path = $"{ChessRoot}/prefabs/pieces/LowPoly1/{type}LowPoly1{suffix}.prefab";
                GameObject model = InstantiatePrefab(LoadPrefab(path), root.transform,
                    char.ToUpperInvariant(type[0]) + type.Substring(1));
                model.transform.position = pawn.transform.position;
                model.transform.rotation = pawn.transform.rotation;
                FitObjectToHeight(model, TargetHeight(type));
                PlaceBoundsBottomAt(model, BoardTop + 0.03f);
                ApplyMaterial(model, black ? blackMaterial : whiteMaterial);
                model.SetActive(false);
            }

            root.SetActive(false);
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

        private static void CreatePromotionPanel(ChessGameController controller)
        {
            GameObject panel = new GameObject("Promotion Panel — Choose Piece", typeof(RectTransform));
            Canvas canvas = panel.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            panel.AddComponent<CanvasScaler>();
            panel.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(920f, 300f);
            panelRect.localScale = Vector3.one * 0.002f;

            Image background = panel.AddComponent<Image>();
            background.color = new Color(0.025f, 0.03f, 0.055f, 0.97f);

            CreatePanelText(panelRect, "PROMOTE PAWN", new Vector2(0f, 95f), new Vector2(860f, 70f), 42);

            string[] labels = { "QUEEN", "ROOK", "BISHOP", "KNIGHT" };
            for (int index = 0; index < labels.Length; index++)
            {
                Button button = CreatePromotionButton(panelRect, labels[index], -315f + index * 210f);
                switch (index)
                {
                    case 0:
                        UnityEventTools.AddPersistentListener(button.onClick, controller.PromoteToQueen);
                        break;
                    case 1:
                        UnityEventTools.AddPersistentListener(button.onClick, controller.PromoteToRook);
                        break;
                    case 2:
                        UnityEventTools.AddPersistentListener(button.onClick, controller.PromoteToBishop);
                        break;
                    default:
                        UnityEventTools.AddPersistentListener(button.onClick, controller.PromoteToKnight);
                        break;
                }
            }

            controller.ConfigurePromotionPanel(panel);
            panel.SetActive(false);
        }

        private static Button CreatePromotionButton(RectTransform parent, string label, float x)
        {
            GameObject buttonObject = new GameObject($"{label} Button", typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(185f, 105f);
            rect.anchoredPosition = new Vector2(x, -35f);

            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.32f, 0.62f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(0.15f, 0.58f, 1f, 1f);
            colors.pressedColor = new Color(0.05f, 0.2f, 0.45f, 1f);
            button.colors = colors;

            CreatePanelText(rect, label, Vector2.zero, rect.sizeDelta, 28);
            return button;
        }

        private static void CreatePanelText(RectTransform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject textObject = new GameObject($"{value} Label", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
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
