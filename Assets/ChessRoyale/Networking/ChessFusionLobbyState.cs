using Fusion;
using UnityEngine;

namespace ChessRoyale.Networking
{
    public enum ChessSeat
    {
        None,
        White,
        Black,
    }

    public enum ChessSeatPreference
    {
        Random,
        White,
        Black,
    }

    // Seats and match readiness for one room. Lives on the shared game-state prefab, which is a
    // master client object: if the room creator leaves, the remaining player takes it over instead of
    // the room losing its state (the tutorial's lobby controller uses the same setup).
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChessFusionLobbyState : NetworkBehaviour, IStateAuthorityChanged
    {
        [Networked] public PlayerRef WhitePlayer { get; set; }
        [Networked] public PlayerRef BlackPlayer { get; set; }
        [Networked] public NetworkBool MatchReady { get; set; }
        [Networked] public NetworkBool OpponentLeft { get; set; }
        [Networked] public PlayerRef CreatorPlayer { get; set; }
        [Networked] public int CreatorSeatPreference { get; set; }
        [Networked] public ChessMatchConfig MatchConfig { get; set; }
        [Networked] public int MatchPhaseValue { get; set; }
        [Networked] public int WhiteAcceptedRevision { get; set; }
        [Networked] public int BlackAcceptedRevision { get; set; }

        public static ChessFusionLobbyState Instance { get; private set; }

        public ChessMatchPhase MatchPhase => (ChessMatchPhase)MatchPhaseValue;
        public bool SeatsFilled => WhitePlayer != PlayerRef.None && BlackPlayer != PlayerRef.None;
        public bool LocalConfigurationAccepted
        {
            get
            {
                if (Runner == null)
                    return false;
                if (Runner.LocalPlayer == WhitePlayer)
                    return WhiteAcceptedRevision == MatchConfig.Revision;
                if (Runner.LocalPlayer == BlackPlayer)
                    return BlackAcceptedRevision == MatchConfig.Revision;
                return false;
            }
        }

        public ChessSeat LocalSeat
        {
            get
            {
                if (Runner == null)
                    return ChessSeat.None;
                if (Runner.LocalPlayer == WhitePlayer)
                    return ChessSeat.White;
                if (Runner.LocalPlayer == BlackPlayer)
                    return ChessSeat.Black;
                return ChessSeat.None;
            }
        }

        public override void Spawned()
        {
            Instance = this;
            if (Object.HasStateAuthority)
                RefreshAssignments();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this)
                Instance = null;
        }

        // Runs when the master role moves to the other player. The new master re-checks the seats,
        // which also starts the reconnect window if the departed player was mid-game.
        public void StateAuthorityChanged()
        {
            if (Object != null && Object.IsValid && Object.HasStateAuthority)
                RefreshAssignments();
        }

        public void RefreshAssignments()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority || Runner == null)
                return;

            CloseIfCreatorLeftBeforeStart();
            if (creatorCloseSent)
                return;

            PlayerRef white = IsActive(WhitePlayer) ? WhitePlayer : PlayerRef.None;
            PlayerRef black = IsActive(BlackPlayer) ? BlackPlayer : PlayerRef.None;

            if (white == PlayerRef.None && black == PlayerRef.None && IsActive(CreatorPlayer))
            {
                if ((ChessSeatPreference)CreatorSeatPreference == ChessSeatPreference.Black)
                    black = CreatorPlayer;
                else
                    white = CreatorPlayer;
            }

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (player == white || player == black)
                    continue;
                if (white == PlayerRef.None)
                    white = player;
                else if (black == PlayerRef.None)
                    black = player;
            }

            PlayerRef previousWhite = WhitePlayer;
            PlayerRef previousBlack = BlackPlayer;
            if (previousWhite != white)
                WhiteAcceptedRevision = 0;
            if (previousBlack != black)
                BlackAcceptedRevision = 0;

            WhitePlayer = white;
            BlackPlayer = black;

            if (MatchPhase == ChessMatchPhase.Configuring && CreatorPlayer != PlayerRef.None)
            {
                if (white == CreatorPlayer)
                    WhiteAcceptedRevision = MatchConfig.Revision;
                if (black == CreatorPlayer)
                    BlackAcceptedRevision = MatchConfig.Revision;
            }

            bool seatsFilled = white != PlayerRef.None && black != PlayerRef.None;
            bool ready = seatsFilled && MatchPhase != ChessMatchPhase.Configuring;
            if (MatchReady && !seatsFilled)
                OpponentLeft = true;
            if (seatsFilled)
                OpponentLeft = false;

            MatchReady = ready;
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            state?.SetSeats(white, black, ready);
            state?.ClearRematchForChangedSeat(previousWhite, white, previousBlack, black);
            state?.UpdateReconnectWindow(white, black, seatsFilled);
            TryStartConfiguredMatch();
        }

        public void ConfigureCreator(PlayerRef creator, ChessSeatPreference preference, ChessMatchConfig config)
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority || MatchReady)
                return;

            if (!config.IsSupported)
            {
                Debug.LogError("The requested Chess Royale match configuration is not supported by this build.", this);
                return;
            }

            if (preference == ChessSeatPreference.Random)
                preference = Random.value < 0.5f ? ChessSeatPreference.White : ChessSeatPreference.Black;

            config.Revision = Mathf.Max(1, config.Revision);
            MatchConfig = config;
            MatchPhaseValue = (int)ChessMatchPhase.Configuring;
            WhiteAcceptedRevision = 0;
            BlackAcceptedRevision = 0;
            CreatorPlayer = creator;
            CreatorSeatPreference = (int)preference;
            if (WhitePlayer == creator)
                WhitePlayer = PlayerRef.None;
            if (BlackPlayer == creator)
                BlackPlayer = PlayerRef.None;
            RefreshAssignments();
        }

        public void RequestAcceptConfiguration()
        {
            RPC_AcceptConfiguration(MatchConfig.Revision);
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_AcceptConfiguration(int acceptedRevision, RpcInfo info = default)
        {
            if (MatchPhase != ChessMatchPhase.Configuring || acceptedRevision != MatchConfig.Revision)
                return;

            if (info.Source == WhitePlayer)
                WhiteAcceptedRevision = acceptedRevision;
            else if (info.Source == BlackPlayer)
                BlackAcceptedRevision = acceptedRevision;
            else
                return;

            TryStartConfiguredMatch();
        }

        private void TryStartConfiguredMatch()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority ||
                MatchPhase != ChessMatchPhase.Configuring || !SeatsFilled || !MatchConfig.IsSupported ||
                WhiteAcceptedRevision != MatchConfig.Revision || BlackAcceptedRevision != MatchConfig.Revision)
                return;

            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            if (state == null || !state.BeginConfiguredMatch(MatchConfig))
                return;

            MatchPhaseValue = (int)ChessMatchPhase.Playing;
            MatchReady = true;
            state.SetSeats(WhitePlayer, BlackPlayer, true);
        }

        public void MarkMatchEnded()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority || MatchPhase != ChessMatchPhase.Playing)
                return;

            MatchPhaseValue = (int)ChessMatchPhase.Ended;
        }

        public void MarkRematchPlaying()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority)
                return;

            MatchPhaseValue = (int)ChessMatchPhase.Playing;
            MatchReady = SeatsFilled;
        }

        private bool IsActive(PlayerRef player)
        {
            if (player == PlayerRef.None || Runner == null)
                return false;

            foreach (PlayerRef active in Runner.ActivePlayers)
            {
                if (active == player)
                    return true;
            }

            return false;
        }

        private bool creatorCloseSent;

        private void CloseIfCreatorLeftBeforeStart()
        {
            if (creatorCloseSent || MatchPhase != ChessMatchPhase.Configuring || CreatorPlayer == PlayerRef.None || IsActive(CreatorPlayer))
                return;

            creatorCloseSent = true;
            RPC_ClosePreMatchRoom();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_ClosePreMatchRoom()
        {
            ChessFusionRoomManager.Instance?.ClosePreMatchRoom();
        }

        public static string SeatLabel(ChessSeat seat)
        {
            switch (seat)
            {
                case ChessSeat.White:
                    return "White";
                case ChessSeat.Black:
                    return "Black";
                default:
                    return "unassigned";
            }
        }
    }
}
