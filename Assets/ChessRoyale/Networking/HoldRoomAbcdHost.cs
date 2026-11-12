#if UNITY_EDITOR
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Networking
{
    public static class HoldRoomAbcdHost
    {
        public const string SentinelPath = "/tmp/chessroyale-hold-abcd";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnableMultiplayerBeforeSceneAwake()
        {
            if (!File.Exists(SentinelPath))
                return;

            PropertyInfo property = typeof(MainMenu).GetProperty("LaunchMultiplayer");
            property.GetSetMethod(true).Invoke(null, new object[] { true });
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OpenChessScene()
        {
            if (!File.Exists(SentinelPath))
                return;

            if (SceneManager.GetActiveScene().name == "ChessRoyaleVR")
                _ = CreateRoom();
            else
                SceneManager.LoadScene("ChessRoyaleVR");
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != "ChessRoyaleVR" || !File.Exists(SentinelPath))
                return;

            SceneManager.sceneLoaded -= OnSceneLoaded;
            _ = CreateRoom();
        }

        private static async Task CreateRoom()
        {
            await Task.Delay(500);
            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            if (room == null)
            {
                Debug.LogError("ABCD hold: the Fusion room manager is missing.");
                return;
            }

            bool opened = await room.CreateRoom("ABCD");
            if (!opened)
            {
                Debug.LogError("ABCD hold failed: " + room.LastError);
                return;
            }

            File.Delete(SentinelPath);
            Debug.Log("ABCD hold: room is open. Join ABCD from the headset and leave Play Mode running.");
        }
    }
}
#endif
