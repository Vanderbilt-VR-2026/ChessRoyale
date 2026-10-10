using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChessRoyale
{
    public enum ChessMoveResolution
    {
        Rejected,
        Accepted,
        Deferred
    }

    public interface IChessMoveRules
    {
        void OnPieceSelected(ChessPieceState piece, BoardCoordinate from);
        void OnSelectionEnded();
        ChessMoveResolution ValidateMove(ChessPieceState piece, BoardCoordinate from, BoardCoordinate to);
        void OnMoveCommitted(ChessPieceState piece, BoardCoordinate from, BoardCoordinate to, ChessPieceState capturedPiece);
        void OnMoveDeferred(ChessPieceState piece, BoardCoordinate from, BoardCoordinate to);
    }

    public sealed class ChessBoardController : MonoBehaviour
    {
        private readonly Dictionary<BoardCoordinate, BoardSquare> squares = new();
        private readonly Dictionary<BoardCoordinate, ChessPieceState> occupancy = new();
        private readonly List<ChessPieceInteractable> pieces = new();
        private IChessMoveRules rules;
        private PendingMove pendingMove;

        private sealed class PendingMove
        {
            public ChessPieceInteractable Piece;
            public BoardCoordinate From;
            public BoardCoordinate To;
            public ChessPieceState Captured;
        }

        public IEnumerable<BoardSquare> Squares => squares.Values;
        public bool HasPendingMove => pendingMove != null;

        private void Awake()
        {
            RebuildState();
        }

        public void RebuildState()
        {
            squares.Clear();
            occupancy.Clear();
            pieces.Clear();

            foreach (BoardSquare square in GetComponentsInChildren<BoardSquare>(true))
                squares[new BoardCoordinate(square.File, square.Rank)] = square;

            foreach (ChessPieceInteractable piece in GetComponentsInChildren<ChessPieceInteractable>(true))
            {
                pieces.Add(piece);
                piece.SetBoard(this);
                if (!piece.State.IsCaptured)
                    occupancy[piece.State.CurrentSquare] = piece.State;
            }

            rules = GetComponents<MonoBehaviour>().OfType<IChessMoveRules>().FirstOrDefault();
            IgnorePieceCollisions();
        }

        public void BeginMove(ChessPieceInteractable piece)
        {
            if (pendingMove != null)
                return;
            rules?.OnPieceSelected(piece.State, piece.State.CurrentSquare);
        }

        public bool TryPlace(ChessPieceInteractable piece, Vector3 releasePosition)
        {
            if (pendingMove != null || !TryFindClosestSquare(releasePosition, out BoardSquare targetSquare))
                return false;

            BoardCoordinate from = piece.State.CurrentSquare;
            BoardCoordinate to = new(targetSquare.File, targetSquare.Rank);
            if (from == to)
            {
                piece.SnapTo(targetSquare);
                rules?.OnSelectionEnded();
                return true;
            }

            occupancy.TryGetValue(to, out ChessPieceState captured);
            if (captured != null && captured.Side == piece.State.Side)
            {
                rules?.OnSelectionEnded();
                return false;
            }

            ChessMoveResolution resolution = rules?.ValidateMove(piece.State, from, to) ?? ChessMoveResolution.Accepted;
            if (resolution == ChessMoveResolution.Rejected)
            {
                rules?.OnSelectionEnded();
                return false;
            }

            if (resolution == ChessMoveResolution.Deferred)
            {
                pendingMove = new PendingMove { Piece = piece, From = from, To = to, Captured = captured };
                piece.SnapTo(targetSquare);
                SetPieceInteraction(false);
                rules.OnMoveDeferred(piece.State, from, to);
                return true;
            }

            CommitMove(piece, from, to, targetSquare, captured);
            return true;
        }

        public bool CompleteDeferredMove(bool commit)
        {
            if (pendingMove == null)
                return false;

            PendingMove move = pendingMove;
            pendingMove = null;
            if (commit && squares.TryGetValue(move.To, out BoardSquare target))
                CommitMove(move.Piece, move.From, move.To, target, move.Captured);
            else
                move.Piece.SnapTo(GetSquare(move.From));

            SetPieceInteraction(true);
            return true;
        }

        public bool TryFindClosestSquare(Vector3 worldPosition, out BoardSquare square)
        {
            square = null;
            float bestDistance = float.PositiveInfinity;
            foreach (BoardSquare candidate in squares.Values)
            {
                Vector2 delta = new(worldPosition.x - candidate.transform.position.x, worldPosition.z - candidate.transform.position.z);
                float distance = delta.sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    square = candidate;
                }
            }

            // A square is 2m wide. Slightly more than its half-diagonal keeps edge drops usable
            // while still rejecting pieces released clearly outside the platform.
            if (square == null || bestDistance > 2.6f)
            {
                square = null;
                return false;
            }

            return true;
        }

        public BoardSquare GetSquare(BoardCoordinate coordinate)
        {
            squares.TryGetValue(coordinate, out BoardSquare square);
            return square;
        }

        public ChessPieceState GetPiece(BoardCoordinate coordinate)
        {
            occupancy.TryGetValue(coordinate, out ChessPieceState piece);
            return piece;
        }

        public void ResetBoard()
        {
            pendingMove = null;
            occupancy.Clear();
            foreach (ChessPieceInteractable piece in pieces)
            {
                piece.State.ResetState();
                occupancy[piece.State.StartingSquare] = piece.State;
                piece.SnapTo(GetSquare(piece.State.StartingSquare));
            }

            SetPieceInteraction(true);
            ClearHighlights();
            rules?.OnSelectionEnded();
        }

        public void ClearHighlights()
        {
            foreach (BoardSquare square in squares.Values)
                square.SetHighlight(BoardSquareHighlight.None);
        }

        public void Highlight(BoardCoordinate coordinate, BoardSquareHighlight highlight)
        {
            if (squares.TryGetValue(coordinate, out BoardSquare square))
                square.SetHighlight(highlight);
        }

        private void CommitMove(ChessPieceInteractable piece, BoardCoordinate from, BoardCoordinate to, BoardSquare target, ChessPieceState captured)
        {
            occupancy.Remove(from);
            if (captured != null)
            {
                occupancy.Remove(to);
                captured.SetCaptured(true);
            }

            piece.State.MoveTo(to);
            occupancy[to] = piece.State;
            piece.SnapTo(target);
            ClearHighlights();
            rules?.OnMoveCommitted(piece.State, from, to, captured);
            rules?.OnSelectionEnded();
        }

        private void SetPieceInteraction(bool enabled)
        {
            foreach (ChessPieceInteractable piece in pieces)
            {
                if (!piece.State.IsCaptured)
                    piece.SetInteractionEnabled(enabled);
            }
        }

        private void IgnorePieceCollisions()
        {
            Collider[][] colliders = pieces.Select(piece => piece.GetComponentsInChildren<Collider>(true)).ToArray();
            for (int first = 0; first < colliders.Length; first++)
            {
                for (int second = first + 1; second < colliders.Length; second++)
                {
                    foreach (Collider left in colliders[first])
                    foreach (Collider right in colliders[second])
                        Physics.IgnoreCollision(left, right, true);
                }
            }
        }
    }
}
