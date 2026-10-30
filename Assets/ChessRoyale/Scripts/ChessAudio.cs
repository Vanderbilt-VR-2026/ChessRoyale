using System.Collections;
using System.Collections.Generic;
using ChessRoyale.Networking;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    public enum ChessSfx
    {
        Grab,
        Move,
        Capture,
        Check,
        Invalid,
        Promotion,
        Victory,
        Defeat,
        Draw,
        NewGame,
        OpponentJoined,
        UiClick,
    }

    // Plays Chess Royale's background music and sound effects.
    //
    // It creates itself when the game starts (no scene setup needed) and survives scene loads.
    // Clips are loaded by file name from Assets/ChessRoyale/Resources/ChessRoyaleAudio/,
    // so adding or replacing a sound only means dropping a file with the right name in that folder.
    // A missing clip is skipped, so the game still runs with a partial sound set.
    //
    // Game sounds come from watching ChessGameController state (turn, captures, check, outcome)
    // instead of hooking TryMove. That way the opponent's moves in online play, which arrive as
    // Photon snapshots rather than local moves, make the same sounds as local moves.
    [DisallowMultipleComponent]
    public sealed class ChessAudio : MonoBehaviour
    {
        private const string ClipFolder = "ChessRoyaleAudio/";
        private const float FollowUpDelay = 0.3f;
        private const float ButtonScanInterval = 0.5f;
        private const float MusicDuckLevel = 0.25f;

        private static readonly Dictionary<ChessSfx, string> ClipNames = new Dictionary<ChessSfx, string>
        {
            { ChessSfx.Grab, "sfx_grab" },
            { ChessSfx.Move, "sfx_move" },
            { ChessSfx.Capture, "sfx_capture" },
            { ChessSfx.Check, "sfx_check" },
            { ChessSfx.Invalid, "sfx_invalid" },
            { ChessSfx.Promotion, "sfx_promotion" },
            { ChessSfx.Victory, "sfx_victory" },
            { ChessSfx.Defeat, "sfx_defeat" },
            { ChessSfx.Draw, "sfx_draw" },
            { ChessSfx.NewGame, "sfx_new_game" },
            { ChessSfx.OpponentJoined, "sfx_opponent_joined" },
            { ChessSfx.UiClick, "sfx_ui_click" },
        };

        [Range(0f, 1f)] [SerializeField] private float musicVolume = 0.3f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float uiVolume = 0.5f;

        private readonly Dictionary<ChessSfx, AudioClip> clips = new Dictionary<ChessSfx, AudioClip>();
        private readonly HashSet<Button> wiredButtons = new HashSet<Button>();
        private readonly HashSet<XRGrabInteractable> wiredGrabs = new HashSet<XRGrabInteractable>();
        private readonly List<AudioSource> spatialPool = new List<AudioSource>();

        private AudioSource musicSource;
        private AudioSource flatSource;
        private AudioClip gameMusic;
        private AudioClip menuMusic;
        private float nextButtonScan;
        private float musicDuckedUntil;

        private ChessGameController watchedGame;
        private ChessPieceInteractable[] watchedPieces = new ChessPieceInteractable[0];
        private int watchedRegisteredCount = -1;
        private bool resetPending;
        private ChessPieceColor lastColor;
        private ChessGameOutcome lastOutcome;
        private bool lastPromotionPending;
        private int lastCapturedCount;
        private bool lastMatchReady;

        public static ChessAudio Instance { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;

            GameObject host = new GameObject("ChessAudio");
            DontDestroyOnLoad(host);
            host.AddComponent<ChessAudio>();
        }

        // Plays a sound with no position (UI, game results, music cues).
        public static void Play(ChessSfx sfx)
        {
            if (Instance != null)
                Instance.PlayFlat(sfx);
        }

        // Plays a sound from a spot in the world, so a piece sounds like it is where it is.
        public static void PlayAt(ChessSfx sfx, Vector3 position)
        {
            if (Instance != null)
                Instance.PlaySpatial(sfx, position);
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            LoadClips();

            musicSource = gameObject.AddComponent<AudioSource>();
            musicSource.loop = true;
            musicSource.playOnAwake = false;
            musicSource.spatialBlend = 0f;
            musicSource.volume = musicVolume;

            flatSource = gameObject.AddComponent<AudioSource>();
            flatSource.playOnAwake = false;
            flatSource.spatialBlend = 0f;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        private void Update()
        {
            // Result stings can run several seconds, so the music dips under them instead of competing.
            bool ducked = Time.unscaledTime < musicDuckedUntil;
            musicSource.volume = Mathf.MoveTowards(musicSource.volume,
                ducked ? musicVolume * MusicDuckLevel : musicVolume, Time.unscaledDeltaTime);
            UpdateMusic();
            WatchGame();
            WatchLobby();

            if (Time.unscaledTime >= nextButtonScan)
            {
                nextButtonScan = Time.unscaledTime + ButtonScanInterval;
                WireButtons();
                WirePieceGrabs();
            }
        }

        private void LoadClips()
        {
            List<string> missing = new List<string>();
            foreach (KeyValuePair<ChessSfx, string> entry in ClipNames)
            {
                AudioClip clip = Resources.Load<AudioClip>(ClipFolder + entry.Value);
                if (clip != null)
                    clips[entry.Key] = clip;
                else
                    missing.Add(entry.Value);
            }

            gameMusic = Resources.Load<AudioClip>(ClipFolder + "music_game");
            menuMusic = Resources.Load<AudioClip>(ClipFolder + "music_menu");
            if (gameMusic == null)
                missing.Add("music_game");

            if (missing.Count > 0)
                Debug.Log($"ChessAudio: no clip for {string.Join(", ", missing)} in Resources/{ClipFolder}. Those sounds are skipped.");
        }

        // The menu uses music_menu when it exists; everything else, and the menu without its own track, uses music_game.
        private void UpdateMusic()
        {
            bool inMenu = ChessGameController.Instance == null;
            AudioClip wanted = inMenu && menuMusic != null ? menuMusic : gameMusic;
            if (wanted == null || (musicSource.clip == wanted && musicSource.isPlaying))
                return;

            musicSource.clip = wanted;
            musicSource.Play();
        }

        private void WatchGame()
        {
            ChessGameController game = ChessGameController.Instance;
            if (game == null)
            {
                watchedGame = null;
                watchedRegisteredCount = -1;
                return;
            }

            if (game != watchedGame || watchedRegisteredCount != game.RegisteredPieceCount)
            {
                // A new scene or a board still registering pieces: take a baseline without playing anything.
                if (watchedGame != null && watchedGame != game)
                    watchedGame.GameReset -= OnGameReset;
                if (watchedGame != game)
                    game.GameReset += OnGameReset;

                watchedGame = game;
                watchedRegisteredCount = game.RegisteredPieceCount;
                watchedPieces = FindObjectsByType<ChessPieceInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                RecordGameState(game);
                return;
            }

            if (resetPending)
            {
                resetPending = false;
                PlayFlat(ChessSfx.NewGame);
                RecordGameState(game);
                return;
            }

            ChessPieceColor color = game.ActiveColor;
            ChessGameOutcome outcome = game.Outcome;
            bool promotionPending = game.IsPromotionPending;
            int capturedCount = CountCaptured();

            bool wasOver = lastOutcome != ChessGameOutcome.InProgress;
            bool isOver = outcome != ChessGameOutcome.InProgress;
            bool boardReset = (wasOver && !isOver) || capturedCount < lastCapturedCount;
            bool turnPassed = color != lastColor;
            bool promotionOpened = promotionPending && !lastPromotionPending;
            bool promotionFinished = !promotionPending && lastPromotionPending && turnPassed;

            if (boardReset)
            {
                PlayFlat(ChessSfx.NewGame);
            }
            else
            {
                if (promotionFinished)
                    PlayFlat(ChessSfx.Promotion);
                else if (turnPassed || promotionOpened)
                    PlayFlat(capturedCount > lastCapturedCount ? ChessSfx.Capture : ChessSfx.Move);
                else if (!isOver && capturedCount > lastCapturedCount)
                    PlayFlat(ChessSfx.Capture);

                if (isOver && !wasOver)
                    StartCoroutine(PlayLater(ResultSound(outcome)));
                else if (turnPassed && !isOver && IsInCheck(game))
                    StartCoroutine(PlayLater(ChessSfx.Check));
            }

            RecordGameState(game);
        }

        // The RESET pedestal and single-player rematch call ResetGame, which can flip the turn back to
        // White without any capture change; flag it so the next check plays the new-game sound, not a move.
        private void OnGameReset()
        {
            resetPending = true;
        }

        private void RecordGameState(ChessGameController game)
        {
            lastColor = game.ActiveColor;
            lastOutcome = game.Outcome;
            lastPromotionPending = game.IsPromotionPending;
            lastCapturedCount = CountCaptured();
        }

        private int CountCaptured()
        {
            int count = 0;
            foreach (ChessPieceInteractable piece in watchedPieces)
            {
                if (piece != null && piece.IsCaptured)
                    count++;
            }

            return count;
        }

        // The controller adds " — check" to the status line when the side to move is in check.
        private static bool IsInCheck(ChessGameController game)
        {
            string status = game.StatusMessage;
            return !string.IsNullOrEmpty(status) && status.EndsWith("check");
        }

        // Online, the result depends on which seat this headset holds. In single player you move
        // both sides, so any decisive result plays the victory sound.
        private static ChessSfx ResultSound(ChessGameOutcome outcome)
        {
            ChessPieceColor? winner = null;
            switch (outcome)
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
                return ChessSfx.Draw;

            ChessSeat seat = LocalSeat();
            if (seat == ChessSeat.None)
                return ChessSfx.Victory;

            ChessPieceColor local = seat == ChessSeat.Black ? ChessPieceColor.Black : ChessPieceColor.White;
            return local == winner ? ChessSfx.Victory : ChessSfx.Defeat;
        }

        private static ChessSeat LocalSeat()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            if (lobby == null)
                return ChessSeat.None;

            try
            {
                return lobby.LocalSeat;
            }
            catch (System.Exception)
            {
                // The lobby object can be mid-despawn while leaving a room.
                return ChessSeat.None;
            }
        }

        private void WatchLobby()
        {
            ChessFusionLobbyState lobby = ChessFusionLobbyState.Instance;
            bool matchReady = false;
            if (lobby != null)
            {
                try
                {
                    matchReady = lobby.MatchReady;
                }
                catch (System.Exception)
                {
                    matchReady = false;
                }
            }

            if (matchReady && !lastMatchReady)
                PlayFlat(ChessSfx.OpponentJoined);
            lastMatchReady = matchReady;
        }

        // Menus, the room-code keyboard, and the clock's Lobby button all build their buttons in code,
        // some while the game runs, so new buttons are picked up on a short timer.
        private void WireButtons()
        {
            wiredButtons.RemoveWhere(button => button == null);
            foreach (Button button in FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (wiredButtons.Add(button))
                    button.onClick.AddListener(() => PlayFlat(ChessSfx.UiClick));
            }
        }

        private void WirePieceGrabs()
        {
            wiredGrabs.RemoveWhere(grab => grab == null);
            foreach (ChessPieceInteractable piece in watchedPieces)
            {
                if (piece == null)
                    continue;

                XRGrabInteractable grab = piece.GetComponent<XRGrabInteractable>();
                if (grab != null && wiredGrabs.Add(grab))
                {
                    Transform pieceTransform = piece.transform;
                    grab.selectEntered.AddListener(_ => PlaySpatial(ChessSfx.Grab, pieceTransform.position));
                }
            }
        }

        private IEnumerator PlayLater(ChessSfx sfx)
        {
            yield return new WaitForSecondsRealtime(FollowUpDelay);
            PlayFlat(sfx);
        }

        private void PlayFlat(ChessSfx sfx)
        {
            if (!clips.TryGetValue(sfx, out AudioClip clip))
                return;

            if (sfx == ChessSfx.Victory || sfx == ChessSfx.Defeat || sfx == ChessSfx.Draw)
                musicDuckedUntil = Time.unscaledTime + clip.length;
            flatSource.PlayOneShot(clip, sfx == ChessSfx.UiClick ? uiVolume : sfxVolume);
        }

        private void PlaySpatial(ChessSfx sfx, Vector3 position)
        {
            if (!clips.TryGetValue(sfx, out AudioClip clip))
                return;

            AudioSource source = FreeSpatialSource();
            source.transform.position = position;
            source.PlayOneShot(clip, sfxVolume);
        }

        private AudioSource FreeSpatialSource()
        {
            foreach (AudioSource pooled in spatialPool)
            {
                if (!pooled.isPlaying)
                    return pooled;
            }

            GameObject child = new GameObject("ChessAudio Spatial");
            child.transform.SetParent(transform, false);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            // Mostly 3D so pieces sound placed in the hall, with some 2D so far pieces stay audible.
            source.spatialBlend = 0.75f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 40f;
            spatialPool.Add(source);
            return source;
        }
    }
}
