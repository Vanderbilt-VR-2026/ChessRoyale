using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class ChessBoardReset : MonoBehaviour
    {
        private XRSimpleInteractable interactable;

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
            foreach (ChessPieceInteractable piece in FindObjectsByType<ChessPieceInteractable>(FindObjectsSortMode.None))
                piece.ResetToStart();
        }
    }
}
