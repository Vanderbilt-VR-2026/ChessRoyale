using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace ChessRoyale
{
    // Resolve far dragging from the physical ray, never from an attachment point that can move itself.
    public sealed class ChessPieceGrabTransformer : XRGeneralGrabTransformer
    {
        private ChessPieceInteractable piece;
        private bool aimInitialized;
        private Vector3 initialAim;
        private Vector3 initialPiecePosition;
        private Vector3 lastSafePosition;
        private Vector3 smoothedPosition;
        private Plane dragPlane;
        private float initialRayDistance;
        private bool previousOverview;
        private int lastSmoothFrame = -1;
        private IXRSelectInteractor selectingInteractor;
        private bool farGrab;
        private bool acceptedRayKnown;
        private Vector3 acceptedRayPosition;
        private Quaternion acceptedRayRotation;
        private const float ImmersiveDragGain = 0.65f;
        private const float ImmersiveResponse = 18f;

        public override void OnGrab(XRGrabInteractable grabInteractable)
        {
            base.OnGrab(grabInteractable);
            aimInitialized = false;
            selectingInteractor = null;
            acceptedRayKnown = false;
            lastSafePosition = grabInteractable.transform.position;
        }

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
            if (piece == null)
                return;

            Transform rayOrigin = null;
            if (grabInteractable.interactorsSelecting.Count > 0)
            {
                var interactor = grabInteractable.interactorsSelecting[0];
                if (!ReferenceEquals(selectingInteractor, interactor))
                {
                    selectingInteractor = interactor;
                    aimInitialized = false;
                    acceptedRayKnown = false;
                    if (interactor is NearFarInteractor nf)
                    {
                        farGrab = nf.interactionAttachController.hasOffset ||
                            nf.selectionRegion.Value == NearFarInteractor.Region.Far;
                    }
                    else
                        farGrab = interactor is XRRayInteractor;
                }
                // Lock the source for this selection. An attachment anchor changing its offset
                // must never switch a held chess piece back into velocity-driven XR movement.
                if (farGrab && interactor is NearFarInteractor nearFar)
                    rayOrigin = nearFar.farInteractionCaster.castOrigin;
                else if (farGrab && interactor is XRRayInteractor rayInteractor)
                    rayOrigin = rayInteractor.rayOriginTransform;
            }
            if (rayOrigin == null)
            {
                // Only true near grabs use the general XR transformer.
                base.Process(grabInteractable, updatePhase, ref targetPose, ref localScale);
                aimInitialized = false;
                targetPose.position = piece.ConstrainHeldPosition(targetPose.position);
                lastSafePosition = targetPose.position;
                return;
            }

            // Ignore sub-threshold tracking noise without integrating it into the drag anchor.
            if (!acceptedRayKnown || Vector3.Distance(acceptedRayPosition, rayOrigin.position) > 0.005f ||
                Quaternion.Angle(acceptedRayRotation, rayOrigin.rotation) > 0.25f)
            {
                acceptedRayPosition = rayOrigin.position;
                acceptedRayRotation = rayOrigin.rotation;
                acceptedRayKnown = true;
            }
            Ray ray = new Ray(acceptedRayPosition, acceptedRayRotation * Vector3.forward);
            bool overview = ChessViewToggle.Instance != null && ChessViewToggle.Instance.Mode == ChessViewToggle.ViewMode.Overview;
            if (!aimInitialized || previousOverview != overview)
            {
                initialPiecePosition = piece.ConstrainHeldPosition(piece.transform.position);
                initialRayDistance = Mathf.Clamp(Vector3.Dot(initialPiecePosition - ray.origin, ray.direction), 0.25f, 35f);
                initialAim = ray.GetPoint(initialRayDistance);
                // Start the drag surface at the point the ray actually picked, not at the
                // board surface beyond it. Edge/corner pieces can otherwise project off-board.
                Vector3 normal = ray.direction.y < -0.15f ? Vector3.up
                    : Vector3.up - ray.direction * (ray.direction.y + 0.5f);
                dragPlane = new Plane(normal, initialAim);
                lastSafePosition = smoothedPosition = initialPiecePosition;
                lastSmoothFrame = -1;
                previousOverview = overview;
                aimInitialized = true;
            }

            // Ray aim may leave the board while a piece is held. Only the final piece position
            // is bounded; rejecting the ray itself freezes dragging near edge squares.
            Vector3 aim = dragPlane.Raycast(ray, out float distance) && distance <= 35f
                ? ray.GetPoint(distance) : ray.GetPoint(initialRayDistance);

            float gain = overview ? 1f : ImmersiveDragGain;
            Vector3 desired = initialPiecePosition + (aim - initialAim) * gain;
            desired.y = initialPiecePosition.y + 0.2f;
            Vector3 bounded = piece.ConstrainHeldPosition(desired);
            // Keep the drag anchor at the boundary rather than accumulating an invisible
            // overshoot that the player would have to undo before the piece moves inward again.
            initialPiecePosition += new Vector3(bounded.x - desired.x, 0f, bounded.z - desired.z);
            desired = bounded;
            if (lastSmoothFrame != Time.frameCount)
            {
                Vector3 next = overview ? desired : Vector3.Lerp(smoothedPosition, desired,
                    1f - Mathf.Exp(-ImmersiveResponse * Time.unscaledDeltaTime));
                smoothedPosition = Vector3.MoveTowards(smoothedPosition, next,
                    (overview ? 12f : 8f) * Time.unscaledDeltaTime);
                lastSmoothFrame = Time.frameCount;
            }
            lastSafePosition = piece.ConstrainHeldPosition(smoothedPosition);
            targetPose.position = lastSafePosition;
        }
    }
}
