using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale.AI
{
    // The single-player setup panel: choose a side and a difficulty, then Start.
    // Built in code and floats in front of the player, like the online lobby panel.
    // It comes back after each game with the result, so "play again" or "harder" is one click.
    public sealed class ChessAISetupPanel : MonoBehaviour
    {
        private const float PanelDistance = 2.1f;

        private static readonly Color ButtonColor = new Color(0.1f, 0.28f, 0.55f, 1f);
        private static readonly Color SelectedColor = new Color(0.95f, 0.66f, 0.16f, 1f);

        private static readonly string[] DifficultyDetails =
        {
            "Easy: looks one move ahead and makes beginner mistakes.",
            "Medium: looks three moves ahead. Punishes blunders.",
            "Expert: thinks up to two seconds, many moves ahead.",
        };

        private ChessAIPlayer player;
        private Canvas canvas;
        private Text resultText;
        private Text detailText;
        private readonly Image[] colorButtons = new Image[3];
        private readonly Image[] difficultyButtons = new Image[3];
        private int colorChoice;
        private ChessAIDifficulty difficulty;
        private Camera followedCamera;

        public bool IsVisible => canvas != null && canvas.enabled;

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
            resultText.text = string.IsNullOrEmpty(result) ? "Play the computer" : result;
            RefreshSelection();
            canvas.enabled = true;
            followedCamera = null; // Snap in front of the player right away.
        }

        public void Hide()
        {
            canvas.enabled = false;
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
            background.color = new Color(0.02f, 0.03f, 0.06f, 0.94f);

            CreateText("Chess Royale: Single Player", new Vector2(0f, 410f), new Vector2(860f, 70f), 40);
            resultText = CreateText("Play the computer", new Vector2(0f, 330f), new Vector2(860f, 80f), 30);

            CreateText("Play as", new Vector2(0f, 240f), new Vector2(860f, 50f), 28);
            string[] colorLabels = { "WHITE", "BLACK", "RANDOM" };
            for (int i = 0; i < 3; i++)
            {
                int choice = i;
                colorButtons[i] = CreateButton(colorLabels[i], new Vector2((i - 1) * 270f, 165f), new Vector2(250f, 76f),
                    () => { colorChoice = choice; RefreshSelection(); });
            }

            CreateText("Difficulty", new Vector2(0f, 60f), new Vector2(860f, 50f), 28);
            string[] difficultyLabels = { "EASY", "MEDIUM", "EXPERT" };
            for (int i = 0; i < 3; i++)
            {
                ChessAIDifficulty level = (ChessAIDifficulty)i;
                difficultyButtons[i] = CreateButton(difficultyLabels[i], new Vector2((i - 1) * 270f, -15f), new Vector2(250f, 76f),
                    () => { difficulty = level; RefreshSelection(); });
            }

            detailText = CreateText(string.Empty, new Vector2(0f, -105f), new Vector2(860f, 70f), 24);

            CreateButton("START", new Vector2(0f, -225f), new Vector2(520f, 96f),
                () => player.StartGame(colorChoice, difficulty)).color = new Color(0.12f, 0.5f, 0.24f, 1f);
            CreateButton("MAIN MENU", new Vector2(0f, -355f), new Vector2(360f, 70f), player.ReturnToMainMenu);

            canvas.enabled = false;
        }

        private void RefreshSelection()
        {
            for (int i = 0; i < 3; i++)
            {
                colorButtons[i].color = i == colorChoice ? SelectedColor : ButtonColor;
                difficultyButtons[i].color = i == (int)difficulty ? SelectedColor : ButtonColor;
            }

            detailText.text = DifficultyDetails[(int)difficulty];
        }

        private Text CreateText(string value, Vector2 position, Vector2 size, int fontSize)
        {
            return CreateText(transform, value, position, size, fontSize);
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
            GameObject buttonObject = new GameObject(label, typeof(RectTransform));
            buttonObject.transform.SetParent(transform, false);
            RectTransform rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Image image = buttonObject.AddComponent<Image>();
            image.color = ButtonColor;
            Button button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            Text text = CreateText(buttonObject.transform, label, Vector2.zero, size, 28);
            text.rectTransform.anchoredPosition = Vector2.zero;
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
