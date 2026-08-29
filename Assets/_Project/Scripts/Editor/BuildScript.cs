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

        [MenuItem("Vent/Build Windows x64")]
        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64, "Builds/Windows/Vent.exe");
        }

        /// <summary>
        /// The release version comes from the git tag, handed down as VENT_VERSION by
        /// tools/release.sh and by the CI workflow. Unset (a plain local <c>make build</c>) leaves
        /// whatever is in ProjectSettings alone. ProjectBootstrap never writes bundleVersion, so a
        /// regen cannot stomp what we set here.
        /// </summary>
        private static void ApplyVersion()
        {
            string version = System.Environment.GetEnvironmentVariable("VENT_VERSION");
            if (string.IsNullOrWhiteSpace(version))
            {
                Debug.Log($"[Vent] VENT_VERSION unset; building as {PlayerSettings.bundleVersion}");
                return;
            }

            PlayerSettings.bundleVersion = version.Trim();
            Debug.Log($"[Vent] Building version {PlayerSettings.bundleVersion}");
        }

        private static void Build(BuildTarget target, string location)
        {
            ApplyVersion();

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

            // Building for another platform switches the editor's active target and leaves it
            // there, so every later editor run reimports every platform-dependent asset. That
            // reimport is slow enough to blow the PlayMode smoke-test deadlines on the next run.
            // Put the target back where we found it.
            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;

            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (EditorUserBuildSettings.activeBuildTarget != previous)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildPipeline.GetBuildTargetGroup(previous), previous);
            }

            BuildSummary summary = report.summary;
            Debug.Log($"[Vent] Build {summary.result}: {summary.totalSize / (1024 * 1024)} MB in {summary.totalTime.TotalSeconds:F0}s → {location}");

            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
