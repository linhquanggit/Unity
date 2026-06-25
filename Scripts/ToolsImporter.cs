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
    private const string PrefScriptPath = "ToolsImporter_ScriptPath";

    private const string UserAgent = "UnityEditor-ToolsImporter";

    private string owner;
    private string repo;
    private string branch;
    private string folder;
    private string scriptPath; // đường dẫn script trong repo, để self-update fetch raw

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
        scriptPath = EditorPrefs.GetString(PrefScriptPath, "Scripts/ToolsImporter.cs");
    }

    private void SaveConfig()
    {
        EditorPrefs.SetString(PrefOwner, owner);
        EditorPrefs.SetString(PrefRepo, repo);
        EditorPrefs.SetString(PrefBranch, branch);
        EditorPrefs.SetString(PrefFolder, folder);
        EditorPrefs.SetString(PrefScriptPath, scriptPath);
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
        scriptPath = EditorGUILayout.TextField("Script path", scriptPath);
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
    // Self-update: fetch script mới nhất từ GitHub (raw) rồi ghi đè chính nó
    // ----------------------------------------------------------------------

    private void UpdateImporter()
    {
        // Vị trí asset của chính script này trong project (vd: Assets/Editor/ToolsImporter.cs).
        var mono = MonoScript.FromScriptableObject(this);
        string assetPath = mono != null ? AssetDatabase.GetAssetPath(mono) : null;
        if (string.IsNullOrEmpty(assetPath))
        {
            EditorUtility.DisplayDialog("Update Importer",
                "Không xác định được vị trí script trong project.", "OK");
            return;
        }
        string localFullPath = Path.GetFullPath(assetPath);

        string rawUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{scriptPath}";
        string newContent;
        try
        {
            EditorUtility.DisplayProgressBar("Update Importer", "Đang fetch script mới từ GitHub...", 0.5f);
            using (var req = UnityWebRequest.Get(rawUrl))
            {
                req.SetRequestHeader("User-Agent", UserAgent);
                SendBlocking(req);
                if (!string.IsNullOrEmpty(req.error))
                {
                    Debug.LogError($"[ToolsImporter] Fetch lỗi: {req.error}\nURL: {rawUrl}");
                    EditorUtility.DisplayDialog("Update Importer",
                        $"Fetch lỗi:\n{req.error}\n\nURL: {rawUrl}", "OK");
                    return;
                }
                newContent = req.downloadHandler.text;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ToolsImporter] Update exception: {e}");
            EditorUtility.DisplayDialog("Update Importer", $"Lỗi: {e.Message}", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Sanity check để không ghi đè bằng nội dung rác (vd trang 404).
        if (string.IsNullOrEmpty(newContent) || !newContent.Contains("class ToolsImporter"))
        {
            EditorUtility.DisplayDialog("Update Importer",
                $"Nội dung tải về không hợp lệ (không thấy class ToolsImporter).\nURL: {rawUrl}", "OK");
            return;
        }

        string current = File.Exists(localFullPath) ? File.ReadAllText(localFullPath) : string.Empty;
        if (current == newContent)
        {
            EditorUtility.DisplayDialog("Update Importer", "Đã là bản mới nhất, không có thay đổi.", "OK");
            return;
        }

        File.WriteAllText(localFullPath, newContent);
        AssetDatabase.ImportAsset(assetPath);
        AssetDatabase.Refresh(); // Unity biên dịch lại -> tool tự cập nhật chính nó
        Debug.Log($"[ToolsImporter] Đã cập nhật script từ {rawUrl} -> {assetPath}");
        EditorUtility.DisplayDialog("Update Importer",
            $"Đã cập nhật script từ GitHub:\n{assetPath}\n\nUnity sẽ biên dịch lại.", "OK");
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
