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
    /// ("Overview"). A small segmented toggle floats at the lower right of the player's view, and
    /// the Y button on the left controller (B on the keyboard in the editor) flips between the two.
    ///
    /// Overview places the XR rig behind the player's king's starting square, facing the opponent,
    /// so looking down shows the whole board. Walking, teleporting and gravity are paused while up
    /// there (turning still works); switching back puts the rig exactly where it was.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChessViewToggle : MonoBehaviour
    {
        public enum ViewMode
        {
            Immersive,
            Overview,
        }

        private const string ChessSceneName = "ChessRoyaleVR";

        // Fixed observation point behind the player's king's starting square, not its current square.
        [SerializeField, Min(0f)] private float overviewHeight = 4.5f;
        [SerializeField, Min(0f)] private float overviewBackOffset = 3f;

        // Fallback board layout from ChessRoyaleSceneBuilder, used only if no squares are found.
        private static readonly Vector3 FallbackBoardCenter = new Vector3(0f, 0.42f, 2f);

        private const float PanelDistance = 1.6f;
        private const float PanelRight = 0.5f;
        private const float PanelDown = 0.42f;

        private static readonly Color PanelColor = ChessUITheme.Surface;
        private static readonly Color HintColor = ChessUITheme.DarkBrown;
        private static readonly Color ActiveColor = ChessUITheme.Burgundy;
        private static readonly Color ActiveTextColor = ChessUITheme.Ivory;
        private static readonly Color InactiveColor = new Color(1f, 1f, 1f, 0f);
        private static readonly Color InactiveTextColor = ChessUITheme.Ivory;

        private readonly List<LocomotionProvider> pausedLocomotion = new List<LocomotionProvider>();

        private InputAction toggleAction;
        private ChessFusionLobbyUI lobbyMenu;
        private Canvas canvas;
        private Image immersiveBackground;
        private Image overviewBackground;
        private Text immersiveLabel;
        private Text overviewLabel;

        private Vector3 savedRigPosition;
        private Quaternion savedRigRotation;
        private float overviewRigY;

        private bool panelPlaced;
        private bool panelFollowing;
        private bool available = true;

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
            UpdateVisibility();
        }

        private void OnEnable()
        {
            toggleAction = new InputAction("Toggle Overview View", InputActionType.Button);
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
            if (IsGameRunning())
                Toggle();
        }

        // The toggle only belongs to a game in progress. Online that means connected, both seats
        // filled, this player seated, and no lobby menu (create / join / rematch) on screen.
        // Single-player setup and results are not part of an active match.
        private bool IsGameRunning()
        {
            if (!available)
                return false;
            ChessGameController game = ChessGameController.Instance;
            if (game == null || game.Outcome != ChessGameOutcome.InProgress)
                return false;
            if (!MainMenu.LaunchMultiplayer)
                return ChessSinglePlayerSettings.GameStarted;

            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (room == null || !room.IsConnected || lobby == null || !lobby.MatchReady || lobby.LocalSeat == ChessSeat.None)
                return false;

            if (lobbyMenu == null)
                lobbyMenu = FindAnyObjectByType<ChessFusionLobbyUI>();
            Canvas menuCanvas = lobbyMenu != null ? lobbyMenu.GetComponent<Canvas>() : null;
            return menuCanvas == null || !menuCanvas.enabled;
        }

        private void UpdateVisibility()
        {
            bool running = IsGameRunning();
            TrackedDeviceGraphicRaycaster raycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = running;
            if (!running && Mode == ViewMode.Overview)
                SetMode(ViewMode.Immersive);

            if (canvas == null || canvas.enabled == running)
                return;

            canvas.enabled = running;
            if (running)
                panelPlaced = false;
        }

        public void Toggle()
        {
            SetMode(Mode == ViewMode.Immersive ? ViewMode.Overview : ViewMode.Immersive);
        }

        public void SetAvailable(bool value)
        {
            if (available == value)
                return;

            if (!value && Mode == ViewMode.Overview)
                SetMode(ViewMode.Immersive);
            available = value;
            UpdateVisibility();
        }

        public void SetMode(ViewMode mode)
        {
            if (mode == Mode || (mode == ViewMode.Overview && !IsGameRunning()))
                return;

            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return;

            if (mode == ViewMode.Overview)
                EnterOverview(origin);
            else
                ExitOverview(origin);

            RefreshButtons();
        }

        private void EnterOverview(XROrigin origin)
        {
            ChessPieceColor color = LocalPlayerColor();
            Vector3 forward = color == ChessPieceColor.Black ? Vector3.back : Vector3.forward;
            Vector3 kingStart = FindKingStart(color);

            savedRigPosition = origin.transform.position;
            savedRigRotation = origin.transform.rotation;
            PauseLocomotion();

            Vector3 position = kingStart - forward * overviewBackOffset;
            position.y = kingStart.y + overviewHeight;
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (origin.Camera != null)
            {
                // Align only yaw, leaving tracked head pitch/roll untouched. Compensate for
                // room-scale walking so the physical head, not just the rig root, is behind the king.
                Vector3 headForward = Vector3.ProjectOnPlane(origin.Camera.transform.forward, Vector3.up);
                Quaternion yaw = headForward.sqrMagnitude > 0.001f
                    ? Quaternion.AngleAxis(Vector3.SignedAngle(headForward, forward, Vector3.up), Vector3.up)
                    : rotation * Quaternion.Inverse(origin.transform.rotation);
                rotation = yaw * origin.transform.rotation;
                Vector3 headOffset = yaw * (origin.Camera.transform.position - origin.transform.position);
                position -= new Vector3(headOffset.x, 0f, headOffset.z);
            }
            PlaceRig(origin, position, rotation);
            panelPlaced = false;
            overviewRigY = position.y;
            Mode = ViewMode.Overview;
        }

        private void ExitOverview(XROrigin origin)
        {
            PlaceRig(origin, savedRigPosition, savedRigRotation);
            ResumeLocomotion();
            Mode = ViewMode.Immersive;
            panelPlaced = false;
        }

        private void LateUpdate()
        {
            DropOverviewIfRigWasMoved();
            UpdateVisibility();
            if (canvas != null && canvas.enabled)
                FollowCamera();
        }

        // Other code can move the rig while we are up (the match start puts each player at their
        // seat; leaving a room puts them back). Snap turning only rotates around the head, so the
        // height stays put; a height change means someone else placed the rig. Hand control back
        // without undoing their move.
        private void DropOverviewIfRigWasMoved()
        {
            if (Mode != ViewMode.Overview)
                return;

            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin != null && Mathf.Abs(origin.transform.position.y - overviewRigY) < 0.25f)
                return;

            ResumeLocomotion();
            Mode = ViewMode.Immersive;
            RefreshButtons();
        }

        private static ChessPieceColor LocalPlayerColor()
        {
            if (!MainMenu.LaunchMultiplayer)
                return ChessSinglePlayerSettings.HumanColor;

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            return lobby != null && lobby.LocalSeat == ChessSeat.Black ? ChessPieceColor.Black : ChessPieceColor.White;
        }

        private static Vector3 FindKingStart(ChessPieceColor color)
        {
            int rank = color == ChessPieceColor.Black ? 7 : 0;
            foreach (ChessBoardSquare square in FindObjectsByType<ChessBoardSquare>(FindObjectsSortMode.None))
            {
                if (square.FileIndex == 4 && square.RankIndex == rank)
                    return square.Center;
            }

            // Standard board: E1/E8 are one unit to the right of centre and seven toward either end.
            Vector3 center = FindBoardCenter();
            return center + new Vector3(1f, 0f, color == ChessPieceColor.Black ? 7f : -7f);
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
        // every locomotion provider except turning while in Overview.
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

            // First frame, or a big jump such as entering/leaving Overview: snap instead of gliding.
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
            bool overview = Mode == ViewMode.Overview;
            if (immersiveBackground != null)
                immersiveBackground.color = overview ? InactiveColor : ActiveColor;
            if (overviewBackground != null)
                overviewBackground.color = overview ? ActiveColor : InactiveColor;
            if (immersiveLabel != null)
                immersiveLabel.color = overview ? InactiveTextColor : ActiveTextColor;
            if (overviewLabel != null)
                overviewLabel.color = overview ? ActiveTextColor : InactiveTextColor;
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

            // Segmented control: [ Immersive view | Overview view ]
            RectTransform track = CreateBox(transform, "Toggle", new Vector2(0f, 40f), new Vector2(640f, 104f), PanelColor);
            CreateSegment(track, "IMMERSIVE", new Vector2(-158f, 0f), ViewMode.Immersive, out immersiveBackground, out immersiveLabel);
            CreateSegment(track, "OVERVIEW", new Vector2(158f, 0f), ViewMode.Overview, out overviewBackground, out overviewLabel);

            RectTransform hint = CreateBox(transform, "Hint", new Vector2(0f, -60f), new Vector2(620f, 60f), HintColor);
            Text hintText = CreateText(hint, "Press Y on the left controller to switch view", 24, FontStyle.Normal);
            ChessUITheme.ApplyPanel(gameObject);
            ChessUITheme.Border(track.GetComponent<Image>());
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
