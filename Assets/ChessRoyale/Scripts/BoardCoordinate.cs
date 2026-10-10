using System;

namespace ChessRoyale
{
    [Serializable]
    public readonly struct BoardCoordinate : IEquatable<BoardCoordinate>
    {
        public BoardCoordinate(int file, int rank)
        {
            File = file;
            Rank = rank;
        }

        public int File { get; }
        public int Rank { get; }
        public bool IsValid => File is >= 0 and < 8 && Rank is >= 0 and < 8;
        public string AlgebraicName => IsValid ? $"{(char)('a' + File)}{Rank + 1}" : "--";

        public bool Equals(BoardCoordinate other) => File == other.File && Rank == other.Rank;
        public override bool Equals(object obj) => obj is BoardCoordinate other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(File, Rank);
        public override string ToString() => AlgebraicName;

        public static bool operator ==(BoardCoordinate left, BoardCoordinate right) => left.Equals(right);
        public static bool operator !=(BoardCoordinate left, BoardCoordinate right) => !left.Equals(right);
    }
}
