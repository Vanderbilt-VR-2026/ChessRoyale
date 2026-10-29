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

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ChessFusionLobbyState : NetworkBehaviour
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

        public void RefreshAssignments()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority || Runner == null)
                return;

            PlayerRef white = Runner.LocalPlayer;
            PlayerRef black = PlayerRef.None;
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (player != white)
                    black = player;
            }

            bool ready = black != PlayerRef.None;
            bool becameReady = ready && !MatchReady;
            if (MatchReady && !ready)
                OpponentLeft = true;
            if (ready)
                OpponentLeft = false;

            WhitePlayer = white;
            BlackPlayer = black;
            MatchReady = ready;
            ChessNetworkGameState state = ChessNetworkGameState.Instance;
            state?.SetSeats(white, black, ready);
            if (becameReady && state != null && state.MoveSequence == 0)
                state.PublishInitialBoard();
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
