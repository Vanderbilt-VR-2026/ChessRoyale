using Fusion.Photon.Realtime;
using UnityEngine;

namespace ChessRoyale.Networking
{
    public static class ChessFusionAppSettings
    {
        public const string SettingsAssetPath = "Assets/Photon/Fusion/Resources/PhotonAppSettings.asset";

        public const string MissingAppIdMessage =
            "Chess Royale Photon Fusion App ID is missing. " +
            "1. Sign in to the Photon Dashboard. " +
            "2. Create a Fusion application for Chess Royale. " +
            "3. Copy its Fusion App ID. " +
            "4. Enter it in Chess Royale Photon App Settings at " + SettingsAssetPath + " (App Id Fusion). " +
            "5. Use that same App ID in both Quest builds.";

        public static bool HasFusionAppId()
        {
            if (!PhotonAppSettings.TryGetGlobal(out PhotonAppSettings settings) || settings == null)
                return false;

            return !string.IsNullOrWhiteSpace(settings.AppSettings.AppIdFusion);
        }

        public static bool TryValidate(Object context, out string error)
        {
            if (HasFusionAppId())
            {
                error = null;
                return true;
            }

            error = MissingAppIdMessage;
            Debug.LogError(error, context);
            return false;
        }
    }
}
