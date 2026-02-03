#if UNITY_EDITOR
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class WebGLFullscreenPostprocessor : IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL)
            return;

        var outputPath = report.summary.outputPath;
        if (string.IsNullOrWhiteSpace(outputPath) || !Directory.Exists(outputPath))
            return;

        PatchIndexHtml(Path.Combine(outputPath, "index.html"));
        PatchStyleCss(Path.Combine(outputPath, "TemplateData", "style.css"));
    }

    private static void PatchIndexHtml(string indexHtmlPath)
    {
        if (!File.Exists(indexHtmlPath))
            return;

        var original = File.ReadAllText(indexHtmlPath, Encoding.UTF8);
        var updated = original;

        // Remove fixed width/height attributes from the Unity canvas tag, if present.
        updated = Regex.Replace(
            updated,
            @"<canvas\b[^>]*\bid\s*=\s*""unity-canvas""[^>]*>",
            match => Regex.Replace(
                match.Value,
                @"\s(?:width|height)\s*=\s*(?:""[^""]*""|\S+)",
                string.Empty,
                RegexOptions.IgnoreCase),
            RegexOptions.IgnoreCase);

        // Ensure any JS sizing sets canvas to fullscreen instead of a fixed pixel size.
        updated = Regex.Replace(
            updated,
            @"canvas\.style\.width\s*=\s*""[^""]*""\s*;",
            "canvas.style.width = \"100%\";",
            RegexOptions.IgnoreCase);

        updated = Regex.Replace(
            updated,
            @"canvas\.style\.height\s*=\s*""[^""]*""\s*;",
            "canvas.style.height = \"100%\";",
            RegexOptions.IgnoreCase);

        if (updated == original)
            return;

        File.WriteAllText(indexHtmlPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static void PatchStyleCss(string styleCssPath)
    {
        if (!File.Exists(styleCssPath))
            return;

        var original = File.ReadAllText(styleCssPath, Encoding.UTF8);
        const string sentinel = "/* Fullscreen canvas override */";

        if (original.Contains(sentinel))
            return;

        var updated = original;
        if (!updated.EndsWith("\n"))
            updated += "\n";

        updated += sentinel + "\n" +
                  "html, body { width: 100%; height: 100%; margin: 0; padding: 0; overflow: hidden; }\n" +
                  "#unity-container { position: fixed !important; left: 0 !important; top: 0 !important; width: 100% !important; height: 100% !important; transform: none !important; }\n" +
                  "#unity-container.unity-desktop { left: 0 !important; top: 0 !important; width: 100% !important; height: 100% !important; transform: none !important; }\n" +
                  "#unity-canvas { width: 100% !important; height: 100% !important; }\n";

        File.WriteAllText(styleCssPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
#endif
