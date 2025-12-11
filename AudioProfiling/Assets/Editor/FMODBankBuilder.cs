using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Automatically rebuilds FMOD banks before every Unity build.
/// Finds FMOD Studio executable through PATH or cached user preference,
/// without any hardcoded system paths.
/// </summary>
public class FMODBankBuilder : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    // EditorPrefs key for cached FMOD Studio path
    private const string PrefKey = "FMODStudioExePath";

    public void OnPreprocessBuild(BuildReport report)
    {
        if (Application.isBatchMode)
        {
            UnityEngine.Debug.Log("[FMOD] Batchmode build: FMOD bank building skipped (handled in CI).");
            return;
        }

        UnityEngine.Debug.Log("[FMOD] Starting automatic bank rebuild...");
        BuildFMODBanks();
    }

    /// <summary>
    /// Main entry for building FMOD banks.
    /// </summary>
    public static void BuildFMODBanks()
    {
        string exe = ResolveFMODStudioExe();

        if (string.IsNullOrEmpty(exe))
        {
            UnityEngine.Debug.LogError("[FMOD] Could not locate FMOD Studio executable. Bank build aborted.");
            return;
        }

        // Try to locate FMOD Project (.fspro)
        string project = ResolveFMODProjectFile();

        if (string.IsNullOrEmpty(project))
        {
            UnityEngine.Debug.LogError("[FMOD] Could not locate FMOD project (.fspro). Bank build aborted.");
            return;
        }

        RunFMODBuild(exe, project);
    }

    /// <summary>
    /// Attempts to locate FMOD Studio executable via:
    /// 1) EditorPrefs (cached user setting)
    /// 2) PATH lookup (industry standard)
    /// </summary>
    private static string ResolveFMODStudioExe()
    {
        // 1) Check cached path
        string cached = EditorPrefs.GetString(PrefKey, "");
        if (!string.IsNullOrEmpty(cached) && File.Exists(cached))
        {
            return cached;
        }

        // 2) Search PATH for FMOD Studio
        string exeName = Application.platform == RuntimePlatform.OSXEditor
            ? "FMOD Studio"
            : "FMOD Studio.exe";

        string found = FindInPath(exeName);
        if (!string.IsNullOrEmpty(found))
        {
            EditorPrefs.SetString(PrefKey, found);
            return found;
        }

        // 3) Ask user manually
        string manual = EditorUtility.OpenFilePanel("Select FMOD Studio executable", "", "");
        if (!string.IsNullOrEmpty(manual))
        {
            EditorPrefs.SetString(PrefKey, manual);
            return manual;
        }

        return null;
    }

    /// <summary>
    /// Searches system PATH for an executable.
    /// </summary>
    private static string FindInPath(string exeName)
    {
        string pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return null;

        string[] paths = pathEnv.Split(Path.PathSeparator);

        foreach (string p in paths)
        {
            try
            {
                string full = Path.Combine(p, exeName);
                if (File.Exists(full))
                {
                    UnityEngine.Debug.Log("[FMOD] Found FMOD Studio via PATH: " + full);
                    return full;
                }
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Locates the FMOD Studio project (.fspro) inside the Unity project structure.
    /// Supports:
    /// - Assets/FMOD
    /// - Assets/Audio
    /// - Any folder under Assets
    /// </summary>
    private static string ResolveFMODProjectFile()
    {
        string[] fsproFiles = Directory.GetFiles(Application.dataPath, "*.fspro", SearchOption.AllDirectories);

        if (fsproFiles.Length > 0)
        {
            return fsproFiles[0];
        }

        return null;
    }

    /// <summary>
    /// Executes FMOD Studio in command-line mode to build soundbanks.
    /// </summary>
    private static void RunFMODBuild(string exe, string project)
    {
        ProcessStartInfo info = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = $"--build \"{project}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        UnityEngine.Debug.Log("[FMOD] Running FMOD Studio: " + info.FileName + " " + info.Arguments);

        using (Process proc = Process.Start(info))
        {
            string output = proc.StandardOutput.ReadToEnd();
            string error = proc.StandardError.ReadToEnd();
            proc.WaitForExit();

            UnityEngine.Debug.Log("[FMOD] FMOD output:\n" + output);

            if (!string.IsNullOrEmpty(error))
                UnityEngine.Debug.LogWarning("[FMOD] FMOD warnings/errors:\n" + error);

            if (proc.ExitCode != 0)
                UnityEngine.Debug.LogError("[FMOD] FMOD build failed with exit code " + proc.ExitCode);
            else
                UnityEngine.Debug.Log("[FMOD] FMOD bank build completed successfully.");
        }
    }
}
