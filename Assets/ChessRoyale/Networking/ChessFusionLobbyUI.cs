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
            Create,
            Playing,
            Rematch
        }

        private Text createCodeText;
        private Text createStatusText;
        private Text joinStatusText;
        private Text resultTitleText;
        private Text rematchStatusText;
        private Text colorChoiceText;
        private ChessSeat resultSeat = ChessSeat.None;
        private InputField codeInput;
        private Camera followedCamera;
        private Canvas canvas;
        private GameObject chooseRoot;
        private GameObject joinRoot;
        private GameObject createRoot;
        private GameObject rematchRoot;
        private Button whiteChoiceButton;
        private Button randomChoiceButton;
        private Button blackChoiceButton;
        private ChessSeatPreference creatorSeatPreference = ChessSeatPreference.Random;
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
            bool ready = connected && lobby != null && lobby.MatchReady;
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
            else if (ready && !ended)
                screen = LobbyScreen.Playing;
            else if (ended && (screen == LobbyScreen.Playing || screen == LobbyScreen.Rematch || ready))
                screen = LobbyScreen.Rematch;
            else if (!connected && screen == LobbyScreen.Playing)
                screen = LobbyScreen.Choose;

            ApplyScreen();

            if (screen == LobbyScreen.Create)
                RefreshCreate(room, lobby);
            else if (screen == LobbyScreen.Join && joinStatusText != null)
                joinStatusText.text = joinMessage;
            else if (screen == LobbyScreen.Rematch)
                RefreshResult(lobby, game);
        }

        private void RefreshCreate(ChessFusionRoomManager room, ChessFusionLobbyState lobby)
        {
            if (createCodeText != null)
            {
                bool open = room != null && room.IsConnected && !string.IsNullOrEmpty(room.CurrentRoomCode);
                createCodeText.text = open ? room.CurrentRoomCode : "LOADING";
            }

            if (createStatusText == null)
                return;

            if (room != null && room.IsConnected)
            {
                if (!string.IsNullOrEmpty(room.DisconnectNotice))
                    createStatusText.text = room.DisconnectNotice;
                else if (lobby != null && lobby.OpponentLeft && !lobby.MatchReady)
                    createStatusText.text = "The other player disconnected.";
                else if (lobby != null && lobby.LocalSeat != ChessSeat.None)
                    createStatusText.text = $"You are {ChessFusionLobbyState.SeatLabel(lobby.LocalSeat)}. Waiting for an opponent.";
                else
                    createStatusText.text = "Waiting for an opponent.";
                return;
            }

            if (!string.IsNullOrEmpty(room?.LastError))
                createStatusText.text = room.LastError;
            else if (busy)
                createStatusText.text = "Creating the room...";
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
                    return local == ChessRoyale.ChessPieceColor.White ? "YOU WIN" : "YOU LOSE";
                case ChessRoyale.ChessGameOutcome.BlackCheckmates:
                case ChessRoyale.ChessGameOutcome.BlackWinsOnTime:
                case ChessRoyale.ChessGameOutcome.BlackWinsByDisconnect:
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
                createCodeText.text = "LOADING";
            if (createStatusText != null)
                createStatusText.text = "Creating the room...";
            ApplyScreen();

            bool created = await room.CreateRoom(code, creatorSeatPreference);
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
                // The chess clock owns the single waiting-room display once the room is open.
                // Hide this transient creation popup so the room code is not shown twice.
                screen = LobbyScreen.Playing;
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
            RefreshColorChoice();
        }

        private void RefreshColorChoice()
        {
            if (colorChoiceText != null)
                colorChoiceText.text = $"Your color: {creatorSeatPreference.ToString().ToUpperInvariant()}";

            SetChoiceButtonColor(whiteChoiceButton, creatorSeatPreference == ChessSeatPreference.White);
            SetChoiceButtonColor(randomChoiceButton, creatorSeatPreference == ChessSeatPreference.Random);
            SetChoiceButtonColor(blackChoiceButton, creatorSeatPreference == ChessSeatPreference.Black);
        }

        private static void SetChoiceButtonColor(Button button, bool selected)
        {
            if (button != null && button.image != null)
                button.image.color = selected ? new Color(0.12f, 0.58f, 0.32f, 1f) : new Color(0.1f, 0.28f, 0.55f, 1f);
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
                || screen == LobbyScreen.Create
                || screen == LobbyScreen.Rematch;
            if (canvas != null)
                canvas.enabled = showMenu;
            if (shownScreen == screen)
                return;

            shownScreen = screen;
            if (chooseRoot != null)
                chooseRoot.SetActive(screen == LobbyScreen.Choose);
            if (joinRoot != null)
                joinRoot.SetActive(screen == LobbyScreen.Join);
            if (createRoot != null)
                createRoot.SetActive(screen == LobbyScreen.Create);
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
            background.color = new Color(0.02f, 0.03f, 0.06f, 0.94f);

            chooseRoot = CreatePage("Choose");
            CreateText(chooseRoot.transform, "Chess Royale Online", new Vector2(0f, 330f), new Vector2(860f, 80f), 42);
            CreateText(chooseRoot.transform, "Room creator chooses their side.", new Vector2(0f, 245f), new Vector2(860f, 60f), 28);
            colorChoiceText = CreateText(chooseRoot.transform, "Your color: RANDOM", new Vector2(0f, 155f), new Vector2(860f, 60f), 28);
            whiteChoiceButton = CreateButton(chooseRoot.transform, "WHITE", new Vector2(-270f, 65f), ChooseWhite, new Vector2(220f, 72f));
            randomChoiceButton = CreateButton(chooseRoot.transform, "RANDOM", new Vector2(0f, 65f), ChooseRandom, new Vector2(220f, 72f));
            blackChoiceButton = CreateButton(chooseRoot.transform, "BLACK", new Vector2(270f, 65f), ChooseBlack, new Vector2(220f, 72f));
            CreateButton(chooseRoot.transform, "CREATE", new Vector2(-160f, -75f), BeginCreate);
            CreateButton(chooseRoot.transform, "JOIN", new Vector2(160f, -75f), ShowJoin);
            CreateButton(chooseRoot.transform, "LEAVE", new Vector2(0f, -205f), LeaveLobbyToMenu);
            RefreshColorChoice();

            createRoot = CreatePage("Create");
            CreateText(createRoot.transform, "Your room code", new Vector2(0f, 180f), new Vector2(860f, 70f), 36);
            createCodeText = CreateText(createRoot.transform, "----", new Vector2(0f, 70f), new Vector2(860f, 90f), 64);
            createStatusText = CreateText(createRoot.transform, "Creating the room...", new Vector2(0f, -40f), new Vector2(860f, 120f), 26);
            CreateButton(createRoot.transform, "LOBBY", new Vector2(0f, -180f), LeaveMatchToChoose);

            joinRoot = CreatePage("Join");
            CreateText(joinRoot.transform, "Join a room", new Vector2(0f, 430f), new Vector2(860f, 70f), 42);
            joinStatusText = CreateText(joinRoot.transform, joinMessage, new Vector2(0f, 350f), new Vector2(860f, 80f), 26);
            codeInput = CreateCodeInput(joinRoot.transform, new Vector2(0f, 260f));
            BuildJoinKeyboard(joinRoot.transform);
            CreateButton(joinRoot.transform, "JOIN", new Vector2(-160f, -430f), SubmitJoin);
            CreateButton(joinRoot.transform, "LOBBY", new Vector2(160f, -430f), ShowChoose);

            rematchRoot = CreatePage("Rematch");
            resultTitleText = CreateText(rematchRoot.transform, "YOU WIN", new Vector2(0f, 180f), new Vector2(860f, 100f), 64);
            rematchStatusText = CreateText(rematchRoot.transform, "The game is over.", new Vector2(0f, 60f), new Vector2(860f, 140f), 26);
            CreateButton(rematchRoot.transform, "REMATCH", new Vector2(0f, -70f), RequestRematch, new Vector2(460f, 72f));
            CreateButton(rematchRoot.transform, "RETURN TO LOBBY", new Vector2(0f, -170f), LeaveMatchToChoose, new Vector2(460f, 72f));
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            return CreateButton(parent, label, position, action, new Vector2(240f, 72f));
        }

        private Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action, Vector2 size)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.1f, 0.28f, 0.55f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(action);
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
            image.color = new Color(1f, 1f, 1f, 0.92f);

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            textObject.transform.SetParent(inputObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);
            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 40;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
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
            placeholder.fontStyle = FontStyle.Bold;
            placeholder.alignment = TextAnchor.MiddleCenter;
            placeholder.color = new Color(0f, 0f, 0f, 0.35f);
            placeholder.text = "ABCD";
            placeholder.raycastTarget = false;

            InputField input = inputObject.AddComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = ChessNetworkTypes.RoomCodeLength;
            input.contentType = InputField.ContentType.Alphanumeric;
            input.keyboardType = TouchScreenKeyboardType.ASCIICapable;
            input.lineType = InputField.LineType.SingleLine;
            input.text = string.Empty;
            input.onValueChanged.AddListener(SanitizeRoomCode);
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
