using System;
using UnityEngine;

[CreateAssetMenu(fileName = "SoulframeServicesConfig", menuName = "SOULFRAME/Services Config", order = 1)]
public class SoulframeServicesConfig : ScriptableObject
{
    [Header("Base URLs")]
    public string whisperBaseUrl = "http://127.0.0.1:8001";
    public string ragBaseUrl = "http://127.0.0.1:8002";
    public string avatarAssetBaseUrl = "http://127.0.0.1:8003";
    public string coquiBaseUrl = "http://127.0.0.1:8004";

    [Header("Request Policy")]
    [Min(1f)] public float requestTimeoutSeconds = 15f;
    [Min(0)] public int retryCount = 1;

    public void NormalizeForWebGlRuntime()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        bool useRelativeApiPaths = !IsCurrentWebPageLoopbackHost();
        whisperBaseUrl = NormalizeServiceBaseUrl(whisperBaseUrl, "/api/whisper", 8001, useRelativeApiPaths);
        ragBaseUrl = NormalizeServiceBaseUrl(ragBaseUrl, "/api/rag", 8002, useRelativeApiPaths);
        avatarAssetBaseUrl = NormalizeServiceBaseUrl(avatarAssetBaseUrl, "/api/avatar", 8003, useRelativeApiPaths);
        coquiBaseUrl = NormalizeServiceBaseUrl(coquiBaseUrl, "/api/tts", 8004, useRelativeApiPaths);
#endif
    }

    /* Funzione utilizzata per normalizzare le URL dei servizi in WebGL, nel caso dobbiamo usarlo 
        come path relativo alla pagina web invece di un indirizzo assoluto */
    private static string NormalizeServiceBaseUrl(string value, string webPath, int legacyPort, bool useRelativeApiPaths)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return webPath;
        }

        string trimmed = value.Trim().TrimEnd('/');
        if (trimmed.StartsWith("/"))
        {
            return trimmed; // Se è un path relativo, lo lasciamo così com'è (verrà risolto come relativo alla pagina web).
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
        {
            return trimmed; // Se non è un URI valido, lo lasciamo così com'è.
        }

        string host = uri.Host.ToLowerInvariant();
        bool isLoopback = host == "127.0.0.1" || host == "localhost" || host == "::1";
        if (isLoopback && uri.Port == legacyPort) 
        {
            return useRelativeApiPaths ? webPath : trimmed;
        }

        return trimmed;
    }

    private static bool IsCurrentWebPageLoopbackHost()
    {
        string currentUrl = Application.absoluteURL;
        if (string.IsNullOrWhiteSpace(currentUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out Uri uri))
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        return host == "127.0.0.1" || host == "localhost" || host == "::1";
    }
}
