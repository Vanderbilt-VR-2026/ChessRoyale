using System.Collections.Generic;
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

    public struct ChaosCarryState : INetworkStruct
    {
        public NetworkBool Active;
        public byte PieceId;
        public sbyte OriginSquare;
        public byte Hand;
        public byte InteractionMode;
        public int ClientGrabNonce;
        public int ExpiresAtTick;
        public byte CarrySequence;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChessNetworkGameState : NetworkBehaviour, IStateAuthorityChanged
    {
        // How long a player who drops mid-game has to rejoin with the same room code before
        // the other player is awarded the win.
        public const float ReconnectWindowSeconds = 30f;

        [Networked] public PlayerRef WhitePlayer { get; set; }
        [Networked] public PlayerRef BlackPlayer { get; set; }
        [Networked] public NetworkBool MatchReady { get; set; }
        [Networked] public int MoveSequence { get; set; }
        [Networked] public int RoundSequence { get; set; }
        [Networked] public int RoundChess960Index { get; set; }
        [Networked] public byte WhiteChecksDelivered { get; set; }
        [Networked] public byte BlackChecksDelivered { get; set; }
        [Networked] public int ActiveColor { get; set; }
        [Networked] public int Outcome { get; set; }
        [Networked] public int PendingPromotionPieceId { get; set; }
        [Networked] public int EnPassantSquareIndex { get; set; }
        [Networked] public int EnPassantPawnId { get; set; }
        [Networked] public int HalfmoveClock { get; set; }
        [Networked] public byte LastWhiteMove { get; set; }
        [Networked] public byte LastBlackMove { get; set; }
        [Networked] public int WhiteTimeMs { get; set; }
        [Networked] public int BlackTimeMs { get; set; }
        [Networked] public NetworkBool WhiteRematch { get; set; }
        [Networked] public NetworkBool BlackRematch { get; set; }
        [Networked] public ChaosCarryState WhiteCarry { get; set; }
        [Networked] public ChaosCarryState BlackCarry { get; set; }
        [Networked] public int ChaosBatchSequence { get; set; }
        // Shared deadline in Fusion simulation time, like the tutorial's lobby countdown. Zero = no one is missing.
        [Networked] public float ReconnectDeadline { get; set; }
        [Networked] public int DroppedColor { get; set; }
        [Networked, Capacity(32)]
        public NetworkArray<NetworkPieceState> Pieces => default;
        [Networked, Capacity(32)]
        public NetworkArray<int> PieceCooldownEndTick => default;

        private int appliedSequence;
        private ChessRulesSnapshot latestSnapshot;
        private int lastWhiteRequestId;
        private int lastBlackRequestId;
        private readonly List<PendingChaosMove> pendingChaosMoves = new List<PendingChaosMove>(2);
        private int chaosBatchCloseTick;
        private byte nextCarrySequence = 1;

        private struct PendingChaosMove
        {
            public PlayerRef Player;
            public ChessPieceColor Seat;
            public byte RequestId;
            public byte PieceId;
            public sbyte OriginSquare;
            public sbyte DestinationSquare;
            public int ReceivedTick;
        }

        public bool LocalPlayerAcceptedRematch
        {
            get
            {
                if (Runner == null)
                    return false;
                if (Runner.LocalPlayer == WhitePlayer)
                    return WhiteRematch;
                if (Runner.LocalPlayer == BlackPlayer)
                    return BlackRematch;
                return false;
            }
        }

        public bool OtherPlayerAcceptedRematch
        {
            get
            {
                if (Runner == null)
                    return false;
                if (Runner.LocalPlayer == WhitePlayer)
                    return BlackRematch;
                if (Runner.LocalPlayer == BlackPlayer)
                    return WhiteRematch;
                return false;
            }
        }

        public static ChessNetworkGameState Instance { get; private set; }

        public float ReconnectSecondsLeft
        {
            get
            {
                if (Object == null || !Object.IsValid || Runner == null || ReconnectDeadline <= 0f)
                    return 0f;
                return Mathf.Max(0f, ReconnectDeadline - Runner.SimulationTime);
            }
        }

        public bool IsChaosMatch => ChessFusionLobbyState.Instance != null && ChessFusionLobbyState.Instance.MatchConfig.IsChaos;

        public int CurrentSimulationTick => Runner != null ? Runner.Tick.Raw : 0;

        public bool IsPieceCoolingDown(byte pieceId)
        {
            return pieceId < 32 && PieceCooldownEndTick[pieceId] > CurrentSimulationTick;
        }

        public float GetPieceCooldownSeconds(byte pieceId)
        {
            if (pieceId >= 32 || Runner == null)
                return 0f;
            int ticks = Mathf.Max(0, PieceCooldownEndTick[pieceId] - CurrentSimulationTick);
            return ticks / Mathf.Max(1f, Runner.TickRate);
        }

        public ChaosCarryState GetCarry(ChessPieceColor color)
        {
            return color == ChessPieceColor.White ? WhiteCarry : BlackCarry;
        }

        public ChaosCarryState GetLocalCarry()
        {
            if (Runner == null)
                return default;
            if (Runner.LocalPlayer == WhitePlayer)
                return WhiteCarry;
            if (Runner.LocalPlayer == BlackPlayer)
                return BlackCarry;
            return default;
        }

        public float GetCarrySeconds(ChessPieceColor color)
        {
            ChaosCarryState carry = GetCarry(color);
            if (!carry.Active || Runner == null)
                return 0f;
            return Mathf.Max(0, carry.ExpiresAtTick - CurrentSimulationTick) / Mathf.Max(1f, Runner.TickRate);
        }

        public override void Spawned()
        {
            Instance = this;
            if (!Object.HasStateAuthority)
                return;

            PendingPromotionPieceId = -1;
            EnPassantSquareIndex = -1;
            EnPassantPawnId = -1;
            WhiteCarry = default;
            BlackCarry = default;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;
        }

        // The new master after a handoff. Move-request numbering is per authority, so start fresh;
        // the lobby state re-checks seats on its own handoff callback.
        public void StateAuthorityChanged()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            lastWhiteRequestId = 0;
            lastBlackRequestId = 0;
            pendingChaosMoves.Clear();
            chaosBatchCloseTick = 0;
            WhiteCarry = default;
            BlackCarry = default;
        }

        // Called by the lobby state whenever seats change. A seat emptying mid-game opens a window
        // for that player to come back; both seats filling again closes it.
        public void UpdateReconnectWindow(PlayerRef white, PlayerRef black, bool matchReady)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            if (matchReady)
            {
                ReconnectDeadline = 0f;
                return;
            }

            if (ReconnectDeadline > 0f)
                return;

            ChessGameController game = ChessGameController.Instance;
            bool gameUnderway = game != null && game.Outcome == ChessGameOutcome.InProgress && MoveSequence > 1;
            if (!gameUnderway || (white == PlayerRef.None && black == PlayerRef.None))
                return;

            DroppedColor = (int)(white == PlayerRef.None ? ChessPieceColor.White : ChessPieceColor.Black);
            ReconnectDeadline = Runner.SimulationTime + ReconnectWindowSeconds;
            Debug.Log($"{(ChessPieceColor)DroppedColor} left mid-game. Holding the seat for {ReconnectWindowSeconds:0} seconds.", this);
        }

        public override void FixedUpdateNetwork()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            ChessGameController game = ChessGameController.Instance;
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (ReconnectDeadline > 0f && Runner.SimulationTime >= ReconnectDeadline)
            {
                ReconnectDeadline = 0f;
                if (lobby == null || !lobby.MatchReady)
                    PublishDisconnect((ChessPieceColor)DroppedColor);
            }

            if (lobby != null && lobby.MatchConfig.IsChaos)
            {
                ExpireChaosCarries();
                if (pendingChaosMoves.Count > 0 && chaosBatchCloseTick > 0 && Runner.Tick.Raw >= chaosBatchCloseTick)
                    ResolveChaosBatch();
                return;
            }

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
            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.RegisteredPieceCount < 32)
                return;

            ChessNetworkMoveCoordinator.Instance?.OnNetworkStateRendered();
            if (game.IsChaosMode)
                game.RefreshLocalControls();
            if (MoveSequence == 0 || MoveSequence == appliedSequence)
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

        public void ClearRematchForChangedSeat(PlayerRef previousWhite, PlayerRef white, PlayerRef previousBlack, PlayerRef black)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            // A different player in a seat (e.g. someone rejoining from another headset) numbers their
            // move requests from 1 again, so forget the previous occupant's last request number.
            if (previousWhite != white)
            {
                WhiteRematch = false;
                lastWhiteRequestId = 0;
            }
            if (previousBlack != black)
            {
                BlackRematch = false;
                lastBlackRequestId = 0;
            }
        }

        public bool BeginConfiguredMatch(ChessMatchConfig config)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority || MoveSequence != 0 || !config.IsSupported)
                return false;
            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.RegisteredPieceCount < 32)
                return false;

            RoundSequence++;
            RoundChess960Index = config.StartingLayout == (byte)ChessStartingLayout.Chess960
                ? Random.Range(0, 960)
                : 0;
            ClearRematchAgreement();
            ClearChaosRoundState();
            ResetClockTimes();
            game.ResetGame(config, RoundChess960Index);
            CopySeatsFromLobby();
            WriteSnapshot(game);
            RPC_RepositionPlayers();
            return true;
        }

        public bool RestartMatch()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return false;

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessGameController game = ChessGameController.Instance;
            if (lobby == null || !lobby.MatchConfig.IsSupported || game == null || game.RegisteredPieceCount < 32)
                return false;

            RoundSequence++;
            if (lobby.MatchConfig.StartingLayout == (byte)ChessStartingLayout.Chess960)
                RoundChess960Index = (RoundChess960Index + Random.Range(1, 960)) % 960;
            else
                RoundChess960Index = 0;
            ClearRematchAgreement();
            ClearChaosRoundState();
            ResetClockTimes();
            game.ResetGame(lobby.MatchConfig, RoundChess960Index);
            CopySeatsFromLobby();
            WriteSnapshot(game);
            lobby.MarkRematchPlaying();
            RPC_RepositionPlayers();
            return true;
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
            RPC_RequestMove(RoundSequence, requestId, pieceId, originSquare, destinationSquare);
        }

        public void RequestBeginChaosCarry(int clientNonce, byte pieceId, sbyte originSquare, byte hand, byte interactionMode)
        {
            RPC_RequestBeginChaosCarry(RoundSequence, clientNonce, pieceId, originSquare, hand, interactionMode);
        }

        public void RequestChaosRelease(byte requestId, byte pieceId, sbyte originSquare, sbyte destinationSquare,
            byte carrySequence)
        {
            RPC_RequestChaosRelease(RoundSequence, requestId, pieceId, originSquare, destinationSquare, carrySequence);
        }

        public void RequestCancelChaosCarry(int clientNonce, byte pieceId)
        {
            RPC_RequestCancelChaosCarry(RoundSequence, clientNonce, pieceId);
        }

        public void RequestPromotion(byte requestId, byte pieceId, byte promotionType)
        {
            RPC_RequestPromotion(RoundSequence, requestId, pieceId, promotionType);
        }

        public void RequestReset()
        {
            RPC_RequestReset();
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestBeginChaosCarry(int roundSequence, int clientNonce, byte pieceId, sbyte originSquare,
            byte hand, byte interactionMode, RpcInfo info = default)
        {
            if (roundSequence != RoundSequence || !IsChaosMatch)
            {
                RejectCarry(info.Source, clientNonce, pieceId, "That carry belongs to another round or mode.");
                return;
            }

            if (!TryValidateSender(info.Source, out ChessPieceColor seat, out string error))
            {
                RejectCarry(info.Source, clientNonce, pieceId, error);
                return;
            }

            ChessGameController game = ChessGameController.Instance;
            ChessPieceInteractable piece = game != null ? game.FindPiece(pieceId) : null;
            ChaosCarryState current = GetCarry(seat);
            if (current.Active || HasPendingChaosMove(seat))
                error = "Finish the current Chaos move first.";
            else if (piece == null || piece.IsCaptured || piece.CurrentSquare == null)
                error = "That piece is not on the board.";
            else if (piece.PieceColor != seat)
                error = "You can carry only your own pieces.";
            else if (ChessSquareIndex.FromSquare(piece.CurrentSquare) != originSquare)
                error = "The piece moved before the carry began.";
            else if (IsPieceCoolingDown(pieceId))
                error = "That piece is cooling down.";

            if (!string.IsNullOrEmpty(error))
            {
                RejectCarry(info.Source, clientNonce, pieceId, error);
                return;
            }

            int durationTicks = Mathf.Max(1, Mathf.CeilToInt(
                ChessModePolicies.Interaction(ChessGameplayMode.Chaos).CarryDurationSeconds * Runner.TickRate));
            byte sequence = nextCarrySequence++;
            if (nextCarrySequence == 0)
                nextCarrySequence = 1;
            SetCarry(seat, new ChaosCarryState
            {
                Active = true,
                PieceId = pieceId,
                OriginSquare = originSquare,
                Hand = hand,
                InteractionMode = interactionMode,
                ClientGrabNonce = clientNonce,
                ExpiresAtTick = Runner.Tick.Raw + durationTicks,
                CarrySequence = sequence,
            });
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestCancelChaosCarry(int roundSequence, int clientNonce, byte pieceId, RpcInfo info = default)
        {
            if (roundSequence != RoundSequence || !TryIdentifySeat(info.Source, out ChessPieceColor seat, out _))
                return;
            ChaosCarryState carry = GetCarry(seat);
            if (carry.Active && carry.ClientGrabNonce == clientNonce && carry.PieceId == pieceId)
                SetCarry(seat, default);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestChaosRelease(int roundSequence, byte requestId, byte pieceId, sbyte originSquare,
            sbyte destinationSquare, byte carrySequence, RpcInfo info = default)
        {
            if (roundSequence != RoundSequence || !IsChaosMatch)
            {
                Reject(info.Source, requestId, pieceId, "That release belongs to another round or mode.");
                return;
            }

            if (!TryValidateSender(info.Source, out ChessPieceColor seat, out string error))
            {
                Reject(info.Source, requestId, pieceId, error);
                return;
            }
            if (!TryAcceptRequest(seat, requestId))
                return;

            ChaosCarryState carry = GetCarry(seat);
            ChessGameController game = ChessGameController.Instance;
            ChessPieceInteractable piece = game != null ? game.FindPiece(pieceId) : null;
            ChessBoardSquare destination = game != null ? game.FindSquareByIndex(destinationSquare) : null;
            if (!carry.Active || carry.PieceId != pieceId || carry.CarrySequence != carrySequence)
                error = "That carry is no longer active.";
            else if (Runner.Tick.Raw > carry.ExpiresAtTick)
                error = "The carry timer expired.";
            else if (piece == null || piece.IsCaptured || ChessSquareIndex.FromSquare(piece.CurrentSquare) != originSquare ||
                     carry.OriginSquare != originSquare)
                error = "The piece was no longer on the expected square.";
            else if (IsPieceCoolingDown(pieceId))
                error = "That piece is cooling down.";
            else if (!game.IsChaosMoveLegal(piece, destination))
                error = "That Chaos move is not legal.";

            if (!string.IsNullOrEmpty(error))
            {
                SetCarry(seat, default);
                Reject(info.Source, requestId, pieceId, error);
                return;
            }

            pendingChaosMoves.Add(new PendingChaosMove
            {
                Player = info.Source,
                Seat = seat,
                RequestId = requestId,
                PieceId = pieceId,
                OriginSquare = originSquare,
                DestinationSquare = destinationSquare,
                ReceivedTick = Runner.Tick.Raw,
            });
            SetCarry(seat, default);
            int ticksPerWindow = Mathf.Max(1, Mathf.CeilToInt(Runner.TickRate * 0.1f));
            int currentWindow = Runner.Tick.Raw / ticksPerWindow;
            chaosBatchCloseTick = (currentWindow + 1) * ticksPerWindow;
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_RequestMove(int roundSequence, byte requestId, byte pieceId, sbyte originSquare, sbyte destinationSquare, RpcInfo info = default)
        {
            if (IsChaosMatch)
            {
                Reject(info.Source, requestId, pieceId, "Chaos moves must begin with an authoritative carry.");
                return;
            }
            if (roundSequence != RoundSequence)
            {
                Reject(info.Source, requestId, pieceId, "That move belongs to an earlier round.");
                return;
            }

            ChessGameController game = ChessGameController.Instance;
            if (!TryValidateSender(info.Source, out ChessPieceColor seat, out string error))
            {
                Reject(info.Source, requestId, pieceId, error);
                return;
            }

            if (!TryAcceptRequest(seat, requestId))
                return;

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
        private void RPC_RequestPromotion(int roundSequence, byte requestId, byte pieceId, byte promotionType, RpcInfo info = default)
        {
            if (roundSequence != RoundSequence)
            {
                Reject(info.Source, requestId, pieceId, "That promotion belongs to an earlier round.");
                return;
            }

            ChessGameController game = ChessGameController.Instance;
            if (!TryValidateSender(info.Source, out ChessPieceColor seat, out string error))
            {
                Reject(info.Source, requestId, pieceId, error);
                return;
            }

            if (!TryAcceptRequest(seat, requestId))
                return;

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
            if (!TryValidateRematch(info.Source, out ChessPieceColor seat, out string error))
            {
                Debug.LogWarning(error, this);
                return;
            }

            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.Outcome == ChessGameOutcome.InProgress)
            {
                Debug.LogWarning("Rematch is available after the game ends.", this);
                return;
            }

            if (seat == ChessPieceColor.White)
                WhiteRematch = true;
            else
                BlackRematch = true;

            if (!WhiteRematch || !BlackRematch)
                return;

            RestartMatch();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RejectMove(PlayerRef requester, byte requestId, byte pieceId, string reason)
        {
            if (Runner != null && requester != Runner.LocalPlayer)
                return;

            Debug.Log($"Chess move rejected: {reason}", this);
            ChessNetworkMoveCoordinator.Instance?.OnMoveRejected(requestId, pieceId);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RepositionPlayers()
        {
            ChessFusionRoomManager.Instance?.RepositionLocalRigForAssignedSeat();
        }

        private bool TryValidateRematch(PlayerRef sender, out ChessPieceColor seat, out string error)
        {
            return TryIdentifySeat(sender, out seat, out error);
        }

        private bool TryAcceptRequest(ChessPieceColor seat, byte requestId)
        {
            int last = seat == ChessPieceColor.White ? lastWhiteRequestId : lastBlackRequestId;
            if (last != 0)
            {
                int delta = (requestId - (byte)last + 256) % 256;
                if (delta == 0 || delta >= 128)
                    return false;
            }

            if (seat == ChessPieceColor.White)
                lastWhiteRequestId = requestId;
            else
                lastBlackRequestId = requestId;
            return true;
        }

        private void ClearRematchAgreement()
        {
            WhiteRematch = false;
            BlackRematch = false;
            lastWhiteRequestId = 0;
            lastBlackRequestId = 0;
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

        private void SetCarry(ChessPieceColor seat, ChaosCarryState carry)
        {
            if (seat == ChessPieceColor.White)
                WhiteCarry = carry;
            else
                BlackCarry = carry;
        }

        private bool HasPendingChaosMove(ChessPieceColor seat)
        {
            for (int index = 0; index < pendingChaosMoves.Count; index++)
            {
                if (pendingChaosMoves[index].Seat == seat)
                    return true;
            }
            return false;
        }

        private void ExpireChaosCarries()
        {
            int tick = Runner.Tick.Raw;
            if (WhiteCarry.Active && tick > WhiteCarry.ExpiresAtTick)
                WhiteCarry = default;
            if (BlackCarry.Active && tick > BlackCarry.ExpiresAtTick)
                BlackCarry = default;
        }

        private void ResolveChaosBatch()
        {
            chaosBatchCloseTick = 0;
            if (pendingChaosMoves.Count == 0)
                return;

            ChessGameController game = ChessGameController.Instance;
            if (game == null || !game.IsChaosMode || game.Outcome != ChessGameOutcome.InProgress)
            {
                pendingChaosMoves.Clear();
                return;
            }

            List<PendingChaosMove> valid = new List<PendingChaosMove>(pendingChaosMoves.Count);
            for (int index = 0; index < pendingChaosMoves.Count; index++)
            {
                PendingChaosMove move = pendingChaosMoves[index];
                ChessPieceInteractable piece = game.FindPiece(move.PieceId);
                ChessBoardSquare destination = game.FindSquareByIndex(move.DestinationSquare);
                if (piece == null || piece.IsCaptured || ChessSquareIndex.FromSquare(piece.CurrentSquare) != move.OriginSquare ||
                    !game.IsChaosMoveLegal(piece, destination))
                {
                    Reject(move.Player, move.RequestId, move.PieceId, "The board changed before that Chaos move resolved.");
                    continue;
                }
                valid.Add(move);
            }
            pendingChaosMoves.Clear();
            if (valid.Count == 0)
                return;

            Dictionary<sbyte, int> destinationClaims = new Dictionary<sbyte, int>();
            for (int index = 0; index < valid.Count; index++)
            {
                destinationClaims.TryGetValue(valid[index].DestinationSquare, out int count);
                destinationClaims[valid[index].DestinationSquare] = count + 1;
            }

            List<ChaosResolvedMove> resolved = new List<ChaosResolvedMove>(valid.Count);
            for (int index = 0; index < valid.Count; index++)
            {
                PendingChaosMove move = valid[index];
                if (destinationClaims[move.DestinationSquare] > 1)
                {
                    SetCooldown(move.PieceId, 1f);
                    Reject(move.Player, move.RequestId, move.PieceId, "Both players claimed that square. The pieces collided.");
                    continue;
                }
                resolved.Add(new ChaosResolvedMove(move.PieceId, move.DestinationSquare));
            }

            if (resolved.Count > 0 && !game.ApplyChaosBatch(resolved))
            {
                for (int index = 0; index < valid.Count; index++)
                    Reject(valid[index].Player, valid[index].RequestId, valid[index].PieceId, "The Chaos batch could not be applied.");
                return;
            }

            if (WhiteCarry.Active && (game.FindPiece(WhiteCarry.PieceId) == null || game.FindPiece(WhiteCarry.PieceId).IsCaptured))
                WhiteCarry = default;
            if (BlackCarry.Active && (game.FindPiece(BlackCarry.PieceId) == null || game.FindPiece(BlackCarry.PieceId).IsCaptured))
                BlackCarry = default;

            for (int index = 0; index < resolved.Count; index++)
            {
                ChessPieceInteractable piece = game.FindPiece(resolved[index].PieceId);
                if (piece != null && !piece.IsCaptured)
                    SetCooldown(piece.PieceId,
                        ChessModePolicies.Interaction(ChessGameplayMode.Chaos).CooldownSeconds(piece.PieceType));
            }

            ChaosBatchSequence++;
            PublishAfterLocalRulesChange();
        }

        private void SetCooldown(byte pieceId, float seconds)
        {
            if (pieceId >= 32 || Runner == null)
                return;
            int ticks = Mathf.Max(1, Mathf.CeilToInt(seconds * Runner.TickRate));
            PieceCooldownEndTick.Set(pieceId, Runner.Tick.Raw + ticks);
        }

        private void ClearChaosRoundState()
        {
            pendingChaosMoves.Clear();
            chaosBatchCloseTick = 0;
            WhiteCarry = default;
            BlackCarry = default;
            for (int index = 0; index < 32; index++)
                PieceCooldownEndTick.Set(index, 0);
            ChaosBatchSequence = 0;
        }

        private void RejectCarry(PlayerRef sender, int clientNonce, byte pieceId, string reason)
        {
            Debug.LogWarning(reason, this);
            RPC_RejectCarry(sender, clientNonce, pieceId, reason);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_RejectCarry(PlayerRef requester, int clientNonce, byte pieceId, string reason)
        {
            if (Runner != null && requester != Runner.LocalPlayer)
                return;
            Debug.Log($"Chaos carry rejected: {reason}", this);
            ChessNetworkMoveCoordinator.Instance?.OnChaosCarryRejected(clientNonce, pieceId, reason);
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
            WhiteChecksDelivered = snapshot.WhiteChecksDelivered;
            BlackChecksDelivered = snapshot.BlackChecksDelivered;
            LastWhiteMove = snapshot.LastWhiteMove;
            LastBlackMove = snapshot.LastBlackMove;
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
            if (snapshot.Outcome != ChessGameOutcome.InProgress)
                ChessFusionLobbyState.Instance?.MarkMatchEnded();
        }

        private ChessRulesSnapshot ReadSnapshot()
        {
            ChessMatchConfig config = ChessFusionLobbyState.Instance != null
                ? ChessFusionLobbyState.Instance.MatchConfig
                : ChessMatchConfig.Default;
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
                WhiteChecksDelivered = WhiteChecksDelivered,
                BlackChecksDelivered = BlackChecksDelivered,
                ThreeCheckEnabled = config.ThreeCheckEnabled,
                GameplayMode = config.GameplayMode,
                StartingLayout = config.StartingLayout,
                Chess960Index = RoundChess960Index,
                LastWhiteMove = LastWhiteMove,
                LastBlackMove = LastBlackMove,
            };
            latestSnapshot = snapshot;
            return snapshot;
        }
    }
}
