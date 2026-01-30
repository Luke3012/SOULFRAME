using System.Runtime.InteropServices;
using UnityEngine;

public class AvaturnWebController : MonoBehaviour
{
    [Header("Configurazione")]
    public string avaturnUrl = "https://soulframe.avaturn.dev?sdk=true";

    [Header("Riferimenti")]
    public UIManager uiManager;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void OpenAvaturnIframe(string url, string gameObjectName, string callbackMethod);
#endif

    void Start()
    {
        if (uiManager == null)
            uiManager = FindFirstObjectByType<UIManager>();
    }

    public void OnClick_NewAvatar()
    {
#if UNITY_EDITOR
        Application.OpenURL(avaturnUrl);
        Debug.Log("Apri URL nel browser: " + avaturnUrl);
        if (uiManager != null) uiManager.UpdateDebugText("Apri URL nel browser esterno");
#elif UNITY_WEBGL && !UNITY_EDITOR
        OpenAvaturnIframe(avaturnUrl, this.gameObject.name, "OnAvatarJsonReceived");
#else
        Debug.LogWarning("AvaturnWebController: piattaforma non supportata qui (usa Mobile prefab originale).");
#endif
    }

    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log("JSON ricevuto dal bridge: " + json);
        if (uiManager != null) uiManager.OnAvatarJsonReceived(json);
    }
}
