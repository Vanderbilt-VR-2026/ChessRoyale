using System;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;

namespace ChessRoyale.Editor
{
    public static class XRStarterAssetsImporter
    {
        public static void Import()
        {
            Sample starterAssets = Sample
                .FindByPackage("com.unity.xr.interaction.toolkit", "3.3.2")
                .FirstOrDefault(sample => string.Equals(sample.displayName, "Starter Assets", StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(starterAssets.displayName))
                throw new InvalidOperationException("XR Interaction Toolkit Starter Assets sample was not found.");

            if (!starterAssets.isImported)
                starterAssets.Import(Sample.ImportOptions.OverridePreviousImports);

            AssetDatabase.Refresh();
            Debug.Log("ChessRoyale: XR Interaction Toolkit Starter Assets are ready.");
        }
    }
}
