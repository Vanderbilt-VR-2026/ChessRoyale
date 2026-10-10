using System;
using UnityEngine;

namespace ChessRoyale
{
    public enum ChessSide
    {
        White,
        Black
    }

    public sealed class ChessClock : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float startingSeconds = 300f;
        [SerializeField] private bool startOnLoad = true;
        [SerializeField] private TextMesh[] whiteDisplays;
        [SerializeField] private TextMesh[] blackDisplays;

        private float whiteSeconds;
        private float blackSeconds;

        public ChessSide ActiveSide { get; private set; } = ChessSide.White;
        public bool IsRunning { get; private set; }
        public bool HasExpired { get; private set; }
        public float WhiteSeconds => whiteSeconds;
        public float BlackSeconds => blackSeconds;

        public event Action<ChessSide> TimeExpired;

        private void Awake()
        {
            ResetClock();
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        private void Start()
        {
            if (startOnLoad)
                StartClock();
        }

        public void Configure(float seconds, TextMesh[] white, TextMesh[] black)
        {
            startingSeconds = Mathf.Max(1f, seconds);
            whiteDisplays = white;
            blackDisplays = black;
            ResetClock();
        }

        public void StartClock()
        {
            if (!HasExpired)
                IsRunning = true;
        }

        public void StopClock()
        {
            IsRunning = false;
        }

        public void SwitchTurn()
        {
            if (!HasExpired)
                ActiveSide = ActiveSide == ChessSide.White ? ChessSide.Black : ChessSide.White;
        }

        public void ResetClock()
        {
            whiteSeconds = startingSeconds;
            blackSeconds = startingSeconds;
            ActiveSide = ChessSide.White;
            IsRunning = false;
            HasExpired = false;
            RefreshDisplays();
        }

        public void Tick(float deltaTime)
        {
            if (!IsRunning || HasExpired || deltaTime <= 0f)
                return;

            if (ActiveSide == ChessSide.White)
                whiteSeconds = Mathf.Max(0f, whiteSeconds - deltaTime);
            else
                blackSeconds = Mathf.Max(0f, blackSeconds - deltaTime);

            float activeSeconds = ActiveSide == ChessSide.White ? whiteSeconds : blackSeconds;
            if (activeSeconds <= 0f)
            {
                HasExpired = true;
                IsRunning = false;
                TimeExpired?.Invoke(ActiveSide);
            }

            RefreshDisplays();
        }

        private void RefreshDisplays()
        {
            SetDisplays(whiteDisplays, FormatTime(whiteSeconds));
            SetDisplays(blackDisplays, FormatTime(blackSeconds));
        }

        private static void SetDisplays(TextMesh[] displays, string value)
        {
            if (displays == null)
                return;

            foreach (TextMesh display in displays)
            {
                if (display != null)
                    display.text = value;
            }
        }

        public static string FormatTime(float seconds)
        {
            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
        }
    }
}
