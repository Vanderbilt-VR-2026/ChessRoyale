using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public sealed class ChessPieceInteractable : MonoBehaviour
    {
        [SerializeField] private float squareSize = 2f;
        [SerializeField] private Vector2 boardCenter = new Vector2(0f, 2f);
        [SerializeField] private float boardHalfExtent = 8f;
        [SerializeField] private float boardTop = 0.42f;

        private XRGrabInteractable grabInteractable;
        private Rigidbody body;
        private Vector3 startingPosition;
        private Quaternion startingRotation;
        private float baseOffset;

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            body = GetComponent<Rigidbody>();
            startingPosition = transform.position;
            startingRotation = transform.rotation;
            baseOffset = CalculateBaseOffset();
        }

        private void OnEnable()
        {
            grabInteractable.selectExited.AddListener(OnReleased);
        }

        private void OnDisable()
        {
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        private void Update()
        {
            if (transform.position.y < -3f)
                ResetToStart();
        }

        private void OnReleased(SelectExitEventArgs _)
        {
            Vector3 position = transform.position;
            float minX = boardCenter.x - boardHalfExtent + squareSize * 0.5f;
            float minZ = boardCenter.y - boardHalfExtent + squareSize * 0.5f;
            float maxX = boardCenter.x + boardHalfExtent - squareSize * 0.5f;
            float maxZ = boardCenter.y + boardHalfExtent - squareSize * 0.5f;

            position.x = Mathf.Clamp(
                minX + Mathf.Round((position.x - minX) / squareSize) * squareSize,
                minX,
                maxX);
            position.z = Mathf.Clamp(
                minZ + Mathf.Round((position.z - minZ) / squareSize) * squareSize,
                minZ,
                maxZ);
            position.y = boardTop + baseOffset;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
        }

        public void ResetToStart()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = startingPosition;
            body.rotation = startingRotation;
        }

        private float CalculateBaseOffset()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 0f;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return transform.position.y - bounds.min.y;
        }
    }
}
