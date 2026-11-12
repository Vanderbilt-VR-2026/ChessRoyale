using ChessRoyale.AI;
using UnityEngine;

namespace ChessRoyale
{
    // What the player picked on the single-player setup panel. Shared by the game controller (which side
    // the player may move), the AI player, and ChessAudio (whether a result is a win or a loss).
    public static class ChessSinglePlayerSettings
    {
        private const string ColorKey = "ChessRoyale.SinglePlayer.Color";
        private const string DifficultyKey = "ChessRoyale.SinglePlayer.Difficulty";

        public static ChessPieceColor HumanColor { get; set; } = ChessPieceColor.White;
        public static ChessAIDifficulty Difficulty { get; set; } = ChessAIDifficulty.Medium;

        // False while the setup panel is up: no one moves until the player presses Start.
        public static bool GameStarted { get; set; }

        public static ChessPieceColor ComputerColor =>
            HumanColor == ChessPieceColor.White ? ChessPieceColor.Black : ChessPieceColor.White;

        // Remembers the last choices between sessions, so "Start" is one click next time.
        // Stored as: 0 = White, 1 = Black, 2 = Random.
        public static int SavedColorChoice
        {
            get { return SafeGet(ColorKey, 0); }
            set { SafeSet(ColorKey, value); }
        }

        public static ChessAIDifficulty SavedDifficulty
        {
            get { return (ChessAIDifficulty)Mathf.Clamp(SafeGet(DifficultyKey, (int)ChessAIDifficulty.Medium), 0, 2); }
            set { SafeSet(DifficultyKey, (int)value); }
        }

        private static int SafeGet(string key, int fallback)
        {
            try
            {
                return PlayerPrefs.GetInt(key, fallback);
            }
            catch (System.Exception)
            {
                return fallback;
            }
        }

        private static void SafeSet(string key, int value)
        {
            try
            {
                PlayerPrefs.SetInt(key, value);
                PlayerPrefs.Save();
            }
            catch (System.Exception)
            {
                // Remembering the choice is a convenience; the game works without it.
            }
        }
    }
}
