using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;

public class GitWindow : EditorWindow
{
    string commitMessage = "";
    bool showConfig = false;

    const string DIR_A = ".git_stream_a";
    const string DIR_B = ".git_stream_b";
    const string DIR_ACTIVE = ".git";
    const string PREF_MODE = "VCS_Storage_Slot";

    [MenuItem("Github/Quick Push")]
    public static void QuickPush()
    {
        PerformGitPush("update");
    }

    [MenuItem("Github/Dashboard")]
    public static void ShowWindow()
    {
        GetWindow<GitWindow>("VCS Dashboard");
    }

    void OnGUI()
    {
        int currentMode = EditorPrefs.GetInt(PREF_MODE, -1);

        string status = "NESSUNO (Disconnesso)";
        if (Directory.Exists(Path.Combine(Application.dataPath, "..", DIR_ACTIVE)))
        {
            status = currentMode == 0 ? "STREAM A" : "STREAM B";
            GUI.color = currentMode == 0 ? Color.green : Color.cyan;
        }
        else
        {
            GUI.color = Color.red;
        }

        GUILayout.Space(10);
        GUILayout.Label($"STATO: {status}", EditorStyles.boldLabel);
        GUI.color = Color.white;
        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Carica A")) SwapFolder(0);
        if (GUILayout.Button("Carica B")) SwapFolder(1);
        GUILayout.EndHorizontal();

        GUILayout.Space(15);
        GUILayout.Label("Operazioni", EditorStyles.boldLabel);

        GUILayout.Label("Note:");
        commitMessage = EditorGUILayout.TextField(commitMessage);

        if (GUILayout.Button("Push"))
        {
            string msg = string.IsNullOrEmpty(commitMessage) ? "update" : commitMessage;
            PerformGitPush(msg);
            commitMessage = "";
        }

        if (GUILayout.Button("Pull"))
        {
            RunCmd("/C git pull");
        }

        // Se serve resettare manualmente lo stato dello script
        GUILayout.Space(20);
        if (GUILayout.Button("Force Unlink (Emergency)"))
        {
            // Rinomina la cartella attiva in base all'ultima memoria nota
            string root = Directory.GetParent(Application.dataPath).FullName;
            string active = Path.Combine(root, DIR_ACTIVE);
            if (Directory.Exists(active))
            {
                string target = currentMode == 0 ? DIR_A : DIR_B;
                Directory.Move(active, Path.Combine(root, target));
            }
            EditorPrefs.SetInt(PREF_MODE, -1);
        }
    }

    void SwapFolder(int targetMode)
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        string activePath = Path.Combine(root, DIR_ACTIVE);
        int currentMode = EditorPrefs.GetInt(PREF_MODE, -1);

        // 1. Se c'è una cartella attiva, mettila via
        if (Directory.Exists(activePath))
        {
            if (currentMode == targetMode) return; // Già attivo

            string storageName = currentMode == 0 ? DIR_A : DIR_B;
            string storagePath = Path.Combine(root, storageName);

            // Sicurezza: se la destinazione esiste già (errore precedente), cancellala o gestiscila
            if (Directory.Exists(storagePath)) Directory.Delete(storagePath, true);

            Directory.Move(activePath, storagePath);
            UnityEngine.Debug.Log($"[VCS] Archiviato {storageName}");
        }

        // 2. Tira fuori la cartella target
        string targetStorageName = targetMode == 0 ? DIR_A : DIR_B;
        string targetStoragePath = Path.Combine(root, targetStorageName);

        if (Directory.Exists(targetStoragePath))
        {
            Directory.Move(targetStoragePath, activePath);
            EditorPrefs.SetInt(PREF_MODE, targetMode);
            UnityEngine.Debug.Log($"[VCS] Caricato {targetStorageName}");
        }
        else
        {
            UnityEngine.Debug.LogError($"[VCS] Errore: Cartella {targetStorageName} non trovata!");
        }

        AssetDatabase.Refresh();
    }

    static void PerformGitPush(string message)
    {
        RunCmd($"/C git add . & git commit -m \"{message}\" & git push");
    }

    static void RunCmd(string args)
    {
        Process process = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = args;
        process.StartInfo = startInfo;
        process.Start();
    }
}