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

            transform.SetPositionAndRotation(RootPosition, RootRotation);
            ApplyPose(head, HeadPosition, HeadRotation);
            ApplyPose(leftHand, LeftHandPosition, LeftHandRotation);
            ApplyPose(rightHand, RightHandPosition, RightHandRotation);
            if (!remoteMarkersCreated)
                EnsureRemoteMarkers();
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

            AddMarker(head, 0.22f, new Color(0.93f, 0.86f, 0.62f));
            AddMarker(leftHand, 0.1f, new Color(0.25f, 0.55f, 1f));
            AddMarker(rightHand, 0.1f, new Color(0.25f, 0.55f, 1f));
            remoteMarkersCreated = true;
        }

        private static void AddMarker(Transform parent, float diameter, Color color)
        {
            if (parent == null)
                return;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Remote Pose Marker";
            marker.transform.SetParent(parent, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * diameter;
            Collider collider = marker.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            Renderer renderer = marker.GetComponent<Renderer>();
            if (renderer == null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return;

            renderer.material = new Material(shader) { color = color };
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
