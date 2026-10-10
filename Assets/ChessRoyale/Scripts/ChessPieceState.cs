using UnityEngine;

namespace ChessRoyale
{
    public enum ChessPieceType
    {
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King
    }

    public sealed class ChessPieceState : MonoBehaviour
    {
        [SerializeField] private ChessSide side;
        [SerializeField] private ChessPieceType pieceType;
        [SerializeField, Range(0, 7)] private int startingFile;
        [SerializeField, Range(0, 7)] private int startingRank;

        public ChessSide Side => side;
        public ChessPieceType PieceType => pieceType;
        public BoardCoordinate StartingSquare => new(startingFile, startingRank);
        private BoardCoordinate currentSquare;
        private bool initialized;

        public BoardCoordinate CurrentSquare => initialized ? currentSquare : StartingSquare;
        public bool IsCaptured { get; private set; }

        private void Awake()
        {
            currentSquare = StartingSquare;
            initialized = true;
        }

        public void Configure(ChessSide pieceSide, ChessPieceType type, int file, int rank)
        {
            side = pieceSide;
            pieceType = type;
            startingFile = file;
            startingRank = rank;
            currentSquare = StartingSquare;
            initialized = true;
            IsCaptured = false;
        }

        public void MoveTo(BoardCoordinate square)
        {
            currentSquare = square;
            initialized = true;
        }

        public void PromoteTo(ChessPieceType type)
        {
            pieceType = type;
        }

        public void SetCaptured(bool captured)
        {
            IsCaptured = captured;
            gameObject.SetActive(!captured);
        }

        public void ResetState()
        {
            currentSquare = StartingSquare;
            initialized = true;
            IsCaptured = false;
            gameObject.SetActive(true);
        }
    }
}
