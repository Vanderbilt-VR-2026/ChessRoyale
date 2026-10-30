using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace ChessRoyale
{
    /// <summary>
    /// Replaces the controller models with gloved hands that match the royal hall (white glove, gold cuff).
    /// Hands switch with the situation: in menus they point and show the selection ray; during a match
    /// they are relaxed gloves with no ray, so nothing clutters the board (see ChessHandMode).
    /// </summary>
    public static class ChessPlayerHands
    {
        private static readonly Color GloveWhite = new Color(0.96f, 0.94f, 0.88f, 1f);
        private static readonly Color CuffGold = new Color(0.83f, 0.63f, 0.25f, 1f);
        private static Material gloveMaterial;
        private static Material cuffMaterial;
        private static Material outlineMaterial;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallSceneHands()
        {
            Apply(null);
            ChessHandMode.Ensure();
        }

        public static void Apply(Transform root)
        {
            Transform[] transforms = root == null
                ? Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                : root.GetComponentsInChildren<Transform>(true);

            foreach (Transform transform in transforms)
            {
                if (transform == null || !transform.name.Contains("Controller Visual"))
                    continue;
                if (transform.Find("Player Hand") != null)
                    continue;

                bool left = transform.name.Contains("Left");
                Transform hand = BuildHand(transform, left);
                foreach (Renderer renderer in transform.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.transform == hand || renderer.transform.IsChildOf(hand))
                        continue;
                    renderer.enabled = false;
                }
            }
        }

        private static Transform BuildHand(Transform visual, bool left)
        {
            EnsureMaterials();

            GameObject hand = new GameObject("Player Hand");
            hand.transform.SetParent(visual, false);
            hand.transform.localPosition = Vector3.zero;
            hand.transform.localRotation = Quaternion.identity;
            hand.transform.localScale = Vector3.one;

            AddPart(hand.transform, "Palm", new Vector3(0f, -0.01f, -0.03f), new Vector3(0.078f, 0.028f, 0.09f), gloveMaterial, true);
            AddPart(hand.transform, "Cuff", new Vector3(0f, -0.01f, 0.03f), new Vector3(0.086f, 0.036f, 0.035f), cuffMaterial, false);
            float thumb = left ? 0.048f : -0.048f;
            AddPart(hand.transform, "Thumb", new Vector3(thumb, -0.005f, -0.01f), new Vector3(0.02f, 0.02f, 0.04f), gloveMaterial, true);

            // The index finger is the one next to the thumb.
            Transform[] fingers = new Transform[4];
            for (int finger = 0; finger < fingers.Length; finger++)
            {
                float x = -0.028f + finger * 0.018f;
                fingers[finger] = AddPart(hand.transform, $"Finger {finger + 1}", new Vector3(x, -0.004f, -0.095f), new Vector3(0.014f, 0.014f, 0.05f), gloveMaterial, true);
            }

            hand.AddComponent<ChessHandPose>().Initialize(left, fingers, left ? 3 : 0);
            return hand.transform;
        }

        private static Transform AddPart(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material, bool outlined)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
            if (outlined)
                AddOutline(part.transform);
            return part.transform;
        }

        // Dark-gold inverted shell (only back faces draw) so the white glove stays visible over the white squares.
        private static void AddOutline(Transform part)
        {
            if (outlineMaterial == null)
                return;

            GameObject outline = GameObject.CreatePrimitive(PrimitiveType.Cube);
            outline.name = "Outline";
            outline.transform.SetParent(part, false);
            outline.transform.localPosition = Vector3.zero;
            outline.transform.localRotation = Quaternion.identity;
            outline.transform.localScale = Vector3.one * 1.25f;
            Collider collider = outline.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);
            outline.GetComponent<Renderer>().sharedMaterial = outlineMaterial;
        }

        private static void EnsureMaterials()
        {
            if (gloveMaterial != null && cuffMaterial != null && outlineMaterial != null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return;

            gloveMaterial = new Material(shader) { name = "Hand Glove", color = GloveWhite };
            cuffMaterial = new Material(shader) { name = "Hand Cuff", color = CuffGold };
            outlineMaterial = new Material(shader) { name = "Hand Outline", color = new Color(0.33f, 0.22f, 0.05f, 1f) };
            outlineMaterial.SetFloat("_Cull", 1f);
        }
    }

    /// <summary>Pose and selection-ray state of one hand.</summary>
    public sealed class ChessHandPose : MonoBehaviour
    {
        private static readonly HashSet<ChessHandPose> Active = new HashSet<ChessHandPose>();

        private bool left;
        private Transform[] fingers;
        private Vector3[] restPositions;
        private Vector3[] restScales;
        private int indexFinger;
        private readonly List<XRInteractorLineVisual> rayLines = new List<XRInteractorLineVisual>();

        public static IEnumerable<ChessHandPose> All => Active;

        public void Initialize(bool isLeft, Transform[] fingerTransforms, int index)
        {
            left = isLeft;
            fingers = fingerTransforms;
            indexFinger = index;
            restPositions = new Vector3[fingers.Length];
            restScales = new Vector3[fingers.Length];
            for (int i = 0; i < fingers.Length; i++)
            {
                restPositions[i] = fingers[i].localPosition;
                restScales[i] = fingers[i].localScale;
            }
        }

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        // Menu mode: pointing hand plus the selection ray. Match mode: relaxed glove, no ray.
        public void ApplyMode(bool menu)
        {
            SetPointing(menu);
            SetRayVisible(menu);
        }

        private void SetPointing(bool pointing)
        {
            if (fingers == null)
                return;

            for (int i = 0; i < fingers.Length; i++)
            {
                Vector3 position = restPositions[i];
                Vector3 scale = restScales[i];
                if (pointing)
                {
                    if (i == indexFinger)
                    {
                        scale.z = 0.085f;
                        position.z = -0.115f;
                    }
                    else
                    {
                        // Curled fingers: short and tucked under the knuckles.
                        scale.z = 0.022f;
                        position.z = -0.072f;
                        position.y -= 0.01f;
                    }
                }

                fingers[i].localPosition = position;
                fingers[i].localScale = scale;
            }
        }

        private void SetRayVisible(bool visible)
        {
            if (rayLines.Count == 0)
                FindRayLines();

            foreach (XRInteractorLineVisual line in rayLines)
            {
                if (line == null)
                    continue;

                line.enabled = visible;
                LineRenderer renderer = line.GetComponent<LineRenderer>();
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }

        // Finds this hand's selection-ray visuals; the teleport arc has its own visual and is left alone.
        private void FindRayLines()
        {
            string handName = left ? "Left Controller" : "Right Controller";
            Transform root = transform;
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name == handName)
                {
                    root = current;
                    break;
                }
            }

            foreach (XRInteractorLineVisual line in root.GetComponentsInChildren<XRInteractorLineVisual>(true))
            {
                if (!IsTeleportVisual(line.transform, root))
                    rayLines.Add(line);
            }
        }

        private static bool IsTeleportVisual(Transform line, Transform root)
        {
            for (Transform current = line; current != null && current != root; current = current.parent)
            {
                if (current.name.Contains("Teleport"))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Chooses between the pointing (menu) and gloved (match) hands.</summary>
    public sealed class ChessHandMode : MonoBehaviour
    {
        private static ChessHandMode instance;

        private bool lastMenuMode;
        private bool hasApplied;
        private float nextRefresh;
        private Networking.ChessFusionLobbyUI lobby;

        public static void Ensure()
        {
            if (instance != null)
                return;

            GameObject driver = new GameObject("Chess Hand Mode");
            instance = driver.AddComponent<ChessHandMode>();
        }

        private void Update()
        {
            bool refresh = Time.unscaledTime >= nextRefresh;
            if (refresh)
            {
                nextRefresh = Time.unscaledTime + 1f;
                lobby = FindAnyObjectByType<Networking.ChessFusionLobbyUI>();
            }

            bool menu = ShouldShowMenuHands();

            // Re-apply once a second as well, so hands that appear late (e.g. a joining player) pick up the mode.
            if (hasApplied && menu == lastMenuMode && !refresh)
                return;

            lastMenuMode = menu;
            hasApplied = true;
            foreach (ChessHandPose pose in ChessHandPose.All)
                pose.ApplyMode(menu);
        }

        private bool ShouldShowMenuHands()
        {
            ChessGameController game = ChessGameController.Instance;

            // No chess game in the scene: start screen or other menu.
            if (game == null)
                return true;

            // Game over (result, rematch / play again) and the promotion choice are used with the ray.
            if (game.Outcome != ChessGameOutcome.InProgress || game.IsPromotionPending)
                return true;

            // Multiplayer lobby screens (create, join, waiting for an opponent) before the match starts.
            return lobby != null && lobby.ShowsMenu;
        }
    }
}
