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
        [SerializeField] private ChessPieceType startingPieceType;
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
            startingPieceType = type;
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
            Transform existing = transform.Find("Promotion Marker");
            if (existing != null)
                Destroy(existing.gameObject);

            GameObject marker = new("Promotion Marker");
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = Vector3.up * 1.6f;
            TextMesh label = marker.AddComponent<TextMesh>();
            label.text = type.ToString().Substring(0, 1);
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 72;
            label.characterSize = 0.09f;
            label.color = side == ChessSide.White ? new Color(0.3f, 0.95f, 1f) : new Color(1f, 0.25f, 0.75f);
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
            pieceType = startingPieceType;
            Transform marker = transform.Find("Promotion Marker");
            if (marker != null)
                Destroy(marker.gameObject);
            gameObject.SetActive(true);
        }
    }
}
