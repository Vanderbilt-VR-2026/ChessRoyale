using Fusion;

namespace ChessRoyale.Networking
{
    public enum ChessGameplayMode : byte
    {
        TurnBased = 0,
        Chaos = 1,
    }

    public enum ChessStartingLayout : byte
    {
        Standard = 0,
        Chess960 = 1,
    }

    public enum ChessMatchPhase : byte
    {
        Configuring = 0,
        Playing = 1,
        Ended = 2,
    }

    public struct ChessMatchConfig : INetworkStruct
    {
        public const byte CurrentSchemaVersion = 1;

        public byte SchemaVersion;
        public byte GameplayMode;
        public byte StartingLayout;
        public NetworkBool ThreeCheckEnabled;
        public NetworkBool PodiumOverviewEnabled;
        public int Revision;

        public static ChessMatchConfig Default => new ChessMatchConfig
        {
            SchemaVersion = CurrentSchemaVersion,
            GameplayMode = (byte)ChessGameplayMode.TurnBased,
            StartingLayout = (byte)ChessStartingLayout.Standard,
            ThreeCheckEnabled = false,
            PodiumOverviewEnabled = true,
            Revision = 1,
        };

        public bool IsSupported => SchemaVersion == CurrentSchemaVersion &&
                                   GameplayMode == (byte)ChessGameplayMode.TurnBased &&
                                   (StartingLayout == (byte)ChessStartingLayout.Standard ||
                                    StartingLayout == (byte)ChessStartingLayout.Chess960);

        public string Summary
        {
            get
            {
                string layout = StartingLayout == (byte)ChessStartingLayout.Chess960 ? "Chess960" : "Standard";
                string checks = ThreeCheckEnabled ? "Three-check on" : "Three-check off";
                string arena = PodiumOverviewEnabled ? "Podium + Overview" : "Ground / no Overview";
                return $"{layout}  |  {checks}\n{arena}";
            }
        }
    }
}
