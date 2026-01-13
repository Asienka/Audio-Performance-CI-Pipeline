using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FMODUnity;
using FMOD;
using FMOD.Studio;
using Debug = UnityEngine.Debug;

/// <summary>
/// Collects per-frame audio performance metrics from FMOD and Unity
/// and saves them as JSON for CI analysis.
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

    // --------------------------------------------------
    // Per-frame metrics
    // --------------------------------------------------
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

    // --------------------------------------------------
    // JSON wrapper
    // --------------------------------------------------
    [System.Serializable]
    private class AudioMetricsWrapper
    {
        public string timestamp;
        public int sampleCount;
        public List<AudioFrameData> samples;
    }

    private void Awake()
    {
        Debug.Log("[AudioProfiler] Starting FMOD initialization...");
        Debug.Log($"[AudioProfiler] Platform: {Application.platform}");
        Debug.Log($"[AudioProfiler] Headless: {SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null}");

        StartCoroutine(InitializeFMOD());
    }

    /// <summary>
    /// Ensures FMOD is initialized correctly in headless / CI environments.
    /// </summary>
    private IEnumerator InitializeFMOD()
    {
        // Wait one frame for automatic RuntimeManager initialization
        yield return null;

        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogWarning("[AudioProfiler] RuntimeManager not initialized automatically. Forcing CoreSystem access...");

            try
            {
                RuntimeManager.CoreSystem.getVersion(out uint version);
                Debug.Log($"[AudioProfiler] FMOD Core version: {version:X}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AudioProfiler] CoreSystem access failed: {e}");
            }
        }

        // Wait another frame AFTER try/catch
        yield return null;

        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogWarning("[AudioProfiler] Attempting NOSOUND output fallback...");

            try
            {
                FMOD.RESULT result = RuntimeManager.CoreSystem.setOutput(FMOD.OUTPUTTYPE.NOSOUND);
                Debug.Log($"[AudioProfiler] setOutput(NOSOUND): {result}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AudioProfiler] NOSOUND fallback failed: {e}");
            }
        }

        // Final wait AFTER try/catch
        yield return null;

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

        Debug.Log("[AudioProfiler] FMOD initialized successfully.");

        // Ensure StudioListener exists
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

    private void SaveAndQuit()
    {
        if (hasSaved)
            return;

        hasSaved = true;

        Debug.Log($"[AudioProfiler] Collected {samples.Count} samples.");

        // Save next to the executable (easy to collect in CI)
        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(outputDir);

        string path = Path.Combine(outputDir, outputFile);
        Debug.Log($"[AudioProfiler] Saving results to: {path}");

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
    /// Writes an empty JSON file so CI does not fail on missing artifacts.
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
            Debug.LogWarning("[AudioProfiler] Force-saving samples on quit.");
            SaveAndQuit();
        }
    }
}
