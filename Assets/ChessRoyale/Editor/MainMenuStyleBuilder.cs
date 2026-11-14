using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Rebuilds the home screen as editable scene UI, with the original menu callbacks.</summary>
public static class MainMenuStyleBuilder
{
    private const string Home = "Assets/ChessRoyale/UI/Home/";
    private static readonly Color Ivory = new Color(0.96f, 0.90f, 0.77f);
    private static readonly Color Gold = new Color(0.62f, 0.48f, 0.29f);

    [MenuItem("Chess Royale/Apply Mature Palace Home Screen")]
    public static void Apply()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("Stop Play mode before applying the home screen.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        var scene = EditorSceneManager.OpenScene("Assets/ChessRoyale/Scenes/MainMenu.unity");
        Canvas canvas = Object.FindAnyObjectByType<Canvas>();
        MainMenu menu = Object.FindAnyObjectByType<MainMenu>();
        if (canvas == null || menu == null)
            throw new System.InvalidOperationException("MainMenu needs its Canvas and MenuManager.");

        var importer = (TextureImporter)AssetImporter.GetAtPath(Home + "PalaceBackground.png");
        importer.textureType = TextureImporterType.Default;
        importer.maxTextureSize = 2048;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(Home + "PalaceSerif.asset");
        if (font == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>(Home + "CormorantGaramond-Medium.ttf");
            font = TMP_FontAsset.CreateFontAsset(source);
            font.name = "Palace Serif";
            AssetDatabase.CreateAsset(font, Home + "PalaceSerif.asset");
            font.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 :.!?\n");
            foreach (Texture2D atlas in font.atlasTextures)
                AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
        }

        for (int i = canvas.transform.childCount - 1; i >= 0; --i)
            Object.DestroyImmediate(canvas.transform.GetChild(i).gameObject);
        RectTransform canvasRect = (RectTransform)canvas.transform;
        canvasRect.sizeDelta = new Vector2(1920, 1080);
        canvasRect.localScale = Vector3.one * 0.001f;
        if (!canvas.TryGetComponent<MainMenuPresentation>(out _))
            canvas.gameObject.AddComponent<MainMenuPresentation>();

        // An opaque warm surround letterboxes non-16:9 displays without cropping the UI.
        RectTransform surround = Rect("Warm Surround", canvasRect);
        Stretch(surround);
        Image surroundImage = surround.gameObject.AddComponent<Image>();
        surroundImage.color = new Color(0.055f, 0.031f, 0.025f);
        surroundImage.raycastTarget = false;
        RectTransform composition = Rect("Palace Composition", canvasRect);
        composition.sizeDelta = new Vector2(1920, 1080);
        var aspect = composition.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = 16f / 9f;

        RectTransform background = Rect("Palace Background", composition);
        Stretch(background);
        RawImage image = background.gameObject.AddComponent<RawImage>();
        image.texture = AssetDatabase.LoadAssetAtPath<Texture2D>(Home + "PalaceBackground.png");
        image.raycastTarget = false;

        // Anchors follow the artwork proportions as the composition is resized.
        Label(composition, "Title", "CHESS ROYALE", font, new Vector2(0.24f, 0.71f), new Vector2(650, 100), 61, 9);
        Button single = MenuButton(composition, "Single Player", "SINGLE PLAYER", font, 0.555f,
            new Color(0.25f, 0.042f, 0.055f, 0.91f), true, menu.StartSinglePlayer);
        Button multi = MenuButton(composition, "Multiplayer", "MULTIPLAYER", font, 0.429f,
            new Color(0.065f, 0.025f, 0.019f, 0.65f), true, menu.StartMultiplayer);
        Button quit = MenuButton(composition, "Quit", "QUIT", font, 0.305f,
            new Color(0.08f, 0.025f, 0.02f, 0.01f), false, menu.QuitGame);
        Navigation nav = new Navigation { mode = Navigation.Mode.Explicit };
        nav.selectOnDown = multi; nav.selectOnUp = quit; single.navigation = nav;
        nav.selectOnDown = quit; nav.selectOnUp = single; multi.navigation = nav;
        nav.selectOnDown = single; nav.selectOnUp = multi; quit.navigation = nav;
        var events = Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if (events != null) events.firstSelectedGameObject = single.gameObject;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Mature palace home screen applied and saved.");
    }

    [MenuItem("Chess Royale/Validate Home Screen And Enter Single Player")]
    public static void ValidateHomeScreen()
    {
        if (!EditorApplication.isPlaying)
            throw new System.InvalidOperationException("Run the MainMenu scene first.");
        Canvas canvas = Object.FindAnyObjectByType<MainMenuPresentation>().GetComponent<Canvas>();
        Button[] buttons = canvas.GetComponentsInChildren<Button>();
        if (buttons.Length != 3)
            throw new System.InvalidOperationException("Expected three home screen buttons.");
        foreach (Button button in buttons)
        {
            if (!button.IsActive() || !button.IsInteractable() || button.onClick.GetPersistentEventCount() != 1
                || button.onClick.GetPersistentTarget(0) == null)
                throw new System.InvalidOperationException("Invalid home button: " + button.name);
            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            label.ForceMeshUpdate();
            if (label.isTextOverflowing)
                throw new System.InvalidOperationException("Home button text overflow: " + button.name);
        }
        Debug.Log("Home screen validation passed: three active buttons, saved callbacks, no label overflow.");
        foreach (Button button in buttons)
            if (button.onClick.GetPersistentMethodName(0) == "StartSinglePlayer")
            {
                button.onClick.Invoke();
                break;
            }
    }

    private static Button MenuButton(RectTransform parent, string name, string text, TMP_FontAsset font,
        float y, Color fill, bool border, UnityAction callback)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = new Vector2(0.086f, y);
        rect.anchorMax = new Vector2(0.375f, y);
        rect.sizeDelta = new Vector2(0, 91);
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = fill;
        if (border)
        {
            // Separate one-pixel strokes keep edges crisp and do not glow on focus.
            Edge(rect, "Top", new Vector2(0, 1), Vector2.one, new Vector2(0, 1));
            Edge(rect, "Bottom", Vector2.zero, new Vector2(1, 0), new Vector2(0, 1));
            Edge(rect, "Left", Vector2.zero, new Vector2(0, 1), new Vector2(1, 0));
            Edge(rect, "Right", new Vector2(1, 0), Vector2.one, new Vector2(1, 0));
        }
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.38f, 1.23f, 1.12f, 1);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.72f, 0.67f, 0.61f, 1);
        colors.fadeDuration = 0.15f;
        button.colors = colors;
        UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, callback);
        TextMeshProUGUI label = Label(rect, "Label", text, font, new Vector2(0.5f, 0.5f), Vector2.zero, border ? 35 : 31, 5);
        Stretch(label.rectTransform);
        return button;
    }

    private static void Edge(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 size)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = min; rect.anchorMax = max; rect.sizeDelta = size;
        Image edge = rect.gameObject.AddComponent<Image>();
        edge.color = Gold; edge.raycastTarget = false;
    }

    private static TextMeshProUGUI Label(RectTransform parent, string name, string text,
        TMP_FontAsset font, Vector2 anchor, Vector2 size, float fontSize, float spacing)
    {
        RectTransform rect = Rect(name, parent);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = text; label.fontSize = fontSize;
        label.characterSpacing = spacing; label.color = Ivory;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }

    private static RectTransform Rect(string name, Transform parent)
    {
        var obj = new GameObject(name, typeof(RectTransform));
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        return (RectTransform)obj.transform;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
