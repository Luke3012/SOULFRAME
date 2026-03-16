using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

public class AvaturnWebController : MonoBehaviour
{
    [Header("Configurazione")]
    public string avaturnUrl = "https://soulframe.avaturn.dev";

    [Header("Riferimenti")]
    public UIFlowController uiFlowController;

    [Header("Desktop Callback")]
    public int callbackPort = 37821;
    public bool useLocalhostLoopback = true;
    public float callbackTimeoutSeconds = 180f;
    public bool desktopUseLocalBridgePage = true;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void OpenAvaturnIframe(string url, string gameObjectName, string callbackMethod);
#endif

    private HttpListener localListener;
    private CancellationTokenSource listenerCancellation;
    private readonly Queue<string> pendingAvatarJson = new Queue<string>();
    private readonly object pendingLock = new object();
    private string activeSessionId;
    private bool localServerReady;
#if !UNITY_WEBGL || UNITY_EDITOR
    private bool localServerFailed;
#endif
    private float callbackDeadlineTs;

    void Start()
    {
        if (uiFlowController == null)
            uiFlowController = FindFirstObjectByType<UIFlowController>();
    }

    void Update()
    {
        string json = null;
        lock (pendingLock)
        {
            if (pendingAvatarJson.Count > 0)
                json = pendingAvatarJson.Dequeue();
        }

        if (!string.IsNullOrEmpty(json))
        {
            OnAvatarJsonReceived(json);
            StopLocalCallbackServer();
        }

        if (localServerReady && callbackDeadlineTs > 0f && Time.realtimeSinceStartup > callbackDeadlineTs)
        {
            Debug.LogWarning("Avaturn callback timeout raggiunto.");
            if (uiFlowController != null)
                uiFlowController.UpdateDebugText("Timeout callback Avaturn. Riprova.");
            StopLocalCallbackServer();
        }
    }

    void OnDisable()
    {
        StopLocalCallbackServer();
    }

    void OnApplicationQuit()
    {
        StopLocalCallbackServer();
    }

    public void OnClick_NewAvatar()
    {
    #if UNITY_WEBGL && !UNITY_EDITOR
        OpenAvaturnIframe(avaturnUrl, this.gameObject.name, "OnAvatarJsonReceived");
#else
        localServerFailed = false;
        StartLocalCallbackServer();

        if (localServerFailed)
        {
            if (uiFlowController != null)
                uiFlowController.UpdateDebugText("Errore callback locale Avaturn.");
            Debug.LogError("Impossibile avviare callback locale Avaturn.");
            return;
        }

        string callbackUrl = BuildCallbackUrl();
        string fullUrl = BuildDesktopLaunchUrl(callbackUrl);
        Debug.Log("Apri Avaturn desktop: " + fullUrl);
        if (uiFlowController != null)
            uiFlowController.UpdateDebugText("Apertura Avaturn nel browser esterno...");
        Application.OpenURL(fullUrl);
#endif
    }

    public void OnAvatarJsonReceived(string json)
    {
        Debug.Log("JSON ricevuto dal bridge: " + json);
        if (uiFlowController != null) uiFlowController.OnAvatarJsonReceived(json);
    }

    public void OnWebOverlayOpened()
    {
        if (uiFlowController != null)
        {
            uiFlowController.OnWebOverlayOpened();
        }
    }

    public void OnWebOverlayClosed()
    {
        if (uiFlowController != null)
        {
            uiFlowController.OnWebOverlayClosed();
        }
    }

    private string BuildCallbackUrl()
    {
        string host = useLocalhostLoopback ? "127.0.0.1" : "localhost";
        return "http://" + host + ":" + callbackPort + "/avaturn-callback?session=" + Uri.EscapeDataString(activeSessionId ?? "");
    }

    private string BuildDesktopLaunchUrl(string callbackUrl)
    {
        if (desktopUseLocalBridgePage)
        {
            string host = useLocalhostLoopback ? "127.0.0.1" : "localhost";
            return "http://" + host + ":" + callbackPort + "/avaturn-desktop?session=" + Uri.EscapeDataString(activeSessionId ?? "");
        }

        return BuildDesktopAvaturnUrl(callbackUrl);
    }

    private string BuildDesktopAvaturnUrl(string callbackUrl)
    {
        string separator = avaturnUrl.Contains("?") ? "&" : "?";
        string escaped = Uri.EscapeDataString(callbackUrl);

        // Varianti comuni per callback/redirect: il servizio usera' quella supportata.
        return avaturnUrl + separator
            + "callback_url=" + escaped
            + "&callbackUrl=" + escaped
            + "&redirect_uri=" + escaped
            + "&return_url=" + escaped;
    }

    private void StartLocalCallbackServer()
    {
        StopLocalCallbackServer();

        activeSessionId = Guid.NewGuid().ToString("N");
        callbackDeadlineTs = Time.realtimeSinceStartup + Mathf.Max(10f, callbackTimeoutSeconds);

        try
        {
            string host = useLocalhostLoopback ? "127.0.0.1" : "localhost";
            localListener = new HttpListener();
            localListener.Prefixes.Add("http://" + host + ":" + callbackPort + "/");
            localListener.Start();

            listenerCancellation = new CancellationTokenSource();
            localServerReady = true;
            Task.Run(() => ListenLoopAsync(listenerCancellation.Token));
        }
        catch (Exception ex)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            localServerFailed = true;
#endif
            localServerReady = false;
            Debug.LogError("Errore avvio callback locale Avaturn: " + ex.Message);
            StopLocalCallbackServer();
        }
    }

    private void StopLocalCallbackServer()
    {
        callbackDeadlineTs = 0f;
        localServerReady = false;

        try
        {
            if (listenerCancellation != null)
            {
                listenerCancellation.Cancel();
                listenerCancellation.Dispose();
                listenerCancellation = null;
            }
        }
        catch
        {
        }

        try
        {
            if (localListener != null)
            {
                if (localListener.IsListening)
                    localListener.Stop();
                localListener.Close();
                localListener = null;
            }
        }
        catch
        {
        }
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && localListener != null && localListener.IsListening)
        {
            HttpListenerContext context = null;
            try
            {
                context = await localListener.GetContextAsync();
            }
            catch (Exception)
            {
                if (token.IsCancellationRequested)
                    break;
                continue;
            }

            if (context == null)
                continue;

            try
            {
                HandleIncomingRequest(context);
            }
            catch (Exception ex)
            {
                Debug.LogError("Errore callback Avaturn: " + ex.Message);
                TryWriteResponse(context.Response, 500, "text/plain", "Errore callback locale");
            }
        }
    }

    private void HandleIncomingRequest(HttpListenerContext context)
    {
        AddCorsHeaders(context.Response);
        if (string.Equals(context.Request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            TryWriteResponse(context.Response, 204, "text/plain", string.Empty);
            return;
        }

        string path = context.Request.Url != null ? context.Request.Url.AbsolutePath : string.Empty;
        if (string.Equals(path, "/favicon.ico", StringComparison.OrdinalIgnoreCase))
        {
            // Evita 404 rumorosi del browser sulla pagina bridge locale.
            TryWriteResponse(context.Response, 204, "image/x-icon", string.Empty);
            return;
        }

        if (string.Equals(path, "/avaturn-desktop", StringComparison.OrdinalIgnoreCase))
        {
            string bridgePage = BuildDesktopBridgeHtml();
            TryWriteResponse(context.Response, 200, "text/html; charset=utf-8", bridgePage);
            return;
        }

        if (!string.Equals(path, "/avaturn-callback", StringComparison.OrdinalIgnoreCase))
        {
            TryWriteResponse(context.Response, 404, "text/plain", "Not found");
            return;
        }

        string session = context.Request.QueryString["session"];
        if (!string.IsNullOrEmpty(activeSessionId) && !string.Equals(session, activeSessionId, StringComparison.Ordinal))
        {
            TryWriteResponse(context.Response, 403, "text/plain", "Sessione non valida");
            return;
        }

        string json = ExtractJsonPayload(context.Request);
        if (string.IsNullOrEmpty(json))
        {
            TryWriteResponse(context.Response, 400, "text/plain", "Payload avatar mancante");
            return;
        }

        lock (pendingLock)
        {
            pendingAvatarJson.Enqueue(json);
        }

        string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Avatar ricevuto</title>"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"></head>"
            + "<body style=\"margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#06090f;color:#e7edf7;font-family:Segoe UI,Arial,sans-serif;\">"
            + "<main style=\"text-align:center;max-width:900px;padding:40px;\">"
            + "<h1 style=\"margin:0 0 10px 0;font-size:42px;line-height:1.1;\">Ritorna a SOULFRAME</h1>"
            + "<p style=\"margin:0;font-size:20px;opacity:.9;\">Avatar ricevuto. Puoi chiudere questa scheda.</p>"
            + "</main>"
            + "</body></html>";
        TryWriteResponse(context.Response, 200, "text/html; charset=utf-8", html);
    }

    private string BuildDesktopBridgeHtml()
    {
        string safeAvaturnUrl = ToJavaScriptString(avaturnUrl);
        string safeSession = ToJavaScriptString(activeSessionId ?? string.Empty);

        return "<!doctype html><html><head><meta charset=\"utf-8\"><title>SOULFRAME Avaturn</title>"
            + "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">"
            + "<style>html,body{margin:0;height:100%;background:#06090f;color:#e7edf7;font-family:Segoe UI,Arial,sans-serif}"
            + "#status{position:fixed;left:14px;bottom:12px;z-index:9;font-size:13px;opacity:.9;background:#0f1624;border:1px solid #1f2c45;border-radius:999px;padding:7px 12px}"
            + "#container{height:100%;width:100%}</style></head><body>"
            + "<div id=\"status\">Caricamento Avaturn...</div>"
            + "<div id=\"container\"></div>"
            + "<script type=\"module\">"
            + "import { AvaturnSDK } from 'https://cdn.jsdelivr.net/npm/@avaturn/sdk/dist/index.js';"
            + "const statusEl=document.getElementById('status');"
            + "const session='" + safeSession + "';"
            + "const callback='/avaturn-callback?session='+encodeURIComponent(session);"
            + "async function sendPayload(payload){"
            + "  await fetch(callback,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(payload)});"
            + "}"
            + "function showCompletionScreen(){"
            + "  document.body.innerHTML='<main style=\"min-height:100vh;display:flex;align-items:center;justify-content:center;background:#06090f;color:#e7edf7;font-family:Segoe UI,Arial,sans-serif;text-align:center;padding:40px;box-sizing:border-box;\"><div><h1 style=\"margin:0 0 10px 0;font-size:42px;line-height:1.1;\">Ritorna a SOULFRAME</h1><p style=\"margin:0;font-size:20px;opacity:.9;\">Avatar inviato con successo. Puoi chiudere questa scheda.</p></div></main>';"
            + "}"
            + "async function run(){"
            + "  try{"
            + "    const container=document.getElementById('container');"
            + "    const sdk=new AvaturnSDK();"
            + "    await sdk.init(container,{url:'" + safeAvaturnUrl + "'});"
            + "    statusEl.textContent='Avaturn pronto';"
            + "    sdk.on('export',async (data)=>{"
            + "      statusEl.textContent='Avatar esportato, invio a SOULFRAME...';"
            + "      const payload={url:data.url||'',urlType:data.urlType||'glb',bodyId:data.bodyId||'default',gender:data.gender||'unknown',avatarId:data.avatarId||Date.now().toString(),status:'ok'};"
            + "      await sendPayload(payload);"
            + "      showCompletionScreen();"
            + "    });"
            + "    sdk.on('error',async (err)=>{"
            + "      statusEl.textContent='Errore Avaturn';"
            + "      await sendPayload({status:'error',error:(err&&err.message)?err.message:'unknown'});"
            + "    });"
            + "  }catch(err){"
            + "    statusEl.textContent='Errore caricamento Avaturn';"
            + "    console.error(err);"
            + "  }"
            + "}"
            + "run();"
            + "</script></body></html>";
    }

    private string ToJavaScriptString(string value)
    {
        if (value == null)
            return string.Empty;

        return value
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\r", string.Empty)
            .Replace("\n", "\\n");
    }

    private string ExtractJsonPayload(HttpListenerRequest request)
    {
        string fromQuery = request.QueryString["json"];
        if (!string.IsNullOrEmpty(fromQuery))
            return WebUtility.UrlDecode(fromQuery);

        string fromUrl = request.QueryString["url"];
        if (!string.IsNullOrEmpty(fromUrl))
            return BuildAvatarJsonFromUrl(WebUtility.UrlDecode(fromUrl), request.QueryString["avatarId"]);

        if (request.HasEntityBody)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8))
            {
                string body = reader.ReadToEnd();
                if (!string.IsNullOrWhiteSpace(body))
                {
                    body = body.Trim();
                    if (body.StartsWith("{"))
                        return body;

                    string[] bodyParts = body.Split('&');
                    foreach (string part in bodyParts)
                    {
                        if (part.StartsWith("json=", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = part.Substring(5);
                            return WebUtility.UrlDecode(value);
                        }

                        if (part.StartsWith("url=", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = part.Substring(4);
                            string avatarId = null;
                            foreach (string s in bodyParts)
                            {
                                if (s.StartsWith("avatarId=", StringComparison.OrdinalIgnoreCase))
                                {
                                    avatarId = WebUtility.UrlDecode(s.Substring(9));
                                    break;
                                }
                            }
                            return BuildAvatarJsonFromUrl(WebUtility.UrlDecode(value), avatarId);
                        }
                    }
                }
            }
        }

        return null;
    }

    private string BuildAvatarJsonFromUrl(string url, string avatarId)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        AvatarCallbackPayload payload = new AvatarCallbackPayload();
        payload.url = url;
        payload.urlType = "glb";
        payload.bodyId = "default";
        payload.gender = "unknown";
        payload.avatarId = string.IsNullOrEmpty(avatarId) ? Guid.NewGuid().ToString("N") : avatarId;
        payload.status = "ok";
        return JsonUtility.ToJson(payload);
    }

    private void TryWriteResponse(HttpListenerResponse response, int statusCode, string contentType, string content)
    {
        try
        {
            AddCorsHeaders(response);
            response.StatusCode = statusCode;
            response.ContentType = contentType;
            byte[] buffer = Encoding.UTF8.GetBytes(content ?? string.Empty);
            response.ContentLength64 = buffer.LongLength;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Flush();
        }
        catch
        {
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private void AddCorsHeaders(HttpListenerResponse response)
    {
        try
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
            response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
        }
        catch
        {
        }
    }

    [Serializable]
    private class AvatarCallbackPayload
    {
        public string url;
        public string urlType;
        public string bodyId;
        public string gender;
        public string avatarId;
        public string status;
    }
}
