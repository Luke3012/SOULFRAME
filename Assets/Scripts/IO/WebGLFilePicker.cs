using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

public class WebGLFilePicker : MonoBehaviour, IFilePickerWebGL
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int WebGLFilePicker_IsSupported();
    [DllImport("__Internal")] private static extern void WebGLFilePicker_PickFile(
        string acceptExtensions,
        string gameObjectName,
        string callbackMethod);
#endif

    [Serializable]
    private class FilePayload
    {
        public string name;
        public string data;
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    private bool waiting;
    private FilePickResult result;
#endif

    public bool IsSupported
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebGLFilePicker_IsSupported() != 0;
#else
            return false;
#endif
        }
    }

    private void Awake()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        MinimalFilePicker.WebGLProvider = this;
#endif
    }

    public IEnumerator PickFile(string acceptExtensions, Action<FilePickResult> onPicked)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        waiting = true;
        result = new FilePickResult();
        WebGLFilePicker_PickFile(acceptExtensions ?? string.Empty, gameObject.name, nameof(OnWebGLFilePicked));
        while (waiting)
        {
            yield return null;
        }
        onPicked?.Invoke(result);
#else
        onPicked?.Invoke(new FilePickResult());
        yield break;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    public void OnWebGLFilePicked(string payload)
    {
        waiting = false;
        result = new FilePickResult();

        if (string.IsNullOrEmpty(payload) || payload == "CANCEL")
        {
            return;
        }

        if (payload.StartsWith("ERR:"))
        {
            Debug.LogWarning("[WebGLFilePicker] " + payload);
            return;
        }

        try
        {
            var data = JsonUtility.FromJson<FilePayload>(payload);
            if (data == null || string.IsNullOrEmpty(data.data))
            {
                return;
            }

            result.FileName = string.IsNullOrEmpty(data.name) ? "file" : data.name;
            result.Bytes = Convert.FromBase64String(data.data);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[WebGLFilePicker] Parse error: " + e.Message);
        }
    }
#endif
}
