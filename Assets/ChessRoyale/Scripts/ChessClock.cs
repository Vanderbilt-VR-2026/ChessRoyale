using ChessRoyale.Networking;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale
{
    /// <summary>
    /// Chess clock: each side gets a fixed amount of time, and only the side to move is counting down.
    /// Shows both timers and the game status on a panel that gently follows the player's view.
    /// Running out of time ends the game via ChessGameController.EndOnTime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ChessClock : MonoBehaviour
    {
        private static readonly Color ActiveFrame = ChessUITheme.Burgundy;
        private static readonly Color IdleFrame = ChessUITheme.DarkBrown;
        private static readonly Color LowTime = new Color(1f, 0.3f, 0.25f, 1f);

        [SerializeField] private float minutesPerSide = 10f;
        [SerializeField] private float lowTimeSeconds = 60f;
        [SerializeField] private float viewDistance = 2.2f;
        [SerializeField] private float viewHeightOffset = -0.9f;

        private ChessGameController game;
        private float whiteSeconds;
        private float blackSeconds;
        private int shownWhite = -1;
        private int shownBlack = -1;
        private string shownStatus;

        private Transform panel;
        private Transform head;
        private bool placed;
        private Image whiteFrame;
        private Image blackFrame;
        private Text whiteText;
        private Text blackText;
        private Text statusText;
        private GameObject waitingRoot;
        private Text waitingCodeText;
        private Text waitingLabelText;
        private GameObject matchLeaveRoot;
        private bool waitingShown;

        public static ChessClock Instance { get; private set; }

        public int StartingTimeMs => Mathf.Max(1, Mathf.RoundToInt(minutesPerSide * 60f * 1000f));

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            ResetClock();
            BuildPanel();
        }

        private void Start()
        {
            game = ChessGameController.Instance;
            if (game != null)
                game.GameReset += ResetClock;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (game != null)
                game.GameReset -= ResetClock;
        }

        public void ApplyNetworkTime(int whiteMs, int blackMs)
        {
            whiteSeconds = Mathf.Max(0, whiteMs) / 1000f;
            blackSeconds = Mathf.Max(0, blackMs) / 1000f;
        }

        public void ResetClock()
        {
            whiteSeconds = blackSeconds = minutesPerSide * 60f;
            shownWhite = shownBlack = -1;
        }

        private void Update()
        {
            if (!MainMenu.LaunchMultiplayer)
            {
                if (panel != null && panel.gameObject.activeSelf)
                    panel.gameObject.SetActive(false);
                return;
            }

            bool showClock = ShouldShowClock();
            if (panel != null && panel.gameObject.activeSelf != showClock)
                panel.gameObject.SetActive(showClock);
            if (!showClock)
                return;

            if (WaitingForOpponent)
            {
                ShowWaiting(true);
                if (waitingCodeText != null)
                {
                    string code = ChessFusionRoomManager.Instance != null
                        ? ChessFusionRoomManager.Instance.CurrentRoomCode
                        : null;
                    if (string.IsNullOrEmpty(code))
                    {
                        ChessFusionLobbyUI lobby = FindAnyObjectByType<ChessFusionLobbyUI>();
                        if (lobby != null)
                            code = lobby.PendingRoomCode;
                    }

                    waitingCodeText.text = string.IsNullOrEmpty(code) ? "----" : code;
                }

                if (waitingLabelText != null)
                {
                    float hold = ChessFusionRoomManager.Instance != null && ChessFusionRoomManager.Instance.IsConnected && ChessNetworkGameState.Instance != null
                        ? ChessNetworkGameState.Instance.ReconnectSecondsLeft
                        : 0f;
                    waitingLabelText.text = hold > 0f
                        ? $"OPPONENT DROPPED - {Mathf.CeilToInt(hold)}s TO REJOIN ROOM"
                        : "ROOM";
                }

                return;
            }

            ShowWaiting(false);
            if (game == null)
                return;

            if (InNetworkRoom)
            {
                if (!NetworkMatchStarted)
                    ApplyNetworkTime(StartingTimeMs, StartingTimeMs);
                else
                    ApplyAuthoritativeClock();
                UpdateDisplay();
                return;
            }

            if (MainMenu.LaunchMultiplayer && game.Outcome == ChessGameOutcome.InProgress)
            {
                if (game.ActiveColor == ChessPieceColor.White)
                {
                    whiteSeconds -= Time.deltaTime;
                    if (whiteSeconds <= 0f)
                    {
                        whiteSeconds = 0f;
                        game.EndOnTime(ChessPieceColor.White);
                    }
                }
                else
                {
                    blackSeconds -= Time.deltaTime;
                    if (blackSeconds <= 0f)
                    {
                        blackSeconds = 0f;
                        game.EndOnTime(ChessPieceColor.Black);
                    }
                }
            }

            UpdateDisplay();
        }

        private static bool InNetworkRoom =>
            ChessFusionRoomManager.Instance != null && ChessFusionRoomManager.Instance.IsConnected;

        private static bool NetworkMatchStarted =>
            ChessFusionLobbyState.Instance != null && ChessFusionLobbyState.Instance.MatchReady;

        private static bool WaitingForOpponent =>
            MainMenu.LaunchMultiplayer && !NetworkMatchStarted && (InNetworkRoom || HostingRoom);

        private static bool HostingRoom
        {
            get
            {
                ChessFusionLobbyUI lobby = FindAnyObjectByType<ChessFusionLobbyUI>();
                return lobby != null && lobby.IsWaitingForOpponent;
            }
        }

        private static bool ShouldShowClock()
        {
            // The creator's lobby already owns the connection/waiting window. Never place
            // the clock's smaller room-code panel on top of it or replace it after connecting.
            if (!MainMenu.LaunchMultiplayer || !InNetworkRoom || HostingRoom)
                return false;

            ChessGameController match = ChessGameController.Instance;
            return match == null || match.Outcome == ChessGameOutcome.InProgress;
        }

        private void ShowWaiting(bool waiting)
        {
            if (waitingShown == waiting)
                return;

            waitingShown = waiting;
            if (waitingRoot != null)
                waitingRoot.SetActive(waiting);
            if (whiteFrame != null)
                whiteFrame.gameObject.SetActive(!waiting);
            if (blackFrame != null)
                blackFrame.gameObject.SetActive(!waiting);
            if (statusText != null)
                statusText.gameObject.SetActive(!waiting);
            if (matchLeaveRoot != null)
                matchLeaveRoot.SetActive(!waiting);
        }

        private void LeaveCreatedRoom()
        {
            ChessFusionLobbyUI lobby = FindAnyObjectByType<ChessFusionLobbyUI>();
            if (lobby != null)
                lobby.LeaveMatchToChoose();
        }

        private void ApplyAuthoritativeClock()
        {
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            int whiteMs = state != null ? state.WhiteTimeMs : 0;
            int blackMs = state != null ? state.BlackTimeMs : 0;
            if (whiteMs <= 0 && blackMs <= 0)
            {
                whiteMs = StartingTimeMs;
                blackMs = StartingTimeMs;
            }

            ApplyNetworkTime(whiteMs, blackMs);
        }

        private void LateUpdate()
        {
            Transform follow = panel;
            if (follow == null || !follow.gameObject.activeSelf)
                return;

            if (head == null)
            {
                Camera camera = Camera.main;
                if (camera == null)
                    return;
                head = camera.transform;
            }

            Vector3 forward = head.forward;
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;

            Vector3 target = head.position + forward * viewDistance + Vector3.up * viewHeightOffset;
            float blend = placed ? 1f - Mathf.Exp(-Time.deltaTime * 4f) : 1f;
            follow.position = Vector3.Lerp(follow.position, target, blend);
            placed = true;

            Vector3 away = follow.position - head.position;
            if (away.sqrMagnitude > 0.0001f)
                follow.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void UpdateDisplay()
        {
            int white = Mathf.CeilToInt(whiteSeconds);
            int black = Mathf.CeilToInt(blackSeconds);
            if (white != shownWhite)
            {
                shownWhite = white;
                whiteText.text = Format(white);
                whiteText.color = whiteSeconds <= lowTimeSeconds ? LowTime : ChessUITheme.Ivory;
            }

            if (black != shownBlack)
            {
                shownBlack = black;
                blackText.text = Format(black);
                blackText.color = blackSeconds <= lowTimeSeconds ? LowTime : ChessUITheme.Ivory;
            }

            bool running = game.Outcome == ChessGameOutcome.InProgress && (!InNetworkRoom || NetworkMatchStarted);
            whiteFrame.color = running && game.ActiveColor == ChessPieceColor.White ? ActiveFrame : IdleFrame;
            blackFrame.color = running && game.ActiveColor == ChessPieceColor.Black ? ActiveFrame : IdleFrame;

            string displayStatus = GetDisplayStatus();
            if (shownStatus != displayStatus)
            {
                shownStatus = displayStatus;
                statusText.text = shownStatus;
            }
        }

        private string GetDisplayStatus()
        {
            if (game.Outcome != ChessGameOutcome.InProgress || !InNetworkRoom)
                return game.StatusMessage;

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (lobby == null || lobby.LocalSeat == ChessSeat.None)
                return game.StatusMessage;

            ChessPieceColor localColor = lobby.LocalSeat == ChessSeat.White
                ? ChessPieceColor.White
                : ChessPieceColor.Black;
            string turn = game.ActiveColor == localColor ? "Your turn" : "Opponent's turn";
            if (!game.ThreeCheckEnabled)
                return turn;

            int yours = localColor == ChessPieceColor.White ? game.WhiteChecksDelivered : game.BlackChecksDelivered;
            int theirs = localColor == ChessPieceColor.White ? game.BlackChecksDelivered : game.WhiteChecksDelivered;
            return $"{turn}  |  Checks {yours}-{theirs}";
        }

        private static string Format(int totalSeconds)
        {
            return $"{totalSeconds / 60}:{totalSeconds % 60:00}";
        }

        private void BuildPanel()
        {
            GameObject canvasObject = new GameObject("Chess Clock Panel", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(640f, 340f);
            rect.localScale = Vector3.one * 0.0014f;
            panel = canvasObject.transform;

            Image background = canvasObject.AddComponent<Image>();
            background.color = ChessUITheme.Surface;
            background.raycastTarget = false;

            whiteFrame = CreateBox(rect, "White Timer", new Vector2(-155f, 35f), out whiteText, "WHITE");
            blackFrame = CreateBox(rect, "Black Timer", new Vector2(155f, 35f), out blackText, "BLACK");
            statusText = CreateText(rect, "Status", new Vector2(0f, -70f), new Vector2(600f, 50f), 30, FontStyle.Normal);
            matchLeaveRoot = CreateLobbyButton(rect, new Vector2(0f, -130f));
            BuildWaitingRoom(rect);
            ChessUITheme.ApplyPanel(canvasObject);
            canvasObject.SetActive(false);
        }

        private void BuildWaitingRoom(RectTransform parent)
        {
            waitingRoot = new GameObject("Waiting Room", typeof(RectTransform));
            waitingRoot.transform.SetParent(parent, false);
            RectTransform root = waitingRoot.GetComponent<RectTransform>();
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.offsetMin = Vector2.zero;
            root.offsetMax = Vector2.zero;

            waitingLabelText = CreateText(root, "Room", new Vector2(0f, 78f), new Vector2(600f, 36f), 24, FontStyle.Bold);
            waitingLabelText.text = "ROOM";
            waitingCodeText = CreateText(root, "Code", new Vector2(0f, 18f), new Vector2(600f, 80f), 64, FontStyle.Bold);
            waitingCodeText.text = "----";

            GameObject buttonObject = new GameObject("Leave", typeof(RectTransform));
            buttonObject.transform.SetParent(waitingRoot.transform, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(280f, 64f);
            buttonRect.anchoredPosition = new Vector2(0f, -78f);
            Image image = buttonObject.AddComponent<Image>();
            image.color = ChessUITheme.DarkBrown;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(LeaveCreatedRoom);
            ChessUITheme.StyleButton(button);
            Text label = CreateText(buttonRect, "Leave Label", Vector2.zero, new Vector2(280f, 64f), 28, FontStyle.Bold);
            label.text = "LOBBY";
            waitingRoot.SetActive(false);
        }

        private GameObject CreateLobbyButton(RectTransform parent, Vector2 position)
        {
            GameObject buttonObject = new GameObject("Lobby", typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(280f, 64f);
            buttonRect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = ChessUITheme.DarkBrown;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(LeaveCreatedRoom);
            ChessUITheme.StyleButton(button);
            Text label = CreateText(buttonRect, "Leave Label", Vector2.zero, new Vector2(280f, 64f), 28, FontStyle.Bold);
            label.text = "LOBBY";
            return buttonObject;
        }

        private static Image CreateBox(RectTransform parent, string name, Vector2 position, out Text timeText, string caption)
        {
            GameObject box = new GameObject(name, typeof(RectTransform));
            box.transform.SetParent(parent, false);
            RectTransform rect = box.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(290f, 150f);
            rect.anchoredPosition = position;

            Image frame = box.AddComponent<Image>();
            frame.raycastTarget = false;
            frame.color = ChessUITheme.DarkBrown;
            ChessUITheme.Border(frame);

            CreateText(rect, "Caption", new Vector2(0f, 48f), new Vector2(270f, 40f), 28, FontStyle.Bold).text = caption;
            timeText = CreateText(rect, "Time", new Vector2(0f, -15f), new Vector2(270f, 90f), 70, FontStyle.Bold);
            return frame;
        }

        private static Text CreateText(RectTransform parent, string name, Vector2 position, Vector2 size, int fontSize, FontStyle style)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;

            Text text = textObject.AddComponent<Text>();
            ChessUITheme.StyleText(text);
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = ChessUITheme.Ivory;
            text.raycastTarget = false;
            return text;
        }
    }
}
