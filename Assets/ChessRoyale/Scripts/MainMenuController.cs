using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    [RequireComponent(typeof(XRSimpleInteractable))]
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private bool quitButton;
        [SerializeField] private string gameSceneName = "ChessRoyaleVR";
        [SerializeField] private TextMesh statusText;

        private XRSimpleInteractable interactable;
        private bool loading;

        private void Awake()
        {
            interactable = GetComponent<XRSimpleInteractable>();
        }

        private void OnEnable()
        {
            interactable.selectEntered.AddListener(OnSelected);
        }

        private void OnDisable()
        {
            interactable.selectEntered.RemoveListener(OnSelected);
        }

        public void Configure(bool exitsApplication, TextMesh loadingStatus)
        {
            quitButton = exitsApplication;
            statusText = loadingStatus;
        }

        private void OnSelected(SelectEnterEventArgs _)
        {
            if (loading)
                return;

            if (quitButton)
            {
                Application.Quit();
                return;
            }

            StartCoroutine(LoadGame());
        }

        private IEnumerator LoadGame()
        {
            loading = true;
            if (statusText != null)
                statusText.text = "LOADING...";

            AsyncOperation load = SceneManager.LoadSceneAsync(gameSceneName);
            if (load == null)
            {
                loading = false;
                if (statusText != null)
                    statusText.text = "LOAD FAILED";
                yield break;
            }

            while (!load.isDone)
            {
                if (statusText != null)
                    statusText.text = $"LOADING {Mathf.RoundToInt(load.progress * 100f)}%";
                yield return null;
            }
        }
    }
}
