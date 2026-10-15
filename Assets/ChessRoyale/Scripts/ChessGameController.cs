using System.Collections.Generic;
using UnityEngine;

namespace ChessRoyale
{
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

        [SerializeField] private ChessPieceColor activeColor = ChessPieceColor.White;

        public static ChessGameController Instance { get; private set; }
        public ChessPieceColor ActiveColor => activeColor;

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
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void RegisterPiece(ChessPieceInteractable piece, ChessBoardSquare square)
        {
            if (piece == null || square == null)
                return;

            if (!pieces.Contains(piece))
                pieces.Add(piece);
            board[square] = piece;
            piece.SetTurnEnabled(piece.PieceColor == activeColor);
        }

        public bool BeginMove(ChessPieceInteractable piece)
        {
            ClearHints();
            if (piece == null || piece.IsCaptured || piece.PieceColor != activeColor || piece.CurrentSquare == null)
                return false;

            foreach (ChessBoardSquare destination in GetLegalMoves(piece))
            {
                legalDestinations.Add(destination);
                destination.SetMoveHint(board.ContainsKey(destination));
            }

            return true;
        }

        public void CancelMove()
        {
            ClearHints();
        }

        public bool TryMove(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            ClearHints();
            if (piece == null || destination == null || piece.PieceColor != activeColor)
                return false;

            HashSet<ChessBoardSquare> moves = GetLegalMoves(piece);
            if (!moves.Contains(destination))
                return false;

            ChessBoardSquare origin = piece.CurrentSquare;
            if (board.TryGetValue(destination, out ChessPieceInteractable capturedPiece))
            {
                board.Remove(destination);
                capturedPiece.Capture();
            }

            board.Remove(origin);
            board[destination] = piece;
            activeColor = Opposite(activeColor);
            return true;
        }

        public void FinishMovePresentation()
        {
            RefreshTurnInteraction();
        }

        public void ResetGame()
        {
            ClearHints();
            board.Clear();
            activeColor = ChessPieceColor.White;

            foreach (ChessPieceInteractable piece in pieces)
            {
                piece.ResetToStart();
                if (piece.CurrentSquare != null)
                    board[piece.CurrentSquare] = piece;
            }

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
            if (square != null && board.TryGetValue(square, out ChessPieceInteractable occupant) && IsCapturableEnemy(piece, occupant))
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

        private static bool IsCapturableEnemy(ChessPieceInteractable piece, ChessPieceInteractable occupant)
        {
            return occupant.PieceColor != piece.PieceColor && occupant.PieceType != ChessPieceType.King;
        }

        private bool WouldLeaveKingInCheck(ChessPieceInteractable piece, ChessBoardSquare destination)
        {
            ChessBoardSquare origin = piece.CurrentSquare;
            board.TryGetValue(destination, out ChessPieceInteractable capturedPiece);

            board.Remove(origin);
            board[destination] = piece;

            ChessBoardSquare kingSquare = piece.PieceType == ChessPieceType.King
                ? destination
                : FindKingSquare(piece.PieceColor);
            bool inCheck = kingSquare == null || IsSquareAttacked(kingSquare, Opposite(piece.PieceColor));

            board.Remove(destination);
            board[origin] = piece;
            if (capturedPiece != null)
                board[destination] = capturedPiece;

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
                if (RayFindsAttacker(target, direction, attacker, ChessPieceType.Bishop, ChessPieceType.Queen))
                    return true;
                if (IsPieceAt(target.FileIndex + direction.x, target.RankIndex + direction.y, attacker, ChessPieceType.King))
                    return true;
            }

            foreach (Vector2Int direction in StraightDirections)
            {
                if (RayFindsAttacker(target, direction, attacker, ChessPieceType.Rook, ChessPieceType.Queen))
                    return true;
                if (IsPieceAt(target.FileIndex + direction.x, target.RankIndex + direction.y, attacker, ChessPieceType.King))
                    return true;
            }

            return false;
        }

        private bool RayFindsAttacker(ChessBoardSquare target, Vector2Int direction, ChessPieceColor attacker, ChessPieceType firstType, ChessPieceType secondType)
        {
            int file = target.FileIndex + direction.x;
            int rank = target.RankIndex + direction.y;
            while (true)
            {
                ChessBoardSquare square = GetSquare(file, rank);
                if (square == null)
                    return false;

                if (board.TryGetValue(square, out ChessPieceInteractable occupant))
                    return occupant.PieceColor == attacker && (occupant.PieceType == firstType || occupant.PieceType == secondType);

                file += direction.x;
                rank += direction.y;
            }
        }

        private bool IsPieceAt(int file, int rank, ChessPieceColor color, ChessPieceType type)
        {
            ChessBoardSquare square = GetSquare(file, rank);
            return square != null &&
                   board.TryGetValue(square, out ChessPieceInteractable piece) &&
                   piece.PieceColor == color &&
                   piece.PieceType == type;
        }

        private ChessBoardSquare GetSquare(int file, int rank)
        {
            squares.TryGetValue(new Vector2Int(file, rank), out ChessBoardSquare square);
            return square;
        }

        private void ClearHints()
        {
            foreach (ChessBoardSquare square in legalDestinations)
                square.ClearMoveHint();
            legalDestinations.Clear();
        }

        private void RefreshTurnInteraction()
        {
            foreach (ChessPieceInteractable piece in pieces)
                piece.SetTurnEnabled(!piece.IsCaptured && piece.PieceColor == activeColor);
        }

        private static ChessPieceColor Opposite(ChessPieceColor color)
        {
            return color == ChessPieceColor.White ? ChessPieceColor.Black : ChessPieceColor.White;
        }
    }
}
