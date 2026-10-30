using UnityEngine;

namespace ChessRoyale
{
    public static class ChessPlayerHands
    {
        private static readonly Color Skin = new Color(0.86f, 0.69f, 0.55f, 1f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallSceneHands()
        {
            Apply(null);
        }

        public static void Apply(Transform root)
        {
            Transform[] transforms = root == null
                ? Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                : root.GetComponentsInChildren<Transform>(true);

            foreach (Transform transform in transforms)
            {
                if (transform == null || !transform.name.Contains("Controller Visual"))
                    continue;
                if (transform.Find("Player Hand") != null)
                    continue;

                bool left = transform.name.Contains("Left");
                Transform hand = BuildHand(transform, left);
                foreach (Renderer renderer in transform.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.transform == hand || renderer.transform.IsChildOf(hand))
                        continue;
                    renderer.enabled = false;
                }
            }
        }

        private static Transform BuildHand(Transform visual, bool left)
        {
            GameObject hand = new GameObject("Player Hand");
            hand.transform.SetParent(visual, false);
            hand.transform.localPosition = Vector3.zero;
            hand.transform.localRotation = Quaternion.identity;
            hand.transform.localScale = Vector3.one;

            AddPart(hand.transform, new Vector3(0f, -0.01f, -0.03f), new Vector3(0.078f, 0.028f, 0.09f));
            float thumb = left ? 0.048f : -0.048f;
            AddPart(hand.transform, new Vector3(thumb, -0.005f, -0.01f), new Vector3(0.02f, 0.02f, 0.04f));
            for (int finger = 0; finger < 4; finger++)
            {
                float x = -0.028f + finger * 0.018f;
                AddPart(hand.transform, new Vector3(x, -0.004f, -0.095f), new Vector3(0.014f, 0.014f, 0.05f));
            }

            return hand.transform;
        }

        private static void AddPart(Transform parent, Vector3 localPosition, Vector3 localScale)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = "Hand Part";
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
                Object.Destroy(collider);

            Renderer renderer = part.GetComponent<Renderer>();
            if (renderer == null)
                return;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader != null)
                renderer.material = new Material(shader) { color = Skin };
        }
    }
}
