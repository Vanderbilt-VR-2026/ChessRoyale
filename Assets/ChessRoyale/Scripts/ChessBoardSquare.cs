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

        private GameObject moveRing;
        private Mesh ringMesh;
        private Renderer ringRenderer;
        private MaterialPropertyBlock materialProperties;
        private bool moveHintActive;
        private GameObject lastMoveFrame;
        private Mesh lastMoveMesh;
        private Renderer lastMoveRenderer;
        private MaterialPropertyBlock lastMoveProperties;

        // Opponent's last move: blue frames, bright on the destination, dim on the origin.
        private static readonly Color LastMoveDestinationColor = new Color(0.25f, 0.62f, 1f, 1f);
        private static readonly Color LastMoveOriginColor = new Color(0.12f, 0.32f, 0.6f, 1f);

        public string Coordinate => coordinate;
        public int FileIndex => fileIndex;
        public int RankIndex => rankIndex;
        public Vector3 Center => transform.position;

        private void Awake()
        {
            if (hintBoundary != null)
                hintBoundary.SetActive(false);
        }

        private void OnEnable()
        {
            ActiveSquares.Add(this);
        }

        private void OnDisable()
        {
            ActiveSquares.Remove(this);
            ClearMoveHint();
            ClearLastMoveHint();
        }

        private void OnDestroy()
        {
            if (ringMesh != null)
                Destroy(ringMesh);
            if (lastMoveMesh != null)
                Destroy(lastMoveMesh);
        }

        public void Initialize(int file, int rank, GameObject boundary = null)
        {
            fileIndex = file;
            rankIndex = rank;
            coordinate = $"{(char)('A' + file)}{rank + 1}";
            name = $"Square {coordinate}";
            hintBoundary = boundary;
            if (hintBoundary != null)
                hintBoundary.SetActive(false);
        }

        // Footprint of all active squares; used to keep held pieces on the board.
        public static bool TryGetBoardBounds(out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (ChessBoardSquare square in ActiveSquares)
            {
                Collider squareCollider = square.GetComponent<Collider>();
                Bounds squareBounds = squareCollider != null
                    ? squareCollider.bounds
                    : new Bounds(square.Center, Vector3.one);
                if (!found)
                {
                    bounds = squareBounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(squareBounds);
                }
            }

            return found;
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
            // All legal destinations, including captures, use the same gold ring.
            moveHintActive = true;
            EnsureMoveRing();
            RefreshHintVisual();
        }

        public void ClearMoveHint()
        {
            moveHintActive = false;
            RefreshHintVisual();
        }

        public void SetCheckHint(bool enabled)
        {
            // Check remains in the game status; no red board indicators.
            RefreshHintVisual();
        }

        public void SetLastMoveHint(bool isDestination)
        {
            EnsureLastMoveFrame();
            Color color = isDestination ? LastMoveDestinationColor : LastMoveOriginColor;
            lastMoveProperties.SetColor("_BaseColor", color);
            lastMoveProperties.SetColor("_Color", color);
            lastMoveProperties.SetColor("_EmissionColor", color * (isDestination ? 1.4f : 0.6f));
            lastMoveRenderer.SetPropertyBlock(lastMoveProperties);
            lastMoveFrame.SetActive(true);
        }

        public void ClearLastMoveHint()
        {
            if (lastMoveFrame != null)
                lastMoveFrame.SetActive(false);
        }

        // A thin square frame just inside the square's edge, so it never overlaps the gold legal-move ring.
        private void EnsureLastMoveFrame()
        {
            if (lastMoveFrame != null)
                return;

            BoxCollider squareCollider = GetComponent<BoxCollider>();
            Vector3 center = squareCollider.center;
            float outer = Mathf.Min(squareCollider.size.x, squareCollider.size.z) * 0.47f;
            float inner = outer * 0.84f;
            float height = center.y + squareCollider.size.y * 0.5f
                + 0.002f / Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.0001f);
            Vector2[] corners = { new Vector2(1f, 1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f), new Vector2(1f, -1f) };
            var vertices = new Vector3[8];
            var normals = new Vector3[8];
            var triangles = new int[24];
            for (int i = 0; i < 4; i++)
            {
                vertices[i * 2] = new Vector3(center.x + corners[i].x * outer, height, center.z + corners[i].y * outer);
                vertices[i * 2 + 1] = new Vector3(center.x + corners[i].x * inner, height, center.z + corners[i].y * inner);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                int next = ((i + 1) % 4) * 2;
                int offset = i * 6;
                triangles[offset] = i * 2;
                triangles[offset + 1] = i * 2 + 1;
                triangles[offset + 2] = next;
                triangles[offset + 3] = next;
                triangles[offset + 4] = i * 2 + 1;
                triangles[offset + 5] = next + 1;
            }

            lastMoveMesh = new Mesh { name = "Last Move Frame", vertices = vertices, normals = normals, triangles = triangles };
            lastMoveMesh.RecalculateBounds();
            lastMoveMesh.UploadMeshData(true);
            lastMoveFrame = new GameObject("Opponent Last Move Frame");
            lastMoveFrame.transform.SetParent(transform, false);
            lastMoveFrame.AddComponent<MeshFilter>().sharedMesh = lastMoveMesh;
            lastMoveRenderer = lastMoveFrame.AddComponent<MeshRenderer>();
            Renderer source = hintBoundary != null ? hintBoundary.GetComponentInChildren<Renderer>(true) : null;
            lastMoveRenderer.sharedMaterial = source != null ? source.sharedMaterial : GetComponent<Renderer>().sharedMaterial;
            lastMoveRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lastMoveRenderer.receiveShadows = false;
            lastMoveRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            lastMoveRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            lastMoveProperties = new MaterialPropertyBlock();
            lastMoveFrame.SetActive(false);
        }

        private void EnsureMoveRing()
        {
            if (moveRing != null)
                return;

            const int segments = 32;
            BoxCollider squareCollider = GetComponent<BoxCollider>();
            Vector3 center = squareCollider.center;
            float radius = Mathf.Min(squareCollider.size.x, squareCollider.size.z) * 0.36f;
            float height = center.y + squareCollider.size.y * 0.5f
                + 0.002f / Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.0001f);
            var vertices = new Vector3[segments * 2];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 origin = new Vector3(center.x, height, center.z);
                vertices[i * 2] = origin + direction * radius;
                vertices[i * 2 + 1] = origin + direction * (radius * 0.88f);
                normals[i * 2] = normals[i * 2 + 1] = Vector3.up;
                int next = ((i + 1) % segments) * 2;
                int offset = i * 6;
                triangles[offset] = i * 2;
                triangles[offset + 1] = i * 2 + 1;
                triangles[offset + 2] = next;
                triangles[offset + 3] = next;
                triangles[offset + 4] = i * 2 + 1;
                triangles[offset + 5] = next + 1;
            }

            ringMesh = new Mesh { name = "Legal Move Ring", vertices = vertices, normals = normals, triangles = triangles };
            ringMesh.RecalculateBounds();
            ringMesh.UploadMeshData(true);
            moveRing = new GameObject("Golden Legal Move Ring");
            moveRing.transform.SetParent(transform, false);
            moveRing.AddComponent<MeshFilter>().sharedMesh = ringMesh;
            ringRenderer = moveRing.AddComponent<MeshRenderer>();
            Renderer source = hintBoundary != null ? hintBoundary.GetComponentInChildren<Renderer>(true) : null;
            ringRenderer.sharedMaterial = source != null ? source.sharedMaterial : GetComponent<Renderer>().sharedMaterial;
            ringRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ringRenderer.receiveShadows = false;
            ringRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            ringRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            materialProperties = new MaterialPropertyBlock();
            Color gold = new Color(1f, 0.68f, 0.16f, 1f);
            materialProperties.SetColor("_BaseColor", gold);
            materialProperties.SetColor("_Color", gold);
            materialProperties.SetColor("_EmissionColor", gold * 1.2f);
            ringRenderer.SetPropertyBlock(materialProperties);
        }

        private void RefreshHintVisual()
        {
            if (hintBoundary != null)
                hintBoundary.SetActive(false);
            if (moveRing != null)
                moveRing.SetActive(moveHintActive);
        }
    }
}
