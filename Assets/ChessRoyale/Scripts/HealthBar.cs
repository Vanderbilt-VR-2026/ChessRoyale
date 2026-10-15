using UnityEngine;
using UnityEngine.UI;

namespace ChessRoyale
{
    // Head-locked health bar that sits at the bottom of the player's view.
    // Screen-space overlays don't render in a VR headset, so the bar is a small
    // world-space canvas parented to the camera instead.
    public sealed class HealthBar : MonoBehaviour
    {
        [SerializeField] private PlayerHealth playerHealth;
        [SerializeField] private float distanceFromEyes = 1f;
        [SerializeField] private float heightBelowEyes = 0.32f;
        [SerializeField] private float fillSpeed = 3f;

        private static readonly Color FrameColor = new Color(0.05f, 0.03f, 0.1f, 0.85f);
        private static readonly Color TrackColor = new Color(0.2f, 0.05f, 0.08f, 0.9f);
        private static readonly Color FullColor = new Color(0.25f, 0.85f, 0.4f);
        private static readonly Color LowColor = new Color(0.95f, 0.2f, 0.25f);

        private RectTransform fill;
        private Image fillImage;
        private Text label;
        private float displayedFraction = 1f;
        private float targetFraction = 1f;

        private void Awake()
        {
            if (playerHealth == null)
                playerHealth = FindFirstObjectByType<PlayerHealth>();
        }

        private void Start()
        {
            Camera head = Camera.main;
            if (head == null || playerHealth == null)
            {
                Debug.LogWarning("HealthBar needs a Main Camera and a PlayerHealth in the scene.", this);
                enabled = false;
                return;
            }

            BuildBar(head.transform);
            targetFraction = displayedFraction = playerHealth.CurrentHealth / playerHealth.MaxHealth;
            Refresh(playerHealth.CurrentHealth, playerHealth.MaxHealth);
            playerHealth.HealthChanged += Refresh;
        }

        private void OnDestroy()
        {
            if (playerHealth != null)
                playerHealth.HealthChanged -= Refresh;
        }

        private void Update()
        {
            if (fill == null)
                return;

            // Unscaled so the bar still drains to empty while the game-over pause is active.
            displayedFraction = Mathf.MoveTowards(displayedFraction, targetFraction, fillSpeed * Time.unscaledDeltaTime);
            fill.anchorMax = new Vector2(displayedFraction, 1f);
            fillImage.color = Color.Lerp(LowColor, FullColor, displayedFraction);
        }

        private void Refresh(float current, float max)
        {
            targetFraction = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            label.text = $"HP  {Mathf.CeilToInt(current)} / {Mathf.CeilToInt(max)}";
        }

        private void BuildBar(Transform head)
        {
            GameObject root = new GameObject("Health Bar", typeof(RectTransform));
            root.transform.SetParent(head, false);
            root.transform.localPosition = new Vector3(0f, -heightBelowEyes, distanceFromEyes);
            // Tilt toward the eyes so the bar isn't viewed at a steep angle.
            root.transform.localRotation = Quaternion.Euler(Mathf.Atan2(heightBelowEyes, distanceFromEyes) * -Mathf.Rad2Deg, 0f, 0f);

            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;

            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(500f, 70f);
            rootRect.localScale = Vector3.one * 0.001f; // 0.5m x 0.07m at 1m away

            Image frame = CreateImage("Frame", rootRect, FrameColor);
            Stretch(frame.rectTransform, 0f);

            Image track = CreateImage("Track", rootRect, TrackColor);
            Stretch(track.rectTransform, 8f);

            fillImage = CreateImage("Fill", track.rectTransform, FullColor);
            fill = fillImage.rectTransform;
            Stretch(fill, 0f);

            label = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
            label.transform.SetParent(rootRect, false);
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 34;
            label.fontStyle = FontStyle.Bold;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            Stretch(label.rectTransform, 0f);
        }

        private static Image CreateImage(string name, RectTransform parent, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
