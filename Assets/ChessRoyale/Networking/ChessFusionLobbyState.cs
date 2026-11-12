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

        public static ChessFusionLobbyState Instance { get; private set; }

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

            PlayerRef white = IsActive(WhitePlayer) ? WhitePlayer : PlayerRef.None;
            PlayerRef black = IsActive(BlackPlayer) ? BlackPlayer : PlayerRef.None;
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (player == white || player == black)
                    continue;
                if (white == PlayerRef.None)
                    white = player;
                else if (black == PlayerRef.None)
                    black = player;
            }

            bool ready = white != PlayerRef.None && black != PlayerRef.None;
            bool becameReady = ready && !MatchReady;
            if (MatchReady && !ready)
                OpponentLeft = true;
            if (ready)
                OpponentLeft = false;

            PlayerRef previousWhite = WhitePlayer;
            PlayerRef previousBlack = BlackPlayer;
            WhitePlayer = white;
            BlackPlayer = black;
            MatchReady = ready;
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            state?.SetSeats(white, black, ready);
            state?.ClearRematchForChangedSeat(previousWhite, white, previousBlack, black);
            state?.UpdateReconnectWindow(white, black, ready);
            if (becameReady && state != null && state.MoveSequence == 0)
                state.PublishInitialBoard();
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
