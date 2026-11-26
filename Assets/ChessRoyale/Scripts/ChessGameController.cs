using System.Collections.Generic;
using System.Text;
using ChessRoyale.AI;
using ChessRoyale.Networking;
using UnityEngine;

namespace ChessRoyale
{
    public enum ChessGameOutcome
    {
        InProgress,
        WhiteCheckmates,
        BlackCheckmates,
        Stalemate,
        DrawInsufficientMaterial,
        DrawThreefoldRepetition,
        DrawFiftyMoveRule,
        WhiteWinsOnTime,
        BlackWinsOnTime,
        WhiteWinsByDisconnect,
        BlackWinsByDisconnect,
        WhiteWinsByThreeCheck,
        BlackWinsByThreeCheck,
        WhiteWinsByKingCapture,
        BlackWinsByKingCapture,
        DrawMutualKingCapture,
    }

    [DisallowMultipleComponent]
    public sealed class ChessGameController : MonoBehaviour
    {
        private static readonly Vector2Int[] KnightOffsets =
        {
            new Vector2Int(1, 2), new Vector2Int(2, 1),
            new Vector2Int(2, -1), new Vector2Int(1, -2),
            new Vector2Int(-1, -2), new Vector2Int(-2, -1),
            new Vector2Int(-2, 1), new Vector2Int(-1, 2),
        };

        private static readonly Vector2Int[] DiagonalDirections =
        {
            new Vector2Int(1, 1), new Vector2Int(1, -1),
            new Vector2Int(-1, 1), new Vector2Int(-1, -1),
        };

        private static readonly Vector2Int[] StraightDirections =
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        private readonly Dictionary<Vector2Int, ChessBoardSquare> squares = new Dictionary<Vector2Int, ChessBoardSquare>();
        private readonly Dictionary<ChessBoardSquare, ChessPieceInteractable> board = new Dictionary<ChessBoardSquare, ChessPieceInteractable>();
        private readonly List<ChessPieceInteractable> pieces = new List<ChessPieceInteractable>();
        private readonly HashSet<ChessBoardSquare> legalDestinations = new HashSet<ChessBoardSquare>();
        private readonly Dictionary<string, int> positionCounts = new Dictionary<string, int>();

        [SerializeField] private ChessPieceColor activeColor = ChessPieceColor.White;
        [SerializeField] private ChessGameOutcome outcome = ChessGameOutcome.InProgress;
        [SerializeField] private string statusMessage = "White to move";
        [SerializeField] private GameObject promotionPanel;

        private ChessBoardSquare enPassantTargetSquare;
        private ChessPieceInteractable enPassantVulnerablePawn;
        private ChessPieceInteractable pendingPromotionPiece;
        private ChessBoardSquare checkedKingSquare;
        private bool pendingMoveWasCapture;
        private int halfmoveClock;
        private bool networkSessionActive;
        private bool networkMatchReady;
        private bool networkHasSeat;
        private bool networkAwaitingAuthority;
        private ChessPieceColor networkSeatColor;
        private bool executingAiMove;
        private ChessPieceType aiPromotionType = ChessPieceType.Queen;
        private bool threeCheckEnabled;
        private ChessGameplayMode gameplayMode;
        private byte whiteChecksDelivered;
        private byte blackChecksDelivered;
        private ChessStartingLayout startingLayout;
        private int chess960Index;
        private int castlingKingStartFile = 4;
        private int castlingQueenRookStartFile = 0;
        private int castlingKingRookStartFile = 7;

        private static bool IsSinglePlayer => !MainMenu.LaunchMultiplayer;

        private byte lastWhiteMove;
        private byte lastBlackMove;

        private void MarkLastMove(ChessPieceInteractable piece)
        {
            if (piece.PieceColor == ChessPieceColor.White)
                lastWhiteMove = (byte)(piece.PieceId + 1);
            else
                lastBlackMove = (byte)(piece.PieceId + 1);
            RefreshLastMoveOutlines();
        }

        private void RefreshLastMoveOutlines()
        {
            foreach (ChessPieceInteractable piece in pieces)
            {
                bool highlighted = !piece.IsCaptured &&
                    ((piece.PieceColor == ChessPieceColor.White && lastWhiteMove == piece.PieceId + 1) ||
                     (piece.PieceColor == ChessPieceColor.Black && lastBlackMove == piece.PieceId + 1));
                ChessLastMoveOutline outline = piece.GetComponent<ChessLastMoveOutline>();
                if (highlighted && outline == null)
                    outline = piece.gameObject.AddComponent<ChessLastMoveOutline>();
                if (outline != null)
                    outline.SetRecentMove(highlighted);
            }
        }

        public event System.Action GameReset;
        public event System.Action<ChessPieceType> PromotionRequested;
        public event System.Action RematchRequested;

        public static ChessGameController Instance { get; private set; }
        public ChessPieceColor ActiveColor => activeColor;
        public ChessGameOutcome Outcome => outcome;
        public string StatusMessage => statusMessage;
        public int RegisteredPieceCount => pieces.Count;
        public bool IsPromotionPending => pendingPromotionPiece != null;
        public bool ThreeCheckEnabled => threeCheckEnabled;
        public ChessGameplayMode GameplayMode => gameplayMode;
        public bool IsChaosMode => gameplayMode == ChessGameplayMode.Chaos;
        public byte WhiteChecksDelivered => whiteChecksDelivered;
        public byte BlackChecksDelivered => blackChecksDelivered;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            foreach (ChessBoardSquare square in FindObjectsByType<ChessBoardSquare>(FindObjectsSortMode.None))
                squares[new Vector2Int(square.FileIndex, square.RankIndex)] = square;

            if (promotionPanel != null)
                promotionPanel.SetActive(false);

            // Single player: the computer opponent (ChessAIPlayer) lives next to the rules.
            if (IsSinglePlayer && GetComponent<ChessAIPlayer>() == null)
                gameObject.AddComponent<ChessAIPlayer>();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void ConfigurePromotionPanel(GameObject panel)
        {
            promotionPanel = panel;
            RefreshPromotionPresentation();
        }

        public void RegisterPiece(ChessPieceInteractable piece, ChessBoardSquare square)
        {
            if (piece == null || square == null)
                return;

            if (!pieces.Contains(piece))
                pieces.Add(piece);
            board[square] = piece;
            piece.SetTurnEnabled(CanLocalPlayerMove(piece));

            if (pieces.Count == 32 && positionCounts.Count == 0)
                RecordCurrentPosition();
        }

        public void SetNetworkPlay(bool inRoom, bool matchReady, bool hasSeat, ChessPieceColor seatColor)
        {
            bool changed = networkSessionActive != inRoom
                || networkMatchReady != matchReady
                || networkHasSeat != hasSeat
                || networkSeatColor != seatColor;
            networkSessionActive = inRoom;
            networkMatchReady = matchReady;
            networkHasSeat = hasSeat;
            networkSeatColor = seatColor;
            if (!inRoom)
                networkAwaitingAuthority = false;
            if (!changed && !inRoom)
                RefreshTurnInteraction();
            if (!changed)
                return;

            if (inRoom && !matchReady)
                ClearMoveHints();
            RefreshPromotionPresentation();
            RefreshTurnInteraction();
        }

        public void SetAwaitingAuthority(bool waiting)
        {
            if (networkAwaitingAuthority == waiting)
                return;

            networkAwaitingAuthority = waiting;
            if (waiting)
                ClearMoveHints();
            RefreshTurnInteraction();
        }

        public ChessPieceInteractable FindPiece(byte pieceId)
        {
            foreach (ChessPieceInteractable piece in pieces)
            {
                if (piece.PieceId == pieceId)
                    return piece;
            }

            return null;
        }

        public ChessBoardSquare FindSquareByIndex(int squareIndex)
        {
            if (!ChessSquareIndex.TryGetFileRank(squareIndex, out int file, out int rank))
                return null;

            return GetSquare(file, rank);
        }

        public bool BeginMove(ChessPieceInteractable piece)
        {
            ClearMoveHints();
            if (!CanLocalPlayerMove(piece) || outcome != ChessGameOutcome.InProgress || pendingPromotionPiece != null ||
                (networkSessionActive && networkAwaitingAuthority) ||
                piece.CurrentSquare == null)
                return false;

            foreach (ChessBoardSquare destination in GetLegalMoves(piece))
            {
                legalDestinations.Add(destination);
                destination.SetMoveHint(IsCaptureDestination(piece, destination));
            }

            return true;
        }

        public void CancelMove()
        {
            ClearMoveHints();
        }

        public bool TryMove(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            ClearMoveHints();
            // In single player, only the computer (through TryPlayEngineMove) may move the computer's pieces.
            if (IsSinglePlayer && piece != null && piece.PieceColor != ChessSinglePlayerSettings.HumanColor && !executingAiMove)
                return false;
            if (outcome != ChessGameOutcome.InProgress || pendingPromotionPiece != null ||
                piece == null || piece.IsCaptured || destination == null || piece.PieceColor != activeColor)
                return false;

            HashSet<ChessBoardSquare> moves = GetLegalMoves(piece);
            if (!moves.Contains(destination))
                return false;

            if (startingLayout == ChessStartingLayout.Chess960 &&
                TryResolveChess960Castle(piece, destination, out ChessPieceInteractable castleKing,
                    out ChessPieceInteractable castleRook, out ChessBoardSquare kingDestination,
                    out ChessBoardSquare rookDestination))
            {
                ExecuteChess960Castle(castleKing, castleRook, kingDestination, rookDestination);
                return true;
            }

            ChessBoardSquare origin = piece.CurrentSquare;
            bool pawnMove = piece.PieceType == ChessPieceType.Pawn;
            ChessPieceInteractable capturedPiece = GetCapturedPiece(piece, destination);
            if (capturedPiece != null)
            {
                board.Remove(capturedPiece.CurrentSquare);
                capturedPiece.Capture();
            }

            board.Remove(origin);
            board[destination] = piece;
            piece.MoveToSquare(destination);
            MarkLastMove(piece);

            if (piece.PieceType == ChessPieceType.King && Mathf.Abs(destination.FileIndex - origin.FileIndex) == 2)
                MoveCastlingRook(piece, origin, destination);

            UpdateEnPassantState(piece, origin, destination);

            if (pawnMove && (destination.RankIndex == 0 || destination.RankIndex == 7))
            {
                pendingPromotionPiece = piece;
                pendingMoveWasCapture = capturedPiece != null;
                if (executingAiMove)
                {
                    CompletePromotion(aiPromotionType);
                    return true;
                }
                ShowPromotionPanel();
                RefreshTurnInteraction();
                return true;
            }

            CompleteTurn(piece, pawnMove, capturedPiece != null);
            return true;
        }

        public bool IsChaosMoveLegal(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            return IsChaosMode && outcome == ChessGameOutcome.InProgress && piece != null && !piece.IsCaptured &&
                   destination != null && GetChaosLegalMovesCore(piece).Contains(destination);
        }

        public bool ApplyChaosBatch(IReadOnlyList<ChaosResolvedMove> moves)
        {
            if (!IsChaosMode || outcome != ChessGameOutcome.InProgress || moves == null || moves.Count == 0)
                return false;

            Dictionary<byte, ChessBoardSquare> origins = new Dictionary<byte, ChessBoardSquare>();
            Dictionary<byte, ChessBoardSquare> destinations = new Dictionary<byte, ChessBoardSquare>();
            HashSet<byte> leavers = new HashSet<byte>();
            for (int index = 0; index < moves.Count; index++)
            {
                ChessPieceInteractable piece = FindPiece(moves[index].PieceId);
                ChessBoardSquare destination = FindSquareByIndex(moves[index].DestinationSquare);
                if (piece == null || piece.IsCaptured || piece.CurrentSquare == null || destination == null)
                    return false;
                origins[piece.PieceId] = piece.CurrentSquare;
                destinations[piece.PieceId] = destination;
                leavers.Add(piece.PieceId);
            }

            List<ChessPieceInteractable> captures = new List<ChessPieceInteractable>();
            foreach (KeyValuePair<byte, ChessBoardSquare> move in destinations)
            {
                if (board.TryGetValue(move.Value, out ChessPieceInteractable occupant) && !leavers.Contains(occupant.PieceId))
                    captures.Add(occupant);
            }

            foreach (ChessBoardSquare origin in origins.Values)
                board.Remove(origin);

            bool whiteKingCaptured = false;
            bool blackKingCaptured = false;
            foreach (ChessPieceInteractable captured in captures)
            {
                if (captured == null || captured.IsCaptured)
                    continue;
                board.Remove(captured.CurrentSquare);
                if (captured.PieceType == ChessPieceType.King)
                {
                    whiteKingCaptured |= captured.PieceColor == ChessPieceColor.White;
                    blackKingCaptured |= captured.PieceColor == ChessPieceColor.Black;
                }
                captured.Capture();
            }

            foreach (KeyValuePair<byte, ChessBoardSquare> move in destinations)
            {
                ChessPieceInteractable piece = FindPiece(move.Key);
                if (piece == null || piece.IsCaptured)
                    continue;
                board[move.Value] = piece;
                piece.MoveToSquare(move.Value);
                if (piece.PieceType == ChessPieceType.Pawn &&
                    (move.Value.RankIndex == 0 || move.Value.RankIndex == 7))
                    piece.PromoteTo(ChessPieceType.Queen);
                MarkLastMove(piece);
            }

            pendingPromotionPiece = null;
            enPassantTargetSquare = null;
            enPassantVulnerablePawn = null;
            halfmoveClock = 0;
            ClearCheckSignal();

            if (whiteKingCaptured && blackKingCaptured)
            {
                outcome = ChessGameOutcome.DrawMutualKingCapture;
                statusMessage = "Draw by mutual king capture";
            }
            else if (blackKingCaptured)
            {
                outcome = ChessGameOutcome.WhiteWinsByKingCapture;
                statusMessage = "White captures the king";
            }
            else if (whiteKingCaptured)
            {
                outcome = ChessGameOutcome.BlackWinsByKingCapture;
                statusMessage = "Black captures the king";
            }
            else
            {
                ChessBoardSquare whiteKing = FindKingSquare(ChessPieceColor.White);
                ChessBoardSquare blackKing = FindKingSquare(ChessPieceColor.Black);
                bool whiteThreatened = whiteKing != null && IsSquareAttacked(whiteKing, ChessPieceColor.Black);
                bool blackThreatened = blackKing != null && IsSquareAttacked(blackKing, ChessPieceColor.White);
                if (whiteThreatened)
                {
                    checkedKingSquare = whiteKing;
                    checkedKingSquare.SetCheckHint(true);
                }
                else if (blackThreatened)
                {
                    checkedKingSquare = blackKing;
                    checkedKingSquare.SetCheckHint(true);
                }
                statusMessage = whiteThreatened && blackThreatened ? "Both kings threatened"
                    : whiteThreatened ? "White king threatened"
                    : blackThreatened ? "Black king threatened"
                    : "Protect your king";
            }

            if (outcome != ChessGameOutcome.InProgress)
                Debug.Log($"Chess Royale result: {statusMessage}");
            RefreshTurnInteraction();
            return true;
        }

        public void FinishMovePresentation()
        {
            RefreshTurnInteraction();
        }

        // Single player: re-check which pieces the player may grab (after Start, or when the setup panel returns).
        public void RefreshLocalControls()
        {
            RefreshTurnInteraction();
        }

        // Middle of the board in world space; the AI player turns the rig around it when you play Black.
        public Vector3 BoardCenter
        {
            get
            {
                if (squares.Count == 0)
                    return transform.position;

                Vector3 sum = Vector3.zero;
                foreach (ChessBoardSquare square in squares.Values)
                    sum += square.transform.position;
                return sum / squares.Count;
            }
        }

        // A plain-data copy of the game for ChessEngine, which can't touch Unity objects from its thread.
        public EnginePosition ExportEnginePosition()
        {
            EnginePosition position = new EnginePosition();
            foreach (KeyValuePair<ChessBoardSquare, ChessPieceInteractable> entry in board)
            {
                ChessPieceInteractable piece = entry.Value;
                if (piece == null || piece.IsCaptured)
                    continue;

                int type = (int)piece.PieceType + 1;
                position.Board[entry.Key.RankIndex * 8 + entry.Key.FileIndex] = piece.PieceColor == ChessPieceColor.White ? type : -type;
            }

            position.SideToMove = activeColor == ChessPieceColor.White ? 1 : -1;
            position.Castling = 0;
            if (HasCastlingRight(ChessPieceColor.White, 0, 7)) position.Castling |= EnginePosition.WhiteKingside;
            if (HasCastlingRight(ChessPieceColor.White, 0, 0)) position.Castling |= EnginePosition.WhiteQueenside;
            if (HasCastlingRight(ChessPieceColor.Black, 7, 7)) position.Castling |= EnginePosition.BlackKingside;
            if (HasCastlingRight(ChessPieceColor.Black, 7, 0)) position.Castling |= EnginePosition.BlackQueenside;
            position.EnPassant = enPassantTargetSquare != null && enPassantVulnerablePawn != null
                ? enPassantTargetSquare.RankIndex * 8 + enPassantTargetSquare.FileIndex
                : -1;
            position.Halfmove = halfmoveClock;
            return position;
        }

        // Plays the computer's move through the normal rules path (animation, captures, sounds, results).
        public bool TryPlayEngineMove(EngineMove move)
        {
            ChessBoardSquare from = GetSquare(move.From % 8, move.From / 8);
            ChessBoardSquare to = GetSquare(move.To % 8, move.To / 8);
            if (from == null || to == null || !board.TryGetValue(from, out ChessPieceInteractable piece))
                return false;

            aiPromotionType = move.Promotion >= EnginePosition.Knight && move.Promotion <= EnginePosition.Queen
                ? (ChessPieceType)(move.Promotion - 1)
                : ChessPieceType.Queen;
            executingAiMove = true;
            try
            {
                return TryMove(piece, to);
            }
            finally
            {
                executingAiMove = false;
                RefreshTurnInteraction();
            }
        }

        private bool HasCastlingRight(ChessPieceColor color, int rank, int rookFile)
        {
            ChessBoardSquare kingSquare = GetSquare(4, rank);
            ChessBoardSquare rookSquare = GetSquare(rookFile, rank);
            return kingSquare != null && rookSquare != null &&
                   board.TryGetValue(kingSquare, out ChessPieceInteractable king) &&
                   board.TryGetValue(rookSquare, out ChessPieceInteractable rook) &&
                   king.PieceColor == color && king.PieceType == ChessPieceType.King && !king.HasMoved &&
                   rook.PieceColor == color && rook.PieceType == ChessPieceType.Rook && !rook.HasMoved;
        }

        public void PromoteToQueen() => SubmitPromotion(ChessPieceType.Queen);
        public void PromoteToRook() => SubmitPromotion(ChessPieceType.Rook);
        public void PromoteToBishop() => SubmitPromotion(ChessPieceType.Bishop);
        public void PromoteToKnight() => SubmitPromotion(ChessPieceType.Knight);

        public bool TryCompletePromotion(ChessPieceType promotedType)
        {
            if (pendingPromotionPiece == null || promotedType == ChessPieceType.Pawn || promotedType == ChessPieceType.King)
                return false;

            CompletePromotion(promotedType);
            return true;
        }

        public void RequestRematch()
        {
            if (networkSessionActive)
            {
                if (!networkMatchReady || outcome == ChessGameOutcome.InProgress)
                    return;

                RematchRequested?.Invoke();
                return;
            }

            ResetGame();
        }

        public sbyte PendingPromotionPieceId => pendingPromotionPiece == null ? (sbyte)-1 : (sbyte)pendingPromotionPiece.PieceId;
        public sbyte EnPassantSquareIndex => ChessSquareIndex.FromSquare(enPassantTargetSquare);
        public sbyte EnPassantPawnId => enPassantVulnerablePawn == null ? (sbyte)-1 : (sbyte)enPassantVulnerablePawn.PieceId;
        public int HalfmoveClock => halfmoveClock;

        public ChessRulesSnapshot ExportRulesSnapshot()
        {
            ChessPieceSnapshot[] exported = new ChessPieceSnapshot[32];
            foreach (ChessPieceInteractable piece in pieces)
            {
                if (piece.PieceId >= exported.Length)
                    continue;

                exported[piece.PieceId] = new ChessPieceSnapshot
                {
                    PieceId = piece.PieceId,
                    Color = piece.PieceColor,
                    Type = piece.PieceType,
                    SquareIndex = piece.IsCaptured ? (sbyte)-1 : ChessSquareIndex.FromSquare(piece.CurrentSquare),
                    HasMoved = piece.HasMoved,
                    Captured = piece.IsCaptured,
                };
            }

            return new ChessRulesSnapshot
            {
                Pieces = exported,
                ActiveColor = activeColor,
                Outcome = outcome,
                StatusMessage = statusMessage,
                PendingPromotionPieceId = PendingPromotionPieceId,
                EnPassantSquareIndex = EnPassantSquareIndex,
                EnPassantPawnId = EnPassantPawnId,
                HalfmoveClock = halfmoveClock,
                WhiteChecksDelivered = whiteChecksDelivered,
                BlackChecksDelivered = blackChecksDelivered,
                ThreeCheckEnabled = threeCheckEnabled,
                GameplayMode = (byte)gameplayMode,
                StartingLayout = (byte)startingLayout,
                Chess960Index = chess960Index,
                LastWhiteMove = lastWhiteMove,
                LastBlackMove = lastBlackMove,
            };
        }

        public void ApplyRulesSnapshot(ChessRulesSnapshot snapshot, bool preserveRepetitionHistory)
        {
            if (snapshot.Pieces == null)
                return;

            ClearMoveHints();
            ClearCheckSignal();
            board.Clear();
            if (!preserveRepetitionHistory)
                positionCounts.Clear();

            for (int index = 0; index < snapshot.Pieces.Length && index < 32; index++)
            {
                ChessPieceSnapshot pieceState = snapshot.Pieces[index];
                ChessPieceInteractable piece = FindPiece(pieceState.PieceId);
                if (piece == null)
                    continue;

                ChessBoardSquare square = pieceState.Captured ? null : FindSquareByIndex(pieceState.SquareIndex);
                piece.ApplyAuthoritativeState(square, pieceState.Type, pieceState.HasMoved, pieceState.Captured);
                if (!pieceState.Captured && square != null)
                    board[square] = piece;
            }

            lastWhiteMove = snapshot.LastWhiteMove;
            lastBlackMove = snapshot.LastBlackMove;
            RefreshLastMoveOutlines();
            activeColor = snapshot.ActiveColor;
            outcome = snapshot.Outcome;
            gameplayMode = (ChessGameplayMode)snapshot.GameplayMode;
            statusMessage = string.IsNullOrEmpty(snapshot.StatusMessage)
                ? DescribeOutcome(snapshot.Outcome, snapshot.ActiveColor)
                : snapshot.StatusMessage;
            halfmoveClock = snapshot.HalfmoveClock;
            threeCheckEnabled = snapshot.ThreeCheckEnabled;
            whiteChecksDelivered = snapshot.WhiteChecksDelivered;
            blackChecksDelivered = snapshot.BlackChecksDelivered;
            startingLayout = (ChessStartingLayout)snapshot.StartingLayout;
            chess960Index = snapshot.Chess960Index;
            ConfigureCastlingOrigins();
            pendingPromotionPiece = snapshot.PendingPromotionPieceId >= 0
                ? FindPiece((byte)snapshot.PendingPromotionPieceId)
                : null;
            // A default/stale snapshot ID must not open promotion for an ordinary piece.
            if (pendingPromotionPiece != null &&
                (pendingPromotionPiece.IsCaptured || pendingPromotionPiece.PieceType != ChessPieceType.Pawn ||
                 pendingPromotionPiece.CurrentSquare == null ||
                 pendingPromotionPiece.CurrentSquare.RankIndex != (pendingPromotionPiece.PieceColor == ChessPieceColor.White ? 7 : 0)))
                pendingPromotionPiece = null;
            enPassantTargetSquare = FindSquareByIndex(snapshot.EnPassantSquareIndex);
            enPassantVulnerablePawn = snapshot.EnPassantPawnId >= 0
                ? FindPiece((byte)snapshot.EnPassantPawnId)
                : null;
            PresentCheckHint();
            RefreshPromotionPresentation();
            RefreshTurnInteraction();
        }

        public void ResetGame()
        {
            ResetGame(ChessMatchConfig.Default, 0);
        }

        public void ResetGame(ChessMatchConfig config, int roundChess960Index)
        {
            lastWhiteMove = lastBlackMove = 0;
            RefreshLastMoveOutlines();
            ClearMoveHints();
            ClearCheckSignal();
            board.Clear();
            activeColor = ChessPieceColor.White;
            outcome = ChessGameOutcome.InProgress;
            gameplayMode = (ChessGameplayMode)config.GameplayMode;
            statusMessage = IsChaosMode ? "Protect your king" : "White to move";
            pendingPromotionPiece = null;
            enPassantTargetSquare = null;
            enPassantVulnerablePawn = null;
            halfmoveClock = 0;
            threeCheckEnabled = config.ThreeCheckEnabled;
            whiteChecksDelivered = 0;
            blackChecksDelivered = 0;
            startingLayout = (ChessStartingLayout)config.StartingLayout;
            chess960Index = Mathf.Clamp(roundChess960Index, 0, 959);
            ConfigureCastlingOrigins();
            positionCounts.Clear();
            if (promotionPanel != null)
                promotionPanel.SetActive(false);

            if (startingLayout == ChessStartingLayout.Chess960)
                ResetToChess960Position();
            else
                ResetToStandardPosition();

            RecordCurrentPosition();
            RefreshTurnInteraction();
            GameReset?.Invoke();
        }

        private void ResetToStandardPosition()
        {
            foreach (ChessPieceInteractable piece in pieces)
            {
                piece.ResetToStart();
                if (piece.CurrentSquare != null)
                    board[piece.CurrentSquare] = piece;
            }
        }

        private void ResetToChess960Position()
        {
            ChessPieceType[] backRank = GenerateChess960BackRank(chess960Index);
            foreach (ChessPieceColor color in new[] { ChessPieceColor.White, ChessPieceColor.Black })
            {
                int backRankIndex = color == ChessPieceColor.White ? 0 : 7;
                int pawnRankIndex = color == ChessPieceColor.White ? 1 : 6;
                foreach (ChessPieceType type in new[]
                         {
                             ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop,
                             ChessPieceType.Queen, ChessPieceType.King,
                         })
                {
                    List<ChessPieceInteractable> matchingPieces = pieces.FindAll(piece =>
                        piece.PieceColor == color && piece.StartingPieceType == type && piece.StartingSquare != null &&
                        piece.StartingSquare.RankIndex == backRankIndex);
                    matchingPieces.Sort((left, right) => left.PieceId.CompareTo(right.PieceId));

                    List<int> matchingFiles = new List<int>();
                    for (int file = 0; file < 8; file++)
                    {
                        if (backRank[file] == type)
                            matchingFiles.Add(file);
                    }

                    for (int index = 0; index < matchingPieces.Count && index < matchingFiles.Count; index++)
                    {
                        ChessBoardSquare square = GetSquare(matchingFiles[index], backRankIndex);
                        matchingPieces[index].ResetToSquare(square);
                        board[square] = matchingPieces[index];
                    }
                }

                foreach (ChessPieceInteractable pawn in pieces.FindAll(piece =>
                             piece.PieceColor == color && piece.StartingPieceType == ChessPieceType.Pawn))
                {
                    int file = pawn.StartingSquare != null ? pawn.StartingSquare.FileIndex : pawn.PieceId & 7;
                    ChessBoardSquare square = GetSquare(file, pawnRankIndex);
                    pawn.ResetToSquare(square);
                    board[square] = pawn;
                }
            }
        }

        private void ConfigureCastlingOrigins()
        {
            castlingKingStartFile = 4;
            castlingQueenRookStartFile = 0;
            castlingKingRookStartFile = 7;
            if (startingLayout != ChessStartingLayout.Chess960)
                return;

            ChessPieceType[] backRank = GenerateChess960BackRank(chess960Index);
            for (int file = 0; file < 8; file++)
            {
                if (backRank[file] == ChessPieceType.King)
                    castlingKingStartFile = file;
            }

            for (int file = 0; file < castlingKingStartFile; file++)
            {
                if (backRank[file] == ChessPieceType.Rook)
                    castlingQueenRookStartFile = file;
            }
            for (int file = castlingKingStartFile + 1; file < 8; file++)
            {
                if (backRank[file] == ChessPieceType.Rook)
                {
                    castlingKingRookStartFile = file;
                    break;
                }
            }
        }

        public static ChessPieceType[] GenerateChess960BackRank(int positionIndex)
        {
            int value = Mathf.Clamp(positionIndex, 0, 959);
            ChessPieceType[] result = new ChessPieceType[8];
            bool[] occupied = new bool[8];

            int bishopOne = (value % 4) * 2 + 1;
            value /= 4;
            int bishopTwo = (value % 4) * 2;
            value /= 4;
            result[bishopOne] = ChessPieceType.Bishop;
            result[bishopTwo] = ChessPieceType.Bishop;
            occupied[bishopOne] = occupied[bishopTwo] = true;

            int queenSlot = value % 6;
            value /= 6;
            int queenFile = NthEmptyFile(occupied, queenSlot);
            result[queenFile] = ChessPieceType.Queen;
            occupied[queenFile] = true;

            int[,] knightPairs =
            {
                { 0, 1 }, { 0, 2 }, { 0, 3 }, { 0, 4 }, { 1, 2 },
                { 1, 3 }, { 1, 4 }, { 2, 3 }, { 2, 4 }, { 3, 4 },
            };
            int firstKnightFile = NthEmptyFile(occupied, knightPairs[value, 0]);
            result[firstKnightFile] = ChessPieceType.Knight;
            occupied[firstKnightFile] = true;
            int secondKnightSlot = knightPairs[value, 1] - (knightPairs[value, 1] > knightPairs[value, 0] ? 1 : 0);
            int secondKnightFile = NthEmptyFile(occupied, secondKnightSlot);
            result[secondKnightFile] = ChessPieceType.Knight;
            occupied[secondKnightFile] = true;

            int[] remaining = new int[3];
            int remainingCount = 0;
            for (int file = 0; file < 8; file++)
            {
                if (!occupied[file])
                    remaining[remainingCount++] = file;
            }
            result[remaining[0]] = ChessPieceType.Rook;
            result[remaining[1]] = ChessPieceType.King;
            result[remaining[2]] = ChessPieceType.Rook;
            return result;
        }

        private static int NthEmptyFile(bool[] occupied, int slot)
        {
            for (int file = 0; file < occupied.Length; file++)
            {
                if (occupied[file])
                    continue;
                if (slot-- == 0)
                    return file;
            }
            return 0;
        }

        // Called by the chess clock when a side runs out of time; the other side wins.
        public void EndOnTime(ChessPieceColor flagged)
        {
            if (outcome != ChessGameOutcome.InProgress)
                return;

            ClearMoveHints();
            ClearCheckSignal();
            pendingPromotionPiece = null;
            if (promotionPanel != null)
                promotionPanel.SetActive(false);

            outcome = flagged == ChessPieceColor.White ? ChessGameOutcome.BlackWinsOnTime : ChessGameOutcome.WhiteWinsOnTime;
            statusMessage = flagged == ChessPieceColor.White ? "Black wins on time" : "White wins on time";
            Debug.Log($"Chess Royale result: {statusMessage}");
            RefreshTurnInteraction();
        }

        public void EndByDisconnect(ChessPieceColor disconnected)
        {
            if (outcome != ChessGameOutcome.InProgress)
                return;

            ClearMoveHints();
            ClearCheckSignal();
            pendingPromotionPiece = null;
            if (promotionPanel != null)
                promotionPanel.SetActive(false);

            bool whiteLeft = disconnected == ChessPieceColor.White;
            outcome = whiteLeft ? ChessGameOutcome.BlackWinsByDisconnect : ChessGameOutcome.WhiteWinsByDisconnect;
            statusMessage = whiteLeft ? "White disconnected. Black wins." : "Black disconnected. White wins.";
            Debug.Log($"Chess Royale result: {statusMessage}");
            RefreshTurnInteraction();
        }

        public HashSet<ChessBoardSquare> GetLegalMoves(ChessPieceInteractable piece)
        {
            return ChessModePolicies.Rules(gameplayMode).GetLegalMoves(this, piece);
        }

        internal HashSet<ChessBoardSquare> GetTurnBasedLegalMovesCore(ChessPieceInteractable piece)
        {
            HashSet<ChessBoardSquare> legal = new HashSet<ChessBoardSquare>();
            if (piece == null || piece.CurrentSquare == null)
                return legal;

            foreach (ChessBoardSquare destination in GetPseudoLegalMoves(piece, true))
            {
                if (!WouldLeaveKingInCheck(piece, destination))
                    legal.Add(destination);
            }

            if (startingLayout == ChessStartingLayout.Chess960)
                AddChess960CastlingRequests(piece, legal);

            return legal;
        }

        internal HashSet<ChessBoardSquare> GetChaosLegalMovesCore(ChessPieceInteractable piece)
        {
            if (piece == null || piece.CurrentSquare == null || piece.IsCaptured)
                return new HashSet<ChessBoardSquare>();
            return GetPseudoLegalMoves(piece, false);
        }

        private HashSet<ChessBoardSquare> GetPseudoLegalMoves(ChessPieceInteractable piece, bool includeSpecialMoves)
        {
            HashSet<ChessBoardSquare> moves = new HashSet<ChessBoardSquare>();
            ChessBoardSquare origin = piece.CurrentSquare;

            switch (piece.PieceType)
            {
                case ChessPieceType.Pawn:
                    AddPawnMoves(piece, moves, includeSpecialMoves);
                    break;
                case ChessPieceType.Knight:
                    foreach (Vector2Int offset in KnightOffsets)
                        AddIfAvailable(piece, origin.FileIndex + offset.x, origin.RankIndex + offset.y, moves);
                    break;
                case ChessPieceType.Bishop:
                    AddSlidingMoves(piece, DiagonalDirections, moves);
                    break;
                case ChessPieceType.Rook:
                    AddSlidingMoves(piece, StraightDirections, moves);
                    break;
                case ChessPieceType.Queen:
                    AddSlidingMoves(piece, DiagonalDirections, moves);
                    AddSlidingMoves(piece, StraightDirections, moves);
                    break;
                case ChessPieceType.King:
                    foreach (Vector2Int direction in DiagonalDirections)
                        AddIfAvailable(piece, origin.FileIndex + direction.x, origin.RankIndex + direction.y, moves);
                    foreach (Vector2Int direction in StraightDirections)
                        AddIfAvailable(piece, origin.FileIndex + direction.x, origin.RankIndex + direction.y, moves);
                    if (includeSpecialMoves && startingLayout != ChessStartingLayout.Chess960)
                        AddCastlingMoves(piece, moves);
                    break;
            }

            return moves;
        }

        private void AddPawnMoves(ChessPieceInteractable piece, HashSet<ChessBoardSquare> moves, bool includeSpecialMoves)
        {
            ChessBoardSquare origin = piece.CurrentSquare;
            int direction = piece.PieceColor == ChessPieceColor.White ? 1 : -1;
            int startingRank = piece.PieceColor == ChessPieceColor.White ? 1 : 6;

            ChessBoardSquare oneStep = GetSquare(origin.FileIndex, origin.RankIndex + direction);
            if (oneStep != null && !board.ContainsKey(oneStep))
            {
                moves.Add(oneStep);
                ChessBoardSquare twoSteps = GetSquare(origin.FileIndex, origin.RankIndex + direction * 2);
                if (!piece.HasMoved && origin.RankIndex == startingRank && twoSteps != null && !board.ContainsKey(twoSteps))
                    moves.Add(twoSteps);
            }

            AddPawnCapture(piece, origin.FileIndex - 1, origin.RankIndex + direction, moves, includeSpecialMoves);
            AddPawnCapture(piece, origin.FileIndex + 1, origin.RankIndex + direction, moves, includeSpecialMoves);
        }

        private void AddPawnCapture(ChessPieceInteractable piece, int file, int rank, HashSet<ChessBoardSquare> moves,
            bool includeSpecialMoves)
        {
            ChessBoardSquare square = GetSquare(file, rank);
            if (square == null)
                return;

            if (board.TryGetValue(square, out ChessPieceInteractable occupant) && IsCapturableEnemy(piece, occupant))
                moves.Add(square);
            else if (includeSpecialMoves && square == enPassantTargetSquare && enPassantVulnerablePawn != null &&
                     enPassantVulnerablePawn.PieceColor != piece.PieceColor)
                moves.Add(square);
        }

        private void AddSlidingMoves(ChessPieceInteractable piece, Vector2Int[] directions, HashSet<ChessBoardSquare> moves)
        {
            foreach (Vector2Int direction in directions)
            {
                int file = piece.CurrentSquare.FileIndex + direction.x;
                int rank = piece.CurrentSquare.RankIndex + direction.y;
                while (true)
                {
                    ChessBoardSquare square = GetSquare(file, rank);
                    if (square == null)
                        break;

                    if (board.TryGetValue(square, out ChessPieceInteractable occupant))
                    {
                        if (IsCapturableEnemy(piece, occupant))
                            moves.Add(square);
                        break;
                    }

                    moves.Add(square);
                    file += direction.x;
                    rank += direction.y;
                }
            }
        }

        private void AddIfAvailable(ChessPieceInteractable piece, int file, int rank, HashSet<ChessBoardSquare> moves)
        {
            ChessBoardSquare square = GetSquare(file, rank);
            if (square == null)
                return;

            if (!board.TryGetValue(square, out ChessPieceInteractable occupant) || IsCapturableEnemy(piece, occupant))
                moves.Add(square);
        }

        private void AddCastlingMoves(ChessPieceInteractable king, HashSet<ChessBoardSquare> moves)
        {
            ChessBoardSquare origin = king.CurrentSquare;
            int homeRank = king.PieceColor == ChessPieceColor.White ? 0 : 7;
            ChessPieceColor enemy = Opposite(king.PieceColor);
            if (king.HasMoved || origin.FileIndex != 4 || origin.RankIndex != homeRank || IsSquareAttacked(origin, enemy))
                return;

            TryAddCastle(king, homeRank, 7, 5, 6, enemy, moves);
            TryAddCastle(king, homeRank, 0, 3, 2, enemy, moves);
        }

        private void TryAddCastle(ChessPieceInteractable king, int rank, int rookFile, int transitFile, int destinationFile,
            ChessPieceColor enemy, HashSet<ChessBoardSquare> moves)
        {
            ChessBoardSquare rookSquare = GetSquare(rookFile, rank);
            if (rookSquare == null || !board.TryGetValue(rookSquare, out ChessPieceInteractable rook) ||
                rook.PieceColor != king.PieceColor || rook.PieceType != ChessPieceType.Rook || rook.HasMoved)
                return;

            int direction = rookFile > king.CurrentSquare.FileIndex ? 1 : -1;
            for (int file = king.CurrentSquare.FileIndex + direction; file != rookFile; file += direction)
            {
                ChessBoardSquare pathSquare = GetSquare(file, rank);
                if (pathSquare == null || board.ContainsKey(pathSquare))
                    return;
            }

            ChessBoardSquare transit = GetSquare(transitFile, rank);
            ChessBoardSquare destination = GetSquare(destinationFile, rank);
            if (transit != null && destination != null &&
                !IsSquareAttacked(transit, enemy) && !IsSquareAttacked(destination, enemy))
                moves.Add(destination);
        }

        private void AddChess960CastlingRequests(ChessPieceInteractable piece, HashSet<ChessBoardSquare> moves)
        {
            if (piece.PieceType != ChessPieceType.King && piece.PieceType != ChessPieceType.Rook)
                return;

            for (int side = 0; side < 2; side++)
            {
                bool kingSide = side == 1;
                if (!TryGetChess960Castle(piece.PieceColor, kingSide, out ChessPieceInteractable king,
                        out ChessPieceInteractable rook, out ChessBoardSquare kingDestination,
                        out ChessBoardSquare rookDestination))
                    continue;

                if (piece == king && king.CurrentSquare != kingDestination)
                    moves.Add(kingDestination);
                else if (piece == rook && king.CurrentSquare == kingDestination && rook.CurrentSquare != rookDestination)
                    moves.Add(rookDestination);
            }
        }

        private bool TryResolveChess960Castle(ChessPieceInteractable requestedPiece, ChessBoardSquare requestedDestination,
            out ChessPieceInteractable king, out ChessPieceInteractable rook, out ChessBoardSquare kingDestination,
            out ChessBoardSquare rookDestination)
        {
            for (int side = 0; side < 2; side++)
            {
                bool kingSide = side == 1;
                if (!TryGetChess960Castle(requestedPiece.PieceColor, kingSide, out king, out rook,
                        out kingDestination, out rookDestination))
                    continue;

                if ((requestedPiece == king && king.CurrentSquare != kingDestination && requestedDestination == kingDestination) ||
                    (requestedPiece == rook && king.CurrentSquare == kingDestination &&
                     rook.CurrentSquare != rookDestination && requestedDestination == rookDestination))
                    return true;
            }

            king = null;
            rook = null;
            kingDestination = null;
            rookDestination = null;
            return false;
        }

        private bool TryGetChess960Castle(ChessPieceColor color, bool kingSide,
            out ChessPieceInteractable king, out ChessPieceInteractable rook,
            out ChessBoardSquare kingDestination, out ChessBoardSquare rookDestination)
        {
            int rank = color == ChessPieceColor.White ? 0 : 7;
            ChessBoardSquare kingOrigin = GetSquare(castlingKingStartFile, rank);
            ChessBoardSquare rookOrigin = GetSquare(kingSide ? castlingKingRookStartFile : castlingQueenRookStartFile, rank);
            kingDestination = GetSquare(kingSide ? 6 : 2, rank);
            rookDestination = GetSquare(kingSide ? 5 : 3, rank);
            king = null;
            rook = null;

            if (kingOrigin == null || rookOrigin == null || kingDestination == null || rookDestination == null ||
                !board.TryGetValue(kingOrigin, out king) || !board.TryGetValue(rookOrigin, out rook) ||
                king.PieceColor != color || king.PieceType != ChessPieceType.King || king.HasMoved ||
                rook.PieceColor != color || rook.PieceType != ChessPieceType.Rook || rook.HasMoved)
                return false;

            if (!PathClearForCastle(kingOrigin.FileIndex, kingDestination.FileIndex, rank, king, rook) ||
                !PathClearForCastle(rookOrigin.FileIndex, rookDestination.FileIndex, rank, king, rook))
                return false;

            ChessPieceColor enemy = Opposite(color);
            int direction = kingDestination.FileIndex == kingOrigin.FileIndex
                ? 0
                : (kingDestination.FileIndex > kingOrigin.FileIndex ? 1 : -1);
            int file = kingOrigin.FileIndex;
            while (true)
            {
                ChessBoardSquare transit = GetSquare(file, rank);
                if (transit == null || IsSquareAttacked(transit, enemy))
                    return false;
                if (file == kingDestination.FileIndex)
                    break;
                file += direction;
            }

            return king.CurrentSquare != kingDestination || rook.CurrentSquare != rookDestination;
        }

        private bool PathClearForCastle(int originFile, int destinationFile, int rank,
            ChessPieceInteractable king, ChessPieceInteractable rook)
        {
            int minimum = Mathf.Min(originFile, destinationFile);
            int maximum = Mathf.Max(originFile, destinationFile);
            for (int file = minimum; file <= maximum; file++)
            {
                ChessBoardSquare square = GetSquare(file, rank);
                if (square == null)
                    return false;
                if (board.TryGetValue(square, out ChessPieceInteractable occupant) && occupant != king && occupant != rook)
                    return false;
            }
            return true;
        }

        private void ExecuteChess960Castle(ChessPieceInteractable king, ChessPieceInteractable rook,
            ChessBoardSquare kingDestination, ChessBoardSquare rookDestination)
        {
            board.Remove(king.CurrentSquare);
            board.Remove(rook.CurrentSquare);
            board[kingDestination] = king;
            board[rookDestination] = rook;
            king.MoveToSquare(kingDestination);
            rook.MoveToSquare(rookDestination);
            MarkLastMove(king);
            enPassantTargetSquare = null;
            enPassantVulnerablePawn = null;
            CompleteTurn(king, false, false);
        }

        private static bool IsCapturableEnemy(ChessPieceInteractable piece, ChessPieceInteractable occupant)
        {
            return occupant.PieceColor != piece.PieceColor && occupant.PieceType != ChessPieceType.King;
        }

        private bool IsCaptureDestination(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            return board.ContainsKey(destination) ||
                   (piece.PieceType == ChessPieceType.Pawn && destination == enPassantTargetSquare);
        }

        private ChessPieceInteractable GetCapturedPiece(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            if (board.TryGetValue(destination, out ChessPieceInteractable captured))
                return captured;

            if (piece.PieceType == ChessPieceType.Pawn && destination == enPassantTargetSquare)
                return enPassantVulnerablePawn;

            return null;
        }

        private bool WouldLeaveKingInCheck(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            ChessBoardSquare origin = piece.CurrentSquare;
            ChessPieceInteractable capturedPiece = GetCapturedPiece(piece, destination);
            ChessBoardSquare capturedSquare = capturedPiece != null ? capturedPiece.CurrentSquare : null;

            board.Remove(origin);
            if (capturedSquare != null)
                board.Remove(capturedSquare);
            board[destination] = piece;

            ChessBoardSquare kingSquare = piece.PieceType == ChessPieceType.King
                ? destination
                : FindKingSquare(piece.PieceColor);
            bool inCheck = kingSquare == null || IsSquareAttacked(kingSquare, Opposite(piece.PieceColor));

            board.Remove(destination);
            board[origin] = piece;
            if (capturedPiece != null)
                board[capturedSquare] = capturedPiece;

            return inCheck;
        }

        private ChessBoardSquare FindKingSquare(ChessPieceColor color)
        {
            foreach (KeyValuePair<ChessBoardSquare, ChessPieceInteractable> entry in board)
            {
                if (entry.Value.PieceColor == color && entry.Value.PieceType == ChessPieceType.King)
                    return entry.Key;
            }

            return null;
        }

        private bool IsSquareAttacked(ChessBoardSquare target, ChessPieceColor attacker)
        {
            int pawnDirection = attacker == ChessPieceColor.White ? 1 : -1;
            if (IsPieceAt(target.FileIndex - 1, target.RankIndex - pawnDirection, attacker, ChessPieceType.Pawn) ||
                IsPieceAt(target.FileIndex + 1, target.RankIndex - pawnDirection, attacker, ChessPieceType.Pawn))
                return true;

            foreach (Vector2Int offset in KnightOffsets)
            {
                if (IsPieceAt(target.FileIndex + offset.x, target.RankIndex + offset.y, attacker, ChessPieceType.Knight))
                    return true;
            }

            foreach (Vector2Int direction in DiagonalDirections)
            {
                if (RayFindsAttacker(target, direction, attacker, ChessPieceType.Bishop, ChessPieceType.Queen) ||
                    IsPieceAt(target.FileIndex + direction.x, target.RankIndex + direction.y, attacker, ChessPieceType.King))
                    return true;
            }

            foreach (Vector2Int direction in StraightDirections)
            {
                if (RayFindsAttacker(target, direction, attacker, ChessPieceType.Rook, ChessPieceType.Queen) ||
                    IsPieceAt(target.FileIndex + direction.x, target.RankIndex + direction.y, attacker, ChessPieceType.King))
                    return true;
            }

            return false;
        }

        private bool RayFindsAttacker(ChessBoardSquare target, Vector2Int direction, ChessPieceColor attacker,
            ChessPieceType firstType, ChessPieceType secondType)
        {
            int file = target.FileIndex + direction.x;
            int rank = target.RankIndex + direction.y;
            while (true)
            {
                ChessBoardSquare square = GetSquare(file, rank);
                if (square == null)
                    return false;

                if (board.TryGetValue(square, out ChessPieceInteractable occupant))
                    return occupant.PieceColor == attacker &&
                           (occupant.PieceType == firstType || occupant.PieceType == secondType);

                file += direction.x;
                rank += direction.y;
            }
        }

        private bool IsPieceAt(int file, int rank, ChessPieceColor color, ChessPieceType type)
        {
            ChessBoardSquare square = GetSquare(file, rank);
            return square != null && board.TryGetValue(square, out ChessPieceInteractable piece) &&
                   piece.PieceColor == color && piece.PieceType == type;
        }

        private void MoveCastlingRook(ChessPieceInteractable king, ChessBoardSquare origin, ChessBoardSquare destination)
        {
            int rookFile = destination.FileIndex > origin.FileIndex ? 7 : 0;
            int rookDestinationFile = destination.FileIndex > origin.FileIndex ? 5 : 3;
            ChessBoardSquare rookOrigin = GetSquare(rookFile, origin.RankIndex);
            ChessBoardSquare rookDestination = GetSquare(rookDestinationFile, origin.RankIndex);
            if (rookOrigin == null || rookDestination == null || !board.TryGetValue(rookOrigin, out ChessPieceInteractable rook))
                return;

            board.Remove(rookOrigin);
            board[rookDestination] = rook;
            rook.MoveToSquare(rookDestination);
        }

        private void UpdateEnPassantState(ChessPieceInteractable piece, ChessBoardSquare origin, ChessBoardSquare destination)
        {
            enPassantTargetSquare = null;
            enPassantVulnerablePawn = null;
            if (piece.PieceType != ChessPieceType.Pawn || Mathf.Abs(destination.RankIndex - origin.RankIndex) != 2)
                return;

            enPassantTargetSquare = GetSquare(origin.FileIndex, (origin.RankIndex + destination.RankIndex) / 2);
            enPassantVulnerablePawn = piece;
        }

        private void CompletePromotion(ChessPieceType promotedType)
        {
            if (pendingPromotionPiece == null)
                return;

            ChessPieceInteractable promotedPiece = pendingPromotionPiece;
            pendingPromotionPiece = null;
            promotedPiece.PromoteTo(promotedType);
            if (promotionPanel != null)
                promotionPanel.SetActive(false);
            CompleteTurn(promotedPiece, true, pendingMoveWasCapture);
        }

        private void CompleteTurn(ChessPieceInteractable movedPiece, bool pawnMove, bool wasCapture)
        {
            halfmoveClock = pawnMove || wasCapture ? 0 : halfmoveClock + 1;
            activeColor = Opposite(activeColor);
            bool deliveredCheck = false;
            ChessBoardSquare defendingKing = FindKingSquare(activeColor);
            if (threeCheckEnabled && defendingKing != null && IsSquareAttacked(defendingKing, movedPiece.PieceColor))
            {
                deliveredCheck = true;
                if (movedPiece.PieceColor == ChessPieceColor.White)
                    whiteChecksDelivered = (byte)Mathf.Min(3, whiteChecksDelivered + 1);
                else
                    blackChecksDelivered = (byte)Mathf.Min(3, blackChecksDelivered + 1);
            }
            RecordCurrentPosition();
            if (deliveredCheck && (whiteChecksDelivered >= 3 || blackChecksDelivered >= 3))
            {
                outcome = whiteChecksDelivered >= 3
                    ? ChessGameOutcome.WhiteWinsByThreeCheck
                    : ChessGameOutcome.BlackWinsByThreeCheck;
                statusMessage = whiteChecksDelivered >= 3
                    ? "White wins by delivering three checks"
                    : "Black wins by delivering three checks";
                ClearCheckSignal();
            }
            else
            {
                EvaluateGameState();
            }
            RefreshTurnInteraction();
        }

        private void EvaluateGameState()
        {
            ClearCheckSignal();
            ChessBoardSquare kingSquare = FindKingSquare(activeColor);
            bool inCheck = kingSquare != null && IsSquareAttacked(kingSquare, Opposite(activeColor));
            bool hasLegalMove = HasAnyLegalMove(activeColor);

            if (!hasLegalMove)
            {
                if (inCheck)
                {
                    outcome = activeColor == ChessPieceColor.White
                        ? ChessGameOutcome.BlackCheckmates
                        : ChessGameOutcome.WhiteCheckmates;
                    statusMessage = activeColor == ChessPieceColor.White ? "Black wins by checkmate" : "White wins by checkmate";
                }
                else
                {
                    outcome = ChessGameOutcome.Stalemate;
                    statusMessage = "Draw by stalemate";
                }
            }
            else if (IsInsufficientMaterial())
            {
                outcome = ChessGameOutcome.DrawInsufficientMaterial;
                statusMessage = "Draw by insufficient material";
            }
            else if (halfmoveClock >= 100)
            {
                outcome = ChessGameOutcome.DrawFiftyMoveRule;
                statusMessage = "Draw by fifty-move rule";
            }
            else if (positionCounts.TryGetValue(BuildPositionKey(), out int repetitions) && repetitions >= 3)
            {
                outcome = ChessGameOutcome.DrawThreefoldRepetition;
                statusMessage = "Draw by threefold repetition";
            }
            else
            {
                outcome = ChessGameOutcome.InProgress;
                statusMessage = $"{activeColor} to move" + (inCheck ? " — check" : string.Empty);
                if (inCheck)
                {
                    checkedKingSquare = kingSquare;
                    checkedKingSquare.SetCheckHint(true);
                }
            }

            if (outcome != ChessGameOutcome.InProgress)
                Debug.Log($"Chess Royale result: {statusMessage}");
        }

        private bool HasAnyLegalMove(ChessPieceColor color)
        {
            foreach (ChessPieceInteractable piece in pieces)
            {
                if (!piece.IsCaptured && piece.PieceColor == color && GetLegalMoves(piece).Count > 0)
                    return true;
            }

            return false;
        }

        private bool IsInsufficientMaterial()
        {
            List<ChessPieceInteractable> remaining = new List<ChessPieceInteractable>();
            foreach (ChessPieceInteractable piece in pieces)
            {
                if (!piece.IsCaptured && piece.PieceType != ChessPieceType.King)
                    remaining.Add(piece);
            }

            if (remaining.Count == 0)
                return true;
            if (remaining.Count == 1)
                return remaining[0].PieceType == ChessPieceType.Bishop || remaining[0].PieceType == ChessPieceType.Knight;

            int bishopColor = -1;
            foreach (ChessPieceInteractable piece in remaining)
            {
                if (piece.PieceType != ChessPieceType.Bishop)
                    return false;

                int squareColor = (piece.CurrentSquare.FileIndex + piece.CurrentSquare.RankIndex) & 1;
                if (bishopColor < 0)
                    bishopColor = squareColor;
                else if (bishopColor != squareColor)
                    return false;
            }

            return true;
        }

        private void SubmitPromotion(ChessPieceType promotedType)
        {
            if (networkSessionActive)
            {
                PromotionRequested?.Invoke(promotedType);
                return;
            }

            CompletePromotion(promotedType);
        }

        private static string DescribeOutcome(ChessGameOutcome result, ChessPieceColor sideToMove)
        {
            switch (result)
            {
                case ChessGameOutcome.WhiteCheckmates:
                    return "White wins by checkmate";
                case ChessGameOutcome.BlackCheckmates:
                    return "Black wins by checkmate";
                case ChessGameOutcome.Stalemate:
                    return "Draw by stalemate";
                case ChessGameOutcome.DrawInsufficientMaterial:
                    return "Draw by insufficient material";
                case ChessGameOutcome.DrawThreefoldRepetition:
                    return "Draw by threefold repetition";
                case ChessGameOutcome.DrawFiftyMoveRule:
                    return "Draw by fifty-move rule";
                case ChessGameOutcome.WhiteWinsOnTime:
                    return "White wins on time";
                case ChessGameOutcome.BlackWinsOnTime:
                    return "Black wins on time";
                case ChessGameOutcome.WhiteWinsByDisconnect:
                    return "Black disconnected. White wins.";
                case ChessGameOutcome.BlackWinsByDisconnect:
                    return "White disconnected. Black wins.";
                case ChessGameOutcome.WhiteWinsByThreeCheck:
                    return "White wins by delivering three checks";
                case ChessGameOutcome.BlackWinsByThreeCheck:
                    return "Black wins by delivering three checks";
                case ChessGameOutcome.WhiteWinsByKingCapture:
                    return "White captures the king";
                case ChessGameOutcome.BlackWinsByKingCapture:
                    return "Black captures the king";
                case ChessGameOutcome.DrawMutualKingCapture:
                    return "Draw by mutual king capture";
                default:
                    return $"{sideToMove} to move";
            }
        }

        private bool CanLocalPlayerMove(ChessPieceInteractable piece)
        {
            if (piece == null || piece.IsCaptured)
                return false;
            if (IsChaosMode)
            {
                if (!networkSessionActive)
                    return true;
                if (!networkMatchReady || !networkHasSeat || piece.PieceColor != networkSeatColor)
                    return false;
                return ChessNetworkGameState.Instance == null || !ChessNetworkGameState.Instance.IsPieceCoolingDown(piece.PieceId);
            }
            if (piece.PieceColor != activeColor)
                return false;
            if (IsSinglePlayer)
                return ChessSinglePlayerSettings.GameStarted && piece.PieceColor == ChessSinglePlayerSettings.HumanColor;
            if (!networkSessionActive)
                return true;

            return networkMatchReady && networkHasSeat && piece.PieceColor == networkSeatColor;
        }

        private void RefreshPromotionPresentation()
        {
            if (pendingPromotionPiece == null)
            {
                if (promotionPanel != null)
                    promotionPanel.SetActive(false);
                return;
            }

            ShowPromotionPanel();
        }

        private void PresentCheckHint()
        {
            ClearCheckSignal();
            if (outcome != ChessGameOutcome.InProgress)
                return;

            if (IsChaosMode)
            {
                ChessBoardSquare whiteKing = FindKingSquare(ChessPieceColor.White);
                ChessBoardSquare blackKing = FindKingSquare(ChessPieceColor.Black);
                bool whiteThreatened = whiteKing != null && IsSquareAttacked(whiteKing, ChessPieceColor.Black);
                bool blackThreatened = blackKing != null && IsSquareAttacked(blackKing, ChessPieceColor.White);
                if (whiteThreatened)
                    checkedKingSquare = whiteKing;
                else if (blackThreatened)
                    checkedKingSquare = blackKing;
                if (checkedKingSquare != null)
                    checkedKingSquare.SetCheckHint(true);
                statusMessage = whiteThreatened && blackThreatened ? "Both kings threatened"
                    : whiteThreatened ? "White king threatened"
                    : blackThreatened ? "Black king threatened"
                    : "Protect your king";
                return;
            }

            ChessBoardSquare kingSquare = FindKingSquare(activeColor);
            if (kingSquare == null || !IsSquareAttacked(kingSquare, Opposite(activeColor)))
                return;

            checkedKingSquare = kingSquare;
            checkedKingSquare.SetCheckHint(true);
            if (outcome == ChessGameOutcome.InProgress)
                statusMessage = $"{activeColor} to move — check";
        }

        private void ShowPromotionPanel()
        {
            if (promotionPanel == null)
                return;

            if (pendingPromotionPiece == null || outcome != ChessGameOutcome.InProgress ||
                (networkSessionActive && (!networkHasSeat || pendingPromotionPiece.PieceColor != networkSeatColor)))
            {
                promotionPanel.SetActive(false);
                return;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                promotionPanel.transform.position = camera.transform.position + camera.transform.forward * 1.6f;
                // World-space UI is read from its local -Z side; +Z points away from the player.
                promotionPanel.transform.rotation = Quaternion.LookRotation(promotionPanel.transform.position - camera.transform.position, Vector3.up);
            }
            ChessUITheme.ApplyPanel(promotionPanel);
            promotionPanel.SetActive(true);
        }

        private ChessBoardSquare GetSquare(int file, int rank)
        {
            squares.TryGetValue(new Vector2Int(file, rank), out ChessBoardSquare square);
            return square;
        }

        private void ClearMoveHints()
        {
            foreach (ChessBoardSquare square in legalDestinations)
                square.ClearMoveHint();
            legalDestinations.Clear();
        }

        private void ClearCheckSignal()
        {
            if (checkedKingSquare != null)
                checkedKingSquare.SetCheckHint(false);
            checkedKingSquare = null;
        }

        private void RefreshTurnInteraction()
        {
            bool gameAcceptsMoves = outcome == ChessGameOutcome.InProgress && pendingPromotionPiece == null && !(networkSessionActive && networkAwaitingAuthority);
            foreach (ChessPieceInteractable piece in pieces)
                piece.SetTurnEnabled(gameAcceptsMoves && CanLocalPlayerMove(piece));
        }

        private void RecordCurrentPosition()
        {
            string key = BuildPositionKey();
            positionCounts.TryGetValue(key, out int count);
            positionCounts[key] = count + 1;
        }

        private string BuildPositionKey()
        {
            StringBuilder key = new StringBuilder(96);
            for (int rank = 0; rank < 8; rank++)
            {
                for (int file = 0; file < 8; file++)
                {
                    ChessBoardSquare square = GetSquare(file, rank);
                    if (square == null || !board.TryGetValue(square, out ChessPieceInteractable piece))
                    {
                        key.Append('.');
                        continue;
                    }

                    char symbol = PieceSymbol(piece.PieceType);
                    key.Append(piece.PieceColor == ChessPieceColor.White ? char.ToUpperInvariant(symbol) : symbol);
                }
            }

            key.Append('|').Append(activeColor == ChessPieceColor.White ? 'w' : 'b');
            key.Append('|').Append(CastlingRightsKey());
            key.Append('|').Append(enPassantTargetSquare != null ? enPassantTargetSquare.Coordinate : "-");
            if (threeCheckEnabled)
                key.Append('|').Append(whiteChecksDelivered).Append(':').Append(blackChecksDelivered);
            return key.ToString();
        }

        private string CastlingRightsKey()
        {
            StringBuilder rights = new StringBuilder(4);
            AppendCastlingRight(rights, ChessPieceColor.White, 0, castlingKingRookStartFile, 'K');
            AppendCastlingRight(rights, ChessPieceColor.White, 0, castlingQueenRookStartFile, 'Q');
            AppendCastlingRight(rights, ChessPieceColor.Black, 7, castlingKingRookStartFile, 'k');
            AppendCastlingRight(rights, ChessPieceColor.Black, 7, castlingQueenRookStartFile, 'q');
            return rights.Length > 0 ? rights.ToString() : "-";
        }

        private void AppendCastlingRight(StringBuilder rights, ChessPieceColor color, int rank, int rookFile, char symbol)
        {
            ChessBoardSquare kingSquare = GetSquare(castlingKingStartFile, rank);
            ChessBoardSquare rookSquare = GetSquare(rookFile, rank);
            if (kingSquare != null && rookSquare != null &&
                board.TryGetValue(kingSquare, out ChessPieceInteractable king) &&
                board.TryGetValue(rookSquare, out ChessPieceInteractable rook) &&
                king.PieceColor == color && king.PieceType == ChessPieceType.King && !king.HasMoved &&
                rook.PieceColor == color && rook.PieceType == ChessPieceType.Rook && !rook.HasMoved)
                rights.Append(symbol);
        }

        private static char PieceSymbol(ChessPieceType type)
        {
            switch (type)
            {
                case ChessPieceType.Knight: return 'n';
                case ChessPieceType.Bishop: return 'b';
                case ChessPieceType.Rook: return 'r';
                case ChessPieceType.Queen: return 'q';
                case ChessPieceType.King: return 'k';
                default: return 'p';
            }
        }

        private static ChessPieceColor Opposite(ChessPieceColor color)
        {
            return color == ChessPieceColor.White ? ChessPieceColor.Black : ChessPieceColor.White;
        }
    }
}
