using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace ChessRoyale
{
    [RequireComponent(typeof(ChessBoardController))]
    public sealed class ChessGameController : MonoBehaviour, IChessMoveRules
    {
        [SerializeField] private ChessClock chessClock;
        [SerializeField] private TextMesh statusDisplay;

        private ChessBoardController board;
        private ChessPosition position;
        private List<ChessMove> selectedMoves = new();
        private ChessMove? moveToCommit;
        private PromotionMenu promotionMenu;

        public ChessSide SideToMove => position?.SideToMove ?? ChessSide.White;
        public ChessGameStatus Status => position?.GetStatus() ?? ChessGameStatus.Playing;

        private void Awake()
        {
            board = GetComponent<ChessBoardController>();
            chessClock ??= GetComponentInChildren<ChessClock>(true);
            ResetGame();
        }

        public void Configure(ChessClock clock, TextMesh display)
        {
            chessClock = clock;
            statusDisplay = display;
        }

        public void OnPieceSelected(ChessPieceState piece, BoardCoordinate from)
        {
            board.ClearHighlights();
            selectedMoves.Clear();
            if (piece.Side != position.SideToMove || IsFinished(position.GetStatus()))
                return;

            selectedMoves.AddRange(position.LegalMovesFrom(from));
            foreach (IGrouping<BoardCoordinate, ChessMove> target in selectedMoves.GroupBy(move => move.To))
                board.Highlight(target.Key, target.Any(move => move.IsCapture) ? BoardSquareHighlight.LegalCapture : BoardSquareHighlight.LegalMove);
        }

        public void OnSelectionEnded()
        {
            if (!board.HasPendingMove)
                board.ClearHighlights();
            selectedMoves.Clear();
        }

        public ChessMoveResolution ValidateMove(ChessPieceState piece, BoardCoordinate from, BoardCoordinate to)
        {
            List<ChessMove> choices = position.LegalMovesFrom(from).Where(move => move.To == to).ToList();
            if (piece.Side != position.SideToMove || choices.Count == 0)
                return ChessMoveResolution.Rejected;

            if (choices[0].IsPromotion)
            {
                selectedMoves = choices;
                return ChessMoveResolution.Deferred;
            }

            moveToCommit = choices[0];
            return ChessMoveResolution.Accepted;
        }

        public void OnMoveDeferred(ChessPieceState piece, BoardCoordinate from, BoardCoordinate to)
        {
            promotionMenu = PromotionMenu.Show(this, board.GetSquare(to).transform.position + Vector3.up * 2.8f, piece.Side);
            SetStatus("Choose promotion: Q  R  B  N");
        }

        public void ChoosePromotion(ChessPieceType type)
        {
            ChessMove? choice = selectedMoves.Cast<ChessMove?>().FirstOrDefault(move => move.Value.Promotion == type);
            if (!choice.HasValue)
                return;

            moveToCommit = choice.Value;
            promotionMenu?.Close();
            promotionMenu = null;
            board.CompleteDeferredMove(true);
        }

        public void CancelPromotion()
        {
            promotionMenu?.Close();
            promotionMenu = null;
            board.CompleteDeferredMove(false);
            UpdateStatus();
        }

        public void OnMoveCommitted(ChessPieceState piece, BoardCoordinate from, BoardCoordinate to, ChessPieceState capturedPiece)
        {
            if (!moveToCommit.HasValue)
                return;

            ChessMove move = moveToCommit.Value;
            if ((move.Flags & ChessMoveFlags.EnPassant) != 0)
                board.CaptureAt(new BoardCoordinate(to.File, from.Rank));
            if ((move.Flags & ChessMoveFlags.Castle) != 0)
            {
                int rookFile = to.File == 6 ? 7 : 0;
                int rookTarget = to.File == 6 ? 5 : 3;
                ChessPieceState rook = board.GetPiece(new BoardCoordinate(rookFile, from.Rank));
                if (rook != null)
                    board.MovePieceDirect(rook, new BoardCoordinate(rookTarget, from.Rank));
            }

            position.Apply(move);
            if (move.IsPromotion)
                piece.PromoteTo(move.Promotion);
            moveToCommit = null;

            ChessGameStatus status = position.GetStatus();
            if (status == ChessGameStatus.Check)
            {
                ChessPieceState king = GetKing(position.SideToMove);
                if (king != null)
                    board.Highlight(king.CurrentSquare, BoardSquareHighlight.Check);
            }

            if (IsFinished(status))
                chessClock?.StopClock();
            else
                chessClock?.SwitchTurn();
            UpdateStatus();
        }

        public void OnBoardReset()
        {
            promotionMenu?.Close();
            promotionMenu = null;
            ResetGame();
        }

        private void ResetGame()
        {
            position = ChessPosition.CreateInitial();
            selectedMoves.Clear();
            moveToCommit = null;
            chessClock?.ResetClock();
            chessClock?.StartClock();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            ChessGameStatus status = position.GetStatus();
            string text = status switch
            {
                ChessGameStatus.Playing => $"{position.SideToMove} to move",
                ChessGameStatus.Check => $"{position.SideToMove} is in check",
                ChessGameStatus.Checkmate => $"Checkmate — {Opponent(position.SideToMove)} wins",
                ChessGameStatus.Stalemate => "Draw — stalemate",
                ChessGameStatus.DrawFiftyMove => "Draw — fifty-move rule",
                ChessGameStatus.DrawInsufficientMaterial => "Draw — insufficient material",
                _ => status.ToString()
            };
            SetStatus(text);
        }

        private void SetStatus(string value)
        {
            if (statusDisplay != null)
                statusDisplay.text = value;
        }

        private ChessPieceState GetKing(ChessSide side)
        {
            return GetComponentsInChildren<ChessPieceState>(true)
                .FirstOrDefault(piece => !piece.IsCaptured && piece.Side == side && piece.PieceType == ChessPieceType.King);
        }

        private static bool IsFinished(ChessGameStatus status) => status is ChessGameStatus.Checkmate or ChessGameStatus.Stalemate
            or ChessGameStatus.DrawFiftyMove or ChessGameStatus.DrawInsufficientMaterial;
        private static ChessSide Opponent(ChessSide side) => side == ChessSide.White ? ChessSide.Black : ChessSide.White;
    }

    public sealed class PromotionMenu : MonoBehaviour
    {
        private ChessGameController controller;

        public static PromotionMenu Show(ChessGameController controller, Vector3 position, ChessSide side)
        {
            GameObject root = new("Promotion Choice");
            root.transform.position = position;
            PromotionMenu menu = root.AddComponent<PromotionMenu>();
            menu.controller = controller;

            ChessPieceType[] choices = { ChessPieceType.Queen, ChessPieceType.Rook, ChessPieceType.Bishop, ChessPieceType.Knight };
            for (int index = 0; index < choices.Length; index++)
                menu.CreateChoice(choices[index], index, side);
            return menu;
        }

        public void Close()
        {
            Destroy(gameObject);
        }

        private void CreateChoice(ChessPieceType type, int index, ChessSide side)
        {
            GameObject choice = GameObject.CreatePrimitive(PrimitiveType.Cube);
            choice.name = $"Promote to {type}";
            choice.transform.SetParent(transform, false);
            choice.transform.localPosition = new Vector3((index - 1.5f) * 1.15f, 0f, 0f);
            choice.transform.localScale = new Vector3(0.9f, 0.32f, 0.9f);

            Renderer renderer = choice.GetComponent<Renderer>();
            renderer.material.color = side == ChessSide.White ? new Color(0.12f, 0.7f, 0.9f) : new Color(0.75f, 0.08f, 0.55f);
            XRSimpleInteractable interactable = choice.AddComponent<XRSimpleInteractable>();
            interactable.selectEntered.AddListener(_ => controller.ChoosePromotion(type));

            GameObject labelObject = new("Label");
            labelObject.transform.SetParent(choice.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 0.6f, -0.51f);
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = type.ToString().Substring(0, 1);
            label.anchor = TextAnchor.MiddleCenter;
            label.fontSize = 80;
            label.characterSize = 0.1f;
            label.color = Color.white;
        }
    }
}
