namespace ChessRoyale
{
    public struct ChessPieceSnapshot
    {
        public byte PieceId;
        public ChessPieceColor Color;
        public ChessPieceType Type;
        public sbyte SquareIndex;
        public bool HasMoved;
        public bool Captured;
    }

    public struct ChessRulesSnapshot
    {
        public ChessPieceSnapshot[] Pieces;
        public ChessPieceColor ActiveColor;
        public ChessGameOutcome Outcome;
        public string StatusMessage;
        public sbyte PendingPromotionPieceId;
        public sbyte EnPassantSquareIndex;
        public sbyte EnPassantPawnId;
        public int HalfmoveClock;
    }

    public static class ChessSquareIndex
    {
        public static sbyte FromSquare(ChessBoardSquare square)
        {
            if (square == null)
                return -1;

            return (sbyte)(square.RankIndex * 8 + square.FileIndex);
        }

        public static bool TryGetFileRank(int squareIndex, out int file, out int rank)
        {
            if (squareIndex < 0 || squareIndex > 63)
            {
                file = 0;
                rank = 0;
                return false;
            }

            file = squareIndex & 7;
            rank = squareIndex >> 3;
            return true;
        }
    }
}