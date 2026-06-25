using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window quét một folder chứa các file .unitypackage và cho phép import từng package.
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
        window.minSize = new Vector2(360, 240);
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
}
