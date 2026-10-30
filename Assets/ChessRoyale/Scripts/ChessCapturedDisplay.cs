using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    /// <summary>
    /// Two display stands beside the board that show the captured pieces as miniatures, with a sign
    /// giving how many pieces and how many points each side has lost. Built at runtime from the board
    /// footprint, so it needs no scene wiring; it follows ChessPieceInteractable.PieceCaptured / PieceRestored,
    /// which also covers rematches and multiplayer state sync.
    /// </summary>
    public sealed class ChessCapturedDisplay : MonoBehaviour
    {
        private const float MiniatureScale = 0.3f;
        private const float StandHeight = 1f;
        private const float StandWidth = 2.2f;
        private const float StandLength = 12.5f;
        private const float StandOffset = 3.6f; // distance from the board edge to the stand centre
        private const float SlotSpacing = 1.5f;
        private const float RowSpacing = 1.1f;
        private const int RowCount = 2;

        private static Material bodyMaterial;
        private static Material trimMaterial;

        private sealed class Stand
        {
            public ChessPieceColor lostColor;
            public Vector3 center;
            public Transform pieces;
            public Text titleText;
            public Text summaryText;
            public readonly List<ChessPieceInteractable> order = new List<ChessPieceInteractable>();
            public readonly Dictionary<ChessPieceInteractable, GameObject> models = new Dictionary<ChessPieceInteractable, GameObject>();
        }

        private readonly Stand[] stands = new Stand[2];
        private bool built;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            // Only in scenes that contain a chess game (not the start menu).
            if (ChessGameController.Instance == null || FindAnyObjectByType<ChessCapturedDisplay>() != null)
                return;

            new GameObject("Captured Piece Stands").AddComponent<ChessCapturedDisplay>();
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        private void OnEnable()
        {
            ChessPieceInteractable.PieceCaptured += OnPieceCaptured;
            ChessPieceInteractable.PieceRestored += OnPieceRestored;
        }

        private void OnDisable()
        {
            ChessPieceInteractable.PieceCaptured -= OnPieceCaptured;
            ChessPieceInteractable.PieceRestored -= OnPieceRestored;
        }

        private void OnPieceCaptured(ChessPieceInteractable piece)
        {
            if (piece == null || !EnsureBuilt())
                return;

            Stand stand = stands[(int)piece.PieceColor];
            if (stand.models.ContainsKey(piece))
                return;

            GameObject model = CreateMiniature(piece, stand);
            stand.order.Add(piece);
            stand.models[piece] = model;
            Layout(stand);
        }

        private void OnPieceRestored(ChessPieceInteractable piece)
        {
            if (piece == null || !built)
                return;

            Stand stand = stands[(int)piece.PieceColor];
            if (!stand.models.TryGetValue(piece, out GameObject model))
                return;

            Destroy(model);
            stand.models.Remove(piece);
            stand.order.Remove(piece);
            Layout(stand);
        }

        // ---------------------------------------------------------------- miniatures

        private static GameObject CreateMiniature(ChessPieceInteractable piece, Stand stand)
        {
            // A captured piece is inactive, so the clone is inactive too: its scripts never run before they are removed.
            GameObject model = Instantiate(piece.gameObject);
            model.name = $"{piece.name} (captured)";

            ChessPieceInteractable pieceScript = model.GetComponent<ChessPieceInteractable>();
            if (pieceScript != null)
                DestroyImmediate(pieceScript);
            foreach (XRGrabInteractable grab in model.GetComponentsInChildren<XRGrabInteractable>(true))
                DestroyImmediate(grab);
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                    DestroyImmediate(behaviour);
            }

            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true))
                DestroyImmediate(collider);
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true))
                DestroyImmediate(body);

            model.transform.SetParent(stand.pieces, true);
            model.transform.localScale = piece.transform.lossyScale * MiniatureScale;
            model.transform.rotation = piece.transform.rotation;
            model.SetActive(true);
            return model;
        }

        private static void Layout(Stand stand)
        {
            int points = 0;
            for (int i = 0; i < stand.order.Count; i++)
            {
                points += Value(stand.order[i].PieceType);

                int row = i % RowCount;
                int column = Mathf.Min(i / RowCount, 7);
                float along = -StandLength * 0.5f + SlotSpacing * 0.5f + column * SlotSpacing;
                float across = (row - (RowCount - 1) * 0.5f) * RowSpacing;
                Vector3 slot = new Vector3(stand.center.x + across, StandHeight, stand.center.z + along);

                GameObject model = stand.models[stand.order[i]];
                if (!TryGetBounds(model, out Bounds bounds))
                    continue;

                model.transform.position += new Vector3(slot.x - bounds.center.x, slot.y - bounds.min.y, slot.z - bounds.center.z);
            }

            int count = stand.order.Count;
            stand.summaryText.text = $"{count} piece{(count == 1 ? string.Empty : "s")}  ·  {points} pts";
        }

        private static bool TryGetBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled)
                    continue;

                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return found;
        }

        private static int Value(ChessPieceType type)
        {
            switch (type)
            {
                case ChessPieceType.Pawn: return 1;
                case ChessPieceType.Knight: return 3;
                case ChessPieceType.Bishop: return 3;
                case ChessPieceType.Rook: return 5;
                case ChessPieceType.Queen: return 9;
                default: return 0;
            }
        }

        // ---------------------------------------------------------------- stands

        private bool EnsureBuilt()
        {
            if (built)
                return true;
            if (!ChessBoardSquare.TryGetBoardBounds(out Bounds board))
                return false;

            EnsureMaterials();

            // White's lost pieces on the left of the board, Black's on the right (as seen from White's seat).
            stands[(int)ChessPieceColor.White] = BuildStand(ChessPieceColor.White, new Vector3(board.min.x - StandOffset, 0f, board.center.z), -1f);
            stands[(int)ChessPieceColor.Black] = BuildStand(ChessPieceColor.Black, new Vector3(board.max.x + StandOffset, 0f, board.center.z), 1f);
            built = true;
            return true;
        }

        private Stand BuildStand(ChessPieceColor lostColor, Vector3 center, float side)
        {
            Stand stand = new Stand { lostColor = lostColor, center = center };

            GameObject root = new GameObject($"Captured {lostColor} Stand");
            root.transform.SetParent(transform, false);
            root.transform.position = center;

            const float plateHeight = 0.08f;
            AddBox(root.transform, "Pedestal", new Vector3(center.x, (StandHeight - plateHeight) * 0.5f, center.z),
                new Vector3(StandWidth, StandHeight - plateHeight, StandLength), bodyMaterial);
            AddBox(root.transform, "Top Plate", new Vector3(center.x, StandHeight - plateHeight * 0.5f, center.z),
                new Vector3(StandWidth + 0.12f, plateHeight, StandLength + 0.12f), trimMaterial);

            GameObject pieces = new GameObject("Pieces");
            pieces.transform.SetParent(root.transform, false);
            stand.pieces = pieces.transform;

            BuildSign(stand, root.transform, side);
            stand.summaryText.text = "0 pieces  ·  0 pts";
            return stand;
        }

        // Sign on the face of the pedestal that looks toward the board.
        private static void BuildSign(Stand stand, Transform parent, float side)
        {
            GameObject canvasObject = new GameObject("Sign", typeof(RectTransform));
            canvasObject.transform.SetParent(parent, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(1400f, 260f);
            rect.localScale = Vector3.one * 0.0035f;

            // Inner face looks toward the board; a world canvas is read from the side opposite its forward axis.
            Vector3 inward = new Vector3(-side, 0f, 0f);
            canvasObject.transform.position = new Vector3(stand.center.x + inward.x * (StandWidth * 0.5f + 0.02f), StandHeight * 0.5f, stand.center.z);
            canvasObject.transform.rotation = Quaternion.LookRotation(-inward, Vector3.up);

            Image background = canvasObject.AddComponent<Image>();
            background.color = new Color(0.03f, 0.03f, 0.04f, 0.92f);
            background.raycastTarget = false;

            stand.titleText = CreateText(rect, new Vector2(0f, 55f), new Vector2(1300f, 130f), 110);
            stand.titleText.text = $"{stand.lostColor.ToString().ToUpperInvariant()} LOST";
            stand.summaryText = CreateText(rect, new Vector2(0f, -65f), new Vector2(1300f, 90f), 70);
        }

        private static Text CreateText(RectTransform parent, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.95f, 0.82f, 0.5f, 1f);
            text.raycastTarget = false;
            return text;
        }

        private static void AddBox(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<Renderer>().sharedMaterial = material;

            // Scenery only: no collider, so it never blocks the XR rays.
            Destroy(box.GetComponent<Collider>());
        }

        private static void EnsureMaterials()
        {
            if (bodyMaterial != null && trimMaterial != null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            bodyMaterial = CreateMaterial(shader, "Stand Body", new Color(0.09f, 0.08f, 0.09f), 0.35f);
            trimMaterial = CreateMaterial(shader, "Stand Trim", new Color(0.83f, 0.63f, 0.25f), 0.6f);
        }

        private static Material CreateMaterial(Shader shader, string name, Color color, float smoothness)
        {
            Material material = new Material(shader) { name = name, color = color };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }
    }
}
