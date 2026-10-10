using NUnit.Framework;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale.Tests
{
    public sealed class PiecePlacementTests
    {
        private GameObject root;
        private ChessBoardController board;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("Board");
            board = root.AddComponent<ChessBoardController>();
            CreateSquare(0, 0, Vector3.zero);
            CreateSquare(1, 0, new Vector3(2f, 0f, 0f));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(root);
        }

        [Test]
        public void OpposingPiece_IsCapturedWhenPlacedOnItsSquare()
        {
            ChessPieceInteractable white = CreatePiece("White", ChessSide.White, 0, 0);
            ChessPieceInteractable black = CreatePiece("Black", ChessSide.Black, 1, 0);
            board.RebuildState();

            bool placed = board.TryPlace(white, new Vector3(2f, 1f, 0f));

            Assert.That(placed, Is.True);
            Assert.That(white.State.CurrentSquare, Is.EqualTo(new BoardCoordinate(1, 0)));
            Assert.That(black.State.IsCaptured, Is.True);
            Assert.That(black.gameObject.activeSelf, Is.False);
            Assert.That(board.GetPiece(new BoardCoordinate(1, 0)), Is.SameAs(white.State));
        }

        [Test]
        public void FriendlyOccupiedSquare_RejectsDrop()
        {
            ChessPieceInteractable first = CreatePiece("First", ChessSide.White, 0, 0);
            ChessPieceInteractable second = CreatePiece("Second", ChessSide.White, 1, 0);
            board.RebuildState();

            bool placed = board.TryPlace(first, new Vector3(2f, 1f, 0f));

            Assert.That(placed, Is.False);
            Assert.That(first.State.CurrentSquare, Is.EqualTo(new BoardCoordinate(0, 0)));
            Assert.That(second.State.IsCaptured, Is.False);
        }

        [Test]
        public void OffBoardDrop_IsRejected()
        {
            ChessPieceInteractable piece = CreatePiece("Piece", ChessSide.White, 0, 0);
            board.RebuildState();

            Assert.That(board.TryPlace(piece, new Vector3(50f, 1f, 50f)), Is.False);
            Assert.That(piece.State.CurrentSquare, Is.EqualTo(new BoardCoordinate(0, 0)));
        }

        [Test]
        public void ResetBoard_RestoresCapturedPiecesAndOccupancy()
        {
            ChessPieceInteractable white = CreatePiece("White", ChessSide.White, 0, 0);
            ChessPieceInteractable black = CreatePiece("Black", ChessSide.Black, 1, 0);
            board.RebuildState();
            board.TryPlace(white, new Vector3(2f, 1f, 0f));

            board.ResetBoard();

            Assert.That(white.State.CurrentSquare, Is.EqualTo(new BoardCoordinate(0, 0)));
            Assert.That(black.State.CurrentSquare, Is.EqualTo(new BoardCoordinate(1, 0)));
            Assert.That(black.gameObject.activeSelf, Is.True);
            Assert.That(board.GetPiece(new BoardCoordinate(0, 0)), Is.SameAs(white.State));
            Assert.That(board.GetPiece(new BoardCoordinate(1, 0)), Is.SameAs(black.State));
        }

        private void CreateSquare(int file, int rank, Vector3 position)
        {
            GameObject squareObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            squareObject.transform.SetParent(root.transform);
            squareObject.transform.position = position;
            squareObject.transform.localScale = new Vector3(2f, 0.1f, 2f);
            squareObject.AddComponent<BoardSquare>().Configure(file, rank);
        }

        private ChessPieceInteractable CreatePiece(string name, ChessSide side, int file, int rank)
        {
            GameObject pieceObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pieceObject.name = name;
            pieceObject.transform.SetParent(root.transform);
            pieceObject.transform.position = new Vector3(file * 2f, 1f, rank * 2f);
            pieceObject.AddComponent<Rigidbody>();
            pieceObject.AddComponent<XRGrabInteractable>();
            pieceObject.AddComponent<ChessPieceState>().Configure(side, ChessPieceType.Pawn, file, rank);
            return pieceObject.AddComponent<ChessPieceInteractable>();
        }
    }
}
