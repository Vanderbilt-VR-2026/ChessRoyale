using System.Collections.Generic;
using UnityEngine;
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
        private float baseOffset;

        public ChessPieceColor PieceColor => pieceColor;
        public ChessPieceType PieceType => pieceType;
        public ChessBoardSquare CurrentSquare => currentSquare;
        public bool HasMoved => hasMoved;

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

        private void OnGrabbed(SelectEnterEventArgs _)
        {
            grabOriginSquare = currentSquare;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.rotation = startingRotation;
        }

        private void OnReleased(SelectExitEventArgs _)
        {
            ChessBoardSquare destination = ChessBoardSquare.FindClosest(transform.position, MaximumSquareDropDistance);
            if (destination == null || IsOccupiedByAnotherPiece(destination))
            {
                SnapToSquare(grabOriginSquare ?? currentSquare ?? startingSquare);
                return;
            }

            if (destination != currentSquare)
                hasMoved = true;
            SnapToSquare(destination);
        }

        public void ResetToStart()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            hasMoved = false;
            if (startingSquare != null)
                SnapToSquare(startingSquare);
            else
            {
                body.position = startingPosition;
                body.rotation = startingRotation;
            }
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

        private void SnapToSquare(ChessBoardSquare square)
        {
            if (square == null)
                return;

            Collider squareCollider = square.GetComponent<Collider>();
            float squareTop = squareCollider != null
                ? squareCollider.bounds.max.y
                : square.Center.y;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = new Vector3(square.Center.x, squareTop + baseOffset, square.Center.z);
            body.rotation = startingRotation;
            currentSquare = square;
        }

        private float CalculateBaseOffset()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return transform.position.y - bounds.min.y;
        }
    }
}
