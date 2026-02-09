using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class GitWindow : EditorWindow
{
    string commitMessage = "";

    Vector2 commitHistoryScroll;
    List<CommitInfo> commitHistory = new List<CommitInfo>();
    int selectedCommitIndex = -1;

    const string DIR_A = ".git_stream_a";
    const string DIR_B = ".git_stream_b";
    const string DIR_ACTIVE = ".git";
    const string PREF_MODE = "VCS_Storage_Slot";

    [MenuItem("Github/Quick Push")]
    public static void QuickPush()
    {
        PerformGitPush("update");
    }

    [MenuItem("Github/Quick Pull")]
    public static void QuickPull()
    {
        UnityEngine.Debug.Log("[VCS] Avvio Quick Pull...");
        RunCmd("/C git pull");
    }

    [MenuItem("Github/Dashboard")]
    public static void ShowWindow()
    {
        GetWindow<GitWindow>("VCS Dashboard");
    }

    void OnEnable()
    {
        LoadCommitHistory();
        minSize = new Vector2(400, 500);
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
            UnityEngine.Debug.Log($"[VCS] Avvio Push: '{msg}'...");
            PerformGitPush(msg);
            commitMessage = "";
        }

        if (GUILayout.Button("Pull"))
        {
            UnityEngine.Debug.Log("[VCS] Avvio Pull...");
            RunCmd("/C git pull");
        }
        GUILayout.Space(15);
        GUILayout.Label("Revert a Commit", EditorStyles.boldLabel);

        if (commitHistory.Count > 0)
        {
            GUILayout.Label($"Commit disponibili ({commitHistory.Count}):");
            commitHistoryScroll = GUILayout.BeginScrollView(commitHistoryScroll, GUILayout.Height(120));

            for (int i = 0; i < commitHistory.Count; i++)
            {
                string label = $"{commitHistory[i].Hash.Substring(0, 7)} - {commitHistory[i].Message} ({commitHistory[i].Author}, {commitHistory[i].Date})";
                
                if (GUILayout.Button(label, selectedCommitIndex == i ? EditorStyles.radioButton : EditorStyles.label))
                {
                    selectedCommitIndex = i;
                }
            }

            GUILayout.EndScrollView();

            GUILayout.Space(10);
            if (selectedCommitIndex >= 0 && selectedCommitIndex < commitHistory.Count)
            {
                GUILayout.Label($"Selezionato: {commitHistory[selectedCommitIndex].Message}");
                
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Soft Revert (mantieni modifiche)"))
                {
                    RevertToCommit(selectedCommitIndex, false);
                }
                if (GUILayout.Button("Hard Revert (ATTENZIONE!)", GUILayout.Width(150)))
                {
                    if (EditorUtility.DisplayDialog("Revert Confermato?",
                        $"Sei sicuro? Questo cancellerà TUTTE le modifiche dopo il commit:\n\n{commitHistory[selectedCommitIndex].Message}\n\nQuesta azione è IRREVERSIBILE!",
                        "Sì, revert hard", "Annulla"))
                    {
                        RevertToCommit(selectedCommitIndex, true);
                    }
                }
                GUILayout.EndHorizontal();
            }
            else
            {
                GUI.enabled = false;
                GUILayout.Button("Seleziona un commit per applicare il revert");
                GUI.enabled = true;
            }
        }
        else
        {
            GUILayout.Label("Caricamento commit in corso...", EditorStyles.helpBox);
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
        LoadCommitHistory();
    }

    static void PerformGitPush(string message)
    {
        RunCmd($"/C git add . & git commit -m \"{message}\" & git push");
    }

    static void RunCmd(string args)
    {
        var result = RunCmdWithResult(args);
        if (!string.IsNullOrEmpty(result.StdOut))
        {
            UnityEngine.Debug.Log($"[VCS] Operazione completata:\n{result.StdOut}");
        }

        if (!string.IsNullOrEmpty(result.StdErr))
        {
            LogCmdStderr(result.StdErr, result.ExitCode);
        }

        if (result.ExitCode != 0 && string.IsNullOrEmpty(result.StdErr))
        {
            UnityEngine.Debug.LogError($"[VCS] Errore: comando terminato con ExitCode {result.ExitCode}");
        }

        if (result.ExitCode == 0 && string.IsNullOrEmpty(result.StdOut) && string.IsNullOrEmpty(result.StdErr))
        {
            UnityEngine.Debug.Log("[VCS] Operazione completata (nessun output)");
        }
    }

    static void LogCmdStderr(string stderr, int exitCode)
    {
        StringBuilder warnings = new StringBuilder();
        StringBuilder errors = new StringBuilder();
        StringBuilder info = new StringBuilder();

        string[] lines = stderr.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith("warning:", System.StringComparison.OrdinalIgnoreCase))
            {
                warnings.AppendLine(line);
                continue;
            }

            bool isError = line.IndexOf("fatal:", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                           line.IndexOf("error:", System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (isError)
                errors.AppendLine(line);
            else
                info.AppendLine(line);
        }

        if (warnings.Length > 0)
            UnityEngine.Debug.LogWarning($"[VCS] Warning:\n{warnings.ToString().Trim()}");

        if (errors.Length > 0)
            UnityEngine.Debug.LogError($"[VCS] Errore:\n{errors.ToString().Trim()}");
        else if (exitCode != 0)
            UnityEngine.Debug.LogError($"[VCS] Errore: comando terminato con ExitCode {exitCode}");

        if (info.Length > 0)
            UnityEngine.Debug.Log($"[VCS] Info:\n{info.ToString().Trim()}");
    }

    static CmdResult RunCmdWithResult(string args)
    {
        Process process = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = args;
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;
        startInfo.WorkingDirectory = Directory.GetParent(Application.dataPath).FullName;

        process.StartInfo = startInfo;

        StringBuilder output = new StringBuilder();
        StringBuilder error = new StringBuilder();

        process.OutputDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                output.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                error.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new CmdResult
        {
            StdOut = output.ToString().Trim(),
            StdErr = error.ToString().Trim(),
            ExitCode = process.ExitCode
        };
    }

    struct CmdResult
    {
        public string StdOut;
        public string StdErr;
        public int ExitCode;
    }

    struct CommitInfo
    {
        public string Hash;
        public string Message;
        public string Author;
        public string Date;
    }

    void LoadCommitHistory()
    {
        commitHistory.Clear();
        selectedCommitIndex = -1;

        UnityEngine.Debug.Log("[VCS] Caricamento storico commit...");
        
        string root = Directory.GetParent(Application.dataPath).FullName;
        
        Process process = new Process();
        ProcessStartInfo startInfo = new ProcessStartInfo();
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.FileName = "cmd.exe";
        startInfo.Arguments = "/C git log --pretty=format:\"%H|%s|%an|%ad\" --date=short";
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.CreateNoWindow = true;
        startInfo.WorkingDirectory = root;

        process.StartInfo = startInfo;

        StringBuilder output = new StringBuilder();
        process.OutputDataReceived += (sender, e) => {
            if (!string.IsNullOrEmpty(e.Data))
                output.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.WaitForExit();

        string[] lines = output.ToString().Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.RemoveEmptyEntries);
        
        foreach (string line in lines)
        {
            string[] parts = line.Split('|');
            if (parts.Length >= 4)
            {
                commitHistory.Add(new CommitInfo
                {
                    Hash = parts[0],
                    Message = parts[1],
                    Author = parts[2],
                    Date = parts[3]
                });
            }
        }

        UnityEngine.Debug.Log($"[VCS] Caricati {commitHistory.Count} commit");
    }

    void RevertToCommit(int commitIndex, bool hardReset)
    {
        if (commitIndex < 0 || commitIndex >= commitHistory.Count)
            return;

        CommitInfo commit = commitHistory[commitIndex];
        
        if (hardReset)
        {
            UnityEngine.Debug.Log($"[VCS] Hard reset a commit {commit.Hash}...");
            RunCmd($"/C git reset --hard {commit.Hash}");
        }
        else
        {
            UnityEngine.Debug.Log($"[VCS] Soft revert a commit {commit.Hash}...");
            RunCmd($"/C git revert --no-edit {commit.Hash}");
        }

        EditorUtility.DisplayDialog("Revert Completato", 
            $"Revert a '{commit.Message}' completato!\n\nRicorda di fare push per condividere i cambiamenti.", "OK");
    }
}

