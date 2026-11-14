using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace ChessRoyale
{
    // Constrain the target before XR applies it, rather than correcting it only in LateUpdate.
    public sealed class ChessPieceGrabTransformer : XRGeneralGrabTransformer
    {
        private ChessPieceInteractable piece;

        public override void OnLink(XRGrabInteractable grabInteractable)
        {
            base.OnLink(grabInteractable);
            piece = grabInteractable.GetComponent<ChessPieceInteractable>();
            allowOneHandedScaling = false;
            allowTwoHandedScaling = false;
        }

        public override void Process(XRGrabInteractable grabInteractable,
            XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            base.Process(grabInteractable, updatePhase, ref targetPose, ref localScale);
            if (piece != null)
                targetPose.position = piece.ConstrainHeldPosition(targetPose.position);
        }
    }
}
