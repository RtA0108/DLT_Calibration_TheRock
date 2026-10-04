using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class E230VideoTextureSmokeBuild
{
    public static void Build()
    {
        string outputDirectory = GetArgumentValue("-e230BuildOutput");
        if (string.IsNullOrEmpty(outputDirectory))
            outputDirectory = Path.Combine(Path.GetTempPath(), "E230UnitySmoke");

        Directory.CreateDirectory(outputDirectory);
        string executable = Path.Combine(outputDirectory, "E230UnitySmoke.exe");
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = executable,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.Development | BuildOptions.CleanBuildCache
        };

        BuildReport buildReport = BuildPipeline.BuildPlayer(options);
        if (buildReport.summary.result != BuildResult.Succeeded)
            throw new Exception($"E230 smoke build failed: {buildReport.summary.result}, " +
                                $"errors={buildReport.summary.totalErrors}");

        Debug.Log($"[E230 Smoke Build] PASS - {executable}, {buildReport.summary.totalSize} bytes");
    }

    private static string GetArgumentValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }
}
