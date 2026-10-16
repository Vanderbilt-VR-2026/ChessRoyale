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
        [SerializeField] private GameObject hintBoundary;

        private Renderer[] hintRenderers;
        private MaterialPropertyBlock materialProperties;
        private bool moveHintActive;
        private bool captureHintActive;
        private bool checkHintActive;

        public string Coordinate => coordinate;
        public int FileIndex => fileIndex;
        public int RankIndex => rankIndex;
        public Vector3 Center => transform.position;

        private void Awake()
        {
            CacheHintRenderers();
        }

        private void OnEnable()
        {
            ActiveSquares.Add(this);
        }

        private void OnDisable()
        {
            ActiveSquares.Remove(this);
        }

        private void Update()
        {
            if (!checkHintActive)
                return;

            float pulse = 1.5f + Mathf.Sin(Time.unscaledTime * 5f) * 0.5f;
            ApplyHintColor(new Color(1f, 0.05f, 0.03f, 1f), pulse);
        }

        public void Initialize(int file, int rank, GameObject boundary = null)
        {
            fileIndex = file;
            rankIndex = rank;
            coordinate = $"{(char)('A' + file)}{rank + 1}";
            name = $"Square {coordinate}";
            hintBoundary = boundary;
            CacheHintRenderers();
            if (hintBoundary != null)
                hintBoundary.SetActive(false);
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
            moveHintActive = true;
            captureHintActive = isCapture;
            RefreshHintVisual();
        }

        public void ClearMoveHint()
        {
            moveHintActive = false;
            captureHintActive = false;
            RefreshHintVisual();
        }

        public void SetCheckHint(bool enabled)
        {
            checkHintActive = enabled;
            RefreshHintVisual();
        }

        private void CacheHintRenderers()
        {
            if (materialProperties == null)
                materialProperties = new MaterialPropertyBlock();
            hintRenderers = hintBoundary != null
                ? hintBoundary.GetComponentsInChildren<Renderer>(true)
                : System.Array.Empty<Renderer>();
        }

        private void RefreshHintVisual()
        {
            if (hintBoundary == null)
                return;

            bool visible = checkHintActive || moveHintActive;
            hintBoundary.SetActive(visible);
            if (!visible)
                return;

            if (checkHintActive)
                ApplyHintColor(new Color(1f, 0.05f, 0.03f, 1f), 1.5f);
            else if (captureHintActive)
                ApplyHintColor(new Color(1f, 0.2f, 0.05f, 1f), 1.25f);
            else
                ApplyHintColor(new Color(0.05f, 0.55f, 1f, 1f), 1.1f);
        }

        private void ApplyHintColor(Color color, float emissionStrength)
        {
            if (hintRenderers == null || hintRenderers.Length == 0)
                CacheHintRenderers();

            materialProperties.Clear();
            materialProperties.SetColor("_BaseColor", color);
            materialProperties.SetColor("_Color", color);
            materialProperties.SetColor("_EmissionColor", color * emissionStrength);
            foreach (Renderer hintRenderer in hintRenderers)
                hintRenderer.SetPropertyBlock(materialProperties);
        }
    }
}
