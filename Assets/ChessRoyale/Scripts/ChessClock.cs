using UnityEngine;
using UnityEngine.UI;

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
        private static readonly Color ActiveFrame = new Color(0.78f, 0.58f, 0.18f, 0.95f);
        private static readonly Color IdleFrame = new Color(0.07f, 0.07f, 0.09f, 0.9f);
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

        private void Awake()
        {
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
            if (game != null)
                game.GameReset -= ResetClock;
        }

        public void ResetClock()
        {
            whiteSeconds = blackSeconds = minutesPerSide * 60f;
            shownWhite = shownBlack = -1;
        }

        private void Update()
        {
            if (game == null)
                return;

            if (game.Outcome == ChessGameOutcome.InProgress)
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

        private void LateUpdate()
        {
            if (panel == null)
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
            panel.position = Vector3.Lerp(panel.position, target, blend);
            placed = true;

            Vector3 away = panel.position - head.position;
            if (away.sqrMagnitude > 0.0001f)
                panel.rotation = Quaternion.LookRotation(away, Vector3.up);
        }

        private void UpdateDisplay()
        {
            int white = Mathf.CeilToInt(whiteSeconds);
            int black = Mathf.CeilToInt(blackSeconds);
            if (white != shownWhite)
            {
                shownWhite = white;
                whiteText.text = Format(white);
                whiteText.color = whiteSeconds <= lowTimeSeconds ? LowTime : Color.white;
            }

            if (black != shownBlack)
            {
                shownBlack = black;
                blackText.text = Format(black);
                blackText.color = blackSeconds <= lowTimeSeconds ? LowTime : Color.white;
            }

            bool running = game.Outcome == ChessGameOutcome.InProgress;
            whiteFrame.color = running && game.ActiveColor == ChessPieceColor.White ? ActiveFrame : IdleFrame;
            blackFrame.color = running && game.ActiveColor == ChessPieceColor.Black ? ActiveFrame : IdleFrame;

            if (shownStatus != game.StatusMessage)
            {
                shownStatus = game.StatusMessage;
                statusText.text = shownStatus;
            }
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

            RectTransform rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(640f, 260f);
            rect.localScale = Vector3.one * 0.0014f;
            panel = canvasObject.transform;

            Image background = canvasObject.AddComponent<Image>();
            background.color = new Color(0.02f, 0.02f, 0.03f, 0.8f);
            background.raycastTarget = false;

            whiteFrame = CreateBox(rect, "White Timer", new Vector2(-155f, 35f), out whiteText, "WHITE");
            blackFrame = CreateBox(rect, "Black Timer", new Vector2(155f, 35f), out blackText, "BLACK");
            statusText = CreateText(rect, "Status", new Vector2(0f, -90f), new Vector2(600f, 50f), 30, FontStyle.Normal);
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
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }
    }
}
