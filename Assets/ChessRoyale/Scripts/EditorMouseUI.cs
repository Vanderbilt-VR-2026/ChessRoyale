#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace ChessRoyale
{
    // Editor-only testing aid: our world-space menus only listen for controller rays, so in Play Mode
    // without a headset they can't be clicked. This adds a mouse raycaster next to every XR raycaster,
    // so the main menu, lobby keyboard, and clock buttons work with the mouse in the Game view.
    // Lets one person test multiplayer with the Quest as one player and the editor as the other.
    // Not compiled into builds.
    public sealed class EditorMouseUI : MonoBehaviour
    {
        private float nextScan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            GameObject host = new GameObject("EditorMouseUI (editor only)");
            DontDestroyOnLoad(host);
            host.AddComponent<EditorMouseUI>();
        }

        private void Update()
        {
            // Menus are built while the game runs, so keep checking twice a second.
            if (Time.unscaledTime < nextScan)
                return;
            nextScan = Time.unscaledTime + 0.5f;

            foreach (TrackedDeviceGraphicRaycaster xrRaycaster in FindObjectsByType<TrackedDeviceGraphicRaycaster>(FindObjectsSortMode.None))
            {
                if (xrRaycaster.GetComponent<GraphicRaycaster>() == null)
                    xrRaycaster.gameObject.AddComponent<GraphicRaycaster>();
            }
        }
    }
}
#endif
