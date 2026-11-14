#if UNITY_EDITOR
using System.IO;
using ChessRoyale.Networking;
using UnityEditor;
using UnityEngine;

namespace ChessRoyale.Editor
{
    [InitializeOnLoad]
    public static class HoldRoomAbcd
    {
        static HoldRoomAbcd()
        {
            EditorApplication.delayCall += EnterPlayModeIfRequested;
        }

        private static void EnterPlayModeIfRequested()
        {
            if (!File.Exists(HoldRoomAbcdHost.SentinelPath))
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            Debug.Log("ABCD hold: entering Play Mode to open the Photon room.");
            EditorApplication.EnterPlaymode();
        }
    }
}
#endif
