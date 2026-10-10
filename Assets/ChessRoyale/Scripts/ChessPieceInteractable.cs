using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable), typeof(ChessPieceState))]
    public sealed class ChessPieceInteractable : MonoBehaviour
    {
        private XRGrabInteractable grabInteractable;
        private Rigidbody body;
        private ChessPieceState state;
        private ChessBoardController board;
        private float baseOffset;

        public ChessPieceState State
        {
            get
            {
                EnsureComponents();
                return state;
            }
        }

        private void Awake()
        {
            EnsureComponents();
            baseOffset = CalculateBaseOffset();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void OnEnable()
        {
            grabInteractable.selectExited.AddListener(OnReleased);
            grabInteractable.selectEntered.AddListener(OnGrabbed);
        }

        private void OnDisable()
        {
            grabInteractable.selectExited.RemoveListener(OnReleased);
            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
        }

        private void Update()
        {
            if (board != null && transform.position.y < -3f)
                ResetToStart();
        }

        private void OnGrabbed(SelectEnterEventArgs _)
        {
            board ??= GetComponentInParent<ChessBoardController>();
            board?.BeginMove(this);
        }

        private void OnReleased(SelectExitEventArgs _)
        {
            board ??= GetComponentInParent<ChessBoardController>();
            if (board == null || !board.TryPlace(this, transform.position))
                ResetToCurrentSquare();
        }

        public void ResetToStart()
        {
            state.ResetState();
            board ??= GetComponentInParent<ChessBoardController>();
            SnapTo(board != null ? board.GetSquare(state.StartingSquare) : null);
        }

        public void ResetToCurrentSquare()
        {
            board ??= GetComponentInParent<ChessBoardController>();
            SnapTo(board != null ? board.GetSquare(state.CurrentSquare) : null);
        }

        public void SnapTo(BoardSquare square)
        {
            EnsureComponents();
            if (square == null)
                return;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = square.SnapPosition + Vector3.up * baseOffset;
            body.rotation = Quaternion.Euler(0f, state.Side == ChessSide.Black ? 180f : 0f, 0f);
        }

        public void SetBoard(ChessBoardController controller)
        {
            board = controller;
        }

        public void SetInteractionEnabled(bool enabled)
        {
            EnsureComponents();
            grabInteractable.enabled = enabled;
        }

        private void EnsureComponents()
        {
            grabInteractable ??= GetComponent<XRGrabInteractable>();
            body ??= GetComponent<Rigidbody>();
            state ??= GetComponent<ChessPieceState>();
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
