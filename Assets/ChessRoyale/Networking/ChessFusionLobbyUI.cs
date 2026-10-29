using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale.Networking
{
    [DisallowMultipleComponent]
    public sealed class ChessFusionLobbyUI : MonoBehaviour
    {
        private const float PanelDistance = 2.1f;

        private Text statusText;
        private Text codeText;
        private InputField codeInput;
        private readonly char[] joinLetters = { 'A', 'A', 'A', 'A' };
        private Text[] letterLabels;
        private Camera followedCamera;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureLobby()
        {
            if (FindAnyObjectByType<ChessFusionLobbyUI>() != null)
                return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "ChessRoyaleVR")
                return;

            GameObject lobby = new GameObject("Chess Fusion Lobby");
            lobby.AddComponent<ChessFusionLobbyUI>();
        }

        private void Awake()
        {
            ChessFusionLobbyUI[] existing = FindObjectsByType<ChessFusionLobbyUI>(FindObjectsSortMode.None);
            if (existing.Length > 1 && existing[0] != this)
            {
                Destroy(gameObject);
                return;
            }

            EnsureEventSystem();
            BuildPanel();
        }

        private void LateUpdate()
        {
            FollowActiveCamera();
            RefreshStatus();
        }

        private void FollowActiveCamera()
        {
            Camera camera = ResolveCamera();
            if (camera == null)
                return;

            if (transform.parent != null)
                transform.SetParent(null, true);

            Vector3 target = camera.transform.position + camera.transform.forward * PanelDistance + Vector3.down * 0.35f;
            Vector3 faceDirection = camera.transform.position - target;
            bool drifted = followedCamera != camera
                || Vector3.Distance(transform.position, target) > 0.45f
                || Vector3.Angle(transform.forward, faceDirection) > 20f;
            if (!drifted)
                return;

            followedCamera = camera;
            transform.position = target;
            transform.rotation = Quaternion.LookRotation(camera.transform.position - target, Vector3.up);
        }

        private static Camera ResolveCamera()
        {
            ChessFusionPlayer[] players = FindObjectsByType<ChessFusionPlayer>(FindObjectsSortMode.None);
            foreach (ChessFusionPlayer player in players)
            {
                if (player.IsLocalPlayer && player.XrCamera != null && player.XrCamera.enabled)
                    return player.XrCamera;
            }

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

        private void RefreshStatus()
        {
            if (statusText == null)
                return;

            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room == null || !room.IsConnected)
            {
                if (!string.IsNullOrEmpty(room?.DisconnectNotice))
                    statusText.text = room.DisconnectNotice + "\nOffline chess is active.";
                else if (!string.IsNullOrEmpty(room?.LastError))
                    statusText.text = room.LastError;
                else
                    statusText.text = "Offline chess is active.\nCreate a room or join a four-letter code.";
                if (codeText != null && (room == null || string.IsNullOrEmpty(room.CurrentRoomCode)))
                    codeText.text = "ROOM ----";
                return;
            }

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessSeat seat = lobby != null ? lobby.LocalSeat : (room.IsSharedModeMasterClient ? ChessSeat.White : ChessSeat.Black);
            string seatLabel = ChessFusionLobbyState.SeatLabel(seat);
            if (!string.IsNullOrEmpty(room.DisconnectNotice))
            {
                statusText.text = $"Room {room.CurrentRoomCode}. You are {seatLabel}.\n{room.DisconnectNotice}";
            }
            else if (lobby != null && lobby.OpponentLeft && !lobby.MatchReady)
            {
                statusText.text = $"Room {room.CurrentRoomCode}. You are {seatLabel}.\nThe other player disconnected.";
            }
            else if (lobby != null && lobby.MatchReady)
            {
                string boardStatus = ChessRoyale.ChessGameController.Instance != null
                    ? ChessRoyale.ChessGameController.Instance.StatusMessage
                    : "Both players connected.";
                statusText.text = $"Room {room.CurrentRoomCode}. You are {seatLabel}.\n{boardStatus}";
            }
            else
            {
                statusText.text = $"Room {room.CurrentRoomCode}. You are {seatLabel}.\nWaiting for an opponent. Piece grabs are paused.";
            }

            if (codeText != null)
                codeText.text = $"ROOM {room.CurrentRoomCode}";
        }

        public void CreateRoom()
        {
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room == null)
                return;

            string code = ChessNetworkTypes.CreateRoomCode();
            ShowCode(code);
            _ = room.CreateRoom(code);
        }

        public void JoinRoom()
        {
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room == null)
                return;

            string typed = codeInput != null ? codeInput.text : string.Empty;
            if (!string.IsNullOrWhiteSpace(typed))
            {
                _ = room.JoinRoom(typed);
                return;
            }

            _ = room.JoinRoom(new string(joinLetters));
        }

        public void LeaveRoom()
        {
            if (ChessFusionRoomManager.Instance == null)
                return;

            _ = ChessFusionRoomManager.Instance.LeaveRoom();
        }

        public void RequestRematch()
        {
            ChessRoyale.ChessGameController.Instance?.RequestRematch();
        }

        public void CycleLetter(int index)
        {
            if (index < 0 || index >= joinLetters.Length)
                return;

            joinLetters[index] = joinLetters[index] == 'Z' ? 'A' : (char)(joinLetters[index] + 1);
            if (letterLabels != null && letterLabels[index] != null)
                letterLabels[index].text = joinLetters[index].ToString();
            if (codeInput != null)
                codeInput.text = new string(joinLetters);
        }

        private void ShowCode(string code)
        {
            if (codeText != null)
                codeText.text = $"ROOM {code}";
            if (codeInput != null)
                codeInput.text = code;
        }

        private void BuildPanel()
        {
            if (GetComponent<Canvas>() != null)
                return;

            Canvas canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(920f, 760f);
            rect.localScale = Vector3.one * 0.0014f;

            Image background = gameObject.AddComponent<Image>();
            background.color = new Color(0.02f, 0.03f, 0.06f, 0.94f);

            CreateText("Chess Royale Online", new Vector2(0f, 250f), new Vector2(860f, 70f), 42);
            codeText = CreateText("ROOM ----", new Vector2(0f, 180f), new Vector2(860f, 64f), 36);
            statusText = CreateText("Offline chess is active.", new Vector2(0f, 70f), new Vector2(860f, 140f), 26);

            CreateButton("CREATE", new Vector2(-230f, -40f), CreateRoom);
            CreateButton("JOIN", new Vector2(0f, -40f), JoinRoom);
            CreateButton("LEAVE", new Vector2(230f, -40f), LeaveRoom);
            CreateButton("REMATCH", new Vector2(0f, -120f), RequestRematch);

            letterLabels = new Text[ChessNetworkTypes.RoomCodeLength];
            for (int index = 0; index < letterLabels.Length; index++)
            {
                int letterIndex = index;
                float x = -180f + index * 120f;
                letterLabels[index] = CreateText("A", new Vector2(x, -210f), new Vector2(90f, 60f), 34);
                CreateButton("+", new Vector2(x, -280f), () => CycleLetter(letterIndex), new Vector2(80f, 54f));
            }

            codeInput = CreateCodeInput(new Vector2(0f, -345f));
        }

        private Text CreateText(string value, Vector2 position, Vector2 size, int fontSize)
        {
            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(transform, false);
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

        private void CreateButton(string label, Vector2 position, UnityEngine.Events.UnityAction action)
        {
            CreateButton(label, position, action, new Vector2(200f, 72f));
        }

        private void CreateButton(string label, Vector2 position, UnityEngine.Events.UnityAction action, Vector2 size)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform));
            buttonObject.transform.SetParent(transform, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.1f, 0.28f, 0.55f, 1f);
            Button button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            Text text = CreateText(label, position, size, 28);
            text.transform.SetParent(buttonObject.transform, false);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.rectTransform.sizeDelta = size;
        }

        private InputField CreateCodeInput(Vector2 position)
        {
            GameObject inputObject = new GameObject("Room Code", typeof(RectTransform));
            inputObject.transform.SetParent(transform, false);
            RectTransform rect = inputObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(280f, 56f);
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
            text.fontSize = 28;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.black;
            text.supportRichText = false;

            InputField input = inputObject.AddComponent<InputField>();
            input.textComponent = text;
            input.characterLimit = ChessNetworkTypes.RoomCodeLength;
            input.contentType = InputField.ContentType.Alphanumeric;
            input.text = new string(joinLetters);
            return input;
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
