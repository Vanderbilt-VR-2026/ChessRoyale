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

        public bool IsLocalPlayer => Object != null && Object.IsValid && Object.HasInputAuthority;

        private void Awake()
        {
            if (xrOrigin == null)
                xrOrigin = GetComponent<XROrigin>();
            if (xrOrigin == null)
                xrOrigin = GetComponentInChildren<XROrigin>(true);

            if (xrCamera == null && xrOrigin != null)
                xrCamera = xrOrigin.Camera;
            if (xrCamera == null)
                xrCamera = GetComponentInChildren<Camera>(true);
        }

        public override void Spawned()
        {
            ApplyOwnership();
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
