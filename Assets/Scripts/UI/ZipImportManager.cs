using System;
using System.Collections;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class ZipImportManager : MonoBehaviour
{
    public TitleSceneManager titleSceneManager;

    [Header("UI")]
    public Button importButton;

    // Note: This is NOT implemented
    public InputField desktopZipPathInput;

    [Header("Import Settings")]
    [Tooltip("If true, unzip into the persistentDataPath root. If false, unzip into a subfolder.")]
    public bool unzipToRoot = true;

    [Tooltip("If unzipToRoot is false, unzip will go to persistentDataPath/<subfolderName>.")]
    public string subfolderName = "ImportedStudyData";

    [Tooltip("If true, overwrites existing files.")]
    public bool overwriteFiles = true;

    // WebGL chunked base64 transfer state
    private MemoryStream webglZipBytes;
    private string webglZipFileName;

    private void Awake()
    {
        if (importButton != null)
            importButton.onClick.AddListener(OnImportButtonPressed);
    }

    public void OnImportButtonPressed()
    {
        SetStatus($"Persistent path: {Application.persistentDataPath}");

#if UNITY_WEBGL && !UNITY_EDITOR
        // WebGL: open browser file picker
        WebGL_OpenZipPicker();
        return;
#endif

#if UNITY_EDITOR
        // Editor: native picker
        string path = EditorUtility.OpenFilePanel("Select ZIP", "", "zip");
        if (string.IsNullOrEmpty(path))
        {
            SetStatus("Import canceled.");
            return;
        }
        StartCoroutine(ImportZipFromDiskPathCoroutine(path));
        return;
#else
        // Standalone runtime: no built-in file picker without plugins.
        // Use a pasted path, or instruct user to place file in a known location.
        if (desktopZipPathInput == null || string.IsNullOrWhiteSpace(desktopZipPathInput.text))
        {
            SetStatus("Standalone build: paste a .zip path into the input field, then press Import.");
            return;
        }

        string runtimePath = desktopZipPathInput.text.Trim().Trim('"');
        if (!File.Exists(runtimePath))
        {
            SetStatus("File not found: " + runtimePath);
            return;
        }
        StartCoroutine(ImportZipFromDiskPathCoroutine(runtimePath));
        return;
#endif
    }

    private IEnumerator ImportZipFromDiskPathCoroutine(string zipPath)
    {
        SetStatus("Reading zip: " + zipPath);

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(zipPath);
        }
        catch (Exception e)
        {
            SetStatus("Failed reading zip: " + e.Message);
            yield break;
        }

        yield return ImportZipBytesCoroutine(bytes, Path.GetFileName(zipPath));
    }

    private IEnumerator ImportZipBytesCoroutine(byte[] zipBytes, string fileName)
    {
        if (zipBytes == null || zipBytes.Length == 0)
        {
            SetStatus("Zip is empty.");
            yield break;
        }

        // Save the zip itself into persistentDataPath (optional but you asked for it)
        string importRoot = GetImportRoot();
        Directory.CreateDirectory(importRoot);

        string savedZipPath = Path.Combine(importRoot, fileName);
        try
        {
            File.WriteAllBytes(savedZipPath, zipBytes);
        }
        catch (Exception e)
        {
            SetStatus("Failed writing zip to persistent storage: " + e.Message);
            yield break;
        }

        SetStatus("Saved zip to: " + savedZipPath + "\nUnzipping...");

        // Unzip safely (prevent Zip Slip)
        try
        {
            UnzipToDirectorySafe(savedZipPath, importRoot, overwriteFiles);
        }
        catch (Exception e)
        {
            SetStatus("Unzip failed: " + e.Message);
            yield break;
        }

        SetStatus("Import complete.\nRoot: " + importRoot);
        yield return null;
    }

    private string GetImportRoot()
    {
        if (unzipToRoot) return Application.persistentDataPath;
        return Path.Combine(Application.persistentDataPath, subfolderName);
    }

    private void UnzipToDirectorySafe(string zipPath, string destinationRoot, bool overwrite)
    {
        Directory.CreateDirectory(destinationRoot);
        string fullDestRoot = Path.GetFullPath(destinationRoot);

        using (FileStream fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (ZipArchive archive = new ZipArchive(fs, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                // Directories in zips often have empty Name
                if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith("/"))
                {
                    string dirPath = Path.Combine(destinationRoot, entry.FullName);
                    Directory.CreateDirectory(dirPath);
                    continue;
                }

                string outPath = Path.Combine(destinationRoot, entry.FullName);

                // Zip slip protection
                string fullOutPath = Path.GetFullPath(outPath);
                if (!fullOutPath.StartsWith(fullDestRoot, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Blocked unsafe zip entry path: " + entry.FullName);
                }

                string outDir = Path.GetDirectoryName(fullOutPath);
                if (!string.IsNullOrEmpty(outDir))
                    Directory.CreateDirectory(outDir);

                if (!overwrite && File.Exists(fullOutPath))
                    continue;

                entry.ExtractToFile(fullOutPath, overwrite: true);
            }
        }
    }

    private void SetStatus(string msg)
    {
        Debug.Log(msg);
        titleSceneManager.WriteMessage(msg, Color.white);
    }

#if UNITY_WEBGL && !UNITY_EDITOR
    // WebGL interop
    [System.Runtime.InteropServices.DllImport("__Internal")]
    private static extern void OpenZipPicker();

    private void WebGL_OpenZipPicker()
    {
        // Reset buffers
        webglZipBytes = new MemoryStream(1024 * 1024);
        webglZipFileName = "Imported.zip";
        SetStatus("Opening file picker...");
        OpenZipPicker();
    }

    // Called from JS with the file name
    public void WebGL_OnZipSelectedName(string name)
    {
        if (!string.IsNullOrEmpty(name))
            webglZipFileName = name;

        SetStatus("Selected: " + webglZipFileName + "\nReading...");
    }

    // Called from JS with base64 chunk data
    public void WebGL_OnZipChunkBase64(string base64Chunk)
    {
        if (string.IsNullOrEmpty(base64Chunk)) return;

        byte[] chunk = Convert.FromBase64String(base64Chunk);
        webglZipBytes.Write(chunk, 0, chunk.Length);
    }

    // Called from JS when done
    public void WebGL_OnZipDone(string _unused)
    {
        byte[] zipBytes = webglZipBytes.ToArray();
        StartCoroutine(ImportZipBytesCoroutine(zipBytes, webglZipFileName));
    }

    // Called from JS on error/cancel
    public void WebGL_OnZipError(string message)
    {
        SetStatus("WebGL import error: " + message);
    }
#endif
}