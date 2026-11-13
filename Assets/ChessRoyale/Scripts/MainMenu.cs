using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    public static bool LaunchMultiplayer { get; private set; }

    private GameObject offlineNotice;

    public void StartSinglePlayer()
    {
        LaunchMultiplayer = false;
        SceneManager.LoadScene("ChessRoyaleVR");
    }

    public void StartMultiplayer()
    {
        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            ShowOfflineNotice();
            return;
        }

        HideOfflineNotice();
        LaunchMultiplayer = true;
        SceneManager.LoadScene("ChessRoyaleVR");
    }

    private void ShowOfflineNotice()
    {
        if (offlineNotice == null)
            offlineNotice = BuildOfflineNotice();
        if (offlineNotice != null)
            offlineNotice.SetActive(true);
    }

    private void HideOfflineNotice()
    {
        if (offlineNotice != null)
            offlineNotice.SetActive(false);
    }

    private GameObject BuildOfflineNotice()
    {
        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
            return null;

        GameObject root = new GameObject("Offline Notice", typeof(RectTransform));
        root.transform.SetParent(canvas.transform, false);
        RectTransform rect = root.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(760f, 420f);
        rect.anchoredPosition = Vector2.zero;

        Image background = root.AddComponent<Image>();
        background.color = new Color(0.05f, 0.05f, 0.07f, 0.96f);

        Text message = CreateText(rect, "Message", new Vector2(0f, 70f), new Vector2(680f, 200f), 36);
        message.text = "Connect to the internet\nto play multiplayer.";

        GameObject buttonObject = new GameObject("OK", typeof(RectTransform));
        buttonObject.transform.SetParent(rect, false);
        RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
        buttonRect.sizeDelta = new Vector2(280f, 80f);
        buttonRect.anchoredPosition = new Vector2(0f, -120f);
        Image buttonImage = buttonObject.AddComponent<Image>();
        buttonImage.color = new Color(0.1f, 0.28f, 0.55f, 1f);
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        button.onClick.AddListener(HideOfflineNotice);
        CreateText(buttonRect, "OK Label", Vector2.zero, new Vector2(280f, 80f), 32).text = "OK";

        return root;
    }

    private static Text CreateText(RectTransform parent, string name, Vector2 position, Vector2 size, int fontSize)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        Text text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    public void StartGame()
    {
        StartSinglePlayer();
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}