using System.Collections.Generic;
using ChessRoyale.Networking;

namespace ChessRoyale
{
    public interface IChessModeRules
    {
        bool IsChaos { get; }
        HashSet<ChessBoardSquare> GetLegalMoves(ChessGameController game, ChessPieceInteractable piece);
    }

    public interface IChessInteractionPolicy
    {
        float CarryDurationSeconds { get; }
        float CooldownSeconds(ChessPieceType type);
        float MaximumFollowSpeed(ChessPieceType type);
        bool UsesOriginGhost { get; }
    }

    public static class ChessModePolicies
    {
        private sealed class TurnBasedRules : IChessModeRules
        {
            public bool IsChaos => false;
            public HashSet<ChessBoardSquare> GetLegalMoves(ChessGameController game, ChessPieceInteractable piece)
                => game.GetTurnBasedLegalMovesCore(piece);
        }

        private sealed class ChaosRules : IChessModeRules
        {
            public bool IsChaos => true;
            public HashSet<ChessBoardSquare> GetLegalMoves(ChessGameController game, ChessPieceInteractable piece)
                => game.GetChaosLegalMovesCore(piece);
        }

        private sealed class TurnBasedInteraction : IChessInteractionPolicy
        {
            public float CarryDurationSeconds => 0f;
            public float CooldownSeconds(ChessPieceType type) => 0f;
            public float MaximumFollowSpeed(ChessPieceType type) => 100f;
            public bool UsesOriginGhost => false;
        }

        private sealed class ChaosInteraction : IChessInteractionPolicy
        {
            public float CarryDurationSeconds => 3f;

            public float CooldownSeconds(ChessPieceType type)
            {
                switch (type)
                {
                    case ChessPieceType.Pawn: return 2f;
                    case ChessPieceType.Knight:
                    case ChessPieceType.Bishop: return 3f;
                    case ChessPieceType.Rook: return 4f;
                    case ChessPieceType.Queen: return 6f;
                    case ChessPieceType.King: return 2f;
                    default: return 3f;
                }
            }

            public float MaximumFollowSpeed(ChessPieceType type)
            {
                switch (type)
                {
                    case ChessPieceType.Pawn: return 5.5f;
                    case ChessPieceType.Knight: return 4.8f;
                    case ChessPieceType.Bishop: return 4.1f;
                    case ChessPieceType.Rook: return 3.4f;
                    case ChessPieceType.Queen: return 2.8f;
                    case ChessPieceType.King: return 4.2f;
                    default: return 4f;
                }
            }

            public bool UsesOriginGhost => true;
        }

        private static readonly IChessModeRules TurnRules = new TurnBasedRules();
        private static readonly IChessModeRules ChaosRulesInstance = new ChaosRules();
        private static readonly IChessInteractionPolicy TurnInteraction = new TurnBasedInteraction();
        private static readonly IChessInteractionPolicy ChaosInteractionInstance = new ChaosInteraction();

        public static IChessModeRules Rules(ChessGameplayMode mode)
            => mode == ChessGameplayMode.Chaos ? ChaosRulesInstance : TurnRules;

        public static IChessInteractionPolicy Interaction(ChessGameplayMode mode)
            => mode == ChessGameplayMode.Chaos ? ChaosInteractionInstance : TurnInteraction;
    }

    public struct ChaosResolvedMove
    {
        public byte PieceId;
        public sbyte DestinationSquare;

        public ChaosResolvedMove(byte pieceId, sbyte destinationSquare)
        {
            PieceId = pieceId;
            DestinationSquare = destinationSquare;
        }
    }
}
