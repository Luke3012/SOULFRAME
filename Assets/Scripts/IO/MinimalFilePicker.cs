using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
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
    public static IFilePickerWebGL WebGLProvider { get; set; }

    public static string OpenFilePanel(string title, string directory, string extensions)
    {
#if UNITY_EDITOR
        return EditorUtility.OpenFilePanel(title, directory, extensions);
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
