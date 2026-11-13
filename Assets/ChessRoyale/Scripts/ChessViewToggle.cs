using System.Collections.Generic;
using ChessRoyale.Networking;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale
{
    /// <summary>
    /// Switches between the normal standing view ("Immersive") and a raised view over the board
    /// ("Bird's-eye"). A small segmented toggle floats at the lower right of the player's view, and
    /// the Y button on the left controller (B on the keyboard in the editor) flips between the two.
    ///
    /// Bird's-eye lifts the XR rig above the player's own side of the board, facing the opponent,
    /// so looking down shows the whole board. Walking, teleporting and gravity are paused while up
    /// there (turning still works); switching back puts the rig exactly where it was.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChessViewToggle : MonoBehaviour
    {
        public enum ViewMode
        {
            Immersive,
            BirdsEye,
        }

        private const string ChessSceneName = "ChessRoyaleVR";

        // Rig height above the board surface, and how far back from the board centre toward the
        // player's own side. With a ~1.6 m eye height this shows all 64 squares when looking down.
        private const float BirdsEyeHeight = 7f;
        private const float BirdsEyeBackOffset = 5f;

        // Fallback board layout from ChessRoyaleSceneBuilder, used only if no squares are found.
        private static readonly Vector3 FallbackBoardCenter = new Vector3(0f, 0.42f, 2f);

        private const float PanelDistance = 1.6f;
        private const float PanelRight = 0.5f;
        private const float PanelDown = 0.42f;

        private static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f, 0.88f);
        private static readonly Color HintColor = new Color(0.08f, 0.09f, 0.11f, 0.75f);
        private static readonly Color ActiveColor = new Color(0.96f, 0.66f, 0.18f, 1f);
        private static readonly Color ActiveTextColor = new Color(0.1f, 0.08f, 0.04f, 1f);
        private static readonly Color InactiveColor = new Color(1f, 1f, 1f, 0f);
        private static readonly Color InactiveTextColor = Color.white;

        private readonly List<LocomotionProvider> pausedLocomotion = new List<LocomotionProvider>();

        private InputAction toggleAction;
        private Canvas canvas;
        private Image immersiveBackground;
        private Image birdsEyeBackground;
        private Text immersiveLabel;
        private Text birdsEyeLabel;

        private Vector3 savedRigPosition;
        private Quaternion savedRigRotation;
        private float birdsEyeRigY;

        private bool panelPlaced;
        private bool panelFollowing;

        public static ChessViewToggle Instance { get; private set; }

        public ViewMode Mode { get; private set; } = ViewMode.Immersive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += (_, _) => EnsureToggle();
            EnsureToggle();
        }

        private static void EnsureToggle()
        {
            if (SceneManager.GetActiveScene().name != ChessSceneName)
                return;
            if (FindAnyObjectByType<ChessViewToggle>() != null)
                return;

            new GameObject("Chess View Toggle").AddComponent<ChessViewToggle>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureEventSystem();
            BuildPanel();
            RefreshButtons();
        }

        private void OnEnable()
        {
            toggleAction = new InputAction("Toggle Bird's-eye View", InputActionType.Button);
            toggleAction.AddBinding("<XRController>{LeftHand}/secondaryButton");
            toggleAction.AddBinding("<Keyboard>/b");
            toggleAction.performed += OnTogglePressed;
            toggleAction.Enable();
        }

        private void OnDisable()
        {
            if (toggleAction == null)
                return;

            toggleAction.performed -= OnTogglePressed;
            toggleAction.Disable();
            toggleAction.Dispose();
            toggleAction = null;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            ResumeLocomotion();
        }

        private void OnTogglePressed(InputAction.CallbackContext context)
        {
            Toggle();
        }

        public void Toggle()
        {
            SetMode(Mode == ViewMode.Immersive ? ViewMode.BirdsEye : ViewMode.Immersive);
        }

        public void SetMode(ViewMode mode)
        {
            if (mode == Mode)
                return;

            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return;

            if (mode == ViewMode.BirdsEye)
                EnterBirdsEye(origin);
            else
                ExitBirdsEye(origin);

            RefreshButtons();
        }

        private void EnterBirdsEye(XROrigin origin)
        {
            Vector3 center = FindBoardCenter();
            Vector3 forward = PlayerForward(origin, center);

            savedRigPosition = origin.transform.position;
            savedRigRotation = origin.transform.rotation;
            PauseLocomotion();

            Vector3 position = center - forward * BirdsEyeBackOffset;
            position.y = center.y + BirdsEyeHeight;
            PlaceRig(origin, position, Quaternion.LookRotation(forward, Vector3.up));
            birdsEyeRigY = position.y;
            Mode = ViewMode.BirdsEye;
        }

        private void ExitBirdsEye(XROrigin origin)
        {
            PlaceRig(origin, savedRigPosition, savedRigRotation);
            ResumeLocomotion();
            Mode = ViewMode.Immersive;
        }

        private void LateUpdate()
        {
            DropBirdsEyeIfRigWasMoved();
            FollowCamera();
        }

        // Other code can move the rig while we are up (the match start puts each player at their
        // seat; leaving a room puts them back). Snap turning only rotates around the head, so the
        // height stays put; a height change means someone else placed the rig. Hand control back
        // without undoing their move.
        private void DropBirdsEyeIfRigWasMoved()
        {
            if (Mode != ViewMode.BirdsEye)
                return;

            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && Mathf.Abs(origin.transform.position.y - birdsEyeRigY) < 0.25f)
                return;

            ResumeLocomotion();
            Mode = ViewMode.Immersive;
            RefreshButtons();
        }

        // White plays toward +Z and Black toward -Z. Online, the seat decides; offline, whichever
        // end of the board the player is standing at.
        private static Vector3 PlayerForward(XROrigin origin, Vector3 boardCenter)
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (lobby != null && lobby.LocalSeat != ChessSeat.None)
                return lobby.LocalSeat == ChessSeat.Black ? Vector3.back : Vector3.forward;

            float offset = origin.transform.position.z - boardCenter.z;
            if (Mathf.Abs(offset) > 0.5f)
                return offset > 0f ? Vector3.back : Vector3.forward;

            Vector3 facing = origin.Camera != null ? origin.Camera.transform.forward : origin.transform.forward;
            return facing.z < 0f ? Vector3.back : Vector3.forward;
        }

        private static Vector3 FindBoardCenter()
        {
            ChessBoardSquare[] squares = FindObjectsByType<ChessBoardSquare>(FindObjectsSortMode.None);
            if (squares.Length == 0)
                return FallbackBoardCenter;

            Bounds bounds = new Bounds(squares[0].Center, Vector3.zero);
            foreach (ChessBoardSquare square in squares)
                bounds.Encapsulate(square.Center);
            return new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
        }

        // Walking, teleporting and gravity would pull the player off the raised view, so pause
        // every locomotion provider except turning while in Bird's-eye.
        private void PauseLocomotion()
        {
            pausedLocomotion.Clear();
            foreach (LocomotionProvider provider in FindObjectsByType<LocomotionProvider>(FindObjectsSortMode.None))
            {
                if (provider == null || !provider.enabled || provider.GetType().Name.Contains("Turn"))
                    continue;

                provider.enabled = false;
                pausedLocomotion.Add(provider);
            }
        }

        private void ResumeLocomotion()
        {
            foreach (LocomotionProvider provider in pausedLocomotion)
            {
                if (provider != null)
                    provider.enabled = true;
            }

            pausedLocomotion.Clear();
        }

        // Same approach as the room manager: a CharacterController keeps its own position and would
        // undo a plain transform move, so switch it off for the jump.
        private static void PlaceRig(XROrigin origin, Vector3 position, Quaternion rotation)
        {
            CharacterController body = origin.GetComponent<CharacterController>();
            bool bodyWasEnabled = body != null && body.enabled;
            if (bodyWasEnabled)
                body.enabled = false;

            origin.transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();

            if (bodyWasEnabled)
                body.enabled = true;
        }

        // ---------- Panel ----------

        // Keeps the toggle at the lower right of the view. It only moves once the head has turned
        // away, then glides into place, so it does not feel glued to the face.
        private void FollowCamera()
        {
            Camera camera = Camera.main;
            if (camera == null || canvas == null)
                return;

            Transform head = camera.transform;
            Vector3 goal = head.position + head.forward * PanelDistance + head.right * PanelRight - head.up * PanelDown;
            Quaternion goalRotation = Quaternion.LookRotation(goal - head.position, head.up);

            // First frame, or a big jump such as entering/leaving Bird's-eye: snap instead of gliding.
            float distance = Vector3.Distance(transform.position, goal);
            if (!panelPlaced || distance > 3f)
            {
                transform.SetPositionAndRotation(goal, goalRotation);
                panelPlaced = true;
                panelFollowing = false;
                return;
            }

            if (distance > 0.35f || Quaternion.Angle(transform.rotation, goalRotation) > 25f)
                panelFollowing = true;
            if (!panelFollowing)
                return;

            float blend = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, goal, blend),
                Quaternion.Slerp(transform.rotation, goalRotation, blend));

            if (Vector3.Distance(transform.position, goal) < 0.01f)
                panelFollowing = false;
        }

        private void RefreshButtons()
        {
            bool birdsEye = Mode == ViewMode.BirdsEye;
            if (immersiveBackground != null)
                immersiveBackground.color = birdsEye ? InactiveColor : ActiveColor;
            if (birdsEyeBackground != null)
                birdsEyeBackground.color = birdsEye ? ActiveColor : InactiveColor;
            if (immersiveLabel != null)
                immersiveLabel.color = birdsEye ? InactiveTextColor : ActiveTextColor;
            if (birdsEyeLabel != null)
                birdsEyeLabel.color = birdsEye ? ActiveTextColor : InactiveTextColor;
        }

        private void BuildPanel()
        {
            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 40;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(640f, 200f);
            rect.localScale = Vector3.one * 0.001f;

            // Segmented control: [ Immersive view | Bird's-eye view ]
            RectTransform track = CreateBox(transform, "Toggle", new Vector2(0f, 40f), new Vector2(640f, 104f), PanelColor);
            CreateSegment(track, "Immersive view", new Vector2(-158f, 0f), ViewMode.Immersive, out immersiveBackground, out immersiveLabel);
            CreateSegment(track, "Bird's-eye view", new Vector2(158f, 0f), ViewMode.BirdsEye, out birdsEyeBackground, out birdsEyeLabel);

            RectTransform hint = CreateBox(transform, "Hint", new Vector2(0f, -60f), new Vector2(620f, 60f), HintColor);
            Text hintText = CreateText(hint, "Press Y on the left controller to switch view", 24, FontStyle.Normal);
            hintText.color = new Color(1f, 1f, 1f, 0.85f);
        }

        private void CreateSegment(RectTransform parent, string label, Vector2 position, ViewMode mode, out Image background, out Text text)
        {
            RectTransform segment = CreateBox(parent, label, position, new Vector2(306f, 84f), InactiveColor);
            background = segment.GetComponent<Image>();
            background.raycastTarget = true;

            Button button = segment.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() => SetMode(mode));

            text = CreateText(segment, label, 30, FontStyle.Bold);
        }

        private static RectTransform CreateBox(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            GameObject box = new GameObject(name, typeof(RectTransform));
            box.transform.SetParent(parent, false);
            RectTransform rect = box.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = box.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        private static Text CreateText(RectTransform parent, string value, int fontSize, FontStyle style)
        {
            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<XRUIInputModule>();
        }
    }
}
