using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace JumpingNinjaEditor
{
    public static class WindowsReleaseBuilder
    {
        private const string DefaultOutputPath = "Builds/JumpingNinja-v1.0.4-Windows/Jumping Ninja.exe";

        public static void Build()
        {
            string outputPath = GetArgumentValue("-windowsOutputPath") ?? DefaultOutputPath;
            outputPath = Path.GetFullPath(outputPath);

            if (PlayerSettings.bundleVersion != "1.0.4")
            {
                throw new InvalidOperationException("Windows release version must be v1.0.4.");
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64)
            {
                throw new InvalidOperationException(
                    "The active build target must be StandaloneWindows64. Pass -buildTarget StandaloneWindows64.");
            }

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
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Windows build failed: {summary.result}, {summary.totalErrors} errors.");
            }

            Debug.Log($"JUMPING_NINJA_WINDOWS_BUILD_OK path={outputPath} bytes={summary.totalSize}");
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
