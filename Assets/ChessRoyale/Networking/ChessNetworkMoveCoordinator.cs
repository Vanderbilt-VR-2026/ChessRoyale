using ChessRoyale;
using UnityEngine;

namespace ChessRoyale.Networking
{
    [DisallowMultipleComponent]
    public sealed class ChessNetworkMoveCoordinator : MonoBehaviour
    {
        // If the room's authority never answers a request (for example it dropped mid-handoff),
        // give the piece back instead of locking the board forever.
        private const float RequestTimeoutSeconds = 4f;

        private byte nextRequestId = 1;
        private byte pendingRequestId;
        private byte pendingPieceId;
        private bool pendingPieceKnown;
        private float requestSentAt;
        private bool requestInFlight;
        private bool listening;

        public static ChessNetworkMoveCoordinator Instance { get; private set; }

        public bool IsOnline =>
            ChessFusionRoomManager.Instance != null
            && ChessFusionRoomManager.Instance.IsConnected
            && ChessNetworkGameState.Instance != null;

        public bool IsWaiting => requestInFlight;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            Unsubscribe();
        }

        private void Update()
        {
            if (requestInFlight && Time.realtimeSinceStartup - requestSentAt > RequestTimeoutSeconds)
            {
                Debug.LogWarning("No answer to the last chess move request. Returning the piece.", this);
                SetWaiting(false);
                if (pendingPieceKnown)
                    ChessGameController.Instance?.FindPiece(pendingPieceId)?.SnapBackToCurrentSquare();
                pendingPieceKnown = false;
            }

            if (listening || ChessGameController.Instance == null)
                return;

            ChessGameController.Instance.PromotionRequested += SubmitPromotion;
            ChessGameController.Instance.RematchRequested += SubmitRematch;
            listening = true;
        }

        public bool BeginLocalGrab(ChessPieceInteractable piece)
        {
            if (!IsOnline || requestInFlight)
                return false;

            return ChessGameController.Instance != null && ChessGameController.Instance.BeginMove(piece);
        }

        public void SubmitReleasedMove(ChessPieceInteractable piece, ChessBoardSquare origin, ChessBoardSquare destination, bool moveAllowed)
        {
            ChessGameController.Instance?.CancelMove();
            if (!moveAllowed || piece == null || origin == null || destination == null || !IsOnline || requestInFlight)
            {
                piece?.SnapBackToCurrentSquare();
                return;
            }

            piece.PresentPredictedDrop(destination);
            pendingPieceId = piece.PieceId;
            pendingPieceKnown = true;
            pendingRequestId = nextRequestId++;
            if (nextRequestId == 0)
                nextRequestId = 1;
            SetWaiting(true);
            ChessNetworkGameState.Instance.RequestMove(
                pendingRequestId,
                piece.PieceId,
                ChessSquareIndex.FromSquare(origin),
                ChessSquareIndex.FromSquare(destination));
        }

        public void OnMoveRejected(byte requestId, byte pieceId)
        {
            if (!requestInFlight || requestId != pendingRequestId)
                return;

            SetWaiting(false);
            ChessPieceInteractable piece = ChessGameController.Instance?.FindPiece(pieceId);
            if (piece != null)
                ChessAudio.PlayAt(ChessSfx.Invalid, piece.transform.position);
            piece?.SnapBackToCurrentSquare();
        }

        public void OnAuthoritativeMoveApplied()
        {
            SetWaiting(false);
            pendingPieceKnown = false;
        }

        private void SubmitPromotion(ChessPieceType type)
        {
            ChessGameController game = ChessGameController.Instance;
            if (!IsOnline || game == null || !game.IsPromotionPending || requestInFlight)
                return;

            pendingPieceKnown = false;
            pendingRequestId = nextRequestId++;
            if (nextRequestId == 0)
                nextRequestId = 1;
            SetWaiting(true);
            ChessNetworkGameState.Instance.RequestPromotion(pendingRequestId, (byte)game.PendingPromotionPieceId, (byte)type);
        }

        private void SubmitRematch()
        {
            ChessGameController game = ChessGameController.Instance;
            if (!IsOnline || game == null || game.Outcome == ChessGameOutcome.InProgress)
                return;

            SetWaiting(false);
            ChessNetworkGameState.Instance.RequestReset();
        }

        private void SetWaiting(bool waiting)
        {
            requestInFlight = waiting;
            if (waiting)
                requestSentAt = Time.realtimeSinceStartup;
            ChessGameController.Instance?.SetAwaitingAuthority(waiting);
        }

        private void Unsubscribe()
        {
            if (!listening || ChessGameController.Instance == null)
                return;

            ChessGameController.Instance.PromotionRequested -= SubmitPromotion;
            ChessGameController.Instance.RematchRequested -= SubmitRematch;
            listening = false;
        }
    }
}
