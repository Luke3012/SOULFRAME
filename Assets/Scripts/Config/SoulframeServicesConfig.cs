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
    public bool corsSafeMode = true;
}
