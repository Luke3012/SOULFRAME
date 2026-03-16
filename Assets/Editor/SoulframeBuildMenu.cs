#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class SoulframeBuildMenu
{
    private const string WebGLOutputPath = "Build";
    private const string WindowsOutputFolder = "Build_Windows64";
    private const string WindowsExecutableName = "SOULFRAME.exe";

    [MenuItem("SOULFRAME/Build/Build WebGL")]
    public static void BuildWebGLMenu()
    {
        BuildWebGL();
    }

    [MenuItem("SOULFRAME/Build/Build Windows x64")]
    public static void BuildWindowsMenu()
    {
        BuildWindows64();
    }

    // Metodo CLI: Unity -batchmode -quit -projectPath . -executeMethod SoulframeBuildMenu.BuildWebGLCli
    public static void BuildWebGLCli()
    {
        BuildWebGL();
    }

    // Metodo CLI: Unity -batchmode -quit -projectPath . -executeMethod SoulframeBuildMenu.BuildWindows64Cli
    public static void BuildWindows64Cli()
    {
        BuildWindows64();
    }

    private static void BuildWebGL()
    {
        BuildForTarget(BuildTarget.WebGL, WebGLOutputPath, BuildOptions.None);
    }

    private static void BuildWindows64()
    {
        string outputPath = Path.Combine(WindowsOutputFolder, WindowsExecutableName);
        BuildForTarget(BuildTarget.StandaloneWindows64, outputPath, BuildOptions.None);
    }

    private static void BuildForTarget(BuildTarget target, string outputPath, BuildOptions options)
    {
        EnsureActiveBuildTarget(target);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Debug.LogError("[Build] Nessuna scena abilitata in Build Settings.");
            return;
        }

        EnsureOutputDirectory(target, outputPath);

        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = target,
            options = options
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[Build] Successo {target}. Output: {outputPath}. Size: {summary.totalSize} bytes.");
            return;
        }

        throw new Exception($"[Build] Fallita build {target}. Risultato: {summary.result}.");
    }

    private static void EnsureOutputDirectory(BuildTarget target, string outputPath)
    {
        string directory;

        if (target == BuildTarget.StandaloneWindows64)
        {
            directory = Path.GetDirectoryName(outputPath);
        }
        else
        {
            directory = outputPath;
        }

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void EnsureActiveBuildTarget(BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget == target)
        {
            return;
        }

        BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
        if (group == BuildTargetGroup.Unknown)
        {
            throw new Exception($"[Build] BuildTargetGroup non valido per target {target}.");
        }

        Debug.Log($"[Build] Switch Active Build Target: {EditorUserBuildSettings.activeBuildTarget} -> {target}");
        bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);
        if (!switched)
        {
            throw new Exception($"[Build] Impossibile cambiare Active Build Target a {target}.");
        }
    }
}
#endif
