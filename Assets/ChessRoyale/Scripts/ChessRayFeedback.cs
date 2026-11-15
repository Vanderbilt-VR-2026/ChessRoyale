using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Attachment;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Casters;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace ChessRoyale
{
    // Local controller feedback only: no colliders, network objects, or scene-camera changes.
    [DisallowMultipleComponent]
    public sealed class ChessRayFeedback : MonoBehaviour
    {
        private NearFarInteractor nearFar;
        private XRRayInteractor ray;
        private CurveInteractionCaster caster;
        private GameObject endpoint;
        private Material endpointMaterial;
        private float originalDistance;
        private float originalRadius;
        private CurveInteractionCaster.HitDetectionType originalCast;
        private XRRayInteractor.HitDetectionType originalRayCast;
        private XRInteractorLineVisual line;
        private bool originalSmoothing;
        private InteractionAttachController attachment;
        private bool originalMomentum;
        private bool originalVelocityScaling;
        private bool originalManipulation;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachToLocalRig();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachToLocalRig();

        private static void AttachToLocalRig()
        {
            if (SceneManager.GetActiveScene().name != "ChessRoyaleVR")
                return;
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return;
            foreach (NearFarInteractor interactor in origin.GetComponentsInChildren<NearFarInteractor>(true))
                if (interactor.GetComponent<ChessRayFeedback>() == null)
                    interactor.gameObject.AddComponent<ChessRayFeedback>();
            foreach (XRRayInteractor interactor in origin.GetComponentsInChildren<XRRayInteractor>(true))
                if (interactor.enableUIInteraction && interactor.GetComponent<ChessRayFeedback>() == null)
                    interactor.gameObject.AddComponent<ChessRayFeedback>();
        }

        private void Start()
        {
            nearFar = GetComponent<NearFarInteractor>();
            ray = GetComponent<XRRayInteractor>();
            caster = nearFar != null ? nearFar.farInteractionCaster as CurveInteractionCaster : null;
            if (caster != null)
            {
                originalDistance = caster.castDistance;
                originalRadius = caster.sphereCastRadius;
                originalCast = caster.hitDetectionType;
            }
            else if (ray != null)
            {
                originalDistance = ray.maxRaycastDistance;
                originalRadius = ray.sphereCastRadius;
                originalRayCast = ray.hitDetectionType;
            }
            attachment = nearFar != null ? nearFar.interactionAttachController as InteractionAttachController : null;
            if (attachment != null)
            {
                originalMomentum = attachment.useMomentum;
                originalVelocityScaling = attachment.useDistanceBasedVelocityScaling;
                originalManipulation = attachment.useManipulationInput;
            }
            line = GetComponent<XRInteractorLineVisual>();
            if (line != null)
                originalSmoothing = line.smoothMovement;
            Shader shader = Resources.Load<Shader>("ChessCaptureGlow");
            if (shader == null)
                return;
            endpointMaterial = new Material(shader) { name = "White Controller Endpoint" };
            endpointMaterial.SetColor("_BaseColor", Color.white);
            endpoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            endpoint.name = "White Ray Endpoint";
            endpoint.layer = 2; // Ignore Raycast.
            Collider collider = endpoint.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            Renderer renderer = endpoint.GetComponent<Renderer>();
            renderer.sharedMaterial = endpointMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            endpoint.SetActive(false);
        }

        private void Update()
        {
            if (attachment != null)
            {
                attachment.useMomentum = false;
                attachment.useDistanceBasedVelocityScaling = false;
                attachment.useManipulationInput = false;
            }
            bool overview = ChessViewToggle.Instance != null && ChessViewToggle.Instance.Mode == ChessViewToggle.ViewMode.Overview;
            if (caster != null)
            {
                caster.castDistance = Mathf.Max(originalDistance, 35f);
                caster.hitDetectionType = overview ? CurveInteractionCaster.HitDetectionType.SphereCast : originalCast;
                caster.sphereCastRadius = overview ? Mathf.Max(originalRadius, 0.18f) : originalRadius;
            }
            else if (ray != null)
            {
                ray.maxRaycastDistance = Mathf.Max(originalDistance, 35f);
                ray.hitDetectionType = overview ? XRRayInteractor.HitDetectionType.SphereCast : originalRayCast;
                ray.sphereCastRadius = overview ? Mathf.Max(originalRadius, 0.18f) : originalRadius;
            }
            if (line != null)
                line.smoothMovement = overview ? false : originalSmoothing;
        }

        private void LateUpdate()
        {
            if (endpoint == null)
                return;
            Vector3 position = Vector3.zero;
            bool visible = false;
            if (nearFar != null && ((ICurveInteractionDataProvider)nearFar).isActive)
                visible = nearFar.TryGetCurveEndPoint(out position, true) != EndPointType.None;
            else if (ray != null && ray.isActiveAndEnabled && (line == null || line.enabled))
                visible = ray.TryGetHitInfo(out position, out _, out _, out _);
            endpoint.SetActive(visible);
            if (!visible)
                return;
            Camera camera = Camera.main;
            float distance = camera != null ? Vector3.Distance(camera.transform.position, position) : 2f;
            endpoint.transform.position = position;
            // Keep the dot readable in Overview without covering a distant piece.
            endpoint.transform.localScale = Vector3.one * Mathf.Clamp(distance * 0.006f, 0.025f, 0.1f);
        }

        private void OnDisable()
        {
            if (endpoint != null)
                endpoint.SetActive(false);
            if (attachment != null)
            {
                attachment.useMomentum = originalMomentum;
                attachment.useDistanceBasedVelocityScaling = originalVelocityScaling;
                attachment.useManipulationInput = originalManipulation;
            }
            if (caster != null)
            {
                caster.castDistance = originalDistance;
                caster.sphereCastRadius = originalRadius;
                caster.hitDetectionType = originalCast;
            }
            else if (ray != null)
            {
                ray.maxRaycastDistance = originalDistance;
                ray.sphereCastRadius = originalRadius;
                ray.hitDetectionType = originalRayCast;
            }
            if (line != null)
                line.smoothMovement = originalSmoothing;
        }

        private void OnDestroy()
        {
            if (endpoint != null)
                Destroy(endpoint);
            if (endpointMaterial != null)
                Destroy(endpointMaterial);
        }
    }
}
