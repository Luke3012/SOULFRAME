using System;
using System.Collections;
using UnityEngine;

public interface IAudioCaptureWebGL
{
    bool IsSupported { get; }
    bool HasPermission { get; }
    bool RequestPermission();
    void SetSampleRate(int sampleRate);
    IEnumerator CaptureToWavBytes(Action<byte[]> onComplete);
    bool StartRecording();
    IEnumerator StopRecordingAsync(Action<byte[]> onComplete);
    bool IsRecording { get; }
}

public class AudioRecorder : MonoBehaviour
{
    [SerializeField] private int sampleRate = 16000;
    [SerializeField] private MonoBehaviour webglCaptureProvider;


#if !UNITY_WEBGL || UNITY_EDITOR
    private AudioClip activeClip;
    private string activeDevice;
    private float recordStartTime;
#endif

    public bool HasMicrophoneAvailable()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webglCaptureProvider is IAudioCaptureWebGL webglCapture && webglCapture.IsSupported)
        {
            return true;
        }
        return false;
#else
        return Microphone.devices != null && Microphone.devices.Length > 0;
#endif
    }

    public IEnumerator RecordFixedDuration(float seconds, Action<byte[]> onComplete)
    {
        if (seconds <= 0f)
        {
            onComplete?.Invoke(null);
            yield break;
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        if (webglCaptureProvider is IAudioCaptureWebGL webglCapture && webglCapture.IsSupported)
        {
            webglCapture.SetSampleRate(sampleRate);
            if (!webglCapture.HasPermission)
            {
                webglCapture.RequestPermission();
                onComplete?.Invoke(null);
                yield break;
            }
            yield return webglCapture.CaptureToWavBytes(onComplete);
            yield break;
        }
        onComplete?.Invoke(null);
#else
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            onComplete?.Invoke(null);
            yield break;
        }

        activeDevice = Microphone.devices[0];
        int lengthSec = Mathf.CeilToInt(seconds);
        activeClip = Microphone.Start(activeDevice, false, lengthSec, sampleRate);
        recordStartTime = Time.realtimeSinceStartup;

        float startTime = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - startTime < seconds)
        {
            yield return null;
        }

        AudioClip recordedClip = activeClip;
        StopRecording();
        byte[] wav = WavEncoder.FromAudioClip(recordedClip);
        onComplete?.Invoke(wav);
#endif
    }

    public bool StartRecording()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webglCaptureProvider is IAudioCaptureWebGL webglCapture && webglCapture.IsSupported)
        {
            webglCapture.SetSampleRate(sampleRate);
            if (!webglCapture.HasPermission)
            {
                webglCapture.RequestPermission();
                return false;
            }
            return webglCapture.StartRecording();
        }
        return false;
#else
        if (Microphone.devices == null || Microphone.devices.Length == 0)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(activeDevice))
        {
            return false;
        }

        activeDevice = Microphone.devices[0];
        activeClip = Microphone.Start(activeDevice, false, 30, sampleRate);
        recordStartTime = Time.realtimeSinceStartup;
        return true;
#endif
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    [Obsolete("EndRecording() always returns null on WebGL. Use StopRecordingAsync() instead for WebGL platform.")]
#endif
    public byte[] EndRecording()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Debug.LogWarning("EndRecording() called on WebGL - this always returns null. Use StopRecordingAsync() instead.");
        return null;
#else
        if (string.IsNullOrEmpty(activeDevice) || activeClip == null)
        {
            StopRecording();
            return null;
        }

        int samples = Microphone.GetPosition(activeDevice);
        if (samples <= 0)
        {
            StopRecording();
            return null;
        }

        int channels = activeClip.channels;
        int frequency = activeClip.frequency;
        float[] data = new float[samples * channels];
        activeClip.GetData(data, 0);
        StopRecording();
        return WavEncoder.FromSamples(data, channels, frequency);
#endif
    }

    public IEnumerator StopRecordingAsync(Action<byte[]> onComplete)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webglCaptureProvider is IAudioCaptureWebGL webglCapture && webglCapture.IsSupported)
        {
            yield return webglCapture.StopRecordingAsync(onComplete);
            yield break;
        }
        onComplete?.Invoke(null);
#else
        onComplete?.Invoke(EndRecording());
        yield break;
#endif
    }

    public void StopRecording()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (webglCaptureProvider is IAudioCaptureWebGL webglCapture && webglCapture.IsSupported && webglCapture.IsRecording)
        {
            StartCoroutine(webglCapture.StopRecordingAsync(_ => { }));
        }
#else
        if (!string.IsNullOrEmpty(activeDevice) && Microphone.IsRecording(activeDevice))
        {
            Microphone.End(activeDevice);
        }
        activeClip = null;
        activeDevice = null;
#endif
    }
}
