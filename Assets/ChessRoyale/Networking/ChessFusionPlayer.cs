using Fusion;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ChessRoyale.Networking
{
    // A player's visible body in an online match, following the tutorial's Network Player pattern.
    //
    // Each headset keeps its own scene XR rig for seeing and interacting. This lightweight avatar
    // (head, hands, body) is spawned by its owner and copies that rig's tracked poses every tick.
    // Poses are explicit networked properties instead of child NetworkTransforms: this keeps the
    // avatar visible even when a child transform misses its first snapshot. The owner hides its own
    // copy, since its real hands are already visible.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChessFusionPlayer : NetworkBehaviour
    {
        private static readonly Color WhiteSeatColor = new Color(0.92f, 0.92f, 0.9f);
        private static readonly Color BlackSeatColor = new Color(0.16f, 0.16f, 0.18f);
        private static readonly Color UnseatedColor = new Color(0.75f, 0.75f, 0.78f);

        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform body;
        [SerializeField] private float bodyDropBelowHead = 0.75f;

        // 0 = not seated, 1 = White, 2 = Black. Repaints the body when the value arrives.
        [Networked, OnChangedRender(nameof(ApplySeatColor))]
        public int SeatColor { get; set; }

        [Networked] private NetworkBool HasTrackedPose { get; set; }
        [Networked] private Vector3 HeadPosition { get; set; }
        [Networked] private Quaternion HeadRotation { get; set; }
        [Networked] private Vector3 LeftHandPosition { get; set; }
        [Networked] private Quaternion LeftHandRotation { get; set; }
        [Networked] private Vector3 RightHandPosition { get; set; }
        [Networked] private Quaternion RightHandRotation { get; set; }
        [Networked] private Vector3 BodyPosition { get; set; }
        [Networked] private Quaternion BodyRotation { get; set; }

        private Transform headSource;
        private Transform leftSource;
        private Transform rightSource;
        private MaterialPropertyBlock colorBlock;

        public static ChessFusionPlayer Local { get; private set; }

        public bool IsLocalPlayer => Object != null && Object.IsValid && Object.HasStateAuthority;

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                Local = this;
                BindToLocalRig();
                SetRenderersEnabled(false);
            }
            else
            {
                SetRenderersEnabled(true);
                ChessRoyale.ChessPlayerHands.Apply(transform);
                ApplyFallbackPose();
            }

            ApplySeatColor();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority)
                return;

            if (headSource == null)
                BindToLocalRig();

            CaptureTrackedPose();

            int seat = CurrentSeatColor();
            if (SeatColor != seat)
                SeatColor = seat;
        }

        public override void Render()
        {
            if (Object == null || !Object.IsValid || Object.HasStateAuthority)
                return;

            if (!HasTrackedPose)
            {
                ApplyFallbackPose();
                return;
            }

            ApplyPose(head, HeadPosition, HeadRotation);
            ApplyPose(leftHand, LeftHandPosition, LeftHandRotation);
            ApplyPose(rightHand, RightHandPosition, RightHandRotation);
            ApplyPose(body, BodyPosition, BodyRotation);
        }

        // Finds this headset's scene rig. Only the owner calls this; proxies never read local tracking.
        private void BindToLocalRig()
        {
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return;

            headSource = origin.Camera != null ? origin.Camera.transform : null;
            leftSource = FindNamedChild(origin.transform, "Left Controller");
            rightSource = FindNamedChild(origin.transform, "Right Controller");
        }

        private void CaptureTrackedPose()
        {
            if (headSource == null)
                return;

            HeadPosition = headSource.position;
            HeadRotation = headSource.rotation;
            CaptureOptional(leftSource, headSource.position + headSource.rotation * new Vector3(-0.3f, -0.35f, 0.2f), out Vector3 leftPosition, out Quaternion leftRotation);
            CaptureOptional(rightSource, headSource.position + headSource.rotation * new Vector3(0.3f, -0.35f, 0.2f), out Vector3 rightPosition, out Quaternion rightRotation);
            LeftHandPosition = leftPosition;
            LeftHandRotation = leftRotation;
            RightHandPosition = rightPosition;
            RightHandRotation = rightRotation;

            CalculateBodyPose(headSource, out Vector3 bodyPosition, out Quaternion bodyRotation);
            BodyPosition = bodyPosition;
            BodyRotation = bodyRotation;
            HasTrackedPose = true;

            ApplyPose(head, HeadPosition, HeadRotation);
            ApplyPose(leftHand, LeftHandPosition, LeftHandRotation);
            ApplyPose(rightHand, RightHandPosition, RightHandRotation);
            ApplyPose(body, BodyPosition, BodyRotation);
        }

        private static void CaptureOptional(Transform source, Vector3 fallbackPosition, out Vector3 position, out Quaternion rotation)
        {
            position = source != null ? source.position : fallbackPosition;
            rotation = source != null ? source.rotation : Quaternion.identity;
        }

        // The body hangs below the head and turns with it, but only left/right, so looking down
        // does not tip the whole avatar over.
        private void CalculateBodyPose(Transform source, out Vector3 position, out Quaternion rotation)
        {
            Vector3 forward = Vector3.ProjectOnPlane(source.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(source.up, Vector3.up);
            rotation = forward.sqrMagnitude < 0.0001f ? transform.rotation : Quaternion.LookRotation(forward.normalized, Vector3.up);
            position = source.position + Vector3.down * bodyDropBelowHead;
        }

        private void ApplyFallbackPose()
        {
            Vector3 up = transform.up;
            Vector3 right = transform.right;
            Vector3 forward = transform.forward;
            ApplyPose(head, transform.position + up * 1.55f, transform.rotation);
            ApplyPose(body, transform.position + up * 0.8f, transform.rotation);
            ApplyPose(leftHand, transform.position + up * 1.05f - right * 0.38f + forward * 0.08f, transform.rotation);
            ApplyPose(rightHand, transform.position + up * 1.05f + right * 0.38f + forward * 0.08f, transform.rotation);
        }

        private static void ApplyPose(Transform target, Vector3 position, Quaternion rotation)
        {
            if (target != null)
                target.SetPositionAndRotation(position, rotation);
        }

        private int CurrentSeatColor()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (lobby == null)
                return 0;

            switch (lobby.LocalSeat)
            {
                case ChessSeat.White:
                    return 1;
                case ChessSeat.Black:
                    return 2;
                default:
                    return 0;
            }
        }

        private void ApplySeatColor()
        {
            if (body == null)
                return;

            Color color = SeatColor == 1 ? WhiteSeatColor : SeatColor == 2 ? BlackSeatColor : UnseatedColor;
            if (colorBlock == null)
                colorBlock = new MaterialPropertyBlock();

            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
            {
                renderer.GetPropertyBlock(colorBlock);
                colorBlock.SetColor("_BaseColor", color);
                colorBlock.SetColor("_Color", color);
                renderer.SetPropertyBlock(colorBlock);
            }
        }

        private void SetRenderersEnabled(bool enabled)
        {
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = enabled;
        }

        private static Transform FindNamedChild(Transform root, string childName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
        }
    }
}
