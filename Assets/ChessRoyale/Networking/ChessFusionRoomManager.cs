using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Networking
{
    [DisallowMultipleComponent]
    public sealed class ChessFusionRoomManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public const string TestRoomCode = "TEST";

        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private NetworkObject gameStatePrefab;
        [SerializeField] private Transform whiteSpawn;
        [SerializeField] private Transform blackSpawn;

        private readonly System.Collections.Generic.List<GameObject> hiddenRigs = new System.Collections.Generic.List<GameObject>();
        private NetworkRunner runner;
        private NetworkObject localPlayer;
        private bool isStarting;
        private bool leavingIntentionally;
        private bool spawnWhenSeated;

        public static ChessFusionRoomManager Instance { get; private set; }

        public NetworkRunner Runner => runner;
        public NetworkObject PlayerPrefab => playerPrefab;
        public string CurrentRoomCode { get; private set; }
        public string LastError { get; private set; }
        public string DisconnectNotice { get; private set; }

        public void ClearDisconnectNotice()
        {
            DisconnectNotice = null;
        }
        public bool IsConnected => runner != null && runner.IsRunning;
        public bool IsSharedModeMasterClient => IsConnected && runner.IsSharedModeMasterClient;
        public int PlayerCount => IsConnected ? runner.ActivePlayers.Count() : 0;
        public int MaximumPlayers => ChessNetworkTypes.MaxPlayers;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSceneRoomManager()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!MainMenu.LaunchMultiplayer)
            {
                if (scene.name == "ChessRoyaleVR")
                    DropNetworkingForSinglePlayer();
                return;
            }

            if (Instance != null || scene.name != "ChessRoyaleVR")
                return;

            GameObject roomObject = new GameObject("Chess Fusion Room");
            roomObject.AddComponent<ChessFusionRoomManager>();
        }

        private static void DropNetworkingForSinglePlayer()
        {
            if (Instance != null)
            {
                ChessFusionRoomManager leftover = Instance;
                _ = leftover.LeaveRoom();
                Destroy(leftover.gameObject);
            }

            ChessFusionLobbyUI[] lobbies = FindObjectsByType<ChessFusionLobbyUI>(FindObjectsSortMode.None);
            foreach (ChessFusionLobbyUI lobby in lobbies)
            {
                if (lobby != null)
                    Destroy(lobby.gameObject);
            }

            ChessRoyale.ChessGameController.Instance?.SetNetworkPlay(false, false, false, ChessRoyale.ChessPieceColor.White);
        }

        private void Awake()
        {
            if (!MainMenu.LaunchMultiplayer)
            {
                Destroy(gameObject);
                return;
            }

            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (GetComponent<ChessNetworkMoveCoordinator>() == null)
                gameObject.AddComponent<ChessNetworkMoveCoordinator>();
        }

        private void LateUpdate()
        {
            if (spawnWhenSeated)
                SpawnLocalPlayer();
            SyncNetworkPlay();
        }

        private void SyncNetworkPlay()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            bool ready = IsConnected && lobby != null && lobby.MatchReady;
            bool seated = IsConnected && lobby != null && lobby.LocalSeat != ChessSeat.None;
            ChessRoyale.ChessPieceColor seat = lobby != null && lobby.LocalSeat == ChessSeat.Black
                ? ChessRoyale.ChessPieceColor.Black
                : ChessRoyale.ChessPieceColor.White;
            ChessRoyale.ChessGameController.Instance?.SetNetworkPlay(IsConnected, ready, seated, seat);
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
            leavingIntentionally = true;
            runner = null;
            localPlayer = null;
            spawnWhenSeated = false;
            CurrentRoomCode = null;
            DisconnectNotice = null;

            if (runnerToShutdown == null)
            {
                leavingIntentionally = false;
                RestoreSceneRigs();
                SyncNetworkPlay();
                return;
            }

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
                leavingIntentionally = false;
                RestoreSceneRigs();
                SyncNetworkPlay();
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

            if (!ChessNetworkTypes.TryNormalizeRoomCode(roomCode, out roomCode, out string codeError))
            {
                LastError = codeError;
                Debug.LogError(LastError, this);
                return false;
            }

            isStarting = true;
            LastError = null;
            DisconnectNotice = null;

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
                SyncNetworkPlay();
                SpawnLobbyState();
                SpawnLocalPlayer();
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

        private static bool IsLocalConnectionFailure(ShutdownReason reason)
        {
            switch (reason)
            {
                case ShutdownReason.Error:
                case ShutdownReason.PhotonCloudTimeout:
                case ShutdownReason.ConnectionTimeout:
                case ShutdownReason.ConnectionRefused:
                case ShutdownReason.OperationTimeout:
                case ShutdownReason.InvalidAuthentication:
                case ShutdownReason.CustomAuthenticationFailed:
                case ShutdownReason.AuthenticationTicketExpired:
                    return true;
                default:
                    return false;
            }
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
                return;
            }

            if (callbackRunner.IsSharedModeMasterClient)
                ChessFusionLobbyState.Instance?.RefreshAssignments();
            if (player == callbackRunner.LocalPlayer)
                SpawnLocalPlayer();
        }

        public void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
        {
            if (callbackRunner != runner)
                return;

            Debug.Log($"Fusion player {player} left room '{CurrentRoomCode}' ({PlayerCount}/{MaximumPlayers}).", this);
            if (player != callbackRunner.LocalPlayer)
                RecordOpponentDisconnect(callbackRunner, player);
            if (callbackRunner.IsSharedModeMasterClient)
                ChessFusionLobbyState.Instance?.RefreshAssignments();
        }

        private void RecordOpponentDisconnect(NetworkRunner callbackRunner, PlayerRef player)
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessRoyale.ChessGameController game = ChessRoyale.ChessGameController.Instance;
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            bool gameUnderway = lobby != null
                && lobby.MatchReady
                && game != null
                && game.Outcome == ChessRoyale.ChessGameOutcome.InProgress
                && state != null
                && state.MoveSequence > 1;

            if (gameUnderway && callbackRunner.IsSharedModeMasterClient)
            {
                ChessRoyale.ChessPieceColor disconnected = player == lobby.WhitePlayer
                    ? ChessRoyale.ChessPieceColor.White
                    : ChessRoyale.ChessPieceColor.Black;
                state.PublishDisconnect(disconnected);
                DisconnectNotice = game.StatusMessage;
                return;
            }

            DisconnectNotice = "The other player disconnected.";
        }

        public void OnShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
        {
            if (callbackRunner != runner)
                return;

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessRoyale.ChessGameController game = ChessRoyale.ChessGameController.Instance;
            ChessSeat seat = lobby != null ? lobby.LocalSeat : ChessSeat.None;
            bool gameUnderway = lobby != null
                && lobby.MatchReady
                && game != null
                && game.Outcome == ChessRoyale.ChessGameOutcome.InProgress
                && ChessNetworkGameState.Instance != null
                && ChessNetworkGameState.Instance.MoveSequence > 1;

            runner = null;
            localPlayer = null;
            spawnWhenSeated = false;
            string roomCode = CurrentRoomCode;
            CurrentRoomCode = null;
            RestoreSceneRigs();
            SyncNetworkPlay();
            if (!leavingIntentionally && IsLocalConnectionFailure(shutdownReason))
            {
                DisconnectNotice = "Your connection to the room failed.";
                LastError = $"Fusion room '{roomCode}' shut down: {shutdownReason}.";
                Debug.LogWarning(LastError, this);
            }
            else if (!leavingIntentionally && gameUnderway)
            {
                ChessRoyale.ChessPieceColor disconnected = seat == ChessSeat.Black
                    ? ChessRoyale.ChessPieceColor.White
                    : ChessRoyale.ChessPieceColor.Black;
                game.EndByDisconnect(disconnected);
                DisconnectNotice = game.StatusMessage + " The room is closed.";
            }
            else if (!leavingIntentionally && shutdownReason != ShutdownReason.Ok)
            {
                DisconnectNotice = "The room closed because the other player disconnected.";
                LastError = $"Fusion room '{roomCode}' shut down: {shutdownReason}.";
                Debug.LogWarning(LastError, this);
            }

            if (callbackRunner != null)
                Destroy(callbackRunner.gameObject);
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
        public void OnSceneLoadDone(NetworkRunner callbackRunner)
        {
            if (callbackRunner != runner || !IsConnected)
                return;

            SyncNetworkPlay();
            SpawnLobbyState();
            SpawnLocalPlayer();
        }
        public void OnObjectExitAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner callbackRunner, NetworkObject obj, PlayerRef player) { }
        public void OnInput(NetworkRunner callbackRunner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner callbackRunner, PlayerRef player, NetworkInput input) { }

        private void SpawnLobbyState()
        {
            if (runner == null || !runner.IsRunning || !runner.IsSharedModeMasterClient || gameStatePrefab == null)
                return;

            if (ChessFusionLobbyState.Instance == null)
                runner.Spawn(gameStatePrefab);

            ChessFusionLobbyState.Instance?.RefreshAssignments();
        }

        private void SpawnLocalPlayer()
        {
            if (runner == null || !runner.IsRunning || playerPrefab == null)
                return;
            if (localPlayer != null && localPlayer.IsValid)
            {
                spawnWhenSeated = false;
                return;
            }

            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            ChessSeat seat = lobby != null ? lobby.LocalSeat : ChessSeat.None;
            if (lobby == null || !lobby.MatchReady || seat == ChessSeat.None)
            {
                spawnWhenSeated = true;
                return;
            }

            spawnWhenSeated = false;
            HideSceneRigs();
            GetSpawnPose(seat == ChessSeat.White, out Vector3 position, out Quaternion rotation);
            localPlayer = runner.Spawn(playerPrefab, position, rotation, runner.LocalPlayer);
            if (localPlayer == null)
            {
                Debug.LogError("Fusion did not spawn the Chess Royale player prefab. Run Tools/Chess Royale/Configure Photon Fusion.", this);
            }
        }

        private void GetSpawnPose(bool white, out Vector3 position, out Quaternion rotation)
        {
            Transform spawn = white ? whiteSpawn : blackSpawn;
            if (spawn != null)
            {
                position = spawn.position;
                rotation = spawn.rotation;
                return;
            }

            position = white ? new Vector3(0f, 0.42f, -8f) : new Vector3(0f, 0.42f, 12f);
            rotation = white ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
        }

        private void HideSceneRigs()
        {
            XROrigin[] origins = FindObjectsByType<XROrigin>(FindObjectsSortMode.None);
            foreach (XROrigin origin in origins)
            {
                if (origin.GetComponent<ChessFusionPlayer>() != null)
                    continue;

                hiddenRigs.Add(origin.gameObject);
                origin.gameObject.SetActive(false);
            }
        }

        private void RestoreSceneRigs()
        {
            foreach (GameObject rig in hiddenRigs)
            {
                if (rig != null)
                    rig.SetActive(true);
            }

            hiddenRigs.Clear();
        }

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
