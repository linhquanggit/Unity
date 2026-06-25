using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window quét một folder chứa các file .unitypackage và cho phép import từng package.
/// Có nút "Update Importer" để git pull bản mới nhất (gồm cả chính script này) rồi tự biên dịch lại.
/// Mở qua menu: Tools > Unity Package Importer.
/// </summary>
public class ToolsImporter : EditorWindow
{
    private const string EditorPrefsKey = "UnityPackageImporter_PackageFolder";

    private string packageFolder;
    private string[] packagePaths = new string[0];
    private Vector2 scrollPosition;

    [MenuItem("Tools/Unity Package Importer")]
    public static void ShowWindow()
    {
        var window = GetWindow<ToolsImporter>("Package Importer");
        window.minSize = new Vector2(360, 260);
    }

    private void OnEnable()
    {
        LoadSettings();
        RefreshPackageList();
    }

    private void LoadSettings()
    {
        packageFolder = EditorPrefs.GetString(EditorPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(packageFolder))
        {
            // Mặc định: folder Tools/ nằm cạnh project (../Tools so với Assets).
            packageFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Tools"));
        }
    }

    private void SaveSettings()
    {
        EditorPrefs.SetString(EditorPrefsKey, packageFolder);
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Unity Package Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Chọn folder chứa các file .unitypackage, rồi bấm Import để nạp package vào project.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Package folder", GUILayout.Width(100));
        EditorGUI.BeginChangeCheck();
        packageFolder = EditorGUILayout.TextField(packageFolder);
        if (EditorGUI.EndChangeCheck())
        {
            SaveSettings();
            RefreshPackageList();
        }
        if (GUILayout.Button("Browse", GUILayout.Width(80)))
        {
            var selected = EditorUtility.OpenFolderPanel("Select Tools Folder", packageFolder, "");
            if (!string.IsNullOrEmpty(selected))
            {
                packageFolder = selected;
                SaveSettings();
                RefreshPackageList();
                GUIUtility.ExitGUI();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // Self-update: kéo bản mới nhất từ git rồi biên dịch lại.
        var prevColor = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.5f, 0.8f, 1f);
        if (GUILayout.Button("⟳ Update Importer (git pull)", GUILayout.Height(26)))
        {
            UpdateImporter();
            GUIUtility.ExitGUI();
        }
        GUI.backgroundColor = prevColor;

        EditorGUILayout.Space();

        if (!Directory.Exists(packageFolder))
        {
            EditorGUILayout.HelpBox($"Folder không tồn tại:\n{packageFolder}", MessageType.Warning);
            if (GUILayout.Button("Refresh"))
            {
                RefreshPackageList();
            }
            return;
        }

        if (GUILayout.Button("Refresh Package List"))
        {
            RefreshPackageList();
        }

        EditorGUILayout.Space();

        if (packagePaths.Length == 0)
        {
            EditorGUILayout.HelpBox("Không tìm thấy file .unitypackage nào trong folder.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField($"Tìm thấy {packagePaths.Length} package:", EditorStyles.boldLabel);
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
        foreach (var path in packagePaths)
        {
            if (GUILayout.Button(Path.GetFileNameWithoutExtension(path), GUILayout.Height(24)))
            {
                ImportPackage(path);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void RefreshPackageList()
    {
        packagePaths = Directory.Exists(packageFolder)
            ? Directory.GetFiles(packageFolder, "*.unitypackage", SearchOption.TopDirectoryOnly)
            : new string[0];
    }

    private void ImportPackage(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogError($"Package không tồn tại: {path}");
            RefreshPackageList();
            return;
        }

        // interactive = true: Unity hiện cửa sổ chọn asset để import.
        AssetDatabase.ImportPackage(path, true);
    }

    // ----------------------------------------------------------------------
    // Self-update qua git
    // ----------------------------------------------------------------------

    private void UpdateImporter()
    {
        // Tìm git repo: ưu tiên từ folder Tools, sau đó từ project root.
        string repoRoot = FindGitRoot(packageFolder)
                          ?? FindGitRoot(Path.GetFullPath(Path.Combine(Application.dataPath, "..")));

        if (string.IsNullOrEmpty(repoRoot))
        {
            EditorUtility.DisplayDialog(
                "Update Importer",
                "Không tìm thấy git repo (.git) từ folder Tools hoặc project root.\n" +
                "Hãy chắc chắn script & Tools nằm trong một git repository.",
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
            RefreshPackageList();
            // Refresh để Unity import package mới và biên dịch lại script đã cập nhật (tool tự update chính nó).
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Update Importer", $"Cập nhật thành công.\n\n{output}", "OK");
        }
        else
        {
            Debug.LogError($"[ToolsImporter] git pull thất bại (exit {exitCode})\n{output}");
            EditorUtility.DisplayDialog("Update Importer", $"git pull thất bại (exit {exitCode}):\n\n{output}", "OK");
        }
    }

    /// <summary>Đi ngược lên cây thư mục tìm folder chứa .git.</summary>
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

    /// <summary>Chạy git với working dir cho trước. Trả exit code, gom stdout+stderr vào output.</summary>
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

    /// <summary>Tìm đường dẫn git (process do Unity khởi chạy có thể thiếu PATH đầy đủ).</summary>
    private static string ResolveGitPath()
    {
        string[] candidates =
        {
            "/usr/bin/git",
            "/usr/local/bin/git",
            "/opt/homebrew/bin/git",
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return "git"; // fallback theo PATH
    }
}
