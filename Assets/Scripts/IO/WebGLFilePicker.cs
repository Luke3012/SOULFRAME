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
    private bool resolved;
    private FilePickResult result;
    private float pickTimeoutSeconds = 20f;
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
        resolved = false;
        result = new FilePickResult();
        float timeoutAt = Time.realtimeSinceStartup + Mathf.Max(1f, pickTimeoutSeconds);
        WebGLFilePicker_PickFile(acceptExtensions ?? string.Empty, gameObject.name, nameof(OnWebGLFilePicked));
        while (waiting)
        {
            if (Time.realtimeSinceStartup >= timeoutAt)
            {
                waiting = false;
                resolved = true;
                Debug.LogWarning("[WebGLFilePicker] Timeout attesa selezione file. Tratto come CANCEL.");
                break;
            }
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
        if (resolved)
        {
            return;
        }

        if (string.IsNullOrEmpty(payload) || payload == "CANCEL")
        {
            waiting = false;
            resolved = true;
            return;
        }

        if (payload.StartsWith("ERR:"))
        {
            waiting = false;
            resolved = true;
            Debug.LogWarning("[WebGLFilePicker] " + payload);
            return;
        }

        try
        {
            var data = JsonUtility.FromJson<FilePayload>(payload);
            if (data == null || string.IsNullOrEmpty(data.data))
            {
                waiting = false;
                resolved = true;
                return;
            }

            result.FileName = string.IsNullOrEmpty(data.name) ? "file" : data.name;
            result.Bytes = Convert.FromBase64String(data.data);
            waiting = false;
            resolved = true;
        }
        catch (Exception e)
        {
            waiting = false;
            resolved = true;
            Debug.LogWarning("[WebGLFilePicker] Parse error: " + e.Message);
        }
    }
#endif
}
