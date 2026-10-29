using Fusion;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ChessRoyale.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChessFusionPlayer : NetworkBehaviour
    {
        [SerializeField] private XROrigin xrOrigin;
        [SerializeField] private Camera xrCamera;
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftHand;
        [SerializeField] private Transform rightHand;

        [Networked] private NetworkBool HasPose { get; set; }
        [Networked] private Vector3 RootPosition { get; set; }
        [Networked] private Quaternion RootRotation { get; set; }
        [Networked] private Vector3 HeadPosition { get; set; }
        [Networked] private Quaternion HeadRotation { get; set; }
        [Networked] private Vector3 LeftHandPosition { get; set; }
        [Networked] private Quaternion LeftHandRotation { get; set; }
        [Networked] private Vector3 RightHandPosition { get; set; }
        [Networked] private Quaternion RightHandRotation { get; set; }

        private bool remoteMarkersCreated;
        private bool remoteColored;
        private Renderer bodyRenderer;
        private Vector3 shownRootPosition;
        private Quaternion shownRootRotation;
        private Vector3 shownHeadPosition;
        private Quaternion shownHeadRotation;
        private Vector3 shownLeftPosition;
        private Quaternion shownLeftRotation;
        private Vector3 shownRightPosition;
        private Quaternion shownRightRotation;
        private bool hasShownPose;

        public bool IsLocalPlayer => Object != null && Object.IsValid && Object.HasInputAuthority;
        public Camera XrCamera => xrCamera;

        private void Awake()
        {
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>() ?? GetComponentInChildren<XROrigin>(true);
            if (xrCamera == null && xrOrigin != null)
                xrCamera = xrOrigin.Camera;
            if (xrCamera == null)
                xrCamera = GetComponentInChildren<Camera>(true);
            if (head == null && xrCamera != null)
                head = xrCamera.transform;
            if (leftHand == null)
                leftHand = FindNamedChild("Left Controller");
            if (rightHand == null)
                rightHand = FindNamedChild("Right Controller");
        }

        public override void Spawned()
        {
            ApplyOwnership();
            if (!IsLocalPlayer)
                EnsureRemoteMarkers();
        }

        public override void FixedUpdateNetwork()
        {
            if (!IsLocalPlayer)
                return;

            RootPosition = transform.position;
            RootRotation = transform.rotation;
            Capture(head, out Vector3 headPosition, out Quaternion headRotation);
            Capture(leftHand, out Vector3 leftPosition, out Quaternion leftRotation);
            Capture(rightHand, out Vector3 rightPosition, out Quaternion rightRotation);
            HeadPosition = headPosition;
            HeadRotation = headRotation;
            LeftHandPosition = leftPosition;
            LeftHandRotation = leftRotation;
            RightHandPosition = rightPosition;
            RightHandRotation = rightRotation;
            HasPose = true;
        }

        public override void Render()
        {
            if (IsLocalPlayer || Object == null || !Object.IsValid || !HasPose)
                return;

            float blend = hasShownPose ? 1f - Mathf.Exp(-Time.deltaTime * 14f) : 1f;
            shownRootPosition = Vector3.Lerp(hasShownPose ? shownRootPosition : RootPosition, RootPosition, blend);
            shownRootRotation = Quaternion.Slerp(hasShownPose ? shownRootRotation : RootRotation, RootRotation, blend);
            shownHeadPosition = Vector3.Lerp(hasShownPose ? shownHeadPosition : HeadPosition, HeadPosition, blend);
            shownHeadRotation = Quaternion.Slerp(hasShownPose ? shownHeadRotation : HeadRotation, HeadRotation, blend);
            shownLeftPosition = Vector3.Lerp(hasShownPose ? shownLeftPosition : LeftHandPosition, LeftHandPosition, blend);
            shownLeftRotation = Quaternion.Slerp(hasShownPose ? shownLeftRotation : LeftHandRotation, LeftHandRotation, blend);
            shownRightPosition = Vector3.Lerp(hasShownPose ? shownRightPosition : RightHandPosition, RightHandPosition, blend);
            shownRightRotation = Quaternion.Slerp(hasShownPose ? shownRightRotation : RightHandRotation, RightHandRotation, blend);
            hasShownPose = true;

            transform.SetPositionAndRotation(shownRootPosition, shownRootRotation);
            ApplyPose(head, shownHeadPosition, shownHeadRotation);
            ApplyPose(leftHand, shownLeftPosition, shownLeftRotation);
            ApplyPose(rightHand, shownRightPosition, shownRightRotation);
            if (!remoteMarkersCreated)
                EnsureRemoteMarkers();
            ColorRemoteBody();
        }

        private void ApplyOwnership()
        {
            if (Object == null || !Object.IsValid)
                return;

            bool isLocal = Object.HasInputAuthority;
            if (xrCamera != null)
                xrCamera.enabled = isLocal;

            foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true))
                listener.enabled = isLocal;

            foreach (UnityEngine.Behaviour behaviour in GetComponentsInChildren<UnityEngine.Behaviour>(true))
            {
                if (behaviour == null || behaviour == this)
                    continue;
                if (!IsRemoteOnlyBehaviour(behaviour))
                    continue;

                behaviour.enabled = isLocal;
            }
        }

        private void EnsureRemoteMarkers()
        {
            if (remoteMarkersCreated || IsLocalPlayer)
                return;

            bodyRenderer = AddMarker(transform, new Color(0.75f, 0.75f, 0.78f), new Vector3(0f, 1f, 0f), new Vector3(0.45f, 0.9f, 0.45f), PrimitiveType.Capsule);
            AddMarker(head, new Color(0.93f, 0.86f, 0.62f), Vector3.zero, Vector3.one * 0.28f, PrimitiveType.Sphere);
            AddMarker(leftHand, new Color(0.25f, 0.55f, 1f), Vector3.zero, Vector3.one * 0.12f, PrimitiveType.Sphere);
            AddMarker(rightHand, new Color(0.25f, 0.55f, 1f), Vector3.zero, Vector3.one * 0.12f, PrimitiveType.Sphere);
            remoteMarkersCreated = true;
        }

        private void ColorRemoteBody()
        {
            if (remoteColored || bodyRenderer == null || Runner == null || Object == null)
                return;

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (lobby == null)
                return;

            bool white = Object.InputAuthority == lobby.WhitePlayer;
            bool black = Object.InputAuthority == lobby.BlackPlayer;
            if (!white && !black)
                return;

            remoteColored = true;
            bodyRenderer.material.color = white
                ? new Color(0.92f, 0.92f, 0.9f)
                : new Color(0.16f, 0.16f, 0.18f);
        }

        private static Renderer AddMarker(Transform parent, Color color, Vector3 localPosition, Vector3 localScale, PrimitiveType primitive)
        {
            if (parent == null)
                return null;

            GameObject marker = GameObject.CreatePrimitive(primitive);
            marker.name = "Remote Pose Marker";
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = localPosition;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = localScale;
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer == null)
                return null;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return renderer;

            renderer.material = new Material(shader) { color = color };
            return renderer;
        }

        private Transform FindNamedChild(string childName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
        }

        private static void Capture(Transform source, out Vector3 position, out Quaternion rotation)
        {
            if (source == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }

            position = source.position;
            rotation = source.rotation;
        }

        private static void ApplyPose(Transform target, Vector3 position, Quaternion rotation)
        {
            if (target == null)
                return;

            target.SetPositionAndRotation(position, rotation);
        }

        private static bool IsRemoteOnlyBehaviour(UnityEngine.Behaviour behaviour)
        {
            string typeName = behaviour.GetType().Name;
            return typeName == "TrackedPoseDriver"
                || typeName == "ActionBasedController"
                || typeName == "XRController"
                || typeName == "LocomotionMediator"
                || typeName == "ContinuousMoveProvider"
                || typeName == "DynamicMoveProvider"
                || typeName == "GrabMoveProvider"
                || typeName == "ContinuousTurnProvider"
                || typeName == "SnapTurnProvider"
                || typeName == "TeleportationProvider"
                || typeName == "XRRayInteractor"
                || typeName == "NearFarInteractor"
                || typeName == "XRDirectInteractor";
        }
    }
}
