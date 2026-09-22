#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;

namespace Sahkku.Editor
{
    public static class BuildWebGL
    {
        private const string ProfilePath = "Assets/Settings/Build Profiles/Web - Desktop - Release.asset";
        private const string DefaultRelativeOutputPath = "../build/webgl";

        private static readonly string[] DefaultScenes = new string[]
        {
            "Assets/Scenes/MainMenu.unity",
            "Assets/Scenes/Game.unity"
        };

        [MenuItem("Sahkku/Build/WebGL (Release)")]
        public static void Build()
        {
            BuildInternal(isCommandLine: false);
        }

        public static void BuildFromCommandLine()
        {
            BuildInternal(isCommandLine: true);
        }

        private static void BuildInternal(bool isCommandLine)
        {
            try
            {
                DebugLog("Starting WebGL build pipeline...");

                string outputPath = GetCommandLineArg("-customBuildPath");
                if (string.IsNullOrEmpty(outputPath))
                {
                    string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                    outputPath = Path.GetFullPath(Path.Combine(projectRoot, DefaultRelativeOutputPath));
                }

                DebugLog($"Target output directory: {outputPath}");

                ApplyBuildProfile();
                BuildAddressables();

                BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
                {
                    scenes = GetEnabledScenes(),
                    locationPathName = outputPath,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None
                };

                BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
                BuildSummary summary = report.summary;

                if (summary.result == BuildResult.Succeeded)
                {
                    DebugLog($"Build succeeded! Total size: {summary.totalSize} bytes at {outputPath}");
                    CopySupplementaryFiles(outputPath);

                    if (isCommandLine)
                    {
                        EditorApplication.Exit(0);
                    }
                }
                else
                {
                    DebugLogError($"Build failed with result: {summary.result} ({summary.totalErrors} errors)");
                    if (isCommandLine)
                    {
                        EditorApplication.Exit(1);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogError($"Unhandled exception during build: {ex}");
                if (isCommandLine)
                {
                    EditorApplication.Exit(1);
                }
                else
                {
                    throw;
                }
            }
        }

        private static void ApplyBuildProfile()
        {
#if UNITY_6000_0_OR_NEWER
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<UnityEditor.Build.Profile.BuildProfile>(ProfilePath);
                if (profile != null)
                {
                    UnityEditor.Build.Profile.BuildProfile.SetActiveBuildProfile(profile);
                    DebugLog($"Applied active build profile: {ProfilePath}");
                }
                else
                {
                    DebugLog($"Build profile not found at '{ProfilePath}', using default Editor settings.");
                }
            }
            catch (Exception ex)
            {
                DebugLogWarning($"Could not set build profile: {ex.Message}");
            }
#endif
        }

        private static void BuildAddressables()
        {
            try
            {
                var addressableSettingsType = Type.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings, Unity.Addressables.Editor");
                if (addressableSettingsType != null)
                {
                    DebugLog("Building Addressable content...");
                    var buildMethod = addressableSettingsType.GetMethod("BuildPlayerContent", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (buildMethod != null)
                    {
                        buildMethod.Invoke(null, null);
                        DebugLog("Addressables content built successfully.");
                    }
                    else
                    {
                        DebugLogWarning("AddressableAssetSettings.BuildPlayerContent() method not found.");
                    }
                }
                else
                {
                    DebugLog("Addressables editor assembly not loaded, skipping Addressables pre-build.");
                }
            }
            catch (Exception ex)
            {
                DebugLogWarning($"Addressables build encountered an issue: {ex.Message}");
            }
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(s => s.enabled)
                .Select(s => s.path)
                .ToArray();

            if (scenes.Length == 0)
            {
                DebugLog("No scenes enabled in EditorBuildSettings. Using default scenes.");
                scenes = DefaultScenes;
            }

            return scenes;
        }

        private static void CopySupplementaryFiles(string outputPath)
        {
            try
            {
                string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
                string repoRoot = Path.GetFullPath(Path.Combine(projectRoot, ".."));

                string[] candidatePdfDirs = new string[] { projectRoot, repoRoot };
                string[] pdfFiles = new string[] { "SahkkuRules.pdf", "SahkkuRegler.pdf" };

                foreach (var fileName in pdfFiles)
                {
                    foreach (var dir in candidatePdfDirs)
                    {
                        string sourcePath = Path.Combine(dir, fileName);
                        if (File.Exists(sourcePath))
                        {
                            string destPath = Path.Combine(outputPath, fileName);
                            File.Copy(sourcePath, destPath, overwrite: true);
                            DebugLog($"Copied {fileName} to build directory.");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogWarning($"Could not copy supplementary files: {ex.Message}");
            }
        }

        private static string GetCommandLineArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static void DebugLog(string message) => UnityEngine.Debug.Log($"[BuildWebGL] {message}");
        private static void DebugLogWarning(string message) => UnityEngine.Debug.LogWarning($"[BuildWebGL] {message}");
        private static void DebugLogError(string message) => UnityEngine.Debug.LogError($"[BuildWebGL] {message}");
    }
}
#endif
