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
        private int nextChaosNonce = 1;
        private int pendingChaosNonce;
        private bool waitingForChaosLease;
        private bool chaosLeaseAcknowledged;
        private byte chaosCarrySequence;
        private bool bufferedChaosRelease;
        private sbyte bufferedOriginSquare;
        private sbyte bufferedDestinationSquare;

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
                CancelAuthorityCarry();
                SetWaiting(false);
                if (pendingPieceKnown)
                    ReturnPendingPiece();
                ResetChaosRequest();
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

            ChessGameController game = ChessGameController.Instance;
            if (game == null || !game.BeginMove(piece))
                return false;
            if (!game.IsChaosMode)
                return true;

            pendingPieceId = piece.PieceId;
            pendingPieceKnown = true;
            bufferedOriginSquare = ChessSquareIndex.FromSquare(piece.CurrentSquare);
            pendingChaosNonce = nextChaosNonce++;
            if (nextChaosNonce <= 0)
                nextChaosNonce = 1;
            waitingForChaosLease = true;
            chaosLeaseAcknowledged = false;
            bufferedChaosRelease = false;
            SetWaiting(true);
            ChessNetworkGameState.Instance.RequestBeginChaosCarry(
                pendingChaosNonce, piece.PieceId, bufferedOriginSquare, 0, 0);
            return true;
        }

        public void SubmitReleasedMove(ChessPieceInteractable piece, ChessBoardSquare origin, ChessBoardSquare destination, bool moveAllowed)
        {
            ChessGameController.Instance?.CancelMove();
            bool chaos = ChessGameController.Instance != null && ChessGameController.Instance.IsChaosMode;
            bool requestBlocksRelease = requestInFlight && (!chaos || !waitingForChaosLease);
            if (!moveAllowed || piece == null || origin == null || destination == null || !IsOnline || requestBlocksRelease)
            {
                if (chaos)
                    CancelAuthorityCarry();
                piece?.SnapBackToCurrentSquare();
                if (chaos)
                {
                    SetWaiting(false);
                    pendingPieceKnown = false;
                    ResetChaosRequest();
                }
                return;
            }

            if (chaos)
            {
                piece.PresentPredictedDrop(destination);
                pendingPieceId = piece.PieceId;
                pendingPieceKnown = true;
                bufferedOriginSquare = ChessSquareIndex.FromSquare(origin);
                bufferedDestinationSquare = ChessSquareIndex.FromSquare(destination);
                bufferedChaosRelease = true;
                if (chaosLeaseAcknowledged)
                    SendBufferedChaosRelease();
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
            pendingPieceKnown = false;
            ResetChaosRequest();
        }

        public void OnAuthoritativeMoveApplied()
        {
            ChessGameController game = ChessGameController.Instance;
            if (game != null && game.IsChaosMode)
            {
                if (!pendingPieceKnown)
                    return;
                ChessPieceInteractable piece = game.FindPiece(pendingPieceId);
                ChessNetworkGameState state = ChessNetworkGameState.Instance;
                bool resolved = piece == null || piece.IsCaptured || state == null ||
                                state.IsPieceCoolingDown(pendingPieceId) ||
                                ChessSquareIndex.FromSquare(piece.CurrentSquare) != bufferedOriginSquare;
                if (!resolved)
                    return;
            }
            SetWaiting(false);
            pendingPieceKnown = false;
            ResetChaosRequest();
        }

        public void OnNetworkStateRendered()
        {
            ChessGameController game = ChessGameController.Instance;
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            if (game == null || state == null || !game.IsChaosMode || !pendingPieceKnown)
                return;

            ChaosCarryState carry = state.GetLocalCarry();
            if (waitingForChaosLease && carry.Active && carry.ClientGrabNonce == pendingChaosNonce &&
                carry.PieceId == pendingPieceId)
            {
                waitingForChaosLease = false;
                chaosLeaseAcknowledged = true;
                chaosCarrySequence = carry.CarrySequence;
                if (bufferedChaosRelease)
                    SendBufferedChaosRelease();
                else
                    SetWaiting(false);
                return;
            }

            if (chaosLeaseAcknowledged && !carry.Active && !requestInFlight)
            {
                ReturnPendingPiece();
                ResetChaosRequest();
            }
        }

        public void OnChaosCarryRejected(int clientNonce, byte pieceId, string reason)
        {
            if (!pendingPieceKnown || clientNonce != pendingChaosNonce || pieceId != pendingPieceId)
                return;
            SetWaiting(false);
            ReturnPendingPiece();
            ResetChaosRequest();
        }

        public void CancelLocalChaosCarry(ChessPieceInteractable piece)
        {
            if (piece == null || !pendingPieceKnown || piece.PieceId != pendingPieceId)
                return;
            CancelAuthorityCarry();
            SetWaiting(false);
            pendingPieceKnown = false;
            ResetChaosRequest();
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

        private void SendBufferedChaosRelease()
        {
            if (!bufferedChaosRelease || !chaosLeaseAcknowledged || ChessNetworkGameState.Instance == null)
                return;
            bufferedChaosRelease = false;
            waitingForChaosLease = false;
            pendingRequestId = nextRequestId++;
            if (nextRequestId == 0)
                nextRequestId = 1;
            SetWaiting(true);
            ChessNetworkGameState.Instance.RequestChaosRelease(
                pendingRequestId, pendingPieceId, bufferedOriginSquare, bufferedDestinationSquare, chaosCarrySequence);
        }

        private void CancelAuthorityCarry()
        {
            if (pendingPieceKnown && pendingChaosNonce != 0 && ChessNetworkGameState.Instance != null)
                ChessNetworkGameState.Instance.RequestCancelChaosCarry(pendingChaosNonce, pendingPieceId);
        }

        private void ReturnPendingPiece()
        {
            ChessPieceInteractable piece = ChessGameController.Instance?.FindPiece(pendingPieceId);
            piece?.CancelSelectionAndSnapBack();
            pendingPieceKnown = false;
        }

        private void ResetChaosRequest()
        {
            waitingForChaosLease = false;
            chaosLeaseAcknowledged = false;
            bufferedChaosRelease = false;
            pendingChaosNonce = 0;
            chaosCarrySequence = 0;
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
