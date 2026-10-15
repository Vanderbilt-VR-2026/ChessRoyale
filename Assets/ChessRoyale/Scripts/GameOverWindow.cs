using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale
{
    // Shows a world-space "Game Over" panel in front of the player when their health runs out.
    public sealed class GameOverWindow : MonoBehaviour
    {
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private float distanceFromPlayer = 2f;
        [SerializeField] private bool pauseGameOnDeath = true;

        [Header("Built by Tools > Chess Royale > Add Game Over Screen (or at runtime if empty)")]
        [SerializeField] private GameObject window;
        [SerializeField] private Button playAgainButton;
        [SerializeField] private Button quitButton;

        private static readonly Color PanelColor = new Color(0.05f, 0.03f, 0.1f, 0.92f);
        private static readonly Color TitleColor = new Color(1f, 0.25f, 0.3f);
        private static readonly Color YesColor = new Color(0.2f, 0.65f, 0.35f);
        private static readonly Color NoColor = new Color(0.55f, 0.18f, 0.2f);

        private Font font;

        private void Awake()
        {
            if (playerHealth == null)
                playerHealth = FindFirstObjectByType<PlayerHealth>();

            if (window == null)
                BuildWindow();
            else
                window.SetActive(false);

            playAgainButton.onClick.AddListener(PlayAgain);
            quitButton.onClick.AddListener(Quit);
        }

        private void OnEnable()
        {
            if (playerHealth != null)
                playerHealth.Died += Show;
        }

        private void OnDisable()
        {
            if (playerHealth != null)
                playerHealth.Died -= Show;
        }

        public void Show()
        {
            PlaceInFrontOfPlayer();
            window.SetActive(true);

            if (pauseGameOnDeath)
                Time.timeScale = 0f;
        }

        public void PlayAgain()
        {
            Time.timeScale = 1f;
            Scene scene = SceneManager.GetActiveScene();
#if UNITY_EDITOR
            // Lets draft scenes that aren't in Build Settings restart too.
            if (scene.buildIndex < 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(
                    scene.path, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            SceneManager.LoadScene(scene.buildIndex);
        }

        public void Quit()
        {
            Time.timeScale = 1f;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void PlaceInFrontOfPlayer()
        {
            Camera head = Camera.main;
            if (head == null)
                return;

            Vector3 forward = head.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f)
                forward = Vector3.forward;
            forward.Normalize();

            window.transform.position = head.transform.position + forward * distanceFromPlayer;
            window.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            window.GetComponent<Canvas>().worldCamera = head;
        }

        // Called at runtime when no window was saved in the scene, and by the editor tool
        // to save one into the scene so it can be restyled in the Inspector.
        public void BuildWindow()
        {
            if (window != null)
                return;

            EnsureEventSystem();
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            GameObject root = new GameObject("Game Over Window");
            root.transform.SetParent(transform, false);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            root.AddComponent<TrackedDeviceGraphicRaycaster>();

            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(800f, 500f);
            rootRect.localScale = Vector3.one * 0.002f; // 1.6m x 1m in the world

            Image panel = CreateUiObject<Image>("Panel", rootRect);
            panel.color = PanelColor;
            Stretch(panel.rectTransform);

            CreateLabel("Title", rootRect, "GAME OVER", 110, TitleColor, new Vector2(0f, 130f), new Vector2(760f, 140f));
            CreateLabel("Prompt", rootRect, "Want to play more?", 54, Color.white, new Vector2(0f, 10f), new Vector2(760f, 80f));
            playAgainButton = CreateButton("Play Again Button", rootRect, "Play Again", YesColor, new Vector2(-180f, -140f));
            quitButton = CreateButton("Quit Button", rootRect, "Quit", NoColor, new Vector2(180f, -140f));

            root.SetActive(false);
            window = root;
        }

        private static void EnsureEventSystem()
        {
            EventSystem eventSystem = FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("EventSystem").AddComponent<EventSystem>();

            // XR ray/poke interactors can only press UI buttons through the XR UI input module.
            if (eventSystem.GetComponent<XRUIInputModule>() == null)
            {
                foreach (BaseInputModule module in eventSystem.GetComponents<BaseInputModule>())
                    module.enabled = false;
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
            }
        }

        private Button CreateButton(string name, RectTransform parent, string label, Color color, Vector2 position)
        {
            Image background = CreateUiObject<Image>(name, parent);
            background.color = color;
            background.rectTransform.anchoredPosition = position;
            background.rectTransform.sizeDelta = new Vector2(300f, 110f);

            Button button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;

            Text text = CreateLabel("Label", background.rectTransform, label, 48, Color.white, Vector2.zero, Vector2.zero);
            Stretch(text.rectTransform);
            return button;
        }

        private Text CreateLabel(string name, RectTransform parent, string content, int size, Color color, Vector2 position, Vector2 dimensions)
        {
            Text text = CreateUiObject<Text>(name, parent);
            text.font = font;
            text.text = content;
            text.fontSize = size;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = dimensions;
            return text;
        }

        private static T CreateUiObject<T>(string name, RectTransform parent) where T : Component
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<T>();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
