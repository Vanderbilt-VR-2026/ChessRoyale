using UnityEngine;
using UnityEngine.UI;

namespace ChessRoyale
{
    // Shared with the palace home screen. Apply explicitly to game UI, never scene geometry.
    public static class ChessUITheme
    {
        public static readonly Color Ivory = new Color(0.96f, 0.9f, 0.77f);
        public static readonly Color Gold = new Color(0.62f, 0.48f, 0.29f);
        public static readonly Color Surface = new Color(0.105f, 0.027f, 0.023f, 0.97f);
        public static readonly Color Burgundy = new Color(0.25f, 0.042f, 0.055f, 0.96f);
        public static readonly Color DarkBrown = new Color(0.065f, 0.025f, 0.019f, 0.94f);
        public static readonly Vector2 ChoiceSize = new Vector2(250f, 76f);
        public const float BorderThickness = 6f;
        public const float SelectedBorderThickness = 8f;
        private static Font font;

        public static void StyleText(Text text)
        {
            if (font == null)
                font = Resources.Load<Font>("CormorantGaramond-Medium");
            if (font != null)
                text.font = font;
            text.fontStyle = FontStyle.Normal;
            text.color = Ivory;
        }

        public static void Border(Image image, float thickness = BorderThickness)
        {
            // Four filled UI quads keep the border opaque and inside the panel. Outline effects
            // produce very thin offset silhouettes that shimmer at headset resolution.
            foreach (Outline outline in image.GetComponents<Outline>())
                outline.enabled = false;
            Edge(image.transform, "Solid Border Top", new Vector2(0f, 1f), Vector2.one,
                new Vector2(0f, -thickness), Vector2.zero);
            Edge(image.transform, "Solid Border Bottom", Vector2.zero, new Vector2(1f, 0f),
                Vector2.zero, new Vector2(0f, thickness));
            Edge(image.transform, "Solid Border Left", Vector2.zero, new Vector2(0f, 1f),
                Vector2.zero, new Vector2(thickness, 0f));
            Edge(image.transform, "Solid Border Right", new Vector2(1f, 0f), Vector2.one,
                new Vector2(-thickness, 0f), Vector2.zero);
        }

        private static void Edge(Transform parent, string name, Vector2 min, Vector2 max,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            Transform existing = parent.Find(name);
            GameObject edge = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            if (existing == null)
                edge.transform.SetParent(parent, false);
            RectTransform rect = edge.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image graphic = edge.GetComponent<Image>();
            if (graphic == null)
                graphic = edge.AddComponent<Image>();
            graphic.color = Gold;
            graphic.raycastTarget = false;
        }

        public static void StyleButton(Button button, bool primary = false)
        {
            // Inactive pages defer Selectable.Awake; resolve the background explicitly.
            if (button.image == null && button.TryGetComponent<Image>(out Image background))
                button.targetGraphic = background;
            if (button.image != null)
            {
                button.image.color = primary ? Burgundy : DarkBrown;
                Border(button.image);
            }
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.38f, 1.23f, 1.12f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.8f, 0.7f, 0.6f);
            colors.disabledColor = new Color(0.55f, 0.5f, 0.45f, 0.65f);
            button.colors = colors;
        }

        public static void SetChoice(Image image, bool selected)
        {
            image.rectTransform.sizeDelta = ChoiceSize;
            image.color = selected ? Burgundy : DarkBrown;
            Border(image, selected ? SelectedBorderThickness : BorderThickness);
        }

        public static void ApplyPanel(GameObject root)
        {
            if (root == null)
                return;
            Image background = root.GetComponent<Image>();
            if (background != null)
            {
                background.color = Surface;
                Border(background);
            }
            foreach (Text text in root.GetComponentsInChildren<Text>(true))
                StyleText(text);
            foreach (Button button in root.GetComponentsInChildren<Button>(true))
            {
                string label = button.name.ToUpperInvariant();
                StyleButton(button, label == "START" || label == "PLAY AGAIN" || label == "CREATE" ||
                    label == "JOIN" || label == "REMATCH" || label == "OK");
            }
            foreach (InputField input in root.GetComponentsInChildren<InputField>(true))
            {
                if (input.image == null && input.TryGetComponent<Image>(out Image inputBackground))
                    input.targetGraphic = inputBackground;
                if (input.image != null)
                {
                    input.image.color = DarkBrown;
                    Border(input.image);
                }
                input.customCaretColor = true;
                input.caretColor = Ivory;
                input.selectionColor = new Color(Gold.r, Gold.g, Gold.b, 0.4f);
                if (input.placeholder is Text placeholder)
                    placeholder.color = new Color(Ivory.r, Ivory.g, Ivory.b, 0.5f);
            }
        }
    }
}
