using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Networking
{
    [DisallowMultipleComponent]
    public sealed class ChessFusionRoomManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public const string TestRoomCode = "TEST";

        [SerializeField] private NetworkObject playerPrefab;

        private NetworkRunner runner;
        private bool isStarting;

        public static ChessFusionRoomManager Instance { get; private set; }

        public NetworkRunner Runner => runner;
        public NetworkObject PlayerPrefab => playerPrefab;
        public string CurrentRoomCode { get; private set; }
        public string LastError { get; private set; }
        public bool IsConnected => runner != null && runner.IsRunning;
        public bool IsSharedModeMasterClient => IsConnected && runner.IsSharedModeMasterClient;
        public int PlayerCount => IsConnected ? runner.ActivePlayers.Count() : 0;
        public int MaximumPlayers => ChessNetworkTypes.MaxPlayers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSceneRoomManager()
        {
            if (Instance != null)
                return;

            Scene scene = SceneManager.GetActiveScene();
            if (scene.name != "ChessRoyaleVR")
                return;

            GameObject roomObject = new GameObject("Chess Fusion Room");
            roomObject.AddComponent<ChessFusionRoomManager>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        public Task<bool> CreateRoom(string roomCode)
        {
            return StartRoom(roomCode, createRoom: true);
        }

        public Task<bool> JoinRoom(string roomCode)
        {
            return StartRoom(roomCode, createRoom: false);
        }

        public async Task LeaveRoom()
        {
            NetworkRunner runnerToShutdown = runner;
            runner = null;
            CurrentRoomCode = null;

            if (runnerToShutdown == null)
                return;

            try
            {
                runnerToShutdown.RemoveCallbacks(this);
                if (runnerToShutdown.IsRunning)
                    await runnerToShutdown.Shutdown();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Chess Royale Fusion leave failed: {exception.Message}", this);
            }
            finally
            {
                if (runnerToShutdown != null)
                    Destroy(runnerToShutdown.gameObject);
            }

            Debug.Log("Left Chess Royale Fusion session.", this);
        }

        private async Task<bool> StartRoom(string roomCode, bool createRoom)
        {
            if (isStarting)
            {
                LastError = "A Fusion connection is already starting.";
                Debug.LogWarning(LastError, this);
                return false;
            }

            if (IsConnected)
            {
                LastError = $"Already connected to Fusion room '{CurrentRoomCode}'.";
                Debug.LogWarning(LastError, this);
                return false;
            }

            if (!ChessFusionAppSettings.TryValidate(this, out string appIdError))
            {
                LastError = appIdError;
                return false;
            }

            if (string.IsNullOrWhiteSpace(roomCode))
            {
                LastError = "Fusion room code cannot be empty.";
                Debug.LogError(LastError, this);
                return false;
            }

            roomCode = roomCode.Trim().ToUpperInvariant();
            isStarting = true;
            LastError = null;

            GameObject runnerObject = new GameObject("Chess Royale NetworkRunner");
            try
            {
                runner = runnerObject.AddComponent<NetworkRunner>();
                runner.ProvideInput = true;
                runner.AddCallbacks(this);

                if (transform.parent != null)
                    transform.SetParent(null, true);
                DontDestroyOnLoad(gameObject);
                DontDestroyOnLoad(runnerObject);

                INetworkSceneManager sceneManager = runnerObject.AddComponent<NetworkSceneManagerDefault>();
                INetworkObjectProvider objectProvider = runnerObject.AddComponent<NetworkObjectProviderDefault>();

                Scene activeScene = SceneManager.GetActiveScene();
                NetworkSceneInfo initialScene = new NetworkSceneInfo();
                if (activeScene.buildIndex >= 0)
                {
                    initialScene.AddSceneRef(SceneRef.FromIndex(activeScene.buildIndex), LoadSceneMode.Single);
                }

                StartGameResult result = await runner.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = roomCode,
                    PlayerCount = MaximumPlayers,
                    EnableClientSessionCreation = createRoom,
                    Scene = initialScene,
                    SceneManager = sceneManager,
                    ObjectProvider = objectProvider,
                });

                if (this == null)
                    return false;

                if (!result.Ok)
                {
                    LastError = DescribeStartFailure(createRoom, roomCode, result.ShutdownReason);
                    Debug.LogError(LastError, this);
                    await CleanupFailedConnection(runnerObject);
                    return false;
                }

                if (PlayerCount > MaximumPlayers)
                {
                    LastError = $"Room '{roomCode}' already has {MaximumPlayers} players.";
                    Debug.LogError(LastError, this);
                    await LeaveRoom();
                    return false;
                }

                CurrentRoomCode = roomCode;
                Debug.Log(
                    $"Connected to Chess Royale Fusion Shared Mode room '{roomCode}' as " +
                    $"{(IsSharedModeMasterClient ? "master client" : "client")} " +
                    $"({PlayerCount}/{MaximumPlayers}).",
                    this);
                return true;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogException(exception, this);
                await CleanupFailedConnection(runnerObject);
                return false;
            }
            finally
            {
                isStarting = false;
            }
        }

        private async Task CleanupFailedConnection(GameObject runnerObject)
        {
            NetworkRunner failedRunner = runner;
            runner = null;
            CurrentRoomCode = null;

            if (failedRunner != null)
            {
                failedRunner.RemoveCallbacks(this);
                if (failedRunner.IsRunning)
                {
                    try
                    {
                        await failedRunner.Shutdown();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"Fusion runner cleanup failed: {exception.Message}", this);
                    }
                }
            }

            if (runnerObject != null)
                Destroy(runnerObject);
        }

        private static string DescribeStartFailure(bool createRoom, string roomCode, ShutdownReason reason)
        {
            string action = createRoom ? "create" : "join";
            switch (reason)
            {
                case ShutdownReason.GameIsFull:
                    return $"Room '{roomCode}' already has {ChessNetworkTypes.MaxPlayers} players. A third player cannot join.";
                case ShutdownReason.GameNotFound:
                    return $"No Fusion room named '{roomCode}' was found.";
                case ShutdownReason.GameIdAlreadyExists:
                    return $"Fusion room '{roomCode}' already exists. Join it instead of creating it.";
                case ShutdownReason.InvalidAuthentication:
                    return "Photon rejected the Chess Royale Fusion App ID. Check Photon App Settings and use the same App ID in both builds.";
                default:
                    return $"Could not {action} Fusion Shared Mode room '{roomCode}': {reason}.";
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (runner != null)
            {
                runner.RemoveCallbacks(this);
                if (runner.IsRunning)
                    _ = runner.Shutdown();
            }
        }

        public void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
        {
            if (callbackRunner != runner)
                return;

            Debug.Log($"Fusion player {player} joined room '{CurrentRoomCode}' ({PlayerCount}/{MaximumPlayers}).", this);
            if (player == callbackRunner.LocalPlayer && PlayerCount > MaximumPlayers)
            {
                LastError = $"Room '{CurrentRoomCode}' already has {MaximumPlayers} players.";
                Debug.LogError(LastError, this);
                _ = LeaveRoom();
            }
        }

        public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
        {
            if (callbackRunner != runner)
                return;

            Debug.Log($"Fusion player {player} left room '{CurrentRoomCode}' ({PlayerCount}/{MaximumPlayers}).", this);
        }

        public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
        {
            if (callbackRunner != runner)
                return;

            runner = null;
            string roomCode = CurrentRoomCode;
            CurrentRoomCode = null;
            if (shutdownReason != ShutdownReason.Ok)
            {
                LastError = $"Fusion room '{roomCode}' shut down: {shutdownReason}.";
                Debug.LogWarning(LastError, this);
            }
        }

        public void OnConnectedToServer(NetworkRunner callbackRunner) { }
        public void OnDisconnectedFromServer(NetworkRunner callbackRunner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner callbackRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner callbackRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnSessionListUpdated(NetworkRunner callbackRunner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner callbackRunner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner callbackRunner, HostMigrationToken hostMigrationToken) { }
        public void OnReliableDataReceived(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner callbackRunner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadStart(NetworkRunner callbackRunner) { }
        public void OnSceneLoadDone(NetworkRunner callbackRunner) { }
        public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
        public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }

        [ContextMenu("Fusion/Create Test Room")]
        private void CreateTestRoom()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("Enter Play Mode before creating a Chess Royale Fusion room.", this);
                return;
            }

            _ = CreateRoom(TestRoomCode);
        }

        [ContextMenu("Fusion/Join Test Room")]
        private void JoinTestRoom()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("Enter Play Mode before joining a Chess Royale Fusion room.", this);
                return;
            }

            _ = JoinRoom(TestRoomCode);
        }

        [ContextMenu("Fusion/Leave Room")]
        private void LeaveTestRoom()
        {
            if (!Application.isPlaying)
            {
                Debug.LogError("Enter Play Mode before leaving a Chess Royale Fusion room.", this);
                return;
            }

            _ = LeaveRoom();
        }
    }
}
