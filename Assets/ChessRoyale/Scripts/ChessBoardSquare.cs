using System.Collections.Generic;
using UnityEngine;

namespace ChessRoyale
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ChessBoardSquare : MonoBehaviour
    {
        private static readonly HashSet<ChessBoardSquare> ActiveSquares = new HashSet<ChessBoardSquare>();

        [SerializeField] private string coordinate;
        [SerializeField] private int fileIndex;
        [SerializeField] private int rankIndex;

        private Renderer squareRenderer;
        private MaterialPropertyBlock materialProperties;

        public string Coordinate => coordinate;
        public int FileIndex => fileIndex;
        public int RankIndex => rankIndex;
        public Vector3 Center => transform.position;

        private void Awake()
        {
            squareRenderer = GetComponent<Renderer>();
            materialProperties = new MaterialPropertyBlock();
        }

        private void OnEnable()
        {
            ActiveSquares.Add(this);
        }

        private void OnDisable()
        {
            ActiveSquares.Remove(this);
        }

        public void Initialize(int file, int rank)
        {
            fileIndex = file;
            rankIndex = rank;
            coordinate = $"{(char)('A' + file)}{rank + 1}";
            name = $"Square {coordinate}";
        }

        public static ChessBoardSquare FindClosest(Vector3 worldPosition, float maxHorizontalDistance)
        {
            ChessBoardSquare closest = null;
            float closestDistanceSquared = maxHorizontalDistance * maxHorizontalDistance;

            foreach (ChessBoardSquare square in ActiveSquares)
            {
                Vector2 offset = new Vector2(
                    worldPosition.x - square.transform.position.x,
                    worldPosition.z - square.transform.position.z);
                float distanceSquared = offset.sqrMagnitude;
                if (distanceSquared > closestDistanceSquared)
                    continue;

                closest = square;
                closestDistanceSquared = distanceSquared;
            }

            return closest;
        }

        public void SetMoveHint(bool isCapture)
        {
            if (squareRenderer == null)
                return;

            Color hintColor = isCapture
                ? new Color(1f, 0.22f, 0.12f, 1f)
                : new Color(0.1f, 0.55f, 1f, 1f);
            materialProperties.Clear();
            materialProperties.SetColor("_BaseColor", hintColor);
            materialProperties.SetColor("_Color", hintColor);
            squareRenderer.SetPropertyBlock(materialProperties);
        }

        public void ClearMoveHint()
        {
            if (squareRenderer != null)
                squareRenderer.SetPropertyBlock(null);
        }
    }
}
