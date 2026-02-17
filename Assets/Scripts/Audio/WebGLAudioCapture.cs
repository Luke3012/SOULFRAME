using System;
using System.Collections;
using System.Runtime.InteropServices;
#if UNITY_WEBGL && !UNITY_EDITOR
using AOT;
#endif
using UnityEngine;

public class WebGLAudioCapture : MonoBehaviour, IAudioCaptureWebGL
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern bool WebGLAudio_IsSupported();

    [DllImport("__Internal")]
    private static extern void WebGLAudio_RequestPermission(Action<int> callback);

    [DllImport("__Internal")]
    private static extern void WebGLAudio_SetSampleRate(int sampleRate);

    [DllImport("__Internal")]
    private static extern int WebGLAudio_StartRecording();

    [DllImport("__Internal")]
    private static extern void WebGLAudio_StopRecording(Action<IntPtr, int> callback);

    [DllImport("__Internal")]
    private static extern int WebGLAudio_IsRecording();

    [DllImport("__Internal")]
    private static extern void WebGLAudio_CaptureFixedDuration(int durationMs, Action<IntPtr, int> callback);
#endif

    // Qui usiamo variabili statiche per tracciare quale istanza WebGLAudioCapture
    // ha avviato un'operazione asincrona (richiesta permesso, stop o cattura a durata fissa),
    // cosi' instradiamo il callback verso l'istanza corretta. Conviene usare una sola istanza
    // per tipo di operazione alla volta: piu' istanze o operazioni sovrapposte possono
    // generare comportamenti inattesi.
    private static WebGLAudioCapture activeStopInstance;
    private static WebGLAudioCapture activeCaptureInstance;
    private static bool permissionGranted = false;
#if UNITY_WEBGL && !UNITY_EDITOR
    private static bool permissionRequested = false;
#endif
    private bool captureCompleted;
    private byte[] recordedData = null;

    public bool IsSupported
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebGLAudio_IsSupported();
#else
            return false;
#endif
        }
    }

    public bool IsRecording
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return WebGLAudio_IsRecording() != 0;
#else
            return false;
#endif
        }
    }

    public bool HasPermission => permissionGranted;

    public bool RequestPermission()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (!IsSupported || permissionRequested)
        {
            return permissionGranted;
        }
        permissionRequested = true;
        WebGLAudio_RequestPermission(OnPermissionResult);
        return permissionGranted;
#else
        return false;
#endif
    }

    public void SetSampleRate(int sampleRate)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLAudio_SetSampleRate(sampleRate);
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [MonoPInvokeCallback(typeof(Action<int>))]
    private static void OnPermissionResult(int granted)
#else
    private static void OnPermissionResult(int granted)
#endif
    {
        permissionGranted = granted != 0;
#if UNITY_WEBGL && !UNITY_EDITOR
        permissionRequested = false;
#endif
        if (permissionGranted)
        {
            Debug.Log("[WebGLAudioCapture] Microphone permission granted");
        }
        else
        {
            Debug.LogWarning("[WebGLAudioCapture] Microphone permission denied");
        }
    }

    public IEnumerator CaptureToWavBytes(Action<byte[]> onComplete)
    {
        if (!IsSupported)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        if (!permissionGranted)
        {
            RequestPermission();
            onComplete?.Invoke(null);
            yield break;
        }

        recordedData = null;
        captureCompleted = false;

#if UNITY_WEBGL && !UNITY_EDITOR
        activeCaptureInstance = this;
        WebGLAudio_CaptureFixedDuration(5000, OnCaptureComplete);
#else
        captureCompleted = true;
#endif

        while (!captureCompleted)
        {
            yield return null;
        }

        onComplete?.Invoke(recordedData);
    }

    public bool StartRecording()
    {
        if (!IsSupported || IsRecording)
        {
            return false;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (!permissionGranted)
        {
            RequestPermission();
            return false;
        }
        int result = WebGLAudio_StartRecording();
        return result != 0;
#else
        return false;
#endif
    }

    public IEnumerator StopRecordingAsync(Action<byte[]> onComplete)
    {
        if (!IsSupported || !IsRecording)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        recordedData = null;

#if UNITY_WEBGL && !UNITY_EDITOR
        captureCompleted = false;
        activeStopInstance = this;
        WebGLAudio_StopRecording(OnStopRecordingComplete);
        while (!captureCompleted)
        {
            yield return null;
        }
#endif

        onComplete?.Invoke(recordedData);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [MonoPInvokeCallback(typeof(Action<IntPtr, int>))]
    private static void OnStopRecordingComplete(IntPtr dataPtr, int length)
    {
        if (activeStopInstance == null)
        {
            return;
        }

        if (length > 0 && dataPtr != IntPtr.Zero)
        {
            activeStopInstance.recordedData = new byte[length];
            Marshal.Copy(dataPtr, activeStopInstance.recordedData, 0, length);
        }
        else
        {
            activeStopInstance.recordedData = null;
        }
        activeStopInstance.captureCompleted = true;
    }

    [MonoPInvokeCallback(typeof(Action<IntPtr, int>))]
    private static void OnCaptureComplete(IntPtr dataPtr, int length)
    {
        if (activeCaptureInstance == null)
        {
            return;
        }

        if (length > 0 && dataPtr != IntPtr.Zero)
        {
            activeCaptureInstance.recordedData = new byte[length];
            Marshal.Copy(dataPtr, activeCaptureInstance.recordedData, 0, length);
        }
        else
        {
            activeCaptureInstance.recordedData = null;
        }
        activeCaptureInstance.captureCompleted = true;
    }
#endif
}
