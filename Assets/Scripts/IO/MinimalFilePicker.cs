using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.IO;
using System.Text;
#endif

public interface IFilePickerWebGL
{
    bool IsSupported { get; }
    IEnumerator PickFile(string acceptExtensions, Action<FilePickResult> onPicked);
}

public struct FilePickResult
{
    public string FileName;
    public byte[] Bytes;
}

public static class MinimalFilePicker
{
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    private static string BuildWindowsFormsFilter(string extensions)
    {
        if (string.IsNullOrWhiteSpace(extensions))
        {
            return "All Files (*.*)|*.*";
        }

        string[] parts = extensions.Split(',');
        var patterns = new StringBuilder();
        var labels = new StringBuilder();

        for (int i = 0; i < parts.Length; i++)
        {
            string ext = parts[i].Trim();
            if (string.IsNullOrEmpty(ext))
            {
                continue;
            }

            if (!ext.StartsWith("."))
            {
                ext = "." + ext;
            }

            if (patterns.Length > 0)
            {
                patterns.Append(';');
                labels.Append(", ");
            }

            patterns.Append('*').Append(ext);
            labels.Append(ext);
        }

        if (patterns.Length == 0)
        {
            return "All Files (*.*)|*.*";
        }

        return $"Supported Files ({labels})|{patterns}|All Files (*.*)|*.*";
    }

    private static string EscapePowerShellSingleQuoted(string value)
    {
        return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
    }

    private static string OpenFilePanelWindows(string title, string directory, string extensions)
    {
        string filter = BuildWindowsFormsFilter(extensions);
        string initialDir = string.IsNullOrWhiteSpace(directory) ? null : directory;
        if (string.IsNullOrWhiteSpace(initialDir) || !Directory.Exists(initialDir))
        {
            initialDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        }

        string psTitle = EscapePowerShellSingleQuoted(string.IsNullOrWhiteSpace(title) ? "Select File" : title);
        string psFilter = EscapePowerShellSingleQuoted(filter);
        string psInitialDir = EscapePowerShellSingleQuoted(initialDir);

        string script =
            "$ErrorActionPreference='Stop'; " +
            "Add-Type -AssemblyName System.Windows.Forms | Out-Null; " +
            "$dlg = New-Object System.Windows.Forms.OpenFileDialog; " +
            "$dlg.CheckFileExists = $true; " +
            "$dlg.Multiselect = $false; " +
            "$dlg.Title = '" + psTitle + "'; " +
            "$dlg.Filter = '" + psFilter + "'; " +
            "$dlg.InitialDirectory = '" + psInitialDir + "'; " +
            "if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($dlg.FileName) }";

        try
        {
            string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -STA -EncodedCommand " + encodedCommand,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = System.Diagnostics.Process.Start(psi))
            {
                if (process == null)
                {
                    return null;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.LogWarning("[MinimalFilePicker] PowerShell picker error: " + error.Trim());
                }

                string selected = string.IsNullOrWhiteSpace(output) ? null : output.Trim();
                return string.IsNullOrWhiteSpace(selected) ? null : selected;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[MinimalFilePicker] PowerShell picker exception: " + ex.Message);
            return null;
        }
    }
#endif

    public static IFilePickerWebGL WebGLProvider { get; set; }

    public static string OpenFilePanel(string title, string directory, string extensions)
    {
#if UNITY_EDITOR
        return EditorUtility.OpenFilePanel(title, directory, extensions);
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
        return OpenFilePanelWindows(title, directory, extensions);
#else
        Debug.LogWarning("[MinimalFilePicker] File picker non disponibile in questa piattaforma. Usa un hook WebGL o una UI custom.");
        return null;
#endif
    }

    public static IEnumerator PickFileWebGL(string acceptExtensions, Action<FilePickResult> onPicked)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (WebGLProvider != null && WebGLProvider.IsSupported)
        {
            yield return WebGLProvider.PickFile(acceptExtensions, onPicked);
            yield break;
        }
#endif
        Debug.LogWarning("[MinimalFilePicker] WebGL file picker non configurato.");
        onPicked?.Invoke(new FilePickResult());
        yield break;
    }
}
