using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace ChessRoyale
{
    // Applies the runtime half of the Quest foveated-rendering configuration.
    // The OpenXR feature and SRP backend are configured in OpenXR Package Settings,
    // but foveation remains off until the active XR display receives a non-zero level.
    public sealed class QuestRenderQuality : MonoBehaviour
    {
        private const float MediumFoveation = 0.5f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            GameObject host = new GameObject(nameof(QuestRenderQuality));
            DontDestroyOnLoad(host);
            host.AddComponent<QuestRenderQuality>();
#endif
        }

        private IEnumerator Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
            while (true)
            {
                displays.Clear();
                SubsystemManager.GetSubsystems(displays);
                foreach (XRDisplaySubsystem display in displays)
                {
                    if (!display.running)
                        continue;

                    // No gaze flag means fixed foveation, which Quest 3 supports without eye tracking.
                    display.foveatedRenderingLevel = MediumFoveation;
                    Destroy(gameObject);
                    yield break;
                }

                yield return null;
            }
#else
            yield break;
#endif
        }
    }
}
