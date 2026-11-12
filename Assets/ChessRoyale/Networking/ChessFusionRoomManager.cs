using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Fusion;
using Photon.Voice.Fusion;
using Photon.Voice.Unity;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ChessRoyale.Networking
{
    // Chess Royale's Fusion Network Manager. It lives in ChessRoyaleVR as the "Fusion Network Manager"
    // object and follows the class tutorial's structure:
    //
    // - Each Create/Join starts a fresh copy of the "Fusion Network Runner" prefab, which carries the
    //   NetworkRunner, NetworkEvents, the scene manager, and Photon Voice. Runners are single-use, so a
    //   fresh one per attempt is what makes Leave -> Create and automatic reconnects possible.
    // - NetworkEvents report joins, leaves, scene loads, and shutdowns back to this script.
    // - The master client spawns one shared game-state object (seats, board, clock). It is a master
    //   client object, so it survives the room creator leaving.
    // - Each player spawns a lightweight avatar that follows their own scene XR rig.
    [DisallowMultipleComponent]
    public sealed class ChessFusionRoomManager : MonoBehaviour
    {
        public const string TestRoomCode = "TEST";

        [Header("Prefabs (set by Tools/Chess Royale/Configure Photon Fusion)")]
        [SerializeField] private NetworkRunner runnerPrefab;
        [SerializeField] private NetworkObject playerPrefab;
        [SerializeField] private NetworkObject gameStatePrefab;

        [Header("Seats")]
        [SerializeField] private Transform whiteSpawn;
        [SerializeField] private Transform blackSpawn;

        [Header("Reconnect")]
        [Tooltip("Seconds between attempts to rejoin after this headset loses its connection mid-match.")]
        [Min(1f)] [SerializeField] private float reconnectDelaySeconds = 3f;
        [Tooltip("Stop trying after this long. Keep it under the 30-second seat hold in ChessNetworkGameState.")]
        [Min(5f)] [SerializeField] private float reconnectWindowSeconds = 25f;

        private NetworkRunner runner;
        private NetworkObject localPlayer;
        private FusionVoiceClient voiceClient;
        private Recorder voiceRecorder;
        private bool microphoneAllowed;
        private bool isStarting;
        private bool leavingIntentionally;
        private int startGeneration;
        private bool spawnWhenSeated;
        private bool repositionWhenSeated;
        private ChessSeatPreference creatorSeatPreference = ChessSeatPreference.Random;
        private bool wasInReadyMatch;
        private ShutdownReason lastStartFailure;
        private bool rigPoseSaved;
        private Vector3 savedRigPosition;
        private Quaternion savedRigRotation;

        public static ChessFusionRoomManager Instance { get; private set; }

        public NetworkRunner Runner => runner;
        public NetworkObject PlayerPrefab => playerPrefab;
        public string CurrentRoomCode { get; private set; }
        public string LastError { get; private set; }
        public string DisconnectNotice { get; private set; }
        public bool IsReconnecting { get; private set; }
        public string ReconnectStatus { get; private set; }

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

            GameObject roomObject = new GameObject("Fusion Network Manager");
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

            if (ChessFusionAppSettings.HasVoiceAppId())
                StartCoroutine(RequestMicrophonePermission());
        }

        private void LateUpdate()
        {
            if (spawnWhenSeated)
                SpawnLocalPlayer();
            if (repositionWhenSeated)
                TryRepositionLocalRig();
            SyncNetworkPlay();
            UpdateVoiceRecording();
        }

        private void SyncNetworkPlay()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            bool ready = IsConnected && lobby != null && lobby.MatchReady;
            bool seated = IsConnected && lobby != null && lobby.LocalSeat != ChessSeat.None;
            if (IsConnected && lobby != null)
                wasInReadyMatch = lobby.MatchReady && seated;

            ChessRoyale.ChessPieceColor seat = lobby != null && lobby.LocalSeat == ChessSeat.Black
                ? ChessRoyale.ChessPieceColor.Black
                : ChessRoyale.ChessPieceColor.White;

            // While rejoining, stay "in a room" so the board stays locked instead of turning into a local game.
            ChessRoyale.ChessGameController.Instance?.SetNetworkPlay(IsConnected || IsReconnecting, ready, seated, seat);
        }

        public Task<bool> CreateRoom(string roomCode)
        {
            return CreateRoom(roomCode, ChessSeatPreference.Random);
        }

        public Task<bool> CreateRoom(string roomCode, ChessSeatPreference seatPreference)
        {
            creatorSeatPreference = seatPreference;
            return StartRoom(roomCode, createRoom: true);
        }

        public Task<bool> JoinRoom(string roomCode)
        {
            return StartRoom(roomCode, createRoom: false);
        }

        public async Task LeaveRoom()
        {
            startGeneration++;
            NetworkRunner runnerToShutdown = runner;
            leavingIntentionally = true;
            IsReconnecting = false;
            ReconnectStatus = null;
            wasInReadyMatch = false;
            runner = null;
            localPlayer = null;
            spawnWhenSeated = false;
            repositionWhenSeated = false;
            CurrentRoomCode = null;
            DisconnectNotice = null;
            ForgetVoice();

            if (runnerToShutdown == null)
            {
                leavingIntentionally = false;
                RestoreRigPose();
                SyncNetworkPlay();
                return;
            }

            try
            {
                Unhook(runnerToShutdown);
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
                RestoreRigPose();
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

            if (!ChessFusionAppSettings.TryValidate(this, out _))
            {
                // The full setup steps go to the Console; the headset panel gets a short line that fits.
                LastError = "This build has no Photon App ID. See the README's multiplayer setup.";
                return false;
            }

            if (!ChessNetworkTypes.TryNormalizeRoomCode(roomCode, out roomCode, out string codeError))
            {
                LastError = codeError;
                Debug.LogError(LastError, this);
                return false;
            }

            isStarting = true;
            int generation = startGeneration;
            LastError = null;
            lastStartFailure = ShutdownReason.Ok;
            if (!IsReconnecting)
                DisconnectNotice = null;

            NetworkRunner created = CreateRunner(roomCode);
            try
            {
                runner = created;
                runner.ProvideInput = true;

                if (transform.parent != null)
                    transform.SetParent(null, true);
                DontDestroyOnLoad(gameObject);
                DontDestroyOnLoad(created.gameObject);

                Scene activeScene = SceneManager.GetActiveScene();
                NetworkSceneInfo initialScene = new NetworkSceneInfo();
                if (activeScene.buildIndex >= 0)
                    initialScene.AddSceneRef(SceneRef.FromIndex(activeScene.buildIndex), LoadSceneMode.Single);

                StartGameResult result = await created.StartGame(new StartGameArgs
                {
                    GameMode = GameMode.Shared,
                    SessionName = roomCode,
                    PlayerCount = MaximumPlayers,
                    EnableClientSessionCreation = createRoom,
                    Scene = initialScene,
                    SceneManager = created.GetComponent<NetworkSceneManagerDefault>(),
                    ObjectProvider = created.GetComponent<NetworkObjectProviderDefault>(),
                });

                if (this == null || generation != startGeneration)
                    return false;

                if (!result.Ok)
                {
                    lastStartFailure = result.ShutdownReason;
                    LastError = DescribeStartFailure(createRoom, roomCode, result.ShutdownReason);
                    Debug.LogError(LastError, this);
                    await CleanupFailedConnection(created);
                    return false;
                }

                if (PlayerCount > MaximumPlayers)
                {
                    LastError = $"Room '{roomCode}' already has {MaximumPlayers} players.";
                    Debug.LogError(LastError, this);
                    await LeaveRoom();
                    return false;
                }

                // Like the tutorial: wait for the scene manager before spawning anything into the scene.
                while (this != null && runner == created && created.IsRunning && created.IsSceneManagerBusy)
                    await Task.Yield();
                if (this == null || generation != startGeneration || runner != created || !created.IsRunning)
                    return false;

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
                await CleanupFailedConnection(created);
                return false;
            }
            finally
            {
                isStarting = false;
            }
        }

        // A fresh runner from the "Fusion Network Runner" prefab. Without the prefab (setup tool not run
        // yet), it builds a bare runner in code so multiplayer still works, just without voice chat.
        private NetworkRunner CreateRunner(string roomCode)
        {
            NetworkRunner created;
            if (runnerPrefab != null)
            {
                created = Instantiate(runnerPrefab);
            }
            else
            {
                Debug.LogWarning("No Fusion Network Runner prefab assigned; voice chat is off. Run Tools/Chess Royale/Configure Photon Fusion.", this);
                created = new GameObject().AddComponent<NetworkRunner>();
            }

            created.name = $"Fusion Network Runner ({roomCode})";
            if (created.GetComponent<NetworkSceneManagerDefault>() == null)
                created.gameObject.AddComponent<NetworkSceneManagerDefault>();
            if (created.GetComponent<NetworkObjectProviderDefault>() == null)
                created.gameObject.AddComponent<NetworkObjectProviderDefault>();

            NetworkEvents events = created.GetComponent<NetworkEvents>();
            if (events == null)
                events = created.gameObject.AddComponent<NetworkEvents>();
            events.PlayerJoined.AddListener(OnPlayerJoined);
            events.PlayerLeft.AddListener(OnPlayerLeft);
            events.OnSceneLoadDone.AddListener(OnSceneLoadDone);
            events.OnShutdown.AddListener(OnRunnerShutdown);

            SetUpVoice(created);
            return created;
        }

        private void Unhook(NetworkRunner target)
        {
            if (target == null)
                return;

            NetworkEvents events = target.GetComponent<NetworkEvents>();
            if (events == null)
                return;

            events.PlayerJoined.RemoveListener(OnPlayerJoined);
            events.PlayerLeft.RemoveListener(OnPlayerLeft);
            events.OnSceneLoadDone.RemoveListener(OnSceneLoadDone);
            events.OnShutdown.RemoveListener(OnRunnerShutdown);
        }

        private async Task CleanupFailedConnection(NetworkRunner failedRunner)
        {
            if (runner == failedRunner)
            {
                runner = null;
                CurrentRoomCode = null;
                ForgetVoice();
            }

            if (failedRunner == null)
                return;

            Unhook(failedRunner);
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

            if (failedRunner != null)
                Destroy(failedRunner.gameObject);
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
                    return $"No room named '{roomCode}' was found.";
                case ShutdownReason.GameIdAlreadyExists:
                    return $"Room '{roomCode}' already exists. Join it instead of creating it.";
                case ShutdownReason.InvalidAuthentication:
                    return "Photon rejected this build's App ID. Both headsets need the same App ID.";
                default:
                    return $"Could not {action} room '{roomCode}': {reason}.";
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            if (runner != null)
            {
                Unhook(runner);
                if (runner.IsRunning)
                    _ = runner.Shutdown();
            }
        }

        private void OnPlayerJoined(NetworkRunner callbackRunner, PlayerRef player)
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

            if (player != callbackRunner.LocalPlayer)
                DisconnectNotice = null;

            // Only the state authority actually changes seats; everyone else returns immediately.
            ChessFusionLobbyState.Instance?.RefreshAssignments();
            if (player == callbackRunner.LocalPlayer)
                SpawnLocalPlayer();
        }

        private void OnPlayerLeft(NetworkRunner callbackRunner, PlayerRef player)
        {
            if (callbackRunner != runner)
                return;

            Debug.Log($"Fusion player {player} left room '{CurrentRoomCode}' ({PlayerCount}/{MaximumPlayers}).", this);
            if (player == callbackRunner.LocalPlayer)
                return;

            ChessFusionLobbyState.Instance?.RefreshAssignments();
            ChessRoyale.ChessGameController game = ChessRoyale.ChessGameController.Instance;
            bool gameUnderway = game != null
                && game.Outcome == ChessRoyale.ChessGameOutcome.InProgress
                && ChessNetworkGameState.Instance != null
                && ChessNetworkGameState.Instance.MoveSequence > 1;
            DisconnectNotice = gameUnderway
                ? $"The other player dropped. Holding their seat for {ChessNetworkGameState.ReconnectWindowSeconds:0} seconds."
                : "The other player disconnected.";
        }

        private void OnRunnerShutdown(NetworkRunner callbackRunner, ShutdownReason shutdownReason)
        {
            if (callbackRunner != runner)
                return;

            ChessRoyale.ChessGameController game = ChessRoyale.ChessGameController.Instance;
            bool gameInProgress = game != null && game.Outcome == ChessRoyale.ChessGameOutcome.InProgress;
            bool wasInMatch = wasInReadyMatch;
            string roomCode = CurrentRoomCode;

            runner = null;
            localPlayer = null;
            spawnWhenSeated = false;
            wasInReadyMatch = false;
            CurrentRoomCode = null;
            ForgetVoice();

            if (string.IsNullOrEmpty(roomCode))
            {
                // A Create/Join attempt that never finished connecting. StartRoom reports that failure
                // itself; don't show a "room closed" notice or move the rig (this also covers retries).
                SyncNetworkPlay();
                if (callbackRunner != null)
                    Destroy(callbackRunner.gameObject);
                return;
            }

            if (!leavingIntentionally && IsLocalConnectionFailure(shutdownReason) && wasInMatch && gameInProgress && !string.IsNullOrEmpty(roomCode))
            {
                // Our own connection dropped mid-game. The room is still open on Photon's side and the
                // other player is holding our seat, so try to get back in with the same code.
                Debug.LogWarning($"Lost connection to room '{roomCode}' ({shutdownReason}). Trying to rejoin.", this);
                BeginReconnect(roomCode);
            }
            else
            {
                RestoreRigPose();
                if (!leavingIntentionally && IsLocalConnectionFailure(shutdownReason))
                {
                    DisconnectNotice = "Your connection to the room failed.";
                    LastError = $"Fusion room '{roomCode}' shut down: {shutdownReason}.";
                    Debug.LogWarning(LastError, this);
                }
                else if (!leavingIntentionally && wasInMatch && gameInProgress)
                {
                    DisconnectNotice = "The room closed during the game.";
                    LastError = $"Fusion room '{roomCode}' shut down: {shutdownReason}.";
                    Debug.LogWarning(LastError, this);
                }
                else if (!leavingIntentionally && shutdownReason != ShutdownReason.Ok)
                {
                    DisconnectNotice = "The room closed because the other player disconnected.";
                    LastError = $"Fusion room '{roomCode}' shut down: {shutdownReason}.";
                    Debug.LogWarning(LastError, this);
                }
            }

            SyncNetworkPlay();
            if (callbackRunner != null)
                Destroy(callbackRunner.gameObject);
        }

        private async void BeginReconnect(string roomCode)
        {
            int generation = ++startGeneration;
            IsReconnecting = true;
            DisconnectNotice = null;
            float giveUpAt = Time.realtimeSinceStartup + reconnectWindowSeconds;
            int attempt = 0;

            while (true)
            {
                attempt++;
                ReconnectStatus = $"Connection lost. Rejoining room {roomCode} (try {attempt})...";
                await Task.Delay(Mathf.CeilToInt(reconnectDelaySeconds * 1000f));
                if (this == null || !IsReconnecting || generation != startGeneration)
                    return;

                bool rejoined = await StartRoom(roomCode, createRoom: false);
                if (this == null || !IsReconnecting || generation != startGeneration)
                    return;

                if (rejoined)
                {
                    IsReconnecting = false;
                    ReconnectStatus = null;
                    DisconnectNotice = null;
                    Debug.Log($"Rejoined room '{roomCode}' after attempt {attempt}.", this);
                    return;
                }

                if (lastStartFailure == ShutdownReason.GameNotFound || Time.realtimeSinceStartup >= giveUpAt)
                    break;
            }

            IsReconnecting = false;
            ReconnectStatus = null;
            DisconnectNotice = lastStartFailure == ShutdownReason.GameNotFound
                ? "Could not rejoin: the room has closed."
                : "Could not reconnect to the room.";
            RestoreRigPose();
            SyncNetworkPlay();
        }

        private void OnSceneLoadDone(NetworkRunner callbackRunner)
        {
            if (callbackRunner != runner || !IsConnected)
                return;

            SyncNetworkPlay();
            SpawnLobbyState();
            SpawnLocalPlayer();
        }

        // The master client creates the one shared game-state object. The spawn flag keeps it owned by
        // whoever is master, matching the tutorial's lobby controller.
        private void SpawnLobbyState()
        {
            if (runner == null || !runner.IsRunning || !runner.IsSharedModeMasterClient || gameStatePrefab == null)
                return;
            if (runner.IsSceneManagerBusy)
                return;

            if (ChessFusionLobbyState.Instance == null)
            {
                NetworkObject stateObject = runner.Spawn(gameStatePrefab, flags: NetworkSpawnFlags.SharedModeStateAuthMasterClient);
                stateObject?.GetComponent<ChessFusionLobbyState>()?.ConfigureCreator(runner.LocalPlayer, creatorSeatPreference);
            }

            ChessFusionLobbyState.Instance?.RefreshAssignments();
        }

        private void SpawnLocalPlayer()
        {
            if (runner == null || !runner.IsRunning || playerPrefab == null)
                return;
            if (runner.IsSceneManagerBusy)
            {
                spawnWhenSeated = true;
                return;
            }
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
            if (playerPrefab.GetComponentInChildren<XROrigin>(true) != null)
            {
                Debug.LogError("The network player prefab is still the old full XR rig. Run Tools/Chess Royale/Configure Photon Fusion to rebuild it as an avatar.", this);
                return;
            }

            XROrigin origin = MoveRigToSeat(seat == ChessSeat.White);
            repositionWhenSeated = false;
            Vector3 position = origin != null ? origin.transform.position : Vector3.zero;
            Quaternion rotation = origin != null ? origin.transform.rotation : Quaternion.identity;
            localPlayer = runner.Spawn(playerPrefab, position, rotation, runner.LocalPlayer);
            if (localPlayer == null)
            {
                Debug.LogError("Fusion did not spawn the Chess Royale avatar. Run Tools/Chess Royale/Configure Photon Fusion.", this);
                return;
            }

            runner.SetPlayerObject(runner.LocalPlayer, localPlayer);
        }

        public void RepositionLocalRigForAssignedSeat()
        {
            repositionWhenSeated = true;
            TryRepositionLocalRig();
        }

        private void TryRepositionLocalRig()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (!IsConnected || lobby == null || lobby.LocalSeat == ChessSeat.None)
                return;

            MoveRigToSeat(lobby.LocalSeat == ChessSeat.White);
            repositionWhenSeated = false;
        }

        // Puts this headset's own rig at its side of the board. The rig is never networked itself;
        // the avatar spawned next to it is what the other player sees.
        private XROrigin MoveRigToSeat(bool white)
        {
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return null;

            if (!rigPoseSaved)
            {
                savedRigPosition = origin.transform.position;
                savedRigRotation = origin.transform.rotation;
                rigPoseSaved = true;
            }

            GetSpawnPose(white, out Vector3 position, out Quaternion rotation);
            EnsureGroundUnder(position);
            PlaceRig(origin, position, rotation);
            return origin;
        }

        // The seats are past the ends of the board, where the only collider in the hall (the board's
        // teleport platform) stops. Without ground there, the rig's gravity drops players forever.
        // Give the marble floor a collider (it is just a visual box), or failing that, a small pad.
        private static void EnsureGroundUnder(Vector3 position)
        {
            if (HasGroundBelow(position))
                return;

            GameObject floor = GameObject.Find("Marble Floor");
            if (floor != null && floor.GetComponent<Collider>() == null)
            {
                floor.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                if (HasGroundBelow(position))
                    return;
            }

            GameObject pad = new GameObject("Seat Floor (generated)");
            pad.transform.position = position + Vector3.down * 0.05f;
            BoxCollider padCollider = pad.AddComponent<BoxCollider>();
            padCollider.size = new Vector3(4f, 0.1f, 4f);
            Physics.SyncTransforms();
        }

        private static bool HasGroundBelow(Vector3 position)
        {
            return Physics.Raycast(position + Vector3.up * 2f, Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
        }

        // A CharacterController keeps its own idea of where it is and would undo a plain transform move,
        // so switch it off for the jump, sync physics, then switch it back on.
        private static void PlaceRig(XROrigin origin, Vector3 position, Quaternion rotation)
        {
            CharacterController body = origin.GetComponent<CharacterController>();
            bool bodyWasEnabled = body != null && body.enabled;
            if (bodyWasEnabled)
                body.enabled = false;

            origin.transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();

            if (bodyWasEnabled)
                body.enabled = true;
        }

        private void RestoreRigPose()
        {
            if (!rigPoseSaved)
                return;

            rigPoseSaved = false;
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin != null)
                PlaceRig(origin, savedRigPosition, savedRigRotation);
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

        // Voice chat, following the tutorial: the runner prefab carries a FusionVoiceClient and a
        // Recorder. Voice joins its own room alongside the chess room, and the microphone only records
        // once permission is granted and both connections are up. No Voice App ID means no voice.
        private void SetUpVoice(NetworkRunner created)
        {
            voiceClient = created.GetComponent<FusionVoiceClient>();
            voiceRecorder = created.GetComponent<Recorder>();
            if (voiceClient == null || voiceRecorder == null)
            {
                voiceClient = null;
                voiceRecorder = null;
                return;
            }

            voiceClient.PrimaryRecorder = voiceRecorder;
            voiceClient.AutoConnectAndJoin = ChessFusionAppSettings.HasVoiceAppId();
            voiceRecorder.SourceType = Recorder.InputSourceType.Microphone;
            voiceRecorder.TransmitEnabled = true;
            voiceRecorder.RecordWhenJoined = false;
            voiceRecorder.RecordingEnabled = false;
        }

        private void UpdateVoiceRecording()
        {
            if (voiceClient == null || voiceRecorder == null)
                return;

            bool shouldRecord = microphoneAllowed
                && IsConnected
                && voiceClient.AutoConnectAndJoin
                && voiceClient.Client != null
                && voiceClient.Client.InRoom;
            if (voiceRecorder.RecordingEnabled != shouldRecord)
                voiceRecorder.RecordingEnabled = shouldRecord;
        }

        private void ForgetVoice()
        {
            if (voiceRecorder != null)
                voiceRecorder.RecordingEnabled = false;
            voiceClient = null;
            voiceRecorder = null;
        }

        private IEnumerator RequestMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
            if (!microphoneAllowed)
            {
                UnityEngine.Android.PermissionCallbacks callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += permission => { if (this != null) microphoneAllowed = true; };
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone, callbacks);
            }
            yield break;
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            yield return Application.RequestUserAuthorization(UserAuthorization.Microphone);
            microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#else
            microphoneAllowed = true;
            yield break;
#endif
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                return;
#if UNITY_ANDROID && !UNITY_EDITOR
            microphoneAllowed = UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone);
#elif UNITY_IOS || UNITY_VISIONOS || UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            microphoneAllowed = Application.HasUserAuthorization(UserAuthorization.Microphone);
#endif
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
