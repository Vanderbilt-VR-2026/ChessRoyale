#if UNITY_EDITOR
using ChessRoyale.Networking;
using UnityEditor;
using UnityEngine;

namespace ChessRoyale.Editor
{
    public sealed class ChessNetworkMovePanel : EditorWindow
    {
        private string originCoordinate = "E2";
        private string destinationCoordinate = "E4";
        private string feedback;
        private MessageType feedbackType = MessageType.Info;
        private Vector2 scrollPosition;

        [MenuItem("Chess Royale/Network Move Panel")]
        private static void Open()
        {
            ChessNetworkMovePanel window = GetWindow<ChessNetworkMovePanel>("Network Moves");
            window.minSize = new Vector2(390f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
            EditorGUILayout.LabelField("Chess Royale Network Opponent", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Click a piece and destination, or enter coordinates such as E2 and E4. " +
                "Moves use the normal client request, Fusion authority validation, and replicated snapshot path.",
                MessageType.Info);

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode and connect both players before sending a move.", MessageType.Warning);
                EditorGUILayout.EndScrollView();
                return;
            }

            ChessFusionRoomManager room = ChessFusionRoomManager.Instance;
            ChessNetworkGameState networkState = ChessNetworkGameState.Instance;
            ChessNetworkMoveCoordinator coordinator = ChessNetworkMoveCoordinator.Instance;
            ChessGameController game = ChessGameController.Instance;

            DrawConnectionStatus(room, networkState, coordinator, game, out ChessPieceColor? localSeat);

            EditorGUILayout.Space(8f);
            DrawBoard(game);

            EditorGUILayout.Space(8f);
            DrawMoveControls(room, networkState, coordinator, game, localSeat);

            if (game != null && game.IsPromotionPending)
            {
                EditorGUILayout.Space(8f);
                DrawPromotionControls(coordinator, game, localSeat);
            }

            if (!string.IsNullOrEmpty(feedback))
            {
                EditorGUILayout.Space(6f);
                EditorGUILayout.HelpBox(feedback, feedbackType);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawConnectionStatus(
            ChessFusionRoomManager room,
            ChessNetworkGameState networkState,
            ChessNetworkMoveCoordinator coordinator,
            ChessGameController game,
            out ChessPieceColor? localSeat)
        {
            localSeat = GetLocalSeat(room, networkState);
            string roomName = room != null && room.IsConnected ? room.CurrentRoomCode : "Not connected";
            string players = room != null && room.IsConnected ? $"{room.PlayerCount}/{room.MaximumPlayers}" : "-";
            string seat = localSeat.HasValue ? localSeat.Value.ToString() : "None";

            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Room", roomName);
            EditorGUILayout.LabelField("Players", players);
            EditorGUILayout.LabelField("Editor seat", seat);
            EditorGUILayout.LabelField("Match ready", networkState != null && networkState.MatchReady ? "Yes" : "No");
            EditorGUILayout.LabelField("Request pending", coordinator != null && coordinator.IsWaiting ? "Yes" : "No");

            if (game == null)
                return;

            EditorGUILayout.LabelField("Turn", game.ActiveColor.ToString());
            EditorGUILayout.LabelField("Outcome", game.Outcome.ToString());
            EditorGUILayout.LabelField("Game status", game.StatusMessage ?? string.Empty);
            if (networkState != null)
                EditorGUILayout.LabelField("Move sequence", networkState.MoveSequence.ToString());
        }

        private void DrawBoard(ChessGameController game)
        {
            EditorGUILayout.LabelField("Board", EditorStyles.boldLabel);
            if (game == null || game.RegisteredPieceCount < 32)
            {
                EditorGUILayout.HelpBox("Waiting for the chess board to initialize.", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(24f);
            for (int file = 0; file < 8; file++)
                GUILayout.Label(((char)('A' + file)).ToString(), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(36f));
            EditorGUILayout.EndHorizontal();

            for (int rank = 7; rank >= 0; rank--)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label((rank + 1).ToString(), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(20f));
                for (int file = 0; file < 8; file++)
                {
                    int squareIndex = rank * 8 + file;
                    ChessPieceInteractable piece = FindPieceAt(game, squareIndex);
                    string coordinate = $"{(char)('A' + file)}{rank + 1}";
                    string label = piece == null ? "·" : GetPieceLabel(piece);
                    Color previousColor = GUI.backgroundColor;
                    if (string.Equals(originCoordinate, coordinate, System.StringComparison.OrdinalIgnoreCase))
                        GUI.backgroundColor = new Color(0.35f, 0.75f, 1f);
                    else if (((file + rank) & 1) == 0)
                        GUI.backgroundColor = new Color(0.82f, 0.82f, 0.82f);

                    if (GUILayout.Button(new GUIContent(label, coordinate), GUILayout.Width(36f), GUILayout.Height(30f)))
                        SelectBoardSquare(coordinate, piece != null);
                    GUI.backgroundColor = previousColor;
                }

                GUILayout.Label((rank + 1).ToString(), EditorStyles.centeredGreyMiniLabel, GUILayout.Width(20f));
                EditorGUILayout.EndHorizontal();
            }
        }

        private void DrawMoveControls(
            ChessFusionRoomManager room,
            ChessNetworkGameState networkState,
            ChessNetworkMoveCoordinator coordinator,
            ChessGameController game,
            ChessPieceColor? localSeat)
        {
            EditorGUILayout.LabelField("Move", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            originCoordinate = NormalizeCoordinateField(EditorGUILayout.TextField("From", originCoordinate));
            destinationCoordinate = NormalizeCoordinateField(EditorGUILayout.TextField("To", destinationCoordinate));
            EditorGUILayout.EndHorizontal();

            bool canSubmit = CanSubmitMove(room, networkState, coordinator, game, localSeat, out string reason);
            using (new EditorGUI.DisabledScope(!canSubmit))
            {
                if (GUILayout.Button("Send Move Through Fusion", GUILayout.Height(32f)))
                    SubmitMove(coordinator, game);
            }

            if (!canSubmit && !string.IsNullOrEmpty(reason))
                EditorGUILayout.LabelField(reason, EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawPromotionControls(
            ChessNetworkMoveCoordinator coordinator,
            ChessGameController game,
            ChessPieceColor? localSeat)
        {
            EditorGUILayout.LabelField("Promotion", EditorStyles.boldLabel);
            bool canPromote = coordinator != null
                && coordinator.IsOnline
                && !coordinator.IsWaiting
                && localSeat.HasValue
                && localSeat.Value == game.ActiveColor;

            using (new EditorGUI.DisabledScope(!canPromote))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Queen")) game.PromoteToQueen();
                if (GUILayout.Button("Rook")) game.PromoteToRook();
                if (GUILayout.Button("Bishop")) game.PromoteToBishop();
                if (GUILayout.Button("Knight")) game.PromoteToKnight();
                EditorGUILayout.EndHorizontal();
            }
        }

        private static ChessPieceColor? GetLocalSeat(ChessFusionRoomManager room, ChessNetworkGameState state)
        {
            if (room == null || room.Runner == null || state == null)
                return null;
            if (room.Runner.LocalPlayer == state.WhitePlayer)
                return ChessPieceColor.White;
            if (room.Runner.LocalPlayer == state.BlackPlayer)
                return ChessPieceColor.Black;
            return null;
        }

        private static bool CanSubmitMove(
            ChessFusionRoomManager room,
            ChessNetworkGameState networkState,
            ChessNetworkMoveCoordinator coordinator,
            ChessGameController game,
            ChessPieceColor? localSeat,
            out string reason)
        {
            if (room == null || !room.IsConnected || networkState == null || coordinator == null || !coordinator.IsOnline)
                reason = "Connect the Editor to a Fusion room first.";
            else if (!networkState.MatchReady || room.PlayerCount < 2)
                reason = "Waiting for the second player to join.";
            else if (game == null || game.RegisteredPieceCount < 32)
                reason = "Waiting for the chess board to initialize.";
            else if (!localSeat.HasValue)
                reason = "The Editor does not currently own a player seat.";
            else if (game.Outcome != ChessGameOutcome.InProgress)
                reason = "The game has ended.";
            else if (game.IsPromotionPending)
                reason = "Choose a promotion piece before making another move.";
            else if (coordinator.IsWaiting)
                reason = "Waiting for authority to answer the previous request.";
            else if (game.ActiveColor != localSeat.Value)
                reason = $"Waiting for {game.ActiveColor} to move on the other client.";
            else
            {
                reason = null;
                return true;
            }

            return false;
        }

        private void SubmitMove(ChessNetworkMoveCoordinator coordinator, ChessGameController game)
        {
            if (!TryParseCoordinate(originCoordinate, out int originIndex)
                || !TryParseCoordinate(destinationCoordinate, out int destinationIndex))
            {
                SetFeedback("Use coordinates from A1 through H8.", MessageType.Error);
                return;
            }

            ChessPieceInteractable piece = FindPieceAt(game, originIndex);
            if (piece == null)
            {
                SetFeedback($"There is no piece on {originCoordinate}.", MessageType.Error);
                return;
            }

            ChessBoardSquare origin = game.FindSquareByIndex(originIndex);
            ChessBoardSquare destination = game.FindSquareByIndex(destinationIndex);
            if (origin == null || destination == null)
            {
                SetFeedback("The board squares are not ready.", MessageType.Error);
                return;
            }

            if (!coordinator.BeginLocalGrab(piece))
            {
                SetFeedback($"{originCoordinate} cannot be selected by the Editor player right now.", MessageType.Error);
                return;
            }

            string submittedMove = $"{originCoordinate}-{destinationCoordinate}";
            coordinator.SubmitReleasedMove(piece, origin, destination, moveAllowed: true);
            SetFeedback($"Submitted {submittedMove}. Waiting for Fusion authority.", MessageType.Info);
            originCoordinate = string.Empty;
            destinationCoordinate = string.Empty;
        }

        private void SelectBoardSquare(string coordinate, bool occupied)
        {
            if (string.IsNullOrEmpty(originCoordinate) || (!string.IsNullOrEmpty(destinationCoordinate) && occupied))
            {
                originCoordinate = coordinate;
                destinationCoordinate = string.Empty;
                return;
            }

            if (string.Equals(originCoordinate, coordinate, System.StringComparison.OrdinalIgnoreCase))
            {
                originCoordinate = string.Empty;
                destinationCoordinate = string.Empty;
                return;
            }

            destinationCoordinate = coordinate;
        }

        private static ChessPieceInteractable FindPieceAt(ChessGameController game, int squareIndex)
        {
            if (game == null)
                return null;

            for (byte pieceId = 0; pieceId < 32; pieceId++)
            {
                ChessPieceInteractable piece = game.FindPiece(pieceId);
                if (piece != null && !piece.IsCaptured && ChessSquareIndex.FromSquare(piece.CurrentSquare) == squareIndex)
                    return piece;
            }

            return null;
        }

        private static string GetPieceLabel(ChessPieceInteractable piece)
        {
            char label;
            switch (piece.PieceType)
            {
                case ChessPieceType.Knight: label = 'N'; break;
                case ChessPieceType.Bishop: label = 'B'; break;
                case ChessPieceType.Rook: label = 'R'; break;
                case ChessPieceType.Queen: label = 'Q'; break;
                case ChessPieceType.King: label = 'K'; break;
                default: label = 'P'; break;
            }

            return piece.PieceColor == ChessPieceColor.White
                ? label.ToString()
                : char.ToLowerInvariant(label).ToString();
        }

        private static string NormalizeCoordinateField(string value)
        {
            value = value == null ? string.Empty : value.Trim().ToUpperInvariant();
            return value.Length <= 2 ? value : value.Substring(0, 2);
        }

        private static bool TryParseCoordinate(string value, out int squareIndex)
        {
            value = NormalizeCoordinateField(value);
            if (value.Length == 2
                && value[0] >= 'A' && value[0] <= 'H'
                && value[1] >= '1' && value[1] <= '8')
            {
                int file = value[0] - 'A';
                int rank = value[1] - '1';
                squareIndex = rank * 8 + file;
                return true;
            }

            squareIndex = -1;
            return false;
        }

        private void SetFeedback(string message, MessageType type)
        {
            feedback = message;
            feedbackType = type;
            Repaint();
        }
    }
}
#endif
