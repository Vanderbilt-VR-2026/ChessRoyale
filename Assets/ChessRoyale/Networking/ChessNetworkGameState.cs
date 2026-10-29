using ChessRoyale;
using Fusion;
using UnityEngine;

namespace ChessRoyale.Networking
{
    public struct NetworkPieceState : INetworkStruct
    {
        public byte PieceId;
        public byte Color;
        public byte Type;
        public sbyte SquareIndex;
        public NetworkBool HasMoved;
        public NetworkBool Captured;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChessNetworkGameState : NetworkBehaviour
    {
        [Networked] public PlayerRef WhitePlayer { get; set; }
        [Networked] public PlayerRef BlackPlayer { get; set; }
        [Networked] public NetworkBool MatchReady { get; set; }
        [Networked] public int MoveSequence { get; set; }
        [Networked] public int ActiveColor { get; set; }
        [Networked] public int Outcome { get; set; }
        [Networked] public int PendingPromotionPieceId { get; set; }
        [Networked] public int EnPassantSquareIndex { get; set; }
        [Networked] public int EnPassantPawnId { get; set; }
        [Networked] public int HalfmoveClock { get; set; }
        [Networked] public int WhiteTimeMs { get; set; }
        [Networked] public int BlackTimeMs { get; set; }
        [Networked, Capacity(32)]
        public NetworkArray<NetworkPieceState> Pieces => default;

        private int appliedSequence;
        private ChessRulesSnapshot latestSnapshot;

        public static ChessNetworkGameState Instance { get; private set; }

        public override void Spawned()
        {
            Instance = this;
            if (!Object.HasStateAuthority)
                return;

            PendingPromotionPieceId = -1;
            EnPassantSquareIndex = -1;
            EnPassantPawnId = -1;
            PublishInitialBoard();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            ChessGameController game = ChessGameController.Instance;
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (game == null || lobby == null || !lobby.MatchReady || game.Outcome != ChessGameOutcome.InProgress)
                return;
            if (WhiteTimeMs <= 0 && BlackTimeMs <= 0)
                return;

            int deltaMs = Mathf.Max(1, Mathf.RoundToInt(Runner.DeltaTime * 1000f));
            if (game.ActiveColor == ChessPieceColor.White)
                WhiteTimeMs = Mathf.Max(0, WhiteTimeMs - deltaMs);
            else
                BlackTimeMs = Mathf.Max(0, BlackTimeMs - deltaMs);

            if (WhiteTimeMs > 0 && BlackTimeMs > 0)
                return;

            ChessPieceColor flagged = WhiteTimeMs <= 0 ? ChessPieceColor.White : ChessPieceColor.Black;
            game.EndOnTime(flagged);
            PublishAfterLocalRulesChange();
        }

        public override void Render()
        {
            if (Object != null && Object.IsValid && Object.HasStateAuthority && MoveSequence == 0)
                PublishInitialBoard();

            if (MoveSequence == 0 || MoveSequence == appliedSequence)
                return;

            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.RegisteredPieceCount < 32)
                return;

            appliedSequence = MoveSequence;
            game.ApplyRulesSnapshot(ReadSnapshot(), Object.HasStateAuthority);
            ChessNetworkMoveCoordinator.Instance?.OnAuthoritativeMoveApplied();
        }

        public void SetSeats(PlayerRef white, PlayerRef black, bool matchReady)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            WhitePlayer = white;
            BlackPlayer = black;
            MatchReady = matchReady;
        }

        public void PublishInitialBoard()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority || MoveSequence != 0)
                return;
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessGameController game = ChessGameController.Instance;
            if (lobby == null || !lobby.MatchReady || game == null || game.RegisteredPieceCount < 32)
                return;

            ResetClockTimes();
            game.ResetGame();
            CopySeatsFromLobby();
            WriteSnapshot(game);
        }

        public void RestartMatch()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.RegisteredPieceCount < 32)
                return;

            ResetClockTimes();
            game.ResetGame();
            CopySeatsFromLobby();
            WriteSnapshot(game);
        }

        public void PublishDisconnect(ChessPieceColor disconnected)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.Outcome != ChessGameOutcome.InProgress)
                return;

            game.EndByDisconnect(disconnected);
            PublishAfterLocalRulesChange();
        }

        public void PublishAfterLocalRulesChange()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            CopySeatsFromLobby();
            WriteSnapshot(ChessGameController.Instance);
        }

        public void RequestMove(byte requestId, byte pieceId, sbyte originSquare, sbyte destinationSquare)
        {
            RPC_RequestMove(requestId, pieceId, originSquare, destinationSquare);
        }

        public void RequestPromotion(byte requestId, byte pieceId, byte promotionType)
        {
            RPC_RequestPromotion(requestId, pieceId, promotionType);
        }

        public void RequestReset()
        {
            RPC_RequestReset();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestMove(byte requestId, byte pieceId, sbyte originSquare, sbyte destinationSquare, RpcInfo info = default)
        {
            ChessGameController game = ChessGameController.Instance;
            if (!TryValidateSender(info.Source, out ChessPieceColor seat, out string error))
            {
                Reject(info.Source, requestId, pieceId, error);
                return;
            }

            ChessPieceInteractable piece = game.FindPiece(pieceId);
            if (piece == null || piece.IsCaptured)
            {
                Reject(info.Source, requestId, pieceId, "That piece is not on the board.");
                return;
            }

            if (piece.PieceColor != seat)
            {
                Reject(info.Source, requestId, pieceId, "You can move only your own pieces.");
                return;
            }

            if (game.ActiveColor != seat || game.IsPromotionPending)
            {
                Reject(info.Source, requestId, pieceId, "It is not your turn.");
                return;
            }

            if (ChessSquareIndex.FromSquare(piece.CurrentSquare) != originSquare)
            {
                Reject(info.Source, requestId, pieceId, "The piece was no longer on the expected square.");
                return;
            }

            ChessBoardSquare destination = game.FindSquareByIndex(destinationSquare);
            if (destination == null || !game.TryMove(piece, destination))
            {
                Reject(info.Source, requestId, pieceId, "That move is not legal.");
                return;
            }

            PublishAfterLocalRulesChange();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestPromotion(byte requestId, byte pieceId, byte promotionType, RpcInfo info = default)
        {
            ChessGameController game = ChessGameController.Instance;
            if (!TryValidateSender(info.Source, out ChessPieceColor seat, out string error))
            {
                Reject(info.Source, requestId, pieceId, error);
                return;
            }

            if (!game.IsPromotionPending || game.PendingPromotionPieceId != pieceId)
            {
                Reject(info.Source, requestId, pieceId, "No promotion is waiting for that piece.");
                return;
            }

            ChessPieceInteractable piece = game.FindPiece(pieceId);
            if (piece == null || piece.PieceColor != seat)
            {
                Reject(info.Source, requestId, pieceId, "Only the player who moved that pawn can promote it.");
                return;
            }

            if (promotionType > (byte)ChessPieceType.Queen || promotionType == (byte)ChessPieceType.Pawn || promotionType == (byte)ChessPieceType.King)
            {
                Reject(info.Source, requestId, pieceId, "Choose a queen, rook, bishop, or knight.");
                return;
            }

            if (!game.TryCompletePromotion((ChessPieceType)promotionType))
            {
                Reject(info.Source, requestId, pieceId, "Promotion was not applied.");
                return;
            }

            PublishAfterLocalRulesChange();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestReset(RpcInfo info = default)
        {
            if (!TryValidateRematch(info.Source, out string error))
            {
                Debug.LogWarning(error, this);
                return;
            }

            ChessGameController game = ChessGameController.Instance;
            if (game == null)
                return;

            ResetClockTimes();
            game.ResetGame();
            PublishAfterLocalRulesChange();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RejectMove(PlayerRef requester, byte requestId, byte pieceId, string reason)
        {
            if (Runner != null && requester != Runner.LocalPlayer)
                return;

            Debug.Log($"Chess move rejected: {reason}", this);
            ChessNetworkMoveCoordinator.Instance?.OnMoveRejected(requestId, pieceId);
        }

        private bool TryValidateRematch(PlayerRef sender, out string error)
        {
            if (!TryIdentifySeat(sender, out _, out error))
                return false;

            error = null;
            return true;
        }

        private bool TryValidateSender(PlayerRef sender, out ChessPieceColor seat, out string error)
        {
            ChessGameController game = ChessGameController.Instance;
            if (game == null)
            {
                seat = ChessPieceColor.White;
                error = "The chess board is not ready.";
                return false;
            }

            if (!TryIdentifySeat(sender, out seat, out error))
                return false;

            if (game.Outcome != ChessGameOutcome.InProgress)
            {
                error = "The game is already over.";
                return false;
            }

            return true;
        }

        private bool TryIdentifySeat(PlayerRef sender, out ChessPieceColor seat, out string error)
        {
            seat = ChessPieceColor.White;
            if (ChessGameController.Instance == null)
            {
                error = "The chess board is not ready.";
                return false;
            }

            if (!MatchReady)
            {
                error = "The match is waiting for both players.";
                return false;
            }

            if (sender == WhitePlayer)
                seat = ChessPieceColor.White;
            else if (sender == BlackPlayer)
                seat = ChessPieceColor.Black;
            else
            {
                error = "Only the two seated players can move.";
                return false;
            }

            error = null;
            return true;
        }

        private void ResetClockTimes()
        {
            int startingMs = ChessClock.Instance != null ? ChessClock.Instance.StartingTimeMs : 10 * 60 * 1000;
            WhiteTimeMs = startingMs;
            BlackTimeMs = startingMs;
        }

        private void Reject(PlayerRef sender, byte requestId, byte pieceId, string reason)
        {
            Debug.LogWarning(reason, this);
            RPC_RejectMove(sender, requestId, pieceId, reason);
        }

        private void CopySeatsFromLobby()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (lobby == null)
                return;

            WhitePlayer = lobby.WhitePlayer;
            BlackPlayer = lobby.BlackPlayer;
            MatchReady = lobby.MatchReady;
        }

        private void WriteSnapshot(ChessGameController game)
        {
            if (game == null)
                return;

            ChessRulesSnapshot snapshot = game.ExportRulesSnapshot();
            ActiveColor = (int)snapshot.ActiveColor;
            Outcome = (int)snapshot.Outcome;
            PendingPromotionPieceId = snapshot.PendingPromotionPieceId;
            EnPassantSquareIndex = snapshot.EnPassantSquareIndex;
            EnPassantPawnId = snapshot.EnPassantPawnId;
            HalfmoveClock = snapshot.HalfmoveClock;
            if (snapshot.Pieces != null)
            {
                int count = Mathf.Min(32, snapshot.Pieces.Length);
                for (int index = 0; index < count; index++)
                {
                    ChessPieceSnapshot piece = snapshot.Pieces[index];
                    Pieces.Set(index, new NetworkPieceState
                    {
                        PieceId = (byte)index,
                        Color = (byte)piece.Color,
                        Type = (byte)piece.Type,
                        SquareIndex = piece.SquareIndex,
                        HasMoved = piece.HasMoved,
                        Captured = piece.Captured,
                    });
                }
            }

            latestSnapshot = snapshot;
            MoveSequence++;
        }

        private ChessRulesSnapshot ReadSnapshot()
        {
            ChessPieceSnapshot[] pieces = new ChessPieceSnapshot[32];
            for (int index = 0; index < 32; index++)
            {
                NetworkPieceState piece = Pieces[index];
                pieces[index] = new ChessPieceSnapshot
                {
                    PieceId = piece.PieceId,
                    Color = (ChessPieceColor)piece.Color,
                    Type = (ChessPieceType)piece.Type,
                    SquareIndex = piece.SquareIndex,
                    HasMoved = piece.HasMoved,
                    Captured = piece.Captured,
                };
            }

            ChessRulesSnapshot snapshot = new ChessRulesSnapshot
            {
                Pieces = pieces,
                ActiveColor = (ChessPieceColor)ActiveColor,
                Outcome = (ChessGameOutcome)Outcome,
                PendingPromotionPieceId = (sbyte)PendingPromotionPieceId,
                EnPassantSquareIndex = (sbyte)EnPassantSquareIndex,
                EnPassantPawnId = (sbyte)EnPassantPawnId,
                HalfmoveClock = HalfmoveClock,
            };
            latestSnapshot = snapshot;
            return snapshot;
        }
    }
}
