using System.Collections.Generic;
using System.Text;
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
        private ChessPieceColor networkSeatColor;

        public event System.Action GameReset;
        public event System.Action<ChessPieceType> PromotionRequested;
        public event System.Action RematchRequested;

        public static ChessGameController Instance { get; private set; }
        public ChessPieceColor ActiveColor => activeColor;
        public ChessGameOutcome Outcome => outcome;
        public string StatusMessage => statusMessage;
        public int RegisteredPieceCount => pieces.Count;
        public bool IsPromotionPending => pendingPromotionPiece != null;

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
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void ConfigurePromotionPanel(GameObject panel)
        {
            promotionPanel = panel;
        }

        public void RegisterPiece(ChessPieceInteractable piece, ChessBoardSquare square)
        {
            if (piece == null || square == null)
                return;

            if (!pieces.Contains(piece))
                pieces.Add(piece);
            board[square] = piece;
            piece.SetTurnEnabled(piece.PieceColor == activeColor);

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
            if (!changed)
                return;

            if (inRoom && !matchReady)
                ClearMoveHints();
            RefreshPromotionPresentation();
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
            if (outcome != ChessGameOutcome.InProgress || pendingPromotionPiece != null ||
                piece == null || destination == null || piece.PieceColor != activeColor)
                return false;

            HashSet<ChessBoardSquare> moves = GetLegalMoves(piece);
            if (!moves.Contains(destination))
                return false;

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

            if (piece.PieceType == ChessPieceType.King && Mathf.Abs(destination.FileIndex - origin.FileIndex) == 2)
                MoveCastlingRook(piece, origin, destination);

            UpdateEnPassantState(piece, origin, destination);

            if (pawnMove && (destination.RankIndex == 0 || destination.RankIndex == 7))
            {
                pendingPromotionPiece = piece;
                pendingMoveWasCapture = capturedPiece != null;
                ShowPromotionPanel();
                RefreshTurnInteraction();
                return true;
            }

            CompleteTurn(piece, pawnMove, capturedPiece != null);
            return true;
        }

        public void FinishMovePresentation()
        {
            RefreshTurnInteraction();
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
            if (networkSessionActive && networkMatchReady)
            {
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

            activeColor = snapshot.ActiveColor;
            outcome = snapshot.Outcome;
            statusMessage = string.IsNullOrEmpty(snapshot.StatusMessage)
                ? DescribeOutcome(snapshot.Outcome, snapshot.ActiveColor)
                : snapshot.StatusMessage;
            halfmoveClock = snapshot.HalfmoveClock;
            pendingPromotionPiece = snapshot.PendingPromotionPieceId >= 0
                ? FindPiece((byte)snapshot.PendingPromotionPieceId)
                : null;
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
            ClearMoveHints();
            ClearCheckSignal();
            board.Clear();
            activeColor = ChessPieceColor.White;
            outcome = ChessGameOutcome.InProgress;
            statusMessage = "White to move";
            pendingPromotionPiece = null;
            enPassantTargetSquare = null;
            enPassantVulnerablePawn = null;
            halfmoveClock = 0;
            positionCounts.Clear();
            if (promotionPanel != null)
                promotionPanel.SetActive(false);

            foreach (ChessPieceInteractable piece in pieces)
            {
                piece.ResetToStart();
                if (piece.CurrentSquare != null)
                    board[piece.CurrentSquare] = piece;
            }

            RecordCurrentPosition();
            RefreshTurnInteraction();
            GameReset?.Invoke();
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
            HashSet<ChessBoardSquare> legal = new HashSet<ChessBoardSquare>();
            if (piece == null || piece.CurrentSquare == null)
                return legal;

            foreach (ChessBoardSquare destination in GetPseudoLegalMoves(piece))
            {
                if (!WouldLeaveKingInCheck(piece, destination))
                    legal.Add(destination);
            }

            return legal;
        }

        private HashSet<ChessBoardSquare> GetPseudoLegalMoves(ChessPieceInteractable piece)
        {
            HashSet<ChessBoardSquare> moves = new HashSet<ChessBoardSquare>();
            ChessBoardSquare origin = piece.CurrentSquare;

            switch (piece.PieceType)
            {
                case ChessPieceType.Pawn:
                    AddPawnMoves(piece, moves);
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
                    AddCastlingMoves(piece, moves);
                    break;
            }

            return moves;
        }

        private void AddPawnMoves(ChessPieceInteractable piece, HashSet<ChessBoardSquare> moves)
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

            AddPawnCapture(piece, origin.FileIndex - 1, origin.RankIndex + direction, moves);
            AddPawnCapture(piece, origin.FileIndex + 1, origin.RankIndex + direction, moves);
        }

        private void AddPawnCapture(ChessPieceInteractable piece, int file, int rank, HashSet<ChessBoardSquare> moves)
        {
            ChessBoardSquare square = GetSquare(file, rank);
            if (square == null)
                return;

            if (board.TryGetValue(square, out ChessPieceInteractable occupant) && IsCapturableEnemy(piece, occupant))
                moves.Add(square);
            else if (square == enPassantTargetSquare && enPassantVulnerablePawn != null &&
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
            RecordCurrentPosition();
            EvaluateGameState();
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
                default:
                    return $"{sideToMove} to move";
            }
        }

        private bool CanLocalPlayerMove(ChessPieceInteractable piece)
        {
            if (piece == null || piece.IsCaptured || piece.PieceColor != activeColor)
                return false;
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

            if (networkSessionActive && (!networkHasSeat || pendingPromotionPiece == null || pendingPromotionPiece.PieceColor != networkSeatColor))
            {
                promotionPanel.SetActive(false);
                return;
            }

            Camera camera = Camera.main;
            if (camera != null)
            {
                promotionPanel.transform.position = camera.transform.position + camera.transform.forward * 1.6f;
                promotionPanel.transform.rotation = Quaternion.LookRotation(camera.transform.position - promotionPanel.transform.position, Vector3.up);
            }
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
            bool gameAcceptsMoves = outcome == ChessGameOutcome.InProgress && pendingPromotionPiece == null;
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
            return key.ToString();
        }

        private string CastlingRightsKey()
        {
            StringBuilder rights = new StringBuilder(4);
            AppendCastlingRight(rights, ChessPieceColor.White, 0, 7, 'K');
            AppendCastlingRight(rights, ChessPieceColor.White, 0, 0, 'Q');
            AppendCastlingRight(rights, ChessPieceColor.Black, 7, 7, 'k');
            AppendCastlingRight(rights, ChessPieceColor.Black, 7, 0, 'q');
            return rights.Length > 0 ? rights.ToString() : "-";
        }

        private void AppendCastlingRight(StringBuilder rights, ChessPieceColor color, int rank, int rookFile, char symbol)
        {
            ChessBoardSquare kingSquare = GetSquare(4, rank);
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
