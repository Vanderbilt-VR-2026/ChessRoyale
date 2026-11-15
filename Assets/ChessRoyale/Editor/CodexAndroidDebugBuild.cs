using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CodexAndroidDebugBuild
{
    private const string OutputPath = "/Users/yfy/Builds/ChessRoyale-Debug.apk";

    [MenuItem("Tools/Chess Royale/Build Debug APK (Temporary)")]
    public static void Build()
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes were found in Editor Build Settings.");

        Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
        bool previousBuildAppBundle = EditorUserBuildSettings.buildAppBundle;
        try
        {
            EditorUserBuildSettings.buildAppBundle = false;
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging,
            });

            BuildSummary summary = report.summary;
            Debug.Log($"[ChessRoyaleBuild] Result={summary.result}, Output='{summary.outputPath}', " +
                      $"Size={summary.totalSize}, Errors={summary.totalErrors}, Warnings={summary.totalWarnings}");
            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android build failed: {summary.result}, errors={summary.totalErrors}.");
        }
        finally
        {
            EditorUserBuildSettings.buildAppBundle = previousBuildAppBundle;
        }
    }
}
