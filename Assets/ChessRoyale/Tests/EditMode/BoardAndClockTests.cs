using NUnit.Framework;
using UnityEngine;

namespace ChessRoyale.Tests
{
    public sealed class BoardAndClockTests
    {
        [Test]
        public void BoardSquare_Configure_UsesAlgebraicCoordinate()
        {
            GameObject squareObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                BoardSquare square = squareObject.AddComponent<BoardSquare>();
                square.Configure(4, 3);

                Assert.That(square.AlgebraicName, Is.EqualTo("E4"));
                Assert.That(squareObject.name, Is.EqualTo("Square E4"));
            }
            finally
            {
                Object.DestroyImmediate(squareObject);
            }
        }

        [TestCase(-1, 0)]
        [TestCase(8, 0)]
        [TestCase(0, -1)]
        [TestCase(0, 8)]
        public void BoardSquare_Configure_RejectsCoordinatesOutsideBoard(int file, int rank)
        {
            GameObject squareObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                BoardSquare square = squareObject.AddComponent<BoardSquare>();
                Assert.Throws<System.ArgumentOutOfRangeException>(() => square.Configure(file, rank));
            }
            finally
            {
                Object.DestroyImmediate(squareObject);
            }
        }

        [Test]
        public void ChessClock_OnlyCountsDownActiveSide()
        {
            GameObject clockObject = new GameObject("Clock");
            try
            {
                ChessClock clock = clockObject.AddComponent<ChessClock>();
                clock.Configure(60f, null, null);
                clock.StartClock();
                clock.Tick(5f);

                Assert.That(clock.WhiteSeconds, Is.EqualTo(55f));
                Assert.That(clock.BlackSeconds, Is.EqualTo(60f));

                clock.SwitchTurn();
                clock.Tick(3f);
                Assert.That(clock.WhiteSeconds, Is.EqualTo(55f));
                Assert.That(clock.BlackSeconds, Is.EqualTo(57f));
            }
            finally
            {
                Object.DestroyImmediate(clockObject);
            }
        }

        [Test]
        public void ChessClock_StopsAndSignalsWhenTimeExpires()
        {
            GameObject clockObject = new GameObject("Clock");
            try
            {
                ChessClock clock = clockObject.AddComponent<ChessClock>();
                ChessSide? expiredSide = null;
                clock.Configure(2f, null, null);
                clock.TimeExpired += side => expiredSide = side;
                clock.StartClock();
                clock.Tick(3f);

                Assert.That(clock.HasExpired, Is.True);
                Assert.That(clock.IsRunning, Is.False);
                Assert.That(expiredSide, Is.EqualTo(ChessSide.White));
                Assert.That(clock.WhiteSeconds, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(clockObject);
            }
        }

        [TestCase(300f, "05:00")]
        [TestCase(61f, "01:01")]
        [TestCase(0f, "00:00")]
        public void ChessClock_FormatsDisplay(float seconds, string expected)
        {
            Assert.That(ChessClock.FormatTime(seconds), Is.EqualTo(expected));
        }
    }
}
