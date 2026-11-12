using Fusion;
using System.Collections.Generic;
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
        private static readonly Color MonkeyFur = new Color(0.24f, 0.09f, 0.035f);
        private static readonly Color MonkeyLightFur = new Color(0.42f, 0.2f, 0.075f);
        private static readonly Color MonkeyFace = new Color(0.68f, 0.43f, 0.22f);
        private static readonly Color MonkeyEye = new Color(0.01f, 0.008f, 0.006f);

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
        private Transform leftArm;
        private Transform rightArm;
        private readonly List<Renderer> seatTintRenderers = new List<Renderer>();
        private bool monkeyBuilt;

        public static ChessFusionPlayer Local { get; private set; }

        public bool IsLocalPlayer => Object != null && Object.IsValid && Object.HasStateAuthority;

        public override void Spawned()
        {
            EnsureMonkeyAvatar();

            if (Object.HasStateAuthority)
            {
                Local = this;
                BindToLocalRig();
                SetRenderersEnabled(false);
            }
            else
            {
                SetRenderersEnabled(true);
                ApplyFallbackPose();
                UpdateMonkeyArms();
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
            UpdateMonkeyArms();
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
            UpdateMonkeyArms();
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
            Color color = SeatColor == 1 ? WhiteSeatColor : SeatColor == 2 ? BlackSeatColor : UnseatedColor;
            if (colorBlock == null)
                colorBlock = new MaterialPropertyBlock();

            if (seatTintRenderers.Count > 0)
            {
                foreach (Renderer renderer in seatTintRenderers)
                    SetRendererColor(renderer, color);
                return;
            }

            if (body == null)
                return;

            foreach (Renderer renderer in body.GetComponentsInChildren<Renderer>(true))
                SetRendererColor(renderer, color);
        }

        private void EnsureMonkeyAvatar()
        {
            if (monkeyBuilt || head == null || leftHand == null || rightHand == null || body == null)
                return;

            monkeyBuilt = true;
            SetNamedChildActive(head, "Head Visual", false);
            SetNamedChildActive(head, "Visor", false);
            SetNamedChildActive(body, "Body Visual", false);

            AddMonkeyPart(head, "Monkey Head", PrimitiveType.Sphere, Vector3.zero, Vector3.zero, new Vector3(0.38f, 0.34f, 0.34f), MonkeyFur);
            AddMonkeyPart(head, "Monkey Muzzle", PrimitiveType.Sphere, new Vector3(0f, -0.07f, 0.16f), Vector3.zero, new Vector3(0.26f, 0.18f, 0.2f), MonkeyFace);
            AddMonkeyPart(head, "Left Ear", PrimitiveType.Sphere, new Vector3(-0.2f, 0.01f, 0f), Vector3.zero, new Vector3(0.14f, 0.18f, 0.08f), MonkeyFace);
            AddMonkeyPart(head, "Right Ear", PrimitiveType.Sphere, new Vector3(0.2f, 0.01f, 0f), Vector3.zero, new Vector3(0.14f, 0.18f, 0.08f), MonkeyFace);
            AddMonkeyPart(head, "Left Eye", PrimitiveType.Sphere, new Vector3(-0.075f, 0.055f, 0.17f), Vector3.zero, Vector3.one * 0.045f, MonkeyEye);
            AddMonkeyPart(head, "Right Eye", PrimitiveType.Sphere, new Vector3(0.075f, 0.055f, 0.17f), Vector3.zero, Vector3.one * 0.045f, MonkeyEye);
            AddMonkeyPart(head, "Monkey Nose", PrimitiveType.Sphere, new Vector3(0f, -0.035f, 0.25f), Vector3.zero, new Vector3(0.09f, 0.055f, 0.045f), MonkeyEye);

            AddMonkeyPart(body, "Monkey Torso", PrimitiveType.Capsule, Vector3.zero, Vector3.zero, new Vector3(0.48f, 0.48f, 0.38f), MonkeyFur);
            AddMonkeyPart(body, "Monkey Belly", PrimitiveType.Sphere, new Vector3(0f, -0.04f, 0.28f), Vector3.zero, new Vector3(0.32f, 0.42f, 0.09f), MonkeyLightFur);
            Renderer seatBadge = AddMonkeyPart(body, "Seat Badge", PrimitiveType.Cube, new Vector3(0f, 0.2f, 0.37f), Vector3.zero, new Vector3(0.24f, 0.09f, 0.035f), UnseatedColor);
            if (seatBadge != null)
                seatTintRenderers.Add(seatBadge);

            AddMonkeyPart(leftHand, "Left Monkey Fist", PrimitiveType.Sphere, Vector3.zero, Vector3.zero, new Vector3(0.18f, 0.16f, 0.2f), MonkeyLightFur);
            AddMonkeyPart(rightHand, "Right Monkey Fist", PrimitiveType.Sphere, Vector3.zero, Vector3.zero, new Vector3(0.18f, 0.16f, 0.2f), MonkeyLightFur);

            leftArm = AddMonkeyPart(transform, "Left Monkey Arm", PrimitiveType.Capsule, Vector3.zero, Vector3.zero, Vector3.one, MonkeyFur)?.transform;
            rightArm = AddMonkeyPart(transform, "Right Monkey Arm", PrimitiveType.Capsule, Vector3.zero, Vector3.zero, Vector3.one, MonkeyFur)?.transform;
        }

        private void UpdateMonkeyArms()
        {
            if (!monkeyBuilt || body == null)
                return;

            UpdateArm(leftArm, body.position + body.rotation * new Vector3(-0.26f, 0.28f, 0f), leftHand != null ? leftHand.position : body.position);
            UpdateArm(rightArm, body.position + body.rotation * new Vector3(0.26f, 0.28f, 0f), rightHand != null ? rightHand.position : body.position);
        }

        private static void UpdateArm(Transform arm, Vector3 shoulder, Vector3 fist)
        {
            if (arm == null)
                return;

            Vector3 direction = fist - shoulder;
            float length = Mathf.Max(0.08f, direction.magnitude);
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.FromToRotation(Vector3.up, direction.normalized)
                : Quaternion.identity;
            arm.SetPositionAndRotation((shoulder + fist) * 0.5f, rotation);
            arm.localScale = new Vector3(0.11f, length * 0.5f, 0.11f);
        }

        private static Renderer AddMonkeyPart(
            Transform parent,
            string partName,
            PrimitiveType primitive,
            Vector3 localPosition,
            Vector3 localEulerAngles,
            Vector3 localScale,
            Color color)
        {
            GameObject part = GameObject.CreatePrimitive(primitive);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.Euler(localEulerAngles);
            part.transform.localScale = localScale;

            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = part.GetComponent<Renderer>();
            SetRendererColor(renderer, color);
            return renderer;
        }

        private static void SetNamedChildActive(Transform root, string childName, bool active)
        {
            Transform child = FindNamedChild(root, childName);
            if (child != null)
                child.gameObject.SetActive(active);
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
                return;

            MaterialPropertyBlock block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
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
