using ChessRoyale.Networking;
using Fusion;
using UnityEngine;

namespace ChessRoyale
{
    [DisallowMultipleComponent]
    public sealed class ChessChaosPieceFeedback : MonoBehaviour
    {
        private const int RingSegments = 40;
        private static Material ghostMaterial;
        private static Material cooldownMaterial;
        private static Material tetherMaterial;
        private static Material remoteCarryMaterial;

        private ChessPieceInteractable piece;
        private GameObject ghostRoot;
        private LineRenderer ghostRing;
        private TextMesh ghostTimer;
        private GameObject cooldownRoot;
        private LineRenderer cooldownRing;
        private TextMesh cooldownText;
        private LineRenderer tether;
        private GameObject remoteCarryVisual;
        private Vector3 remoteCarryPosition;

        private void Awake()
        {
            piece = GetComponent<ChessPieceInteractable>();
            EnsureMaterials();
            BuildFeedback();
            HideAll();
        }

        private void OnDestroy()
        {
            if (ghostRoot != null) Destroy(ghostRoot);
            if (cooldownRoot != null) Destroy(cooldownRoot);
            if (tether != null) Destroy(tether.gameObject);
            if (remoteCarryVisual != null) Destroy(remoteCarryVisual);
        }

        private void LateUpdate()
        {
            ChessGameController game = ChessGameController.Instance;
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            if (piece == null || game == null || state == null || !game.IsChaosMode || piece.IsCaptured)
            {
                HideAll();
                return;
            }

            float cooldown = state.GetPieceCooldownSeconds(piece.PieceId);
            UpdateCooldown(cooldown);

            ChaosCarryState carry = state.GetCarry(piece.PieceColor);
            bool carryingThisPiece = carry.Active && carry.PieceId == piece.PieceId;
            UpdateCarry(state, carry, carryingThisPiece);
        }

        private void UpdateCooldown(float seconds)
        {
            bool visible = seconds > 0.01f && piece.CurrentSquare != null;
            cooldownRoot.SetActive(visible);
            if (!visible)
                return;

            PositionAtSquare(cooldownRoot.transform, piece.CurrentSquare, 0.035f);
            cooldownText.text = seconds.ToString("0.0");
            FaceCamera(cooldownText.transform);
        }

        private void UpdateCarry(ChessNetworkGameState state, ChaosCarryState carry, bool active)
        {
            ghostRoot.SetActive(active);
            if (!active || piece.CurrentSquare == null)
            {
                tether.gameObject.SetActive(false);
                remoteCarryVisual.SetActive(false);
                return;
            }

            PositionAtSquare(ghostRoot.transform, piece.CurrentSquare, 0.03f);
            ghostTimer.text = $"HOLD {state.GetCarrySeconds(piece.PieceColor):0.0}";
            FaceCamera(ghostTimer.transform);

            NetworkRunner runner = ChessFusionRoomManager.Instance != null ? ChessFusionRoomManager.Instance.Runner : null;
            PlayerRef owner = piece.PieceColor == ChessPieceColor.White ? state.WhitePlayer : state.BlackPlayer;
            bool localCarry = runner != null && runner.LocalPlayer == owner && piece.IsLocallySelected;
            Transform hand = localCarry ? piece.SelectingTransform : ResolveRemoteHand(owner, carry.Hand);

            remoteCarryVisual.SetActive(!localCarry && hand != null);
            Vector3 carriedPosition = piece.transform.position;
            if (!localCarry && hand != null)
            {
                float speed = ChessModePolicies.Interaction(ChessGameplayMode.Chaos).MaximumFollowSpeed(piece.PieceType);
                remoteCarryPosition = Vector3.MoveTowards(remoteCarryPosition, hand.position, speed * Time.deltaTime);
                remoteCarryVisual.transform.position = remoteCarryPosition;
                remoteCarryVisual.transform.rotation = transform.rotation;
                carriedPosition = remoteCarryPosition;
            }

            bool showTether = hand != null;
            tether.gameObject.SetActive(showTether);
            if (showTether)
            {
                tether.SetPosition(0, hand.position);
                tether.SetPosition(1, carriedPosition);
            }
        }

        private static Transform ResolveRemoteHand(PlayerRef owner, byte hand)
        {
            ChessFusionPlayer player = ChessFusionPlayer.FindForPlayer(owner);
            if (player == null)
                return null;
            return hand == 1 ? player.LeftHandTransform : player.RightHandTransform;
        }

        private void BuildFeedback()
        {
            ghostRoot = new GameObject($"{name} Chaos Origin Ghost");
            ghostRing = CreateRing(ghostRoot.transform, ghostMaterial, 0.045f);
            ghostTimer = CreateText(ghostRoot.transform, new Color(0.35f, 0.95f, 1f), 0.12f);
            ghostTimer.transform.localPosition = Vector3.up * 0.22f;

            cooldownRoot = new GameObject($"{name} Chaos Cooldown");
            cooldownRing = CreateRing(cooldownRoot.transform, cooldownMaterial, 0.06f);
            cooldownText = CreateText(cooldownRoot.transform, new Color(1f, 0.78f, 0.18f), 0.15f);
            cooldownText.transform.localPosition = Vector3.up * 0.26f;

            GameObject tetherObject = new GameObject($"{name} Chaos Tether");
            tether = tetherObject.AddComponent<LineRenderer>();
            tether.useWorldSpace = true;
            tether.positionCount = 2;
            tether.startWidth = 0.025f;
            tether.endWidth = 0.01f;
            tether.material = tetherMaterial;
            tether.numCapVertices = 3;

            remoteCarryVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            remoteCarryVisual.name = $"{name} Remote Chaos Carry";
            Collider remoteCollider = remoteCarryVisual.GetComponent<Collider>();
            if (remoteCollider != null) Destroy(remoteCollider);
            Renderer remoteRenderer = remoteCarryVisual.GetComponent<Renderer>();
            if (remoteRenderer != null) remoteRenderer.sharedMaterial = remoteCarryMaterial;
            Bounds bounds = GetPieceBounds();
            remoteCarryVisual.transform.localScale = new Vector3(
                Mathf.Max(0.08f, bounds.size.x),
                Mathf.Max(0.08f, bounds.size.y * 0.5f),
                Mathf.Max(0.08f, bounds.size.z));
            remoteCarryPosition = transform.position;
        }

        private Bounds GetPieceBounds()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(transform.position, Vector3.one * 0.25f);
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return bounds;
        }

        private static LineRenderer CreateRing(Transform parent, Material material, float width)
        {
            GameObject ringObject = new GameObject("Ring");
            ringObject.transform.SetParent(parent, false);
            LineRenderer ring = ringObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = RingSegments;
            ring.startWidth = ring.endWidth = width;
            ring.material = material;
            for (int index = 0; index < RingSegments; index++)
            {
                float angle = index * Mathf.PI * 2f / RingSegments;
                ring.SetPosition(index, new Vector3(Mathf.Cos(angle) * 0.42f, 0f, Mathf.Sin(angle) * 0.42f));
            }
            return ring;
        }

        private static TextMesh CreateText(Transform parent, Color color, float size)
        {
            GameObject textObject = new GameObject("Countdown");
            textObject.transform.SetParent(parent, false);
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = size;
            text.color = color;
            return text;
        }

        private static void PositionAtSquare(Transform target, ChessBoardSquare square, float lift)
        {
            Collider collider = square.GetComponent<Collider>();
            float y = collider != null ? collider.bounds.max.y : square.Center.y;
            target.position = new Vector3(square.Center.x, y + lift, square.Center.z);
            target.rotation = Quaternion.identity;
        }

        private static void FaceCamera(Transform target)
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;
            Vector3 direction = target.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
                target.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private void HideAll()
        {
            if (ghostRoot != null) ghostRoot.SetActive(false);
            if (cooldownRoot != null) cooldownRoot.SetActive(false);
            if (tether != null) tether.gameObject.SetActive(false);
            if (remoteCarryVisual != null) remoteCarryVisual.SetActive(false);
        }

        private static void EnsureMaterials()
        {
            if (ghostMaterial != null)
                return;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            ghostMaterial = MakeMaterial(shader, new Color(0.25f, 0.9f, 1f, 1f));
            cooldownMaterial = MakeMaterial(shader, new Color(1f, 0.65f, 0.08f, 1f));
            tetherMaterial = MakeMaterial(shader, new Color(0.35f, 0.95f, 1f, 1f));
            remoteCarryMaterial = MakeMaterial(shader, new Color(1f, 0.76f, 0.2f, 1f));
        }

        private static Material MakeMaterial(Shader shader, Color color)
        {
            Material material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            return material;
        }
    }
}
