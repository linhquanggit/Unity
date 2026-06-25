#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Editor window: bấm "Load" để lấy danh sách .unitypackage từ repo GitHub,
/// vẽ mỗi package thành một node; click node -> tải về và import vào project.
/// Có nút "Update Importer" để git pull tự cập nhật chính script này.
/// Mở qua menu: Tools > Unity Package Importer.
/// </summary>
public class ToolsImporter : EditorWindow
{
    // ---- Cấu hình repo (lưu EditorPrefs) ----
    private const string PrefOwner = "ToolsImporter_Owner";
    private const string PrefRepo = "ToolsImporter_Repo";
    private const string PrefBranch = "ToolsImporter_Branch";
    private const string PrefFolder = "ToolsImporter_Folder";

    private const string UserAgent = "UnityEditor-ToolsImporter";

    private string owner;
    private string repo;
    private string branch;
    private string folder;

    private GitHubContent[] remotePackages = new GitHubContent[0];
    private bool loaded;
    private string statusMessage = "Bấm Load để lấy danh sách package từ GitHub.";
    private Vector2 scrollPosition;
    private bool showConfig;

    [MenuItem("Tools/Unity Package Importer")]
    public static void ShowWindow()
    {
        var window = GetWindow<ToolsImporter>("Package Importer");
        window.minSize = new Vector2(380, 300);
    }

    private void OnEnable()
    {
        owner = EditorPrefs.GetString(PrefOwner, "linhquanggit");
        repo = EditorPrefs.GetString(PrefRepo, "Unity");
        branch = EditorPrefs.GetString(PrefBranch, "main");
        folder = EditorPrefs.GetString(PrefFolder, "Tools");
    }

    private void SaveConfig()
    {
        EditorPrefs.SetString(PrefOwner, owner);
        EditorPrefs.SetString(PrefRepo, repo);
        EditorPrefs.SetString(PrefBranch, branch);
        EditorPrefs.SetString(PrefFolder, folder);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Unity Package Importer", EditorStyles.boldLabel);

        DrawConfig();

        EditorGUILayout.Space();

        // ---- Load + Update ----
        EditorGUILayout.BeginHorizontal();
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
        if (GUILayout.Button("⤓ Load", GUILayout.Height(28)))
        {
            LoadFromGit();
            GUIUtility.ExitGUI();
        }
        GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
        if (GUILayout.Button("⟳ Update Importer", GUILayout.Height(28), GUILayout.Width(150)))
        {
            UpdateImporter();
            GUIUtility.ExitGUI();
        }
        GUI.backgroundColor = prevColor;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(statusMessage, loaded && remotePackages.Length > 0 ? MessageType.Info : MessageType.None);

        if (!loaded) return;

        if (remotePackages.Length == 0)
        {
            EditorGUILayout.HelpBox($"Không có .unitypackage nào trong '{folder}' trên repo.", MessageType.Warning);
            return;
        }

        EditorGUILayout.LabelField($"{remotePackages.Length} package — click để import:", EditorStyles.boldLabel);
        DrawNodes();
    }

    private void DrawConfig()
    {
        showConfig = EditorGUILayout.Foldout(showConfig, "Repo config", true);
        if (!showConfig) return;

        EditorGUI.indentLevel++;
        EditorGUI.BeginChangeCheck();
        owner = EditorGUILayout.TextField("Owner", owner);
        repo = EditorGUILayout.TextField("Repo", repo);
        branch = EditorGUILayout.TextField("Branch", branch);
        folder = EditorGUILayout.TextField("Folder", folder);
        if (EditorGUI.EndChangeCheck()) SaveConfig();
        EditorGUILayout.LabelField("API", $"github.com/{owner}/{repo} @ {branch}/{folder}", EditorStyles.miniLabel);
        EditorGUI.indentLevel--;
    }

    private void DrawNodes()
    {
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (var pkg in remotePackages)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            if (GUILayout.Button(pkg.name, EditorStyles.boldLabel))
            {
                DownloadAndImport(pkg);
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.LabelField(FormatSize(pkg.size), EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }

    // ----------------------------------------------------------------------
    // Load danh sách package từ GitHub API
    // ----------------------------------------------------------------------

    private void LoadFromGit()
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/contents/{folder}?ref={branch}";
        try
        {
            EditorUtility.DisplayProgressBar("Load", "Đang lấy danh sách package từ GitHub...", 0.5f);
            using (var req = UnityWebRequest.Get(url))
            {
                req.SetRequestHeader("User-Agent", UserAgent);
                req.SetRequestHeader("Accept", "application/vnd.github+json");
                SendBlocking(req);

                if (!string.IsNullOrEmpty(req.error))
                {
                    string body = req.downloadHandler != null ? req.downloadHandler.text : string.Empty;
                    Debug.LogError($"[ToolsImporter] Load lỗi: {req.error}\n{body}");
                    statusMessage = $"Load lỗi: {req.error}";
                    remotePackages = new GitHubContent[0];
                    loaded = true;
                    return;
                }

                string json = req.downloadHandler.text;
                var list = JsonUtility.FromJson<GitHubContentList>("{\"items\":" + json + "}");
                remotePackages = (list != null && list.items != null ? list.items : new GitHubContent[0])
                    .Where(c => c.type == "file" && c.name != null && c.name.EndsWith(".unitypackage"))
                    .ToArray();
                loaded = true;
                statusMessage = $"Đã load {remotePackages.Length} package từ {owner}/{repo}@{branch}/{folder}.";
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ToolsImporter] Load exception: {e}");
            statusMessage = $"Load exception: {e.Message}";
            remotePackages = new GitHubContent[0];
            loaded = true;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private void DownloadAndImport(GitHubContent pkg)
    {
        if (string.IsNullOrEmpty(pkg.download_url))
        {
            EditorUtility.DisplayDialog("Import", $"Package '{pkg.name}' không có download_url.", "OK");
            return;
        }

        string cacheDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Temp", "ToolsImporterCache"));
        string localPath = Path.Combine(cacheDir, pkg.name);

        try
        {
            Directory.CreateDirectory(cacheDir);
            EditorUtility.DisplayProgressBar("Import", $"Đang tải {pkg.name} ({FormatSize(pkg.size)})...", 0.5f);

            using (var req = UnityWebRequest.Get(pkg.download_url))
            {
                req.SetRequestHeader("User-Agent", UserAgent);
                SendBlocking(req);

                if (!string.IsNullOrEmpty(req.error))
                {
                    Debug.LogError($"[ToolsImporter] Tải '{pkg.name}' lỗi: {req.error}");
                    EditorUtility.DisplayDialog("Import", $"Tải '{pkg.name}' lỗi:\n{req.error}", "OK");
                    return;
                }
                File.WriteAllBytes(localPath, req.downloadHandler.data);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ToolsImporter] Import exception: {e}");
            EditorUtility.DisplayDialog("Import", $"Lỗi: {e.Message}", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // interactive = true: Unity hiện cửa sổ chọn asset để import.
        AssetDatabase.ImportPackage(localPath, true);
    }

    /// <summary>Gửi UnityWebRequest và chờ xong (blocking, dùng trong editor).</summary>
    private static void SendBlocking(UnityWebRequest req)
    {
        var op = req.SendWebRequest();
        while (!op.isDone)
        {
            System.Threading.Thread.Sleep(10);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / (1024f * 1024f):0.0} MB";
        if (bytes >= 1024) return $"{bytes / 1024f:0.0} KB";
        return $"{bytes} B";
    }

    // ----------------------------------------------------------------------
    // Self-update qua git (pull repo chứa script này)
    // ----------------------------------------------------------------------

    private void UpdateImporter()
    {
        string repoRoot = FindGitRoot(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));
        if (string.IsNullOrEmpty(repoRoot))
        {
            EditorUtility.DisplayDialog(
                "Update Importer",
                "Không tìm thấy git repo (.git) từ project root.\n" +
                "Self-update chỉ chạy khi project nằm trong git repository.",
                "OK");
            return;
        }

        string output;
        int exitCode;
        EditorUtility.DisplayProgressBar("Update Importer", $"git pull tại:\n{repoRoot}", 0.5f);
        try
        {
            exitCode = RunGit(repoRoot, "pull --ff-only", out output);
        }
        catch (System.Exception e)
        {
            EditorUtility.ClearProgressBar();
            Debug.LogError($"[ToolsImporter] Không chạy được git: {e.Message}");
            EditorUtility.DisplayDialog("Update Importer", $"Không chạy được git:\n{e.Message}", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        if (exitCode == 0)
        {
            Debug.Log($"[ToolsImporter] git pull OK tại {repoRoot}\n{output}");
            AssetDatabase.Refresh(); // biên dịch lại script đã cập nhật
            EditorUtility.DisplayDialog("Update Importer", $"Cập nhật thành công.\n\n{output}", "OK");
        }
        else
        {
            Debug.LogError($"[ToolsImporter] git pull thất bại (exit {exitCode})\n{output}");
            EditorUtility.DisplayDialog("Update Importer", $"git pull thất bại (exit {exitCode}):\n\n{output}", "OK");
        }
    }

    private static string FindGitRoot(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                    File.Exists(Path.Combine(dir.FullName, ".git")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static int RunGit(string workingDir, string args, out string output)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ResolveGitPath(),
            Arguments = args,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var process = System.Diagnostics.Process.Start(psi))
        {
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            output = (stdout + "\n" + stderr).Trim();
            return process.ExitCode;
        }
    }

    private static string ResolveGitPath()
    {
        string[] candidates = { "/usr/bin/git", "/usr/local/bin/git", "/opt/homebrew/bin/git" };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return "git";
    }

    // ---- JSON model cho GitHub Contents API ----
    [System.Serializable]
    private class GitHubContent
    {
        public string name;
        public string path;
        public string type;
        public long size;
        public string download_url;
    }

    [System.Serializable]
    private class GitHubContentList
    {
        public GitHubContent[] items;
    }
}
#endif
