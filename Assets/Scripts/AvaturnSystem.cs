using Avaturn.Core.Runtime.Scripts;
using Avaturn.Core.Runtime.Scripts.Avatar;
using Avaturn.Core.Runtime.Scripts.Avatar.Data;
using Avaturn.Core.Runtime.Scripts.WebGL;
using UnityEngine;
using UnityEngine.Events;

public class AvaturnSystem : MonoBehaviour
{
    [Header("Core Components")]
    public AvaturnIframeControllerWebGL iframeController;
    public DownloadAvatar downloadAvatar;
    public AvatarReceiver avatarReceiver;

    [Header("Configuration")]
    public string subdomain = "soulframe";
    public string customUrl = "";

    // RIMOSSO: private bool isIframeSetup = false;

    void Awake()
    {
        // Disabilita la UI del prefab Avaturn
        DisableAvaturnUI();

        // Inizializza i componenti
        if (iframeController == null)
            iframeController = GetComponentInChildren<AvaturnIframeControllerWebGL>(true);

        if (downloadAvatar == null)
            downloadAvatar = GetComponentInChildren<DownloadAvatar>(true);

        if (avatarReceiver == null)
            avatarReceiver = GetComponentInChildren<AvatarReceiver>(true);
    }

    void DisableAvaturnUI()
    {
        // Disabilita tutti i canvas e UI elements del prefab
        var canvases = GetComponentsInChildren<Canvas>(true);
        foreach (var canvas in canvases)
        {
            canvas.gameObject.SetActive(false);
        }

        // Disabilita specifici componenti UI
        var menuWebGL = GetComponentInChildren<MenuWebGL>(true);
        if (menuWebGL != null) menuWebGL.gameObject.SetActive(false);

        var platformInfo = GetComponentInChildren<PlatformInfo>(true);
        if (platformInfo != null) platformInfo.gameObject.SetActive(false);
    }

    public void ShowAvaturnIframe()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Usa AvaturnWebController invece dell'iframe controller originale
        var webController = FindFirstObjectByType<AvaturnWebController>();
        if (webController != null)
        {
            webController.OnClick_NewAvatar();
            Debug.Log("Avaturn iframe mostrato tramite WebController");
        }
        else
        {
            Debug.LogError("AvaturnWebController non trovato nella scena");
        }
#else
        Debug.Log("Avaturn iframe funziona solo in WebGL build");
#endif
    }

    public void HideAvaturnIframe()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        // Per ora non facciamo nulla, l'iframe si chiude automaticamente
        Debug.Log("Avaturn iframe nascosto");
#endif
    }

    public void SetupAvatarCallbacks(System.Action<AvatarInfo> onAvatarReceived, System.Action<Transform> onAvatarDownloaded)
    {
        if (avatarReceiver != null)
        {
            // Scollega eventuali callback precedenti
            avatarReceiver.SetOnReceived(null);
            // Conversione esplicita da Action<T> a UnityAction<T>
            avatarReceiver.SetOnReceived(onAvatarReceived == null ? null : new UnityAction<AvatarInfo>(onAvatarReceived));
            Debug.Log("AvatarReceiver callback configurato");
        }
    }

    public string GetAvaturnUrl()
    {
        return string.IsNullOrEmpty(customUrl)
            ? $"https://{subdomain}.avaturn.dev"
            : customUrl;
    }
}