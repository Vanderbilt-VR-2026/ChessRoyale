using System;
using System.Threading.Tasks;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace ChessRoyale.AI
{
    // Runs the computer opponent in single player.
    //
    // Added to the game controller automatically in single player. It shows the setup panel (play as White,
    // Black or Random; Easy, Medium or Expert), then on the computer's turn hands a copy of the position to
    // ChessEngine on a background thread, so the headset never stutters while it thinks, and plays the
    // result through the same rules code a human move uses.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ChessGameController))]
    public sealed class ChessAIPlayer : MonoBehaviour
    {
        // Even when the answer is instant, wait a moment so the move doesn't feel jarring.
        private const float MinimumThinkSeconds = 0.7f;
        // Let the player see the final position before the setup panel returns.
        private const float ResultPanelDelaySeconds = 3f;

        private ChessGameController game;
        private ChessEngine engine;
        private ChessAISetupPanel panel;
        private Task<EngineMove> search;
        private int searchGeneration;
        private int generation;
        private ChessAIDifficulty searchDifficulty;
        private float turnStartedAt;
        private ChessPieceColor lastActiveColor;
        private float gameOverAt = -1f;
        private bool rigPoseSaved;
        private Vector3 rigStartPosition;
        private Quaternion rigStartRotation;

        public static ChessAIPlayer Instance { get; private set; }
        public bool IsThinking => search != null && ChessSinglePlayerSettings.GameStarted;

        private void Awake()
        {
            Instance = this;
            game = GetComponent<ChessGameController>();
            engine = new ChessEngine(Environment.TickCount);
            ChessSinglePlayerSettings.GameStarted = false;
        }

        private void OnEnable()
        {
            if (game != null)
                game.GameReset += OnGameReset;
        }

        private void OnDisable()
        {
            if (game != null)
                game.GameReset -= OnGameReset;
            engine?.Cancel();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            engine?.Cancel();
            ChessSinglePlayerSettings.GameStarted = false;
        }

        private void Start()
        {
            panel = ChessAISetupPanel.Create(this);
            panel.Show(null);
            game.RefreshLocalControls();
        }

        // Called by the setup panel. colorChoice: 0 = White, 1 = Black, 2 = Random.
        public void StartGame(int colorChoice, ChessAIDifficulty difficulty)
        {
            ChessSinglePlayerSettings.SavedColorChoice = colorChoice;
            ChessSinglePlayerSettings.SavedDifficulty = difficulty;

            if (colorChoice == 2)
                colorChoice = UnityEngine.Random.value < 0.5f ? 0 : 1;
            ChessSinglePlayerSettings.HumanColor = colorChoice == 1 ? ChessPieceColor.Black : ChessPieceColor.White;
            ChessSinglePlayerSettings.Difficulty = difficulty;
            ChessSinglePlayerSettings.GameStarted = true;

            game.ResetGame(); // Also bumps the generation through OnGameReset.
            FaceBoardFrom(ChessSinglePlayerSettings.HumanColor);
            panel.Hide();
            game.RefreshLocalControls();
            Debug.Log($"Chess Royale single player: you are {ChessSinglePlayerSettings.HumanColor}, computer is {difficulty}.");
        }

        public void ReturnToMainMenu()
        {
            engine.Cancel();
            ChessSinglePlayerSettings.GameStarted = false;
            UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        }

        private void OnGameReset()
        {
            generation++;
            engine.Cancel();
            gameOverAt = -1f;
            turnStartedAt = Time.time;
            lastActiveColor = game.ActiveColor;
        }

        private void Update()
        {
            if (!ChessSinglePlayerSettings.GameStarted || game == null)
                return;

            if (game.Outcome != ChessGameOutcome.InProgress)
            {
                ShowResultWhenReady();
                return;
            }

            if (game.ActiveColor != lastActiveColor)
            {
                lastActiveColor = game.ActiveColor;
                turnStartedAt = Time.time;
            }

            // A search that's still running must finish before another can start: they share one engine.
            if (search != null)
            {
                if (!search.IsCompleted)
                    return;
                if (searchGeneration == generation && Time.time - turnStartedAt < MinimumThinkSeconds)
                    return;

                Task<EngineMove> finished = search;
                search = null;
                PlayResult(finished);
                return;
            }

            if (game.ActiveColor != ChessSinglePlayerSettings.ComputerColor || game.IsPromotionPending ||
                game.RegisteredPieceCount < 32)
                return;

            EnginePosition position = game.ExportEnginePosition();
            searchDifficulty = ChessSinglePlayerSettings.Difficulty;
            searchGeneration = generation;
            ChessEngine searcher = engine;
            ChessAIDifficulty difficulty = searchDifficulty;
            search = Task.Run(() => searcher.FindMove(position, difficulty));
        }

        private void PlayResult(Task<EngineMove> finished)
        {
            if (finished.IsFaulted)
            {
                Debug.LogException(finished.Exception);
                return; // The next frame starts a fresh search.
            }

            // The board was reset (or a new game started) while it was thinking: that answer is stale.
            if (searchGeneration != generation || game.ActiveColor != ChessSinglePlayerSettings.ComputerColor)
                return;

            EngineMove move = finished.Result;
            if (!move.IsValid)
                return;

            if (game.TryPlayEngineMove(move))
                Debug.Log($"Chess Royale AI ({searchDifficulty}) played {move}, looking {engine.DepthReached} moves ahead ({engine.NodesSearched:N0} positions).");
            else
                Debug.LogWarning($"Chess Royale AI move {move} was rejected by the game rules.");
        }

        private void ShowResultWhenReady()
        {
            if (gameOverAt < 0f)
            {
                gameOverAt = Time.time;
                return;
            }

            if (Time.time - gameOverAt < ResultPanelDelaySeconds || panel == null || panel.IsVisible)
                return;

            ChessSinglePlayerSettings.GameStarted = false;
            panel.Show(DescribeResult());
            game.RefreshLocalControls();
        }

        private string DescribeResult()
        {
            ChessPieceColor? winner = null;
            switch (game.Outcome)
            {
                case ChessGameOutcome.WhiteCheckmates:
                case ChessGameOutcome.WhiteWinsOnTime:
                case ChessGameOutcome.WhiteWinsByDisconnect:
                    winner = ChessPieceColor.White;
                    break;
                case ChessGameOutcome.BlackCheckmates:
                case ChessGameOutcome.BlackWinsOnTime:
                case ChessGameOutcome.BlackWinsByDisconnect:
                    winner = ChessPieceColor.Black;
                    break;
            }

            if (winner == null)
                return $"Draw. {game.StatusMessage}.";
            return winner == ChessSinglePlayerSettings.HumanColor
                ? $"You win! {game.StatusMessage}."
                : $"The computer wins. {game.StatusMessage}.";
        }

        // Playing Black means looking at the board from the other side: turn the rig around the board's center.
        private void FaceBoardFrom(ChessPieceColor color)
        {
            XROrigin origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
                return;

            if (!rigPoseSaved)
            {
                rigStartPosition = origin.transform.position;
                rigStartRotation = origin.transform.rotation;
                rigPoseSaved = true;
            }

            Vector3 position = rigStartPosition;
            Quaternion rotation = rigStartRotation;
            if (color == ChessPieceColor.Black)
            {
                Vector3 center = game.BoardCenter;
                Quaternion turn = Quaternion.AngleAxis(180f, Vector3.up);
                Vector3 offset = rigStartPosition - center;
                offset.y = 0f;
                position = new Vector3(center.x, rigStartPosition.y, center.z) + turn * offset;
                rotation = turn * rigStartRotation;
            }

            // A CharacterController would undo a plain transform move, so switch it off for the jump.
            CharacterController body = origin.GetComponent<CharacterController>();
            bool bodyWasEnabled = body != null && body.enabled;
            if (bodyWasEnabled)
                body.enabled = false;
            origin.transform.SetPositionAndRotation(position, rotation);
            Physics.SyncTransforms();
            if (bodyWasEnabled)
                body.enabled = true;
        }
    }
}
