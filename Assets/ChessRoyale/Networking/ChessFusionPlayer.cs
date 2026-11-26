using Fusion;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

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
        private static Material runtimePrimitiveMaterial;

        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;
        [SerializeField] private Transform body;
        [SerializeField] private float bodyDropBelowHead = 0.75f;
        [SerializeField] private float fixedHeadHeight = 1.55f;
        [Header("Humanoid Avatar")]
        [SerializeField] private GameObject humanoidAvatarPrefab;
        [SerializeField] private float avatarFeetBelowBody = 0.8f;
        [SerializeField] private float walkingSpeedThreshold = 0.08f;
        [SerializeField] private float maxHeadAlignmentOffset = 0.12f;

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
        [Networked] private float MovementSpeed { get; set; }
        [Networked] private NetworkBool LeftGripping { get; set; }
        [Networked] private NetworkBool RightGripping { get; set; }

        private Transform headSource;
        private Transform leftSource;
        private Transform rightSource;
        private XRBaseInteractor leftInteractor;
        private XRBaseInteractor rightInteractor;
        private MaterialPropertyBlock colorBlock;
        private Transform leftArm;
        private Transform rightArm;
        private readonly List<Renderer> seatTintRenderers = new List<Renderer>();
        private bool monkeyBuilt;

        private GameObject humanoidAvatar;
        private Transform humanoidRoot;
        private Animator humanoidAnimator;
        private Transform humanoidHead;
        private Transform humanoidLeftUpperArm;
        private Transform humanoidLeftLowerArm;
        private Transform humanoidLeftHand;
        private Transform humanoidRightUpperArm;
        private Transform humanoidRightLowerArm;
        private Transform humanoidRightHand;
        private Transform[] leftFingerBones;
        private Transform[] rightFingerBones;
        private Quaternion[] leftFingerOpenRotations;
        private Quaternion[] rightFingerOpenRotations;
        private Quaternion humanoidHeadRotationOffset = Quaternion.identity;
        private Quaternion humanoidLeftHandRotationOffset = Quaternion.identity;
        private Quaternion humanoidRightHandRotationOffset = Quaternion.identity;
        private Vector3 previousCapturedBodyPosition;
        private bool hasPreviousCapturedBodyPosition;
        private float animationTime;
        private bool humanoidReady;

        private static readonly int StandStateHash = Animator.StringToHash("Base Layer.Stand");
        private static readonly int WalkStateHash = Animator.StringToHash("Base Layer.Wallk");

        public static ChessFusionPlayer Local { get; private set; }

        public bool IsLocalPlayer => Object != null && Object.IsValid && Object.HasStateAuthority;
        public Transform LeftHandTransform => leftHand;
        public Transform RightHandTransform => rightHand;

        public static ChessFusionPlayer FindForPlayer(PlayerRef player)
        {
            foreach (ChessFusionPlayer candidate in FindObjectsByType<ChessFusionPlayer>(FindObjectsSortMode.None))
            {
                if (candidate.Object != null && candidate.Object.IsValid && candidate.Object.InputAuthority == player)
                    return candidate;
            }
            return null;
        }

        public override void Spawned()
        {
            EnsureVisibleAvatar();

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
                UpdateVisibleAvatar();
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
            UpdateVisibleAvatar();
        }

        private void LateUpdate()
        {
            if (!humanoidReady || Object == null || !Object.IsValid || Object.HasStateAuthority)
                return;

            UpdateHumanoidAvatar();
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
            leftInteractor = FindInteractor(leftSource, origin.transform, "Left");
            rightInteractor = FindInteractor(rightSource, origin.transform, "Right");
        }

        private void CaptureTrackedPose()
        {
            if (headSource == null)
                return;

            // Keep every avatar at one consistent height. Room-scale X/Z movement still moves the
            // avatar, but crouching or changing the headset's floor calibration cannot stretch it.
            Vector3 fixedHeadPosition = headSource.position;
            fixedHeadPosition.y = transform.position.y + fixedHeadHeight;
            HeadPosition = fixedHeadPosition;
            HeadRotation = headSource.rotation;
            CaptureOptional(leftSource, HeadPosition + headSource.rotation * new Vector3(-0.3f, -0.35f, 0.2f), out Vector3 leftPosition, out Quaternion leftRotation);
            CaptureOptional(rightSource, HeadPosition + headSource.rotation * new Vector3(0.3f, -0.35f, 0.2f), out Vector3 rightPosition, out Quaternion rightRotation);
            LeftHandPosition = leftPosition;
            LeftHandRotation = leftRotation;
            RightHandPosition = rightPosition;
            RightHandRotation = rightRotation;

            CalculateBodyPose(headSource, out Vector3 bodyPosition, out Quaternion bodyRotation);
            BodyPosition = bodyPosition;
            BodyRotation = bodyRotation;
            Vector3 planarDelta = Vector3.ProjectOnPlane(bodyPosition - previousCapturedBodyPosition, Vector3.up);
            MovementSpeed = hasPreviousCapturedBodyPosition && Runner.DeltaTime > 0f
                ? Mathf.Clamp(planarDelta.magnitude / Runner.DeltaTime, 0f, 4f)
                : 0f;
            previousCapturedBodyPosition = bodyPosition;
            hasPreviousCapturedBodyPosition = true;
            LeftGripping = leftInteractor != null && leftInteractor.hasSelection;
            RightGripping = rightInteractor != null && rightInteractor.hasSelection;
            HasTrackedPose = true;

            ApplyPose(head, HeadPosition, HeadRotation);
            ApplyPose(leftHand, LeftHandPosition, LeftHandRotation);
            ApplyPose(rightHand, RightHandPosition, RightHandRotation);
            ApplyPose(body, BodyPosition, BodyRotation);
        }

        private static XRBaseInteractor FindInteractor(Transform controller, Transform rigRoot, string sideName)
        {
            XRBaseInteractor interactor = controller != null
                ? controller.GetComponentInChildren<XRBaseInteractor>(true)
                : null;
            if (interactor != null || rigRoot == null)
                return interactor;

            foreach (XRBaseInteractor candidate in rigRoot.GetComponentsInChildren<XRBaseInteractor>(true))
            {
                if (candidate.name.Contains(sideName))
                    return candidate;
            }

            return null;
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
            position = new Vector3(
                source.position.x,
                transform.position.y + fixedHeadHeight - bodyDropBelowHead,
                source.position.z);
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
            UpdateVisibleAvatar();
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

        private void EnsureVisibleAvatar()
        {
            if (TryBuildHumanoidAvatar())
                return;

            EnsureMonkeyAvatar();
        }

        private bool TryBuildHumanoidAvatar()
        {
            if (humanoidReady)
                return true;
            if (humanoidAvatarPrefab == null || head == null || leftHand == null || rightHand == null || body == null)
                return false;

            humanoidAvatar = Instantiate(humanoidAvatarPrefab, transform);
            humanoidAvatar.name = "Network Humanoid Avatar";
            humanoidRoot = humanoidAvatar.transform;
            humanoidRoot.localPosition = Vector3.zero;
            humanoidRoot.localRotation = Quaternion.identity;

            humanoidAnimator = humanoidAvatar.GetComponent<Animator>();
            if (humanoidAnimator == null || !humanoidAnimator.isHuman)
            {
                Destroy(humanoidAvatar);
                humanoidAvatar = null;
                return false;
            }

            humanoidAnimator.applyRootMotion = false;
            humanoidAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            humanoidAnimator.speed = 0f;
            humanoidAnimator.Play(StandStateHash, 0, 0f);
            humanoidAnimator.Update(0f);

            humanoidHead = humanoidAnimator.GetBoneTransform(HumanBodyBones.Head);
            humanoidLeftUpperArm = humanoidAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            humanoidLeftLowerArm = humanoidAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            humanoidLeftHand = humanoidAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            humanoidRightUpperArm = humanoidAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            humanoidRightLowerArm = humanoidAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            humanoidRightHand = humanoidAnimator.GetBoneTransform(HumanBodyBones.RightHand);

            humanoidReady = humanoidHead != null &&
                humanoidLeftUpperArm != null && humanoidLeftLowerArm != null && humanoidLeftHand != null &&
                humanoidRightUpperArm != null && humanoidRightLowerArm != null && humanoidRightHand != null;
            if (!humanoidReady)
            {
                Destroy(humanoidAvatar);
                humanoidAvatar = null;
                return false;
            }

            humanoidHeadRotationOffset = Quaternion.Inverse(humanoidRoot.rotation) * humanoidHead.rotation;
            humanoidLeftHandRotationOffset = Quaternion.Inverse(humanoidRoot.rotation) * humanoidLeftHand.rotation;
            humanoidRightHandRotationOffset = Quaternion.Inverse(humanoidRoot.rotation) * humanoidRightHand.rotation;
            leftFingerBones = GetFingerBones(humanoidAnimator, true);
            rightFingerBones = GetFingerBones(humanoidAnimator, false);
            leftFingerOpenRotations = CaptureLocalRotations(leftFingerBones);
            rightFingerOpenRotations = CaptureLocalRotations(rightFingerBones);

            SetNamedChildActive(head, "Head Visual", false);
            SetNamedChildActive(head, "Visor", false);
            SetNamedChildActive(body, "Body Visual", false);
            SetNamedChildActive(leftHand, "Left Controller Visual", false);
            SetNamedChildActive(rightHand, "Right Controller Visual", false);

            return true;
        }

        private void UpdateVisibleAvatar()
        {
            if (humanoidReady)
                return;

            UpdateMonkeyArms();
        }

        private void UpdateHumanoidAvatar()
        {
            if (humanoidRoot == null || humanoidAnimator == null || body == null)
                return;

            humanoidRoot.SetPositionAndRotation(body.position - body.up * avatarFeetBelowBody, body.rotation);

            bool walking = MovementSpeed > walkingSpeedThreshold;
            animationTime += Time.deltaTime * (walking ? Mathf.Clamp(MovementSpeed * 1.4f, 0.8f, 2.2f) : 0.35f);
            humanoidAnimator.Play(walking ? WalkStateHash : StandStateHash, 0, animationTime % 1f);
            humanoidAnimator.Update(0f);

            if (humanoidHead != null && head != null)
            {
                // Move the whole rig a little to meet the headset instead of pulling the head bone
                // away from the neck, which visibly stretches the avatar's head and neck mesh.
                Vector3 headAlignment = Vector3.ClampMagnitude(
                    head.position - humanoidHead.position,
                    maxHeadAlignmentOffset);
                humanoidRoot.position += headAlignment;
                humanoidHead.rotation = head.rotation * humanoidHeadRotationOffset;
            }

            SolveTwoBoneArm(humanoidLeftUpperArm, humanoidLeftLowerArm, humanoidLeftHand, leftHand,
                -humanoidRoot.right, humanoidLeftHandRotationOffset);
            SolveTwoBoneArm(humanoidRightUpperArm, humanoidRightLowerArm, humanoidRightHand, rightHand,
                humanoidRoot.right, humanoidRightHandRotationOffset);

            ApplyFingerCurl(leftFingerBones, leftFingerOpenRotations, LeftGripping);
            ApplyFingerCurl(rightFingerBones, rightFingerOpenRotations, RightGripping);
        }

        private static void SolveTwoBoneArm(
            Transform upperArm,
            Transform lowerArm,
            Transform handBone,
            Transform target,
            Vector3 elbowSide,
            Quaternion handRotationOffset)
        {
            if (upperArm == null || lowerArm == null || handBone == null || target == null)
                return;

            Vector3 shoulder = upperArm.position;
            Vector3 elbow = lowerArm.position;
            Vector3 wrist = handBone.position;
            float upperLength = Vector3.Distance(shoulder, elbow);
            float lowerLength = Vector3.Distance(elbow, wrist);
            Vector3 toTarget = target.position - shoulder;
            float targetDistance = Mathf.Clamp(toTarget.magnitude, 0.02f, Mathf.Max(0.02f, upperLength + lowerLength - 0.005f));
            Vector3 targetDirection = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : upperArm.forward;

            Vector3 bendDirection = Vector3.ProjectOnPlane(elbowSide + Vector3.down * 0.3f, targetDirection).normalized;
            if (bendDirection.sqrMagnitude < 0.0001f)
                bendDirection = Vector3.ProjectOnPlane(Vector3.up, targetDirection).normalized;

            float cosine = Mathf.Clamp(
                (upperLength * upperLength + targetDistance * targetDistance - lowerLength * lowerLength) /
                Mathf.Max(0.0001f, 2f * upperLength * targetDistance), -1f, 1f);
            float sine = Mathf.Sqrt(Mathf.Max(0f, 1f - cosine * cosine));
            Vector3 desiredElbow = shoulder + targetDirection * (cosine * upperLength) + bendDirection * (sine * upperLength);

            upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder, desiredElbow - shoulder) * upperArm.rotation;
            elbow = lowerArm.position;
            wrist = handBone.position;
            lowerArm.rotation = Quaternion.FromToRotation(wrist - elbow, target.position - elbow) * lowerArm.rotation;
            handBone.SetPositionAndRotation(target.position, target.rotation * handRotationOffset);
        }

        private static Transform[] GetFingerBones(Animator animator, bool left)
        {
            HumanBodyBones[] bones = left
                ? new[]
                {
                    HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
                    HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
                    HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
                    HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
                }
                : new[]
                {
                    HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
                    HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
                    HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
                    HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
                };

            Transform[] transforms = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                transforms[i] = animator.GetBoneTransform(bones[i]);
            return transforms;
        }

        private static Quaternion[] CaptureLocalRotations(Transform[] bones)
        {
            Quaternion[] rotations = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                rotations[i] = bones[i] != null ? bones[i].localRotation : Quaternion.identity;
            return rotations;
        }

        private static void ApplyFingerCurl(Transform[] bones, Quaternion[] openRotations, bool gripping)
        {
            if (bones == null || openRotations == null)
                return;

            float curl = gripping ? 62f : 0f;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                    bones[i].localRotation = openRotations[i] * Quaternion.AngleAxis(curl, Vector3.right);
            }
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
            Material primitiveMaterial = GetRuntimePrimitiveMaterial();
            if (renderer != null && primitiveMaterial != null)
                renderer.sharedMaterial = primitiveMaterial;
            SetRendererColor(renderer, color);
            return renderer;
        }

        private static Material GetRuntimePrimitiveMaterial()
        {
            if (runtimePrimitiveMaterial != null)
                return runtimePrimitiveMaterial;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return null;

            runtimePrimitiveMaterial = new Material(shader)
            {
                name = "Chess Royale Runtime Avatar Material",
                hideFlags = HideFlags.HideAndDontSave,
            };
            return runtimePrimitiveMaterial;
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
