using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// One-click WebGL build so testers can play Apogée from a link. The output folder is
/// zipped and uploaded to itch.io (project kind: HTML, "play in the browser"). Settings are
/// tuned for a plain static host: Brotli with Unity's JS decompression fallback, so no
/// server-side Content-Encoding header is needed, and a portrait canvas for phones.
///
/// From the editor:  menu  Apogée > Build WebGL
/// From a terminal:  Unity -batchmode -quit -projectPath . -executeMethod WebBuild.BuildWeb
/// (requires the WebGL module: Unity Hub > Installs > 6000.5.6f1 > Add modules > WebGL)
/// </summary>
public static class WebBuild
{
    const string OutputDir = "WebBuild";

    [MenuItem("Apogée/Build WebGL")]
    public static void BuildWeb()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            Debug.LogError("[WebBuild] WebGL module not installed. Unity Hub > Installs > 6000.5.6f1 > Add modules > WebGL Build Support.");
            return;
        }

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.WebGL, ScriptingImplementation.IL2CPP);
        // Brotli plus Unity's JS decompression fallback: a ~20 MB download that still works
        // on hosts which cannot set Content-Encoding headers (GitHub Pages, itch.io).
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.WebGL.template = "PROJECT:Apogee";   // Assets/WebGLTemplates/Apogee: portrait canvas, phone-friendly
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultWebScreenWidth = 540;
        PlayerSettings.defaultWebScreenHeight = 960;

        var scenes = new[] { "Assets/Scenes/SampleScene.unity" };
        Directory.CreateDirectory(OutputDir);

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
            Debug.Log($"[WebBuild] OK - {summary.totalSize / (1024 * 1024)} MB in {Path.GetFullPath(OutputDir)}");
        else
            Debug.LogError($"[WebBuild] Build {summary.result}: {summary.totalErrors} error(s)");
    }
}
