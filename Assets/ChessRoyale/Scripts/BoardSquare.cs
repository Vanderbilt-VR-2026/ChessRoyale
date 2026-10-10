using System;
using UnityEngine;

namespace ChessRoyale
{
    public enum BoardSquareHighlight
    {
        None,
        LegalMove,
        LegalCapture,
        Check
    }

    [RequireComponent(typeof(Renderer))]
    public sealed class BoardSquare : MonoBehaviour
    {
        [SerializeField, Range(0, 7)] private int file;
        [SerializeField, Range(0, 7)] private int rank;

        private Renderer squareRenderer;
        private MaterialPropertyBlock propertyBlock;

        public int File => file;
        public int Rank => rank;
        public BoardCoordinate Coordinate => new(file, rank);
        public string AlgebraicName => $"{(char)('A' + file)}{rank + 1}";
        public Vector3 SnapPosition => transform.position + Vector3.up * transform.lossyScale.y * 0.5f;

        public void Configure(int boardFile, int boardRank)
        {
            if (boardFile is < 0 or > 7)
                throw new ArgumentOutOfRangeException(nameof(boardFile));
            if (boardRank is < 0 or > 7)
                throw new ArgumentOutOfRangeException(nameof(boardRank));

            file = boardFile;
            rank = boardRank;
            name = $"Square {AlgebraicName}";
        }

        public void SetHighlight(BoardSquareHighlight highlight)
        {
            squareRenderer ??= GetComponent<Renderer>();
            propertyBlock ??= new MaterialPropertyBlock();
            squareRenderer.GetPropertyBlock(propertyBlock);

            Color emission = highlight switch
            {
                BoardSquareHighlight.LegalMove => new Color(0f, 1.4f, 1.8f),
                BoardSquareHighlight.LegalCapture => new Color(1.8f, 0.12f, 0.7f),
                BoardSquareHighlight.Check => new Color(2.1f, 0.05f, 0.05f),
                _ => Color.black
            };

            propertyBlock.SetColor("_EmissionColor", emission);
            squareRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
