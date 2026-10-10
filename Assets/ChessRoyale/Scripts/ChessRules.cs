using System;
using System.Collections.Generic;
using System.Linq;

namespace ChessRoyale
{
    [Flags]
    public enum ChessMoveFlags
    {
        None = 0,
        Capture = 1,
        Castle = 2,
        EnPassant = 4,
        Promotion = 8
    }

    public enum ChessGameStatus
    {
        Playing,
        Check,
        Checkmate,
        Stalemate,
        DrawFiftyMove,
        DrawInsufficientMaterial
    }

    public readonly struct RulePiece
    {
        public RulePiece(ChessSide side, ChessPieceType type)
        {
            Side = side;
            Type = type;
        }

        public ChessSide Side { get; }
        public ChessPieceType Type { get; }
    }

    public readonly struct ChessMove : IEquatable<ChessMove>
    {
        public ChessMove(BoardCoordinate from, BoardCoordinate to, ChessMoveFlags flags = ChessMoveFlags.None,
            ChessPieceType promotion = ChessPieceType.Queen)
        {
            From = from;
            To = to;
            Flags = flags;
            Promotion = promotion;
        }

        public BoardCoordinate From { get; }
        public BoardCoordinate To { get; }
        public ChessMoveFlags Flags { get; }
        public ChessPieceType Promotion { get; }
        public bool IsCapture => (Flags & ChessMoveFlags.Capture) != 0;
        public bool IsPromotion => (Flags & ChessMoveFlags.Promotion) != 0;

        public bool Equals(ChessMove other) => From == other.From && To == other.To && Flags == other.Flags && Promotion == other.Promotion;
        public override bool Equals(object obj) => obj is ChessMove other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(From, To, Flags, Promotion);
    }

    /// <summary>Pure chess state used by the VR interaction layer.</summary>
    public sealed class ChessPosition
    {
        private readonly RulePiece?[,] board = new RulePiece?[8, 8];

        public ChessSide SideToMove { get; private set; } = ChessSide.White;
        public BoardCoordinate? EnPassantTarget { get; private set; }
        public bool WhiteCastleKingSide { get; private set; } = true;
        public bool WhiteCastleQueenSide { get; private set; } = true;
        public bool BlackCastleKingSide { get; private set; } = true;
        public bool BlackCastleQueenSide { get; private set; } = true;
        public int HalfmoveClock { get; private set; }

        public RulePiece? this[BoardCoordinate square] => square.IsValid ? board[square.File, square.Rank] : null;

        public static ChessPosition CreateInitial()
        {
            ChessPosition position = new();
            ChessPieceType[] backRank =
            {
                ChessPieceType.Rook, ChessPieceType.Knight, ChessPieceType.Bishop, ChessPieceType.Queen,
                ChessPieceType.King, ChessPieceType.Bishop, ChessPieceType.Knight, ChessPieceType.Rook
            };

            for (int file = 0; file < 8; file++)
            {
                position.board[file, 0] = new RulePiece(ChessSide.White, backRank[file]);
                position.board[file, 1] = new RulePiece(ChessSide.White, ChessPieceType.Pawn);
                position.board[file, 6] = new RulePiece(ChessSide.Black, ChessPieceType.Pawn);
                position.board[file, 7] = new RulePiece(ChessSide.Black, backRank[file]);
            }

            return position;
        }

        public IReadOnlyList<ChessMove> LegalMovesFrom(BoardCoordinate from)
        {
            RulePiece? piece = this[from];
            if (!piece.HasValue || piece.Value.Side != SideToMove)
                return Array.Empty<ChessMove>();

            List<ChessMove> legal = new();
            foreach (ChessMove move in PseudoMovesFrom(from))
            {
                ChessPosition next = Clone();
                next.MakeMove(move);
                if (!next.IsKingInCheck(piece.Value.Side))
                    legal.Add(move);
            }
            return legal;
        }

        public IReadOnlyList<ChessMove> AllLegalMoves()
        {
            List<ChessMove> moves = new();
            for (int file = 0; file < 8; file++)
            for (int rank = 0; rank < 8; rank++)
                moves.AddRange(LegalMovesFrom(new BoardCoordinate(file, rank)));
            return moves;
        }

        public void Apply(ChessMove move)
        {
            if (!LegalMovesFrom(move.From).Contains(move))
                throw new InvalidOperationException($"Illegal chess move: {move.From}-{move.To}");
            MakeMove(move);
        }

        public ChessGameStatus GetStatus()
        {
            if (HalfmoveClock >= 100)
                return ChessGameStatus.DrawFiftyMove;
            if (HasInsufficientMaterial())
                return ChessGameStatus.DrawInsufficientMaterial;

            bool check = IsKingInCheck(SideToMove);
            if (AllLegalMoves().Count > 0)
                return check ? ChessGameStatus.Check : ChessGameStatus.Playing;
            return check ? ChessGameStatus.Checkmate : ChessGameStatus.Stalemate;
        }

        public bool IsKingInCheck(ChessSide side)
        {
            BoardCoordinate king = default;
            bool found = false;
            for (int file = 0; file < 8 && !found; file++)
            for (int rank = 0; rank < 8; rank++)
            {
                RulePiece? piece = board[file, rank];
                if (piece.HasValue && piece.Value.Side == side && piece.Value.Type == ChessPieceType.King)
                {
                    king = new BoardCoordinate(file, rank);
                    found = true;
                    break;
                }
            }
            return found && IsAttacked(king, Opponent(side));
        }

        private IEnumerable<ChessMove> PseudoMovesFrom(BoardCoordinate from)
        {
            RulePiece piece = board[from.File, from.Rank].Value;
            switch (piece.Type)
            {
                case ChessPieceType.Pawn:
                    return PawnMoves(from, piece.Side);
                case ChessPieceType.Knight:
                    return JumpMoves(from, piece.Side, new[] { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) });
                case ChessPieceType.Bishop:
                    return SlideMoves(from, piece.Side, new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) });
                case ChessPieceType.Rook:
                    return SlideMoves(from, piece.Side, new[] { (1, 0), (-1, 0), (0, 1), (0, -1) });
                case ChessPieceType.Queen:
                    return SlideMoves(from, piece.Side, new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) });
                case ChessPieceType.King:
                    return KingMoves(from, piece.Side);
                default:
                    return Array.Empty<ChessMove>();
            }
        }

        private IEnumerable<ChessMove> PawnMoves(BoardCoordinate from, ChessSide side)
        {
            List<ChessMove> moves = new();
            int direction = side == ChessSide.White ? 1 : -1;
            int startRank = side == ChessSide.White ? 1 : 6;
            AddPawnAdvance(moves, from, new BoardCoordinate(from.File, from.Rank + direction));
            BoardCoordinate two = new(from.File, from.Rank + 2 * direction);
            if (from.Rank == startRank && !board[from.File, from.Rank + direction].HasValue && !board[two.File, two.Rank].HasValue)
                moves.Add(new ChessMove(from, two));

            foreach (int fileDelta in new[] { -1, 1 })
            {
                BoardCoordinate to = new(from.File + fileDelta, from.Rank + direction);
                if (!to.IsValid)
                    continue;
                RulePiece? target = board[to.File, to.Rank];
                if (target.HasValue && target.Value.Side != side && target.Value.Type != ChessPieceType.King)
                    AddPawnMove(moves, from, to, ChessMoveFlags.Capture);
                else if (EnPassantTarget.HasValue && EnPassantTarget.Value == to)
                    moves.Add(new ChessMove(from, to, ChessMoveFlags.Capture | ChessMoveFlags.EnPassant));
            }
            return moves;
        }

        private void AddPawnAdvance(List<ChessMove> moves, BoardCoordinate from, BoardCoordinate to)
        {
            if (to.IsValid && !board[to.File, to.Rank].HasValue)
                AddPawnMove(moves, from, to, ChessMoveFlags.None);
        }

        private static void AddPawnMove(List<ChessMove> moves, BoardCoordinate from, BoardCoordinate to, ChessMoveFlags flags)
        {
            if (to.Rank is 0 or 7)
            {
                foreach (ChessPieceType type in new[] { ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight })
                    moves.Add(new ChessMove(from, to, flags | ChessMoveFlags.Promotion, type));
            }
            else
            {
                moves.Add(new ChessMove(from, to, flags));
            }
        }

        private IEnumerable<ChessMove> JumpMoves(BoardCoordinate from, ChessSide side, IEnumerable<(int x, int y)> offsets)
        {
            List<ChessMove> moves = new();
            foreach ((int x, int y) in offsets)
            {
                BoardCoordinate to = new(from.File + x, from.Rank + y);
                AddIfAvailable(moves, from, to, side);
            }
            return moves;
        }

        private IEnumerable<ChessMove> SlideMoves(BoardCoordinate from, ChessSide side, IEnumerable<(int x, int y)> directions)
        {
            List<ChessMove> moves = new();
            foreach ((int x, int y) in directions)
            {
                BoardCoordinate to = new(from.File + x, from.Rank + y);
                while (to.IsValid)
                {
                    RulePiece? target = board[to.File, to.Rank];
                    if (!target.HasValue)
                        moves.Add(new ChessMove(from, to));
                    else
                    {
                        if (target.Value.Side != side && target.Value.Type != ChessPieceType.King)
                            moves.Add(new ChessMove(from, to, ChessMoveFlags.Capture));
                        break;
                    }
                    to = new BoardCoordinate(to.File + x, to.Rank + y);
                }
            }
            return moves;
        }

        private IEnumerable<ChessMove> KingMoves(BoardCoordinate from, ChessSide side)
        {
            List<ChessMove> moves = JumpMoves(from, side, new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) }).ToList();
            int rank = side == ChessSide.White ? 0 : 7;
            bool kingSide = side == ChessSide.White ? WhiteCastleKingSide : BlackCastleKingSide;
            bool queenSide = side == ChessSide.White ? WhiteCastleQueenSide : BlackCastleQueenSide;
            ChessSide opponent = Opponent(side);
            if (from == new BoardCoordinate(4, rank) && !IsAttacked(from, opponent))
            {
                if (kingSide && !board[5, rank].HasValue && !board[6, rank].HasValue &&
                    IsRook(side, 7, rank) && !IsAttacked(new BoardCoordinate(5, rank), opponent) && !IsAttacked(new BoardCoordinate(6, rank), opponent))
                    moves.Add(new ChessMove(from, new BoardCoordinate(6, rank), ChessMoveFlags.Castle));
                if (queenSide && !board[1, rank].HasValue && !board[2, rank].HasValue && !board[3, rank].HasValue &&
                    IsRook(side, 0, rank) && !IsAttacked(new BoardCoordinate(3, rank), opponent) && !IsAttacked(new BoardCoordinate(2, rank), opponent))
                    moves.Add(new ChessMove(from, new BoardCoordinate(2, rank), ChessMoveFlags.Castle));
            }
            return moves;
        }

        private void AddIfAvailable(List<ChessMove> moves, BoardCoordinate from, BoardCoordinate to, ChessSide side)
        {
            if (!to.IsValid)
                return;
            RulePiece? target = board[to.File, to.Rank];
            if (!target.HasValue)
                moves.Add(new ChessMove(from, to));
            else if (target.Value.Side != side && target.Value.Type != ChessPieceType.King)
                moves.Add(new ChessMove(from, to, ChessMoveFlags.Capture));
        }

        private bool IsAttacked(BoardCoordinate square, ChessSide bySide)
        {
            int pawnSourceDelta = bySide == ChessSide.White ? -1 : 1;
            foreach (int fileDelta in new[] { -1, 1 })
                if (Matches(square.File + fileDelta, square.Rank + pawnSourceDelta, bySide, ChessPieceType.Pawn))
                    return true;

            foreach ((int x, int y) in new[] { (1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2) })
                if (Matches(square.File + x, square.Rank + y, bySide, ChessPieceType.Knight))
                    return true;

            foreach ((int x, int y) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                if (Matches(square.File + x, square.Rank + y, bySide, ChessPieceType.King))
                    return true;
                bool diagonal = x != 0 && y != 0;
                int file = square.File + x;
                int rank = square.Rank + y;
                while (file is >= 0 and < 8 && rank is >= 0 and < 8)
                {
                    RulePiece? piece = board[file, rank];
                    if (piece.HasValue)
                    {
                        if (piece.Value.Side == bySide && (piece.Value.Type == ChessPieceType.Queen ||
                            piece.Value.Type == (diagonal ? ChessPieceType.Bishop : ChessPieceType.Rook)))
                            return true;
                        break;
                    }
                    file += x;
                    rank += y;
                }
            }
            return false;
        }

        private void MakeMove(ChessMove move)
        {
            RulePiece moving = board[move.From.File, move.From.Rank].Value;
            RulePiece? captured = board[move.To.File, move.To.Rank];
            UpdateCastlingRights(move, moving, captured);

            board[move.From.File, move.From.Rank] = null;
            if ((move.Flags & ChessMoveFlags.EnPassant) != 0)
                board[move.To.File, move.From.Rank] = null;
            if ((move.Flags & ChessMoveFlags.Castle) != 0)
            {
                int rookFrom = move.To.File == 6 ? 7 : 0;
                int rookTo = move.To.File == 6 ? 5 : 3;
                board[rookTo, move.From.Rank] = board[rookFrom, move.From.Rank];
                board[rookFrom, move.From.Rank] = null;
            }

            board[move.To.File, move.To.Rank] = move.IsPromotion ? new RulePiece(moving.Side, move.Promotion) : moving;
            EnPassantTarget = moving.Type == ChessPieceType.Pawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
                ? new BoardCoordinate(move.From.File, (move.From.Rank + move.To.Rank) / 2)
                : null;
            HalfmoveClock = moving.Type == ChessPieceType.Pawn || captured.HasValue || move.IsCapture ? 0 : HalfmoveClock + 1;
            SideToMove = Opponent(SideToMove);
        }

        private void UpdateCastlingRights(ChessMove move, RulePiece moving, RulePiece? captured)
        {
            if (moving.Type == ChessPieceType.King)
            {
                if (moving.Side == ChessSide.White) { WhiteCastleKingSide = false; WhiteCastleQueenSide = false; }
                else { BlackCastleKingSide = false; BlackCastleQueenSide = false; }
            }
            if (moving.Type == ChessPieceType.Rook)
                RemoveRookRight(moving.Side, move.From);
            if (captured.HasValue && captured.Value.Type == ChessPieceType.Rook)
                RemoveRookRight(captured.Value.Side, move.To);
        }

        private void RemoveRookRight(ChessSide side, BoardCoordinate square)
        {
            int rank = side == ChessSide.White ? 0 : 7;
            if (square == new BoardCoordinate(0, rank))
            {
                if (side == ChessSide.White) WhiteCastleQueenSide = false; else BlackCastleQueenSide = false;
            }
            if (square == new BoardCoordinate(7, rank))
            {
                if (side == ChessSide.White) WhiteCastleKingSide = false; else BlackCastleKingSide = false;
            }
        }

        private bool HasInsufficientMaterial()
        {
            List<(RulePiece piece, BoardCoordinate square)> material = new();
            for (int file = 0; file < 8; file++)
            for (int rank = 0; rank < 8; rank++)
                if (board[file, rank].HasValue && board[file, rank].Value.Type != ChessPieceType.King)
                    material.Add((board[file, rank].Value, new BoardCoordinate(file, rank)));

            if (material.Count == 0)
                return true;
            if (material.Count == 1)
                return material[0].piece.Type is ChessPieceType.Bishop or ChessPieceType.Knight;
            if (material.All(item => item.piece.Type == ChessPieceType.Bishop))
                return material.Select(item => (item.square.File + item.square.Rank) & 1).Distinct().Count() == 1;
            return false;
        }

        private ChessPosition Clone()
        {
            ChessPosition clone = new()
            {
                SideToMove = SideToMove,
                EnPassantTarget = EnPassantTarget,
                WhiteCastleKingSide = WhiteCastleKingSide,
                WhiteCastleQueenSide = WhiteCastleQueenSide,
                BlackCastleKingSide = BlackCastleKingSide,
                BlackCastleQueenSide = BlackCastleQueenSide,
                HalfmoveClock = HalfmoveClock
            };
            Array.Copy(board, clone.board, board.Length);
            return clone;
        }

        private bool Matches(int file, int rank, ChessSide side, ChessPieceType type)
        {
            if (file is < 0 or >= 8 || rank is < 0 or >= 8)
                return false;
            RulePiece? piece = board[file, rank];
            return piece.HasValue && piece.Value.Side == side && piece.Value.Type == type;
        }

        private bool IsRook(ChessSide side, int file, int rank) => Matches(file, rank, side, ChessPieceType.Rook);
        private static ChessSide Opponent(ChessSide side) => side == ChessSide.White ? ChessSide.Black : ChessSide.White;
    }
}
