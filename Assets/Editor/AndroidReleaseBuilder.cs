using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace JumpingNinjaEditor
{
    public static class AndroidReleaseBuilder
    {
        private const string DefaultOutputPath = "Builds/JumpingNinja-v1.0.5.apk";

        public static void BuildApk()
        {
            string outputPath = GetArgumentValue("-androidOutputPath") ?? DefaultOutputPath;
            outputPath = Path.GetFullPath(outputPath);

            if (PlayerSettings.bundleVersion != "1.0.5" || PlayerSettings.Android.bundleVersionCode != 5)
            {
                throw new InvalidOperationException("Android release version must be v1.0.5 (version code 5).");
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Unity could not switch to the Android build target.");
            }

            EditorUserBuildSettings.buildAppBundle = false;
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                throw new InvalidOperationException("No enabled scenes were found in Build Settings.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? "Builds");
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = outputPath,
                target = BuildTarget.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Android APK build failed: {summary.result}, {summary.totalErrors} errors.");
            }

            Debug.Log($"JUMPING_NINJA_ANDROID_APK_OK path={outputPath} bytes={summary.totalSize}");
        }

        private static string GetArgumentValue(string argumentName)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], argumentName, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }
    }
}
