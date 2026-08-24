using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Vent.Core.Utility;

namespace Vent.Editor
{
    /// <summary>Player builds. Menu or headless: <c>-executeMethod Vent.Editor.BuildScript.BuildMacOS</c>.</summary>
    public static class BuildScript
    {
        [MenuItem("Vent/Build macOS")]
        public static void BuildMacOS()
        {
            Build(BuildTarget.StandaloneOSX, "Builds/Vent.app");
        }

        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Vent.exe");
        }

        private static void Build(BuildTarget target, string location)
        {
            var scenes = new string[SceneNames.BuildOrder.Length];
            for (int i = 0; i < scenes.Length; i++)
            {
                scenes[i] = $"{Paths.Scenes}/{SceneNames.BuildOrder[i]}.unity";
            }

            Directory.CreateDirectory(Path.GetDirectoryName(location) ?? "Builds");
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = target,
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[Vent] Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:F0}s → {location}");

            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
