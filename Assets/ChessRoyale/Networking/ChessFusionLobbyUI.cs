using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale.Networking
{
    [DisallowMultipleComponent]
    public sealed class ChessFusionLobbyUI : MonoBehaviour
    {
        private const float PanelDistance = 2.1f;

        private enum LobbyScreen
        {
            Choose,
            Join,
            Configure,
            Create,
            Confirm,
            Playing,
            Rematch
        }

        private Text createCodeText;
        private Text createStatusText;
        private Text joinStatusText;
        private Text resultTitleText;
        private Text rematchStatusText;
        private Text colorChoiceText;
        private Text configureSummaryText;
        private Text createSummaryText;
        private Text confirmSummaryText;
        private Text confirmStatusText;
        private ChessSeat resultSeat = ChessSeat.None;
        private InputField codeInput;
        private Camera followedCamera;
        private Canvas canvas;
        private GameObject chooseRoot;
        private GameObject joinRoot;
        private GameObject configureRoot;
        private GameObject createRoot;
        private GameObject confirmRoot;
        private GameObject rematchRoot;
        private Button whiteChoiceButton;
        private Button randomChoiceButton;
        private Button blackChoiceButton;
        private Button standardButton;
        private Button chess960Button;
        private Button threeCheckOffButton;
        private Button threeCheckOnButton;
        private Button arenaOffButton;
        private Button arenaOnButton;
        private Button readyButton;
        private ChessSeatPreference creatorSeatPreference = ChessSeatPreference.Random;
        private ChessStartingLayout pendingStartingLayout = ChessStartingLayout.Standard;
        private bool pendingThreeCheck;
        private bool pendingPodiumOverview = true;
        private LobbyScreen screen = LobbyScreen.Choose;
        private LobbyScreen shownScreen = (LobbyScreen)(-1);
        private int flowGeneration;

        public bool IsWaitingForOpponent => screen == LobbyScreen.Create;

        public bool IsOpeningRoom => busy && screen == LobbyScreen.Create;

        public string PendingRoomCode { get; private set; }

        public string CreateNotice { get; private set; }
        private bool busy;
        private bool showingReconnect;
        private string joinMessage = "Type a four-letter room code.";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLobby()
        {
            if (!MainMenu.LaunchMultiplayer)
                return;
            if (FindAnyObjectByType<ChessFusionLobbyUI>() != null)
                return;
            if (SceneManager.GetActiveScene().name != "ChessRoyaleVR")
                return;

            GameObject lobby = new GameObject("Chess Fusion Lobby");
            lobby.AddComponent<ChessFusionLobbyUI>();
        }

        private void Awake()
        {
            if (!MainMenu.LaunchMultiplayer)
            {
                Destroy(gameObject);
                return;
            }

            ChessFusionLobbyUI[] existing = FindObjectsByType<ChessFusionLobbyUI>(FindObjectsSortMode.None);
            if (existing.Length > 1 && existing[0] != this)
            {
                Destroy(gameObject);
                return;
            }

            EnsureEventSystem();
            BuildPanel();
            ApplyScreen();
        }

        private void LateUpdate()
        {
            if (canvas != null && canvas.enabled)
                FollowActiveCamera();
            RefreshScreen();
        }

        private void FollowActiveCamera()
        {
            Camera camera = ResolveCamera();
            if (camera == null)
                return;

            if (transform.parent != null)
                transform.SetParent(null, true);

            Vector3 target = camera.transform.position + camera.transform.forward * PanelDistance + Vector3.down * 0.35f;
            Vector3 faceDirection = target - camera.transform.position;
            bool drifted = followedCamera != camera
                || Vector3.Distance(transform.position, target) > 0.45f
                || Vector3.Angle(transform.forward, faceDirection) > 20f;
            if (!drifted)
                return;

            followedCamera = camera;
            transform.position = target;
            transform.rotation = Quaternion.LookRotation(faceDirection, Vector3.up);
        }

        // The headset always uses its own scene rig now (online avatars are separate), so the main camera is it.
        private static Camera ResolveCamera()
        {
            if (Camera.main != null && Camera.main.enabled)
                return Camera.main;

            Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (Camera camera in cameras)
            {
                if (camera.enabled)
                    return camera;
            }

            return null;
        }

        private void RefreshScreen()
        {
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessRoyale.ChessGameController game = ChessRoyale.ChessGameController.Instance;
            bool connected = room != null && room.IsConnected;
            bool ready = connected && lobby != null && lobby.MatchReady && lobby.MatchPhase == ChessMatchPhase.Playing;
            bool ended = game != null && game.Outcome != ChessRoyale.ChessGameOutcome.InProgress;
            if (lobby != null && lobby.LocalSeat != ChessSeat.None)
                resultSeat = lobby.LocalSeat;

            if (connected)
                showingReconnect = false;

            if (room != null && room.IsReconnecting)
            {
                showingReconnect = true;
                screen = LobbyScreen.Join;
                joinMessage = room.ReconnectStatus;
            }
            else if (showingReconnect && !connected)
            {
                showingReconnect = false;
                screen = LobbyScreen.Join;
                joinMessage = string.IsNullOrEmpty(room?.DisconnectNotice)
                    ? "Type a four-letter room code."
                    : room.DisconnectNotice;
            }
            else if (connected && lobby != null && lobby.MatchPhase == ChessMatchPhase.Configuring)
            {
                screen = lobby.CreatorPlayer == room.Runner.LocalPlayer ? LobbyScreen.Create : LobbyScreen.Confirm;
            }
            else if (ready && !ended)
                screen = LobbyScreen.Playing;
            else if ((ended || (lobby != null && lobby.MatchPhase == ChessMatchPhase.Ended)) &&
                     (screen == LobbyScreen.Playing || screen == LobbyScreen.Rematch || lobby != null && lobby.MatchReady))
                screen = LobbyScreen.Rematch;
            else if (!connected && screen == LobbyScreen.Playing)
                screen = LobbyScreen.Choose;

            ApplyScreen();

            if (screen == LobbyScreen.Create)
                RefreshCreate(room, lobby);
            else if (screen == LobbyScreen.Confirm)
                RefreshConfirm(room, lobby);
            else if (screen == LobbyScreen.Join && joinStatusText != null)
                joinStatusText.text = joinMessage;
            else if (screen == LobbyScreen.Rematch)
                RefreshResult(lobby, game);
        }

        private void RefreshCreate(ChessFusionRoomManager room, ChessFusionLobbyState lobby)
        {
            if (createCodeText != null)
            {
                // A locally generated code is not a usable room until Fusion StartGame succeeds.
                createCodeText.text = room != null && room.IsConnected && !string.IsNullOrEmpty(room.CurrentRoomCode)
                    ? room.CurrentRoomCode
                    : "----";
            }

            if (createStatusText == null)
                return;

            if (room != null && room.IsConnected)
            {
                if (createSummaryText != null && lobby != null)
                    createSummaryText.text = lobby.MatchConfig.Summary;
                if (!string.IsNullOrEmpty(room.DisconnectNotice))
                    createStatusText.text = room.DisconnectNotice;
                else if (lobby != null && lobby.OpponentLeft && !lobby.MatchReady)
                    createStatusText.text = "The other player disconnected.";
                else if (lobby != null && lobby.SeatsFilled)
                    createStatusText.text = "Opponent joined. Waiting for them to press Ready.";
                else if (lobby != null && lobby.LocalSeat != ChessSeat.None)
                    createStatusText.text = $"You are {ChessFusionLobbyState.SeatLabel(lobby.LocalSeat)}. Waiting for an opponent.";
                else
                    createStatusText.text = "Room ready. Waiting for an opponent.";
                return;
            }

            if (!string.IsNullOrEmpty(room?.LastError))
                createStatusText.text = room.LastError;
            else if (busy)
                createStatusText.text = "Creating the room...";
        }

        private void RefreshConfirm(ChessFusionRoomManager room, ChessFusionLobbyState lobby)
        {
            if (confirmSummaryText != null)
            {
                string roomCode = room != null ? room.CurrentRoomCode : "----";
                string seat = lobby != null ? ChessFusionLobbyState.SeatLabel(lobby.LocalSeat) : "loading";
                string summary = lobby != null ? lobby.MatchConfig.Summary : "Loading match settings...";
                confirmSummaryText.text = $"ROOM {roomCode}\n{summary}\nYou are {seat}.";
            }

            bool canAccept = lobby != null && lobby.MatchConfig.IsSupported && lobby.LocalSeat != ChessSeat.None;
            if (readyButton != null)
                readyButton.interactable = canAccept && !lobby.LocalConfigurationAccepted;
            if (confirmStatusText != null)
                confirmStatusText.text = lobby == null || !lobby.MatchConfig.IsSupported
                    ? "Loading match settings..."
                    : lobby.LocalConfigurationAccepted
                        ? "Ready. Waiting for the host to start the match."
                        : "Review the host's settings, then press Ready.";
        }

        private void RefreshResult(ChessFusionLobbyState lobby, ChessRoyale.ChessGameController game)
        {
            if (resultTitleText != null)
                resultTitleText.text = ResultTitle(game, resultSeat);
            if (rematchStatusText != null)
            {
                string detail = game != null && !string.IsNullOrEmpty(game.StatusMessage)
                    ? game.StatusMessage
                    : "The game is over.";
                rematchStatusText.text = detail + RematchStatus(game, lobby);
            }
        }

        private static string ResultTitle(ChessRoyale.ChessGameController game, ChessSeat seat)
        {
            if (game == null)
                return "GAME OVER";

            ChessRoyale.ChessPieceColor local = seat == ChessSeat.Black
                ? ChessRoyale.ChessPieceColor.Black
                : ChessRoyale.ChessPieceColor.White;
            switch (game.Outcome)
            {
                case ChessRoyale.ChessGameOutcome.WhiteCheckmates:
                case ChessRoyale.ChessGameOutcome.WhiteWinsOnTime:
                case ChessRoyale.ChessGameOutcome.WhiteWinsByDisconnect:
                case ChessRoyale.ChessGameOutcome.WhiteWinsByThreeCheck:
                    return local == ChessRoyale.ChessPieceColor.White ? "YOU WIN" : "YOU LOSE";
                case ChessRoyale.ChessGameOutcome.BlackCheckmates:
                case ChessRoyale.ChessGameOutcome.BlackWinsOnTime:
                case ChessRoyale.ChessGameOutcome.BlackWinsByDisconnect:
                case ChessRoyale.ChessGameOutcome.BlackWinsByThreeCheck:
                    return local == ChessRoyale.ChessPieceColor.Black ? "YOU WIN" : "YOU LOSE";
                case ChessRoyale.ChessGameOutcome.Stalemate:
                case ChessRoyale.ChessGameOutcome.DrawInsufficientMaterial:
                case ChessRoyale.ChessGameOutcome.DrawThreefoldRepetition:
                case ChessRoyale.ChessGameOutcome.DrawFiftyMoveRule:
                    return "DRAW";
                default:
                    return "GAME OVER";
            }
        }

        public void ShowChoose()
        {
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (busy || (room != null && room.IsConnected))
            {
                LeaveMatchToChoose();
                return;
            }

            screen = LobbyScreen.Choose;
            ApplyScreen();
        }

        public async void BeginCreate()
        {
            if (busy)
                return;

            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room == null)
                return;

            busy = true;
            int generation = ++flowGeneration;
            string code = ChessNetworkTypes.CreateRoomCode();
            PendingRoomCode = code;
            CreateNotice = "LOADING";
            screen = LobbyScreen.Create;
            if (createCodeText != null)
                createCodeText.text = "----";
            if (createStatusText != null)
                createStatusText.text = "Creating the room...";
            ApplyScreen();

            ChessMatchConfig config = ChessMatchConfig.Default;
            config.StartingLayout = (byte)pendingStartingLayout;
            config.ThreeCheckEnabled = pendingThreeCheck;
            config.PodiumOverviewEnabled = pendingPodiumOverview;
            bool created = await room.CreateRoom(code, creatorSeatPreference, config);
            if (this == null || generation != flowGeneration)
                return;

            busy = false;

            if (screen == LobbyScreen.Playing || screen == LobbyScreen.Rematch)
            {
                CreateNotice = null;
                return;
            }

            if (created)
            {
                CreateNotice = null;
                // Keep the same window from connection progress through waiting for an opponent.
                // RefreshScreen hides it only once both players are ready to start the match.
                screen = LobbyScreen.Create;
                RefreshCreate(room, ChessFusionLobbyState.Instance);
            }
            else
            {
                screen = LobbyScreen.Create;
                CreateNotice = string.IsNullOrEmpty(room.LastError)
                    ? "Could not create the room."
                    : room.LastError;
                if (createStatusText != null)
                    createStatusText.text = CreateNotice;
            }

            ApplyScreen();
        }

        public void ShowJoin()
        {
            if (busy)
                return;

            joinMessage = "Type a four-letter room code.";
            screen = LobbyScreen.Join;
            if (codeInput != null)
                codeInput.text = string.Empty;
            ApplyScreen();
        }

        public void ShowRoomClosedNotice()
        {
            busy = false;
            joinMessage = "The room closed because its creator left before the match started.";
            screen = LobbyScreen.Join;
            ApplyScreen();
        }

        public void ShowConfigure()
        {
            if (busy)
                return;

            screen = LobbyScreen.Configure;
            RefreshConfigurationChoices();
            ApplyScreen();
        }

        public void ChooseStandard()
        {
            pendingStartingLayout = ChessStartingLayout.Standard;
            RefreshConfigurationChoices();
        }

        public void ChooseChess960()
        {
            pendingStartingLayout = ChessStartingLayout.Chess960;
            RefreshConfigurationChoices();
        }

        public void DisableThreeCheck()
        {
            pendingThreeCheck = false;
            RefreshConfigurationChoices();
        }

        public void EnableThreeCheck()
        {
            pendingThreeCheck = true;
            RefreshConfigurationChoices();
        }

        public void DisablePodiumOverview()
        {
            pendingPodiumOverview = false;
            RefreshConfigurationChoices();
        }

        public void EnablePodiumOverview()
        {
            pendingPodiumOverview = true;
            RefreshConfigurationChoices();
        }

        public void AcceptConfiguration()
        {
            ChessFusionLobbyState.Instance?.RequestAcceptConfiguration();
            RefreshConfirm(ChessFusionRoomManager.Instance, ChessFusionLobbyState.Instance);
        }

        public void ChooseWhite()
        {
            SetCreatorSeatPreference(ChessSeatPreference.White);
        }

        public void ChooseRandom()
        {
            SetCreatorSeatPreference(ChessSeatPreference.Random);
        }

        public void ChooseBlack()
        {
            SetCreatorSeatPreference(ChessSeatPreference.Black);
        }

        private void SetCreatorSeatPreference(ChessSeatPreference preference)
        {
            if (busy)
                return;

            creatorSeatPreference = preference;
            RefreshConfigurationChoices();
        }

        private void RefreshColorChoice()
        {
            if (colorChoiceText != null)
                colorChoiceText.text = $"Your color: {creatorSeatPreference.ToString().ToUpperInvariant()}";

            SetChoiceButtonColor(whiteChoiceButton, creatorSeatPreference == ChessSeatPreference.White);
            SetChoiceButtonColor(randomChoiceButton, creatorSeatPreference == ChessSeatPreference.Random);
            SetChoiceButtonColor(blackChoiceButton, creatorSeatPreference == ChessSeatPreference.Black);
        }

        private void RefreshConfigurationChoices()
        {
            RefreshColorChoice();
            SetChoiceButtonColor(standardButton, pendingStartingLayout == ChessStartingLayout.Standard);
            SetChoiceButtonColor(chess960Button, pendingStartingLayout == ChessStartingLayout.Chess960);
            SetChoiceButtonColor(threeCheckOffButton, !pendingThreeCheck);
            SetChoiceButtonColor(threeCheckOnButton, pendingThreeCheck);
            SetChoiceButtonColor(arenaOffButton, !pendingPodiumOverview);
            SetChoiceButtonColor(arenaOnButton, pendingPodiumOverview);
            if (configureSummaryText != null)
            {
                ChessMatchConfig config = ChessMatchConfig.Default;
                config.StartingLayout = (byte)pendingStartingLayout;
                config.ThreeCheckEnabled = pendingThreeCheck;
                config.PodiumOverviewEnabled = pendingPodiumOverview;
                configureSummaryText.text = config.Summary;
            }
        }

        private static void SetChoiceButtonColor(Button button, bool selected)
        {
            if (button != null && button.image != null)
                ChessUITheme.SetChoice(button.image, selected);
        }

        public async void SubmitJoin()
        {
            if (busy)
                return;

            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room == null)
                return;

            string code = codeInput != null ? codeInput.text : string.Empty;
            busy = true;
            int generation = ++flowGeneration;
            joinMessage = "Looking for that room...";
            screen = LobbyScreen.Join;
            ApplyScreen();

            bool joined = await room.JoinRoom(code);
            if (this == null || generation != flowGeneration)
                return;

            busy = false;

            if (!joined)
            {
                joinMessage = string.IsNullOrEmpty(room.LastError)
                    ? "That room was not found. Try another code."
                    : room.LastError + "\nTry another code.";
                screen = LobbyScreen.Join;
                ApplyScreen();
                return;
            }

            if (screen == LobbyScreen.Playing || screen == LobbyScreen.Rematch)
                return;

            screen = LobbyScreen.Join;
            joinMessage = "Joined. Waiting for the match to start.";
            ApplyScreen();
        }

        public async void LeaveLobbyToMenu()
        {
            if (busy)
                return;

            busy = true;
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room != null && room.IsConnected)
                await room.LeaveRoom();
            if (this == null)
                return;

            SceneManager.LoadScene("MainMenu");
        }

        public void RequestRematch()
        {
            ChessRoyale.ChessGameController.Instance?.RequestRematch();
        }

        public async void LeaveMatchToChoose()
        {
            await ReturnToLobby();
        }

        private async System.Threading.Tasks.Task ReturnToLobby()
        {
            int generation = ++flowGeneration;
            busy = true;
            PendingRoomCode = null;
            CreateNotice = null;
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room != null)
                await room.LeaveRoom();
            if (this == null || generation != flowGeneration)
                return;

            busy = false;
            screen = LobbyScreen.Choose;
            ApplyScreen();
        }

        private static string RematchStatus(ChessRoyale.ChessGameController game, ChessFusionLobbyState lobby)
        {
            if (game == null || game.Outcome == ChessRoyale.ChessGameOutcome.InProgress)
                return string.Empty;
            if (lobby == null || !lobby.MatchReady)
                return "\nRematch waits until both players are seated.";

            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            if (state == null)
                return "\nBoth players must agree to rematch.";
            if (state.LocalPlayerAcceptedRematch && state.OtherPlayerAcceptedRematch)
                return string.Empty;
            if (state.LocalPlayerAcceptedRematch)
                return "\nYou agreed to a rematch. Waiting for the other player.";
            if (state.OtherPlayerAcceptedRematch)
                return "\nThe other player agreed to a rematch.";
            return "\nBoth players must agree to rematch.";
        }

        private void TypeLetter(char letter)
        {
            if (codeInput == null)
                return;

            codeInput.text = LettersOnly(codeInput.text + letter);
        }

        private void DeleteLetter()
        {
            if (codeInput == null || codeInput.text.Length == 0)
                return;

            codeInput.text = codeInput.text.Substring(0, codeInput.text.Length - 1);
        }

        private void SanitizeRoomCode(string value)
        {
            if (codeInput == null)
                return;

            string letters = LettersOnly(value);
            if (codeInput.text != letters)
                codeInput.text = letters;
        }

        private static string LettersOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            char[] letters = new char[ChessNetworkTypes.RoomCodeLength];
            int count = 0;
            foreach (char character in value.ToUpperInvariant())
            {
                if (character < 'A' || character > 'Z')
                    continue;

                letters[count++] = character;
                if (count == letters.Length)
                    break;
            }

            return new string(letters, 0, count);
        }

        private void ApplyScreen()
        {
            bool showMenu = screen == LobbyScreen.Choose
                || screen == LobbyScreen.Join
                || screen == LobbyScreen.Configure
                || screen == LobbyScreen.Create
                || screen == LobbyScreen.Confirm
                || screen == LobbyScreen.Rematch;
            if (canvas != null)
                canvas.enabled = showMenu;
            TrackedDeviceGraphicRaycaster raycaster = GetComponent<TrackedDeviceGraphicRaycaster>();
            if (raycaster != null)
                raycaster.enabled = showMenu;
            if (shownScreen == screen)
                return;

            shownScreen = screen;
            // Reapply after every page transition so all multiplayer choices retain the shared style.
            ChessUITheme.ApplyPanel(gameObject);
            RefreshColorChoice();
            if (chooseRoot != null)
                chooseRoot.SetActive(screen == LobbyScreen.Choose);
            if (joinRoot != null)
                joinRoot.SetActive(screen == LobbyScreen.Join);
            if (configureRoot != null)
                configureRoot.SetActive(screen == LobbyScreen.Configure);
            if (createRoot != null)
                createRoot.SetActive(screen == LobbyScreen.Create);
            if (confirmRoot != null)
                confirmRoot.SetActive(screen == LobbyScreen.Confirm);
            if (rematchRoot != null)
                rematchRoot.SetActive(screen == LobbyScreen.Rematch);
        }

        private void BuildPanel()
        {
            if (GetComponent<Canvas>() != null)
                return;

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(920f, 1040f);
            rect.localScale = Vector3.one * 0.0014f;

            Image background = gameObject.AddComponent<Image>();
            background.color = ChessUITheme.Surface;

            chooseRoot = CreatePage("Choose");
            CreateText(chooseRoot.transform, "Chess Royale Online", new Vector2(0f, 410f), new Vector2(860f, 80f), 42);
            CreateText(chooseRoot.transform, "Create a match or join with a four-letter code.", new Vector2(0f, 315f), new Vector2(860f, 80f), 28);
            CreateButton(chooseRoot.transform, "CREATE", new Vector2(-160f, 90f), ShowConfigure);
            CreateButton(chooseRoot.transform, "JOIN", new Vector2(160f, 90f), ShowJoin);
            CreateButton(chooseRoot.transform, "LEAVE", new Vector2(0f, -70f), LeaveLobbyToMenu);

            configureRoot = CreatePage("Configure");
            CreateText(configureRoot.transform, "CREATE MATCH", new Vector2(0f, 455f), new Vector2(860f, 70f), 42);
            CreateText(configureRoot.transform, "STARTING LAYOUT", new Vector2(0f, 370f), new Vector2(860f, 50f), 24);
            standardButton = CreateButton(configureRoot.transform, "STANDARD", new Vector2(-155f, 310f), ChooseStandard, new Vector2(280f, 68f));
            chess960Button = CreateButton(configureRoot.transform, "CHESS960", new Vector2(155f, 310f), ChooseChess960, new Vector2(280f, 68f));
            CreateText(configureRoot.transform, "THREE-CHECK", new Vector2(0f, 230f), new Vector2(860f, 50f), 24);
            threeCheckOffButton = CreateButton(configureRoot.transform, "OFF", new Vector2(-155f, 170f), DisableThreeCheck, new Vector2(280f, 68f));
            threeCheckOnButton = CreateButton(configureRoot.transform, "ON", new Vector2(155f, 170f), EnableThreeCheck, new Vector2(280f, 68f));
            CreateText(configureRoot.transform, "PODIUM + OVERVIEW", new Vector2(0f, 90f), new Vector2(860f, 50f), 24);
            arenaOffButton = CreateButton(configureRoot.transform, "OFF", new Vector2(-155f, 30f), DisablePodiumOverview, new Vector2(280f, 68f));
            arenaOnButton = CreateButton(configureRoot.transform, "ON", new Vector2(155f, 30f), EnablePodiumOverview, new Vector2(280f, 68f));
            colorChoiceText = CreateText(configureRoot.transform, "YOUR COLOR: RANDOM", new Vector2(0f, -65f), new Vector2(860f, 50f), 24);
            whiteChoiceButton = CreateButton(configureRoot.transform, "WHITE", new Vector2(-285f, -125f), ChooseWhite, new Vector2(240f, 68f));
            randomChoiceButton = CreateButton(configureRoot.transform, "RANDOM", new Vector2(0f, -125f), ChooseRandom, new Vector2(240f, 68f));
            blackChoiceButton = CreateButton(configureRoot.transform, "BLACK", new Vector2(285f, -125f), ChooseBlack, new Vector2(240f, 68f));
            configureSummaryText = CreateText(configureRoot.transform, string.Empty, new Vector2(0f, -225f), new Vector2(860f, 90f), 23);
            CreateButton(configureRoot.transform, "CREATE ROOM", new Vector2(0f, -345f), BeginCreate, new Vector2(460f, 72f));
            CreateButton(configureRoot.transform, "BACK", new Vector2(0f, -445f), ShowChoose, new Vector2(360f, 68f));
            RefreshConfigurationChoices();

            createRoot = CreatePage("Create");
            CreateText(createRoot.transform, "YOUR ROOM CODE", new Vector2(0f, 315f), new Vector2(860f, 70f), 36);
            createCodeText = CreateText(createRoot.transform, "----", new Vector2(0f, 210f), new Vector2(860f, 90f), 64);
            createSummaryText = CreateText(createRoot.transform, string.Empty, new Vector2(0f, 70f), new Vector2(860f, 110f), 25);
            createStatusText = CreateText(createRoot.transform, "Creating the room...", new Vector2(0f, -70f), new Vector2(860f, 140f), 26);
            CreateButton(createRoot.transform, "LOBBY", new Vector2(0f, -230f), LeaveMatchToChoose);

            joinRoot = CreatePage("Join");
            CreateText(joinRoot.transform, "Join a room", new Vector2(0f, 430f), new Vector2(860f, 70f), 42);
            joinStatusText = CreateText(joinRoot.transform, joinMessage, new Vector2(0f, 350f), new Vector2(860f, 80f), 26);
            codeInput = CreateCodeInput(joinRoot.transform, new Vector2(0f, 260f));
            BuildJoinKeyboard(joinRoot.transform);
            CreateButton(joinRoot.transform, "JOIN", new Vector2(-160f, -430f), SubmitJoin);
            CreateButton(joinRoot.transform, "LOBBY", new Vector2(160f, -430f), ShowChoose);

            confirmRoot = CreatePage("Confirm");
            CreateText(confirmRoot.transform, "MATCH SETTINGS", new Vector2(0f, 360f), new Vector2(860f, 80f), 42);
            confirmSummaryText = CreateText(confirmRoot.transform, "Loading match settings...", new Vector2(0f, 130f), new Vector2(860f, 260f), 30);
            confirmStatusText = CreateText(confirmRoot.transform, "Review the settings, then press Ready.", new Vector2(0f, -65f), new Vector2(860f, 100f), 25);
            readyButton = CreateButton(confirmRoot.transform, "READY", new Vector2(0f, -190f), AcceptConfiguration, new Vector2(460f, 76f));
            CreateButton(confirmRoot.transform, "LEAVE", new Vector2(0f, -300f), LeaveMatchToChoose, new Vector2(360f, 68f));

            rematchRoot = CreatePage("Rematch");
            resultTitleText = CreateText(rematchRoot.transform, "YOU WIN", new Vector2(0f, 180f), new Vector2(860f, 100f), 64);
            rematchStatusText = CreateText(rematchRoot.transform, "The game is over.", new Vector2(0f, 60f), new Vector2(860f, 140f), 26);
            CreateButton(rematchRoot.transform, "REMATCH", new Vector2(0f, -70f), RequestRematch, new Vector2(460f, 72f));
            CreateButton(rematchRoot.transform, "RETURN TO LOBBY", new Vector2(0f, -170f), LeaveMatchToChoose, new Vector2(460f, 72f));
            ChessUITheme.ApplyPanel(gameObject);
            RefreshConfigurationChoices();
        }

        private GameObject CreatePage(string name)
        {
            GameObject page = new GameObject(name, typeof(RectTransform));
            page.transform.SetParent(transform, false);
            RectTransform rect = page.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            page.SetActive(false);
            return page;
        }

        private Text CreateText(Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = textObject.AddComponent<Text>();
            text.text = value;
            ChessUITheme.StyleText(text);
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = ChessUITheme.Ivory;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            return CreateButton(parent, label, position, action, ChessUITheme.ChoiceSize);
        }

        private Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action, Vector2 size)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = ChessUITheme.DarkBrown;
            Button button = buttonObject.AddComponent<Button>();
            // Pages are initially inactive, so Selectable.Awake has not assigned this yet.
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            ChessUITheme.StyleButton(button, label == "CREATE" || label == "JOIN" || label == "REMATCH");
            Text text = CreateText(buttonObject.transform, label, Vector2.zero, size, 28);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = size;
            return button;
        }

        private InputField CreateCodeInput(Transform parent, Vector2 position)
        {
            GameObject inputObject = new GameObject("Room Code", typeof(RectTransform));
            inputObject.transform.SetParent(parent, false);
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(420f, 88f);
            rect.anchoredPosition = position;
            Image image = inputObject.AddComponent<Image>();
            image.color = ChessUITheme.DarkBrown;

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);
            Text text = textObject.AddComponent<Text>();
            ChessUITheme.StyleText(text);
            text.fontSize = 40;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = ChessUITheme.Ivory;
            text.supportRichText = false;

            GameObject placeholderObject = new GameObject("Placeholder", typeof(RectTransform));
            placeholderObject.transform.SetParent(inputObject.transform, false);
            RectTransform placeholderRect = placeholderObject.GetComponent<RectTransform>();
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(8f, 4f);
            placeholderRect.offsetMax = new Vector2(-8f, -4f);
            Text placeholder = placeholderObject.AddComponent<Text>();
            placeholder.font = text.font;
            placeholder.fontSize = 32;
            placeholder.fontStyle = FontStyle.Normal;
            placeholder.alignment = TextAnchor.MiddleCenter;
            placeholder.color = new Color(ChessUITheme.Ivory.r, ChessUITheme.Ivory.g, ChessUITheme.Ivory.b, 0.5f);
            placeholder.text = "ABCD";
            placeholder.raycastTarget = false;

            InputField input = inputObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = ChessNetworkTypes.RoomCodeLength;
            input.contentType = InputField.ContentType.Alphanumeric;
            input.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            input.lineType = InputField.LineType.SingleLine;
            input.text = string.Empty;
            input.onValueChanged.AddListener(SanitizeRoomCode);
            input.customCaretColor = true;
            input.caretColor = ChessUITheme.Ivory;
            input.selectionColor = new Color(ChessUITheme.Gold.r, ChessUITheme.Gold.g, ChessUITheme.Gold.b, 0.4f);
            ChessUITheme.Border(image);
            return input;
        }

        private void BuildJoinKeyboard(Transform parent)
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const int columns = 7;
            for (int index = 0; index < alphabet.Length; index++)
            {
                char letter = alphabet[index];
                int column = index % columns;
                int row = index / columns;
                float x = (column - 3) * 118f;
                float y = 150f - row * 84f;
                CreateButton(parent, letter.ToString(), new Vector2(x, y), () => TypeLetter(letter), new Vector2(104f, 70f));
            }

            CreateButton(parent, "DEL", new Vector2((6 - 3) * 118f, 150f - 3 * 84f), DeleteLetter, new Vector2(104f, 70f));
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
                return;

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<XRUIInputModule>();
        }
    }
}
