using UnityEngine;

namespace ChessRoyale
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ChessBoardSquare : MonoBehaviour
    {
        [SerializeField] private string coordinate;
        [SerializeField] private int fileIndex;
        [SerializeField] private int rankIndex;

        public string Coordinate => coordinate;
        public int FileIndex => fileIndex;
        public int RankIndex => rankIndex;
        public Vector3 Center => transform.position;

        public void Initialize(int file, int rank)
        {
            fileIndex = file;
            rankIndex = rank;
            coordinate = $"{(char)('A' + file)}{rank + 1}";
            name = $"Square {coordinate}";
        }
    }
}
