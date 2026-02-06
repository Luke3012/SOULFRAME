using System;
using UnityEngine;

public static class WavEncoder
{
    public static byte[] FromAudioClip(AudioClip clip)
    {
        if (clip == null)
        {
            return null;
        }

        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        return FromSamples(samples, clip.channels, clip.frequency);
    }

    public static byte[] FromSamples(float[] samples, int channels, int sampleRate)
    {
        if (samples == null || samples.Length == 0)
        {
            return null;
        }

        int sampleCount = samples.Length;
        int byteCount = sampleCount * sizeof(short);
        int headerSize = 44;
        byte[] wav = new byte[headerSize + byteCount];

        WriteHeader(wav, byteCount, channels, sampleRate);

        int offset = headerSize;
        for (int i = 0; i < sampleCount; i++)
        {
            short val = (short)Mathf.Clamp(samples[i] * short.MaxValue, short.MinValue, short.MaxValue);
            wav[offset++] = (byte)(val & 0xff);
            wav[offset++] = (byte)((val >> 8) & 0xff);
        }

        return wav;
    }

    private static void WriteHeader(byte[] buffer, int dataLength, int channels, int sampleRate)
    {
        int fileSize = 36 + dataLength;
        int byteRate = sampleRate * channels * sizeof(short);
        short blockAlign = (short)(channels * sizeof(short));
        short bitsPerSample = 16;

        WriteString(buffer, 0, "RIFF");
        WriteInt(buffer, 4, fileSize);
        WriteString(buffer, 8, "WAVE");
        WriteString(buffer, 12, "fmt ");
        WriteInt(buffer, 16, 16);
        WriteShort(buffer, 20, 1);
        WriteShort(buffer, 22, (short)channels);
        WriteInt(buffer, 24, sampleRate);
        WriteInt(buffer, 28, byteRate);
        WriteShort(buffer, 32, blockAlign);
        WriteShort(buffer, 34, bitsPerSample);
        WriteString(buffer, 36, "data");
        WriteInt(buffer, 40, dataLength);
    }

    private static void WriteString(byte[] buffer, int offset, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            buffer[offset + i] = (byte)value[i];
        }
    }

    private static void WriteInt(byte[] buffer, int offset, int value)
    {
        buffer[offset + 0] = (byte)(value & 0xff);
        buffer[offset + 1] = (byte)((value >> 8) & 0xff);
        buffer[offset + 2] = (byte)((value >> 16) & 0xff);
        buffer[offset + 3] = (byte)((value >> 24) & 0xff);
    }

    private static void WriteShort(byte[] buffer, int offset, short value)
    {
        buffer[offset + 0] = (byte)(value & 0xff);
        buffer[offset + 1] = (byte)((value >> 8) & 0xff);
    }
}
