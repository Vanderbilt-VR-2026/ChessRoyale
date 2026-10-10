using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class ChessBoardReset : MonoBehaviour
    {
        private XRSimpleInteractable interactable;
        [SerializeField] private ChessBoardController board;

        private void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
        }

        private void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
        }

        private void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
        }

        private void OnSelected(SelectEnterEventArgs _)
        {
            ResetAllPieces();
        }

        public void ResetAllPieces()
        {
            board ??= FindFirstObjectByType<ChessBoardController>();
            board?.ResetBoard();
        }

        public void Configure(ChessBoardController controller)
        {
            board = controller;
        }
    }
}
