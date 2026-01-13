using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FMODUnity;
using FMOD;
using FMOD.Studio;
using Debug = UnityEngine.Debug;

/// <summary>
/// Collects per-frame Unity + FMOD audio performance metrics
/// and writes them to a JSON file for CI analysis.
/// Designed to work in headless / CI environments.
/// </summary>
public class LogAudioMetrics : MonoBehaviour
{
    [Header("Profiling Settings")]
    [Tooltip("How long to record audio metrics (seconds).")]
    public float duration = 10f;

    [Tooltip("Profiler output file name.")]
    public string outputFile = "profiler_output.json";

    private float timer = 0f;
    private bool hasSaved = false;
    private bool isProfilingStarted = false;

    private readonly List<AudioFrameData> samples = new();

    // -------------------------------------------------
    // Per-frame audio metrics
    // -------------------------------------------------
    [System.Serializable]
    private struct AudioFrameData
    {
        public float time;
        public float unityFrameMs;

        public float fmodCpuDsp;
        public float fmodCpuStream;
        public float fmodCpuUpdate;
        public float totalFmodCpu;

        public int voices;
    }

    // -------------------------------------------------
    // JSON wrapper
    // -------------------------------------------------
    [System.Serializable]
    private class AudioMetricsWrapper
    {
        public string timestamp;
        public int sampleCount;
        public List<AudioFrameData> samples;
    }

    private void Awake()
    {
        Debug.Log("[AudioProfiler] Initializing audio profiler...");
        Debug.Log($"[AudioProfiler] Platform: {Application.platform}");
        Debug.Log($"[AudioProfiler] Headless mode: {SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null}");

        // FMOD may not initialize automatically in headless mode
        StartCoroutine(InitializeFMOD());
    }

    /// <summary>
    /// Ensures FMOD RuntimeManager is initialized in headless environments.
    /// </summary>
    private IEnumerator InitializeFMOD()
    {
        // Wait one frame for RuntimeManager auto-init
        yield return null;

        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogWarning("[AudioProfiler] FMOD not initialized automatically. Forcing initialization...");

            try
            {
                // Accessing CoreSystem forces FMOD initialization
                RuntimeManager.CoreSystem.getVersion(out uint version);
                Debug.Log($"[AudioProfiler] FMOD Core version: {version:X}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AudioProfiler] FMOD init failed: {e}");
            }

            yield return null;
        }

        // Final fallback: force NOSOUND output (CI-safe)
        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogWarning("[AudioProfiler] Attempting NOSOUND fallback...");

            bool nosoundSet = false;

            try
            {
                FMOD.RESULT result = RuntimeManager.CoreSystem.setOutput(FMOD.OUTPUTTYPE.NOSOUND);
                Debug.Log($"[AudioProfiler] setOutput(NOSOUND): {result}");
                nosoundSet = (result == FMOD.RESULT.OK);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AudioProfiler] Failed to set NOSOUND output: {e}");
            }

            // yield MUST be outside try/catch
            if (nosoundSet)
                yield return null;
        }

        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogError("[AudioProfiler] FMOD initialization FAILED. Aborting profiling.");

            SaveEmptyResults("FMOD initialization failed");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
            yield break;
        }

        // FMOD initialized successfully
        Debug.Log("[AudioProfiler] FMOD initialized successfully.");

        // Ensure StudioListener exists (required for voice updates)
        if (FindFirstObjectByType<StudioListener>() == null)
        {
            gameObject.AddComponent<StudioListener>();
            Debug.Log("[AudioProfiler] StudioListener added.");
        }

        isProfilingStarted = true;
    }

    private void Update()
    {
        if (hasSaved || !isProfilingStarted)
            return;

        timer += Time.deltaTime;

        float frameMs = Time.deltaTime * 1000f;

        // FMOD CPU usage (low-level)
        RuntimeManager.CoreSystem.getCPUUsage(out FMOD.CPU_USAGE cpu);

        // Active voices (channels)
        RuntimeManager.StudioSystem.getBus("bus:/", out Bus masterBus);
        masterBus.getChannelGroup(out ChannelGroup group);
        group.getNumChannels(out int channelCount);

        samples.Add(new AudioFrameData
        {
            time = Time.time,
            unityFrameMs = frameMs,

            fmodCpuDsp = cpu.dsp,
            fmodCpuStream = cpu.stream,
            fmodCpuUpdate = cpu.update,
            totalFmodCpu = cpu.dsp + cpu.stream + cpu.update,

            voices = channelCount
        });

        if (timer >= duration)
        {
            SaveAndQuit();
        }
    }

    /// <summary>
    /// Writes profiler results next to the executable and quits.
    /// </summary>
    private void SaveAndQuit()
    {
        hasSaved = true;

        Debug.Log($"[AudioProfiler] Collected {samples.Count} samples.");

        // Save next to executable (CI-friendly)
        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(outputDir);

        string path = Path.Combine(outputDir, outputFile);
        Debug.Log($"[AudioProfiler] Saving JSON to: {path}");

        var wrapper = new AudioMetricsWrapper
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = samples.Count,
            samples = samples
        };

        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
            Debug.Log("[AudioProfiler] JSON saved successfully.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AudioProfiler] Failed to save JSON: {e}");
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Writes an empty profiler file to avoid CI pipeline failure.
    /// </summary>
    private void SaveEmptyResults(string reason)
    {
        hasSaved = true;

        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(outputDir);

        string path = Path.Combine(outputDir, outputFile);

        var wrapper = new AudioMetricsWrapper
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = 0,
            samples = new List<AudioFrameData>()
        };

        try
        {
            File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
            Debug.Log($"[AudioProfiler] Empty results saved ({reason}).");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AudioProfiler] Failed to save empty results: {e}");
        }
    }

    private void OnApplicationQuit()
    {
        if (!hasSaved && samples.Count > 0)
        {
            Debug.LogWarning("[AudioProfiler] Force-saving on application quit.");
            SaveAndQuit();
        }
    }
}
