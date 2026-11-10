using Fusion;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ChessRoyale.Networking
{
    // A player's visible body in an online match, following the tutorial's Network Player pattern.
    //
    // Each headset keeps its own scene XR rig for seeing and interacting. This lightweight avatar
    // (head, hands, body) is spawned by its owner, copies that rig's tracked poses every tick, and
    // NetworkTransform components send the result to the other player. The owner hides its own copy,
    // since its real hands are already visible.
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
                ChessRoyale.ChessPlayerHands.Apply(transform);
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

            Follow(head, headSource);
            Follow(leftHand, leftSource);
            Follow(rightHand, rightSource);
            FollowBody();

            int seat = CurrentSeatColor();
            if (SeatColor != seat)
                SeatColor = seat;
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

        private static void Follow(Transform target, Transform source)
        {
            if (target == null || source == null)
                return;

            target.SetPositionAndRotation(source.position, source.rotation);
        }

        // The body hangs below the head and turns with it, but only left/right, so looking down
        // does not tip the whole avatar over.
        private void FollowBody()
        {
            if (body == null || headSource == null)
                return;

            Vector3 forward = Vector3.ProjectOnPlane(headSource.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
                forward = Vector3.ProjectOnPlane(headSource.up, Vector3.up);
            Quaternion yaw = forward.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(forward.normalized, Vector3.up);
            body.SetPositionAndRotation(headSource.position + Vector3.down * bodyDropBelowHead, yaw);
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
