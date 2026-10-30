namespace ChessRoyale.Networking
{
    public static class ChessNetworkTypes
    {
        public const int MaxPlayers = 2;
        public const int RoomCodeLength = 4;

        public static string CreateRoomCode()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            char[] code = new char[RoomCodeLength];
            for (int index = 0; index < code.Length; index++)
                code[index] = alphabet[UnityEngine.Random.Range(0, alphabet.Length)];
            return new string(code);
        }

        public static bool TryNormalizeRoomCode(string value, out string roomCode, out string error)
        {
            roomCode = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToUpperInvariant();
            if (roomCode.Length != RoomCodeLength)
            {
                error = "Room codes are four letters, A through Z.";
                roomCode = null;
                return false;
            }

            for (int index = 0; index < roomCode.Length; index++)
            {
                char letter = roomCode[index];
                if (letter < 'A' || letter > 'Z')
                {
                    error = "Room codes are four letters, A through Z.";
                    roomCode = null;
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
