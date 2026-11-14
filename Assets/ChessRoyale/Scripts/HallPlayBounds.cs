using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale
{
    /// <summary>
    /// The hall floor, dais, and walls are visual boxes. This gives them colliders as soon as the
    /// chess scene loads, so a player cannot fall through the floor or walk out through the walls
    /// in single player, in the multiplayer lobby, or during a match.
    /// </summary>
    public static class HallPlayBounds
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded += (_, _) => Apply();
            Apply();
        }

        private static void Apply()
        {
            if (SceneManager.GetActiveScene().name != "ChessRoyaleVR")
                return;

            GameObject hall = GameObject.Find("Grand Hall Environment");
            if (hall == null)
                return;

            foreach (Transform child in hall.GetComponentsInChildren<Transform>(true))
            {
                if (!NeedsCollider(child.name) || child.GetComponent<Collider>() != null)
                    continue;
                if (child.GetComponent<MeshFilter>() == null)
                    continue;

                BoxCollider box = child.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = false;
            }
        }

        private static bool NeedsCollider(string objectName)
        {
            return objectName == "Marble Floor"
                || objectName.StartsWith("Dais Step")
                || objectName.StartsWith("Wall ")
                || objectName.Contains("Fence")
                || objectName.Contains("Railing");
        }
    }
}
