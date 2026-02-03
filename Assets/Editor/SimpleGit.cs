using UnityEngine;
using UnityEditor;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Generic;

public class GitWindow : EditorWindow
{
    string commitMessage = "";

    string patchContent = "";
    Vector2 patchScrollPos;
    bool forcePatchApply = false;
    bool sanitizePatch = true;
    bool fixWhitespace = true;
    bool allow3Way = true;
    bool ignoreWhitespace = false;
    bool autoStripLevel = true;
    int stripLevel = 1;
    string patchDiagnostics = "";
    string lastPatchLog = "";

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
        GUILayout.Label("Applica Patch Git", EditorStyles.boldLabel);
        GUILayout.Label("Incolla qui la patch (formato git diff):");

        patchScrollPos = GUILayout.BeginScrollView(patchScrollPos, GUILayout.Height(150));
        patchContent = EditorGUILayout.TextArea(patchContent, GUILayout.ExpandHeight(true));
        GUILayout.EndScrollView();

        if (sanitizePatch && !string.IsNullOrEmpty(patchContent))
        {
            PatchStats stats;
            SanitizePatch(patchContent, out stats);
            patchDiagnostics = BuildPatchDiagnostics(stats);
        }
        else if (string.IsNullOrEmpty(patchContent))
        {
            patchDiagnostics = "";
        }

        sanitizePatch = EditorGUILayout.Toggle("Sanitize patch (recommended)", sanitizePatch);
        fixWhitespace = EditorGUILayout.Toggle("Fix whitespace (--whitespace=fix)", fixWhitespace);
        allow3Way = EditorGUILayout.Toggle("Allow 3-way fallback", allow3Way);
        ignoreWhitespace = EditorGUILayout.Toggle("Ignore whitespace (last resort)", ignoreWhitespace);
        autoStripLevel = EditorGUILayout.Toggle("Auto -p level", autoStripLevel);
        if (!autoStripLevel)
        {
            stripLevel = EditorGUILayout.IntSlider("Strip level (-p)", stripLevel, 0, 2);
        }
        forcePatchApply = EditorGUILayout.Toggle("Force Apply (ignora errori)", forcePatchApply);

        if (!sanitizePatch)
        {
            patchDiagnostics = "";
        }

        if (!string.IsNullOrEmpty(patchDiagnostics))
        {
            EditorGUILayout.HelpBox(patchDiagnostics, MessageType.Info);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Apply (Safe)"))
        {
            ApplyPatch(false);
        }
        if (GUILayout.Button("Apply (Force)"))
        {
            ApplyPatch(true);
        }
        if (GUILayout.Button("Clear"))
        {
            patchContent = "";
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(lastPatchLog))
        {
            EditorGUILayout.HelpBox(lastPatchLog, MessageType.None);
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

    void ApplyPatch(bool force)
    {
        if (string.IsNullOrWhiteSpace(patchContent))
        {
            UnityEngine.Debug.LogWarning("[VCS] Patch vuota, inserisci il contenuto della patch.");
            EditorUtility.DisplayDialog("Patch Git", "Il campo patch è vuoto!\nIncolla il contenuto della patch prima di applicarla.", "OK");
            return;
        }

        string root = Directory.GetParent(Application.dataPath).FullName;
        string tempPatchFile = Path.Combine(root, "temp_patch.patch");

        string sanitized = patchContent;
        PatchStats stats = new PatchStats();
        if (sanitizePatch)
        {
            sanitized = SanitizePatch(patchContent, out stats);
            patchDiagnostics = BuildPatchDiagnostics(stats);
        }
        else
        {
            patchDiagnostics = "";
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            EditorUtility.DisplayDialog("Patch Git", "Patch vuota dopo sanitizzazione.", "OK");
            return;
        }

        try
        {
            File.WriteAllText(tempPatchFile, sanitized, new UTF8Encoding(false));
            EnsureNewlineAtEnd(tempPatchFile);

            var dirtyCheck = RunCmdWithResult("/C git status --porcelain");
            if (!string.IsNullOrEmpty(dirtyCheck.StdOut))
            {
                bool proceed = EditorUtility.DisplayDialog("Working tree non pulito",
                    "Sono presenti modifiche non committate. Applicare comunque la patch?",
                    "Continua", "Annulla");
                if (!proceed)
                {
                    return;
                }
            }

            int pLevel = autoStripLevel ? DetectStripLevel(sanitized) : stripLevel;
            string baseOptions = BuildApplyBaseOptions(pLevel);
            string whitespaceOptions = fixWhitespace ? " --whitespace=fix" : " --whitespace=nowarn";
            string patchPath = " temp_patch.patch";

            var checkResult = RunCmdWithResult($"/C git apply --check{baseOptions}{whitespaceOptions}{patchPath}");
            lastPatchLog = FormatCmdResult("git apply --check", checkResult);

            if (checkResult.ExitCode != 0)
            {
                UnityEngine.Debug.LogWarning("[VCS] git apply --check fallito, provo fallback applicazione.");
            }

            var attempts = new List<ApplyAttempt>
            {
                new ApplyAttempt("apply --reject --recount", $"/C git apply --reject --recount{baseOptions}{whitespaceOptions}{patchPath}")
            };

            if (allow3Way)
            {
                attempts.Add(new ApplyAttempt("apply --3way", $"/C git apply --3way{baseOptions}{whitespaceOptions}{patchPath}"));
            }

            if (ignoreWhitespace || force || forcePatchApply)
            {
                attempts.Add(new ApplyAttempt("apply --ignore-space-change --ignore-whitespace",
                    $"/C git apply --ignore-space-change --ignore-whitespace{baseOptions}{whitespaceOptions}{patchPath}"));
            }

            CmdResult finalResult = default;
            bool applied = false;
            foreach (var attempt in attempts)
            {
                finalResult = RunCmdWithResult(attempt.Command);
                lastPatchLog = FormatCmdResult(attempt.Label, finalResult);
                if (finalResult.ExitCode == 0)
                {
                    applied = true;
                    break;
                }
            }

            if (!applied)
            {
                EditorUtility.DisplayDialog("Patch Git",
                    "Patch non applicata. Controlla l'output nei log e prova con opzioni diverse.",
                    "OK");
                return;
            }

            UnityEngine.Debug.Log("[VCS] Patch applicata. Ricordati di fare commit e push se vuoi condividere le modifiche.");
            
            string msgExtra = "\n\nSe ci sono conflitti, troverai file .rej con le parti non applicate.";
            EditorUtility.DisplayDialog("Patch Git", $"Patch applicata con successo!{msgExtra}\n\nRicorda:\n- I file sono stati modificati\n- Fai commit per salvare le modifiche\n- Fai push per condividerle", "OK");
            
            patchContent = "";
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"[VCS] Errore nell'applicazione della patch: {e.Message}");
            EditorUtility.DisplayDialog("Errore Patch", $"Errore durante l'applicazione della patch:\n{e.Message}", "OK");
        }
        finally
        {
            if (File.Exists(tempPatchFile))
            {
                try { File.Delete(tempPatchFile); } catch { }
            }
        }
    }

    string SanitizePatch(string input, out PatchStats stats)
    {
        stats = new PatchStats();
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        stats.HasCRLF = input.Contains("\r\n");
        string normalized = input.Replace("\r\n", "\n").Replace("\r", "\n");
        stats.LineCount = normalized.Split('\n').Length;
        stats.HasDiffHeader = normalized.Contains("diff --git");
        stats.HasFence = normalized.Contains("```");
        stats.HasCopyPatch = normalized.ToLower().Contains("copy patch");
        stats.HasUnityYaml = normalized.Contains(".unity") || normalized.Contains(".prefab");

        normalized = normalized.Replace("\u00A0", " ").Replace("\u200B", "");

        var sb = new StringBuilder();
        using (var reader = new StringReader(normalized))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("```"))
                    continue;
                if (string.Equals(trimmed, "copy patch", System.StringComparison.OrdinalIgnoreCase))
                    continue;
                sb.AppendLine(line);
            }
        }

        string result = sb.ToString();
        if (!result.EndsWith("\n"))
            result += "\n";

        return result;
    }

    string BuildPatchDiagnostics(PatchStats stats)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Righe: {stats.LineCount}");
        sb.AppendLine($"diff --git: {(stats.HasDiffHeader ? "sì" : "no")}");
        if (stats.HasCRLF)
            sb.AppendLine("CRLF rilevati: normalizzati a LF.");
        if (stats.HasFence)
            sb.AppendLine("Fence ``` rilevate e rimosse.");
        if (stats.HasCopyPatch)
            sb.AppendLine("\"copy patch\" rilevato e rimosso.");
        if (stats.HasUnityYaml)
            sb.AppendLine("Nota: patch include file Unity (.unity/.prefab) → patch fragile.");
        return sb.ToString().Trim();
    }

    int DetectStripLevel(string patchText)
    {
        if (patchText.Contains("diff --git a/") || patchText.Contains("diff --git b/"))
            return 1;
        return 0;
    }

    string BuildApplyBaseOptions(int pLevel)
    {
        return $" -p{pLevel}";
    }

    void EnsureNewlineAtEnd(string filePath)
    {
        string content = File.ReadAllText(filePath, Encoding.UTF8);
        if (!content.EndsWith("\n"))
        {
            File.AppendAllText(filePath, "\n", Encoding.UTF8);
        }
    }

    struct PatchStats
    {
        public int LineCount;
        public bool HasDiffHeader;
        public bool HasFence;
        public bool HasCopyPatch;
        public bool HasUnityYaml;
        public bool HasCRLF;
    }

    struct ApplyAttempt
    {
        public string Label;
        public string Command;
        public ApplyAttempt(string label, string command)
        {
            Label = label;
            Command = command;
        }
    }

    string FormatCmdResult(string label, CmdResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine(label);
        if (!string.IsNullOrEmpty(result.StdOut))
            sb.AppendLine(result.StdOut.Trim());
        if (!string.IsNullOrEmpty(result.StdErr))
            sb.AppendLine(result.StdErr.Trim());
        sb.AppendLine($"ExitCode: {result.ExitCode}");
        return sb.ToString().Trim();
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
            if (result.StdErr.Contains("error:") || result.StdErr.Contains("fatal:"))
                UnityEngine.Debug.LogError($"[VCS] Errore:\n{result.StdErr}");
            else
                UnityEngine.Debug.Log($"[VCS] Info:\n{result.StdErr}");
        }

        if (string.IsNullOrEmpty(result.StdOut) && string.IsNullOrEmpty(result.StdErr))
        {
            UnityEngine.Debug.Log("[VCS] Operazione completata (nessun output)");
        }
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
        startInfo.RedirectStandardError = true;
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
