using System.Collections.Generic;
using UnityEngine;
using ChessRoyale.Networking;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    public enum ChessPieceColor
    {
        White,
        Black,
    }

    public enum ChessPieceType
    {
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King,
    }

    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public sealed class ChessPieceInteractable : MonoBehaviour
    {
        private const float MaximumSquareDropDistance = 1.45f;
        private const float BoardElevationTolerance = 0.01f;
        private static readonly HashSet<ChessPieceInteractable> ActivePieces = new HashSet<ChessPieceInteractable>();

        [SerializeField] private ChessPieceColor pieceColor;
        [SerializeField] private ChessPieceType pieceType;
        [SerializeField] private ChessBoardSquare currentSquare;
        [SerializeField] private bool hasMoved;

        private XRGrabInteractable grabInteractable;
        private Rigidbody body;
        private Vector3 startingPosition;
        private Quaternion startingRotation;
        private ChessBoardSquare startingSquare;
        private ChessBoardSquare grabOriginSquare;
        private ChessPieceType startingPieceType;
        private Renderer[] startingRenderers;
        private Transform promotionModelsRoot;
        private float baseOffset;
        private bool moveAllowed;
        private bool captured;
        private bool holdDropPresentation;
        private bool boardSettlementPending;
        private Bounds boardBounds;
        private bool hasBoardBounds;

        public ChessPieceColor PieceColor => pieceColor;
        public ChessPieceType PieceType => pieceType;
        public ChessBoardSquare CurrentSquare => currentSquare;
        public ChessBoardSquare StartingSquare => startingSquare;
        public bool HasMoved => hasMoved;
        public bool IsCaptured => captured;
        public byte PieceId { get; private set; } = byte.MaxValue;

        public void Initialize(ChessPieceColor color, ChessPieceType type)
        {
            pieceColor = color;
            pieceType = type;
        }

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            startingPosition = transform.position;
            startingRotation = transform.rotation;
            startingPieceType = pieceType;
            promotionModelsRoot = transform.Find("Promotion Models");
            startingRenderers = GetComponentsInChildren<Renderer>();
            baseOffset = CalculateBaseOffset();

            body.isKinematic = true;
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            grabInteractable.trackRotation = false;
            grabInteractable.throwOnDetach = false;
        }

        private void Start()
        {
            startingSquare = ChessBoardSquare.FindClosest(startingPosition, MaximumSquareDropDistance);
            currentSquare = startingSquare;
            AssignStableId();
            ChessGameController.Instance?.RegisterPiece(this, startingSquare);
        }

        private void OnEnable()
        {
            ActivePieces.Add(this);
            grabInteractable.selectEntered.AddListener(OnGrabbed);
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            ActivePieces.Remove(this);
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        private void Update()
        {
            if (transform.position.y < -3f)
                ResetToStart();
        }

        private void LateUpdate()
        {
            if (!captured && hasBoardBounds && grabInteractable != null && grabInteractable.isSelected)
            {
                ConfineToBoard();
                return;
            }

            if (captured || holdDropPresentation || currentSquare == null || grabInteractable == null || grabInteractable.isSelected)
                return;

            Vector3 restingPosition = GetRestingPosition(currentSquare);
            if (!boardSettlementPending && Mathf.Abs(body.position.y - restingPosition.y) <= BoardElevationTolerance)
                return;

            // XR finishes detach processing late in the frame. Re-applying the board pose here
            // prevents its final hand pose from leaving a released piece above or below the board.
            SnapToSquare(currentSquare);
            boardSettlementPending = false;
        }

        private void OnGrabbed(SelectEnterEventArgs _)
        {
            grabOriginSquare = currentSquare;
            holdDropPresentation = false;
            hasBoardBounds = ChessBoardSquare.TryGetBoardBounds(out boardBounds);
            ChessNetworkMoveCoordinator coordinator = ChessNetworkMoveCoordinator.Instance;
            if (coordinator != null && coordinator.IsOnline)
                moveAllowed = coordinator.BeginLocalGrab(this);
            else
                moveAllowed = ChessGameController.Instance == null || ChessGameController.Instance.BeginMove(this);
            boardSettlementPending = true;
            StopMotion();
            body.rotation = startingRotation;
        }

        private void OnReleased(SelectExitEventArgs _)
        {
            ChessBoardSquare destination = ChessBoardSquare.FindClosest(transform.position, MaximumSquareDropDistance);
            ChessNetworkMoveCoordinator coordinator = ChessNetworkMoveCoordinator.Instance;
            if (coordinator != null && coordinator.IsOnline)
            {
                coordinator.SubmitReleasedMove(this, grabOriginSquare, destination, moveAllowed);
                return;
            }

            ChessGameController game = ChessGameController.Instance;
            if (!moveAllowed || destination == null)
            {
                game?.CancelMove();
                SnapToSquare(grabOriginSquare ?? currentSquare ?? startingSquare);
                return;
            }

            if (game != null)
            {
                if (!game.TryMove(this, destination))
                {
                    SnapToSquare(grabOriginSquare ?? currentSquare ?? startingSquare);
                    return;
                }

                game.FinishMovePresentation();
                return;
            }
            else if (IsOccupiedByAnotherPiece(destination))
            {
                SnapToSquare(grabOriginSquare ?? currentSquare ?? startingSquare);
                return;
            }

            hasMoved |= destination != currentSquare;
            SnapToSquare(destination);
            game?.FinishMovePresentation();
        }

        // A held piece follows the hand, but never leaves the board footprint or sinks below board level;
        // only capture removes it.
        private void ConfineToBoard()
        {
            Vector3 position = transform.position;
            Vector3 confined = new Vector3(
                Mathf.Clamp(position.x, boardBounds.min.x, boardBounds.max.x),
                Mathf.Max(position.y, boardBounds.max.y + baseOffset),
                Mathf.Clamp(position.z, boardBounds.min.z, boardBounds.max.z));
            if (confined == position)
                return;

            transform.position = confined;
            body.position = confined;
        }

        public void ResetToStart()
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            StopMotion();
            hasMoved = false;
            captured = false;
            boardSettlementPending = false;
            RestoreStartingAppearance();
            if (startingSquare != null)
                SnapToSquare(startingSquare);
            else
            {
                body.position = startingPosition;
                body.rotation = startingRotation;
            }
        }

        public void SetDropHold(bool hold)
        {
            holdDropPresentation = hold;
        }

        public void SnapBackToCurrentSquare()
        {
            holdDropPresentation = false;
            boardSettlementPending = true;
            SnapToSquare(currentSquare ?? grabOriginSquare ?? startingSquare);
        }

        public void ApplyAuthoritativeState(ChessBoardSquare square, ChessPieceType type, bool moved, bool isCaptured)
        {
            holdDropPresentation = false;
            boardSettlementPending = true;
            if (isCaptured)
            {
                if (pieceType != startingPieceType && type == startingPieceType)
                    RestoreStartingAppearance();
                hasMoved = moved;
                currentSquare = null;
                if (!captured)
                    Capture();
                else if (gameObject.activeSelf)
                    gameObject.SetActive(false);
                return;
            }

            if (captured || !gameObject.activeSelf)
            {
                captured = false;
                gameObject.SetActive(true);
            }

            if (type == startingPieceType)
            {
                if (pieceType != startingPieceType)
                    RestoreStartingAppearance();
            }
            else if (pieceType != type)
            {
                PromoteTo(type);
            }

            hasMoved = moved;
            if (square != null)
                SnapToSquare(square);
        }

        public void SetTurnEnabled(bool enabled)
        {
            if (grabInteractable != null && !captured)
                grabInteractable.enabled = enabled;
        }

        public void Capture()
        {
            captured = true;
            currentSquare = null;
            boardSettlementPending = false;
            StopMotion();
            gameObject.SetActive(false);
        }

        public void MoveToSquare(ChessBoardSquare square, bool markMoved = true)
        {
            if (square == null)
                return;

            if (markMoved && square != currentSquare)
                hasMoved = true;
            SnapToSquare(square);
            boardSettlementPending = true;
        }

        public void PromoteTo(ChessPieceType promotedType)
        {
            if (startingPieceType != ChessPieceType.Pawn || promotedType == ChessPieceType.Pawn || promotedType == ChessPieceType.King)
                return;

            pieceType = promotedType;
            SetPromotionAppearance(promotedType);
            baseOffset = CalculateBaseOffset();
            UpdateColliderToVisibleModel();
            SnapToSquare(currentSquare);
        }

        private bool IsOccupiedByAnotherPiece(ChessBoardSquare square)
        {
            foreach (ChessPieceInteractable piece in ActivePieces)
            {
                if (piece != this && piece.currentSquare == square)
                    return true;
            }

            return false;
        }

        private void AssignStableId()
        {
            if (startingSquare == null)
                return;

            int file = startingSquare.FileIndex;
            if (pieceColor == ChessPieceColor.White)
                PieceId = (byte)(startingSquare.RankIndex == 0 ? file : 8 + file);
            else
                PieceId = (byte)(startingSquare.RankIndex == 7 ? 24 + file : 16 + file);
        }

        private void SnapToSquare(ChessBoardSquare square)
        {
            if (square == null)
                return;

            Vector3 restingPosition = GetRestingPosition(square);
            StopMotion();
            body.position = restingPosition;
            body.rotation = startingRotation;
            currentSquare = square;
        }

        private void StopMotion()
        {
            if (body.isKinematic)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        private Vector3 GetRestingPosition(ChessBoardSquare square)
        {
            Collider squareCollider = square.GetComponent<Collider>();
            float squareTop = squareCollider != null
                ? squareCollider.bounds.max.y
                : square.Center.y;

            return new Vector3(square.Center.x, squareTop + baseOffset, square.Center.z);
        }

        private void RestoreStartingAppearance()
        {
            pieceType = startingPieceType;
            if (startingRenderers != null)
            {
                foreach (Renderer renderer in startingRenderers)
                    renderer.enabled = true;
            }

            if (promotionModelsRoot != null)
                promotionModelsRoot.gameObject.SetActive(false);

            baseOffset = CalculateBaseOffset();
            UpdateColliderToVisibleModel();
        }

        private void SetPromotionAppearance(ChessPieceType promotedType)
        {
            if (promotionModelsRoot == null)
                return;

            foreach (Renderer renderer in startingRenderers)
                renderer.enabled = false;

            promotionModelsRoot.gameObject.SetActive(true);
            foreach (Transform option in promotionModelsRoot)
                option.gameObject.SetActive(string.Equals(option.name, promotedType.ToString(), System.StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateColliderToVisibleModel()
        {
            List<Renderer> renderers = GetVisibleRenderers();
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (renderers.Count == 0 || capsule == null)
                return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float scale = Mathf.Max(0.0001f, transform.lossyScale.x);
            capsule.center = transform.InverseTransformPoint(bounds.center);
            capsule.height = bounds.size.y / scale;
            capsule.radius = Mathf.Max(bounds.size.x, bounds.size.z) * 0.42f / scale;
        }

        private float CalculateBaseOffset()
        {
            List<Renderer> renderers = GetVisibleRenderers();
            if (renderers.Count == 0)
                return 0f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Count; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return transform.position.y - bounds.min.y;
        }

        private List<Renderer> GetVisibleRenderers()
        {
            List<Renderer> visible = new List<Renderer>();
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
            {
                if (renderer.enabled)
                    visible.Add(renderer);
            }

            return visible;
        }
    }
}
