using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale.AI
{
    // The single-player setup panel: choose a side and a difficulty, then Start.
    // Built in code and floats in front of the player, like the online lobby panel.
    // Setup and match results use separate content roots so settings never obscure the result.
    public sealed class ChessAISetupPanel : MonoBehaviour
    {
        private const float PanelDistance = 2.1f;

        private static readonly Color ButtonColor = ChessUITheme.DarkBrown;

        private static readonly string[] DifficultyDetails =
        {
            "Easy: looks one move ahead and makes beginner mistakes.",
            "Medium: looks three moves ahead. Punishes blunders.",
            "Expert: thinks up to two seconds, many moves ahead.",
        };

        private ChessAIPlayer player;
        private Canvas canvas;
        private Image panelBackground;
        private static readonly Color HomeIvory = new Color(0.96f, 0.9f, 0.77f);
        private Transform setupContent;
        private GameObject resultContent;
        private Text resultText;
        private Text outcomeTitle;
        private Text detailText;
        private readonly Image[] colorButtons = new Image[3];
        private readonly Image[] difficultyButtons = new Image[3];
        private int colorChoice;
        private ChessAIDifficulty difficulty;
        private Camera followedCamera;

        public bool IsVisible => gameObject.activeInHierarchy && canvas != null && canvas.enabled;

        public bool IsShowingResult => IsVisible && resultContent != null && resultContent.activeSelf;

        public static ChessAISetupPanel Create(ChessAIPlayer owner)
        {
            GameObject root = new GameObject("Single Player Setup", typeof(RectTransform));
            ChessAISetupPanel panel = root.AddComponent<ChessAISetupPanel>();
            panel.player = owner;
            panel.colorChoice = ChessSinglePlayerSettings.SavedColorChoice;
            panel.difficulty = ChessSinglePlayerSettings.SavedDifficulty;
            panel.Build();
            return panel;
        }

        public void Show(string result)
        {
            // Setup must never interrupt a running match, even if a stale caller tries to show it.
            if (ChessSinglePlayerSettings.GameStarted)
            {
                Debug.LogWarning("Chess Royale: ignored setup panel request during an active single-player game.");
                return;
            }

            GetComponent<RectTransform>().sizeDelta = new Vector2(920f, 980f);
            panelBackground.color = ChessUITheme.Surface;
            setupContent.gameObject.SetActive(true);
            resultContent.SetActive(false);
            resultText.text = string.IsNullOrEmpty(result) ? "Play the computer" : result;
            RefreshSelection();
            canvas.enabled = true;
            followedCamera = null; // Snap in front of the player right away.
            gameObject.SetActive(true);
        }

        public void ShowResult(ChessGameOutcome outcome, ChessPieceColor humanColor)
        {
            if (ChessSinglePlayerSettings.GameStarted || outcome == ChessGameOutcome.InProgress)
                return;

            ChessPieceColor? winner = null;
            switch (outcome)
            {
                case ChessGameOutcome.WhiteCheckmates:
                case ChessGameOutcome.WhiteWinsOnTime:
                case ChessGameOutcome.WhiteWinsByDisconnect:
                    winner = ChessPieceColor.White;
                    break;
                case ChessGameOutcome.BlackCheckmates:
                case ChessGameOutcome.BlackWinsOnTime:
                case ChessGameOutcome.BlackWinsByDisconnect:
                    winner = ChessPieceColor.Black;
                    break;
            }

            bool won = winner == humanColor;
            outcomeTitle.text = winner == null ? "DRAW" : won ? "YOU WIN" : "YOU LOSE";
            outcomeTitle.color = HomeIvory;
            panelBackground.color = new Color(0.105f, 0.027f, 0.023f, 0.97f);
            GetComponent<RectTransform>().sizeDelta = new Vector2(920f, 720f);
            setupContent.gameObject.SetActive(false);
            resultContent.SetActive(true);
            canvas.enabled = true;
            followedCamera = null;
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            canvas.enabled = false;
            // Disabling Canvas alone leaves the XR raycaster and button handlers active.
            gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (canvas != null && canvas.enabled)
                FollowCamera();
        }

        private void FollowCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
                return;

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

        private void Build()
        {
            EnsureEventSystem();

            canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rect = GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(920f, 980f);
            rect.localScale = Vector3.one * 0.0014f;

            Image background = gameObject.AddComponent<Image>();
            panelBackground = background;
            background.color = ChessUITheme.Surface;
            ChessUITheme.Border(background);

            GameObject setup = new GameObject("Setup Content", typeof(RectTransform));
            setup.transform.SetParent(transform, false);
            setupContent = setup.transform;

            CreateText("Chess Royale: Single Player", new Vector2(0f, 410f), new Vector2(860f, 70f), 40);
            resultText = CreateText("Play the computer", new Vector2(0f, 330f), new Vector2(860f, 80f), 30);

            CreateText("Play as", new Vector2(0f, 240f), new Vector2(860f, 50f), 28);
            string[] colorLabels = { "WHITE", "BLACK", "RANDOM" };
            for (int i = 0; i < 3; i++)
            {
                int choice = i;
                colorButtons[i] = CreateButton(colorLabels[i], new Vector2((i - 1) * 270f, 165f), ChessUITheme.ChoiceSize,
                    () => { colorChoice = choice; RefreshSelection(); });
            }

            CreateText("Difficulty", new Vector2(0f, 60f), new Vector2(860f, 50f), 28);
            string[] difficultyLabels = { "EASY", "MEDIUM", "EXPERT" };
            for (int i = 0; i < 3; i++)
            {
                ChessAIDifficulty level = (ChessAIDifficulty)i;
                difficultyButtons[i] = CreateButton(difficultyLabels[i], new Vector2((i - 1) * 270f, -15f), ChessUITheme.ChoiceSize,
                    () => { difficulty = level; RefreshSelection(); });
            }

            detailText = CreateText(string.Empty, new Vector2(0f, -105f), new Vector2(860f, 70f), 24);

            CreateButton("START", new Vector2(0f, -225f), new Vector2(520f, 96f),
                () => player.StartGame(colorChoice, difficulty)).color = ChessUITheme.Burgundy;
            CreateButton("MAIN MENU", new Vector2(0f, -355f), new Vector2(360f, 70f), player.ReturnToMainMenu);

            BuildResultContent();
            ChessUITheme.ApplyPanel(gameObject);
            Hide();
        }

        private void BuildResultContent()
        {
            resultContent = new GameObject("Match Result Content", typeof(RectTransform));
            resultContent.transform.SetParent(transform, false);
            Transform root = resultContent.transform;
            outcomeTitle = CreateText(root, "", new Vector2(0f, 205f), new Vector2(820f, 110f), 64);
            outcomeTitle.fontStyle = FontStyle.Normal;
            Font homeFont = Resources.Load<Font>("CormorantGaramond-Medium");
            if (homeFont != null)
                outcomeTitle.font = homeFont;
            Color ivory = HomeIvory;
            Color burgundy = new Color(0.25f, 0.042f, 0.055f, 0.91f);
            Color darkBrown = new Color(0.065f, 0.025f, 0.019f, 0.85f);
            Image replay = CreateButton(root, "PLAY AGAIN", new Vector2(0f, 25f), new Vector2(700f, 88f), player.ReplayGame,
                burgundy, ivory);
            Image settings = CreateButton(root, "CHANGE SETTINGS", new Vector2(0f, -90f), new Vector2(700f, 88f), () => Show(null),
                darkBrown, ivory);
            Image menu = CreateButton(root, "MAIN MENU", new Vector2(0f, -205f), new Vector2(700f, 88f), player.ReturnToMainMenu,
                darkBrown, ivory);
            foreach (Image button in new[] { replay, settings, menu })
            {
                Text label = button.GetComponentInChildren<Text>();
                label.fontStyle = FontStyle.Normal;
                label.fontSize = 34;
                if (homeFont != null)
                    label.font = homeFont;
                Button selectable = button.GetComponent<Button>();
                ColorBlock colors = selectable.colors;
                colors.highlightedColor = new Color(1.38f, 1.23f, 1.12f);
                colors.selectedColor = colors.highlightedColor;
                selectable.colors = colors;
                ChessUITheme.Border(button);
            }
            resultContent.SetActive(false);
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < 3; i++)
            {
                ChessUITheme.SetChoice(colorButtons[i], i == colorChoice);
                ChessUITheme.SetChoice(difficultyButtons[i], i == (int)difficulty);
            }

            detailText.text = DifficultyDetails[(int)difficulty];
        }

        private Text CreateText(string value, Vector2 position, Vector2 size, int fontSize)
        {
            return CreateText(setupContent, value, position, size, fontSize);
        }

        private static Text CreateText(Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
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

        private Image CreateButton(string label, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            return CreateButton(setupContent, label, position, size, action, ButtonColor, ChessUITheme.Ivory);
        }

        private Image CreateButton(Transform parent, string label, Vector2 position, Vector2 size,
            UnityEngine.Events.UnityAction action, Color backgroundColor, Color textColor)
        {
            GameObject buttonObject = new GameObject(label, typeof(RectTransform));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = backgroundColor;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            Text text = CreateText(buttonObject.transform, label, Vector2.zero, size, 28);
            text.rectTransform.anchoredPosition = Vector2.zero;
            text.color = textColor;
            return image;
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
