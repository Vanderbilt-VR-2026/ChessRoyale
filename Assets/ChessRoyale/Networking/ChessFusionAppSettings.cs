using System.Security.Cryptography;
using System.Text;
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

        // Voice chat is optional and uses its own Photon app. An empty Voice App ID turns voice off.
        public static bool HasVoiceAppId()
        {
            if (!PhotonAppSettings.TryGetGlobal(out PhotonAppSettings settings) || settings == null)
                return false;

            return !string.IsNullOrWhiteSpace(settings.AppSettings.AppIdVoice);
        }

        // Safe to include in device logs: the raw Photon App ID is never returned.
        // The short fingerprint lets two builds prove they use the same App ID without exposing it.
        public static string GetDiagnosticSummary()
        {
            if (!PhotonAppSettings.TryGetGlobal(out PhotonAppSettings settings) || settings == null)
                return $"PhotonSettings=missing, UnityVersion='{Application.version}', Platform={Application.platform}";

            Photon.Realtime.AppSettings app = settings.AppSettings;
            string appVersion = string.IsNullOrWhiteSpace(app.AppVersion) ? "<default>" : app.AppVersion;
            string fixedRegion = string.IsNullOrWhiteSpace(app.FixedRegion) ? "<best-region>" : app.FixedRegion;
            return $"AppIdFingerprint={Fingerprint(app.AppIdFusion)}, AppVersion='{appVersion}', " +
                   $"FixedRegion='{fixedRegion}', UnityVersion='{Application.version}', Platform={Application.platform}";
        }

        private static string Fingerprint(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<missing>";

            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()));
                StringBuilder result = new StringBuilder(12);
                for (int i = 0; i < 6; i++)
                    result.Append(hash[i].ToString("x2"));
                return result.ToString();
            }
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
