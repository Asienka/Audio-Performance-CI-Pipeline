using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FMODUnity;
using FMOD;
using FMOD.Studio;
using Debug = UnityEngine.Debug;

public class LogAudioMetrics : MonoBehaviour
{
    [Header("Profiling Settings")]
    [Tooltip("How long to record audio metrics (seconds).")]
    public float duration = 10f;

    [Tooltip("Profiler output file name.")]
    public string outputFile = "profiler_output.json";

    [Tooltip("Enable detailed logging during profiling.")]
    public bool verboseLogging = false;

    [Header("Sampling")]
    [Tooltip("Sample every N frames (1 = every frame). Higher values reduce data volume.")]
    [Range(1, 10)]
    public int samplingInterval = 1;

    private float timer = 0f;
    private bool hasSaved = false;
    private int frameCount = 0;
    private int totalSamples = 0;

    private readonly List<AudioFrameData> samples = new();

    // ----------------------------
    // Per-frame audio metrics
    // ----------------------------
    [Serializable]
    private struct AudioFrameData
    {
        public float time;
        public float unityFrameMs;
        public float deltaTime;

        public float fmodCpuDsp;
        public float fmodCpuStream;
        public float fmodCpuUpdate;
        public float totalFmodCpu;

        public int voices;
        public int realChannels;
        public int virtualChannels;
    }

    // ----------------------------
    // JSON wrapper with metadata
    // ----------------------------
    [Serializable]
    private class AudioMetricsWrapper
    {
        public string timestamp;
        public string unityVersion;
        public string platform;
        public int sampleCount;
        public float totalDuration;
        public int samplingInterval;
        public List<AudioFrameData> samples;
    }

    private void Start()
    {
        Debug.Log($"[AudioProfiler] Starting profiling session");
        Debug.Log($"[AudioProfiler] Duration: {duration}s, Sampling interval: {samplingInterval}");
        Debug.Log($"[AudioProfiler] Output: {outputFile}");

        // Verify FMOD is initialized
        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogError("[AudioProfiler] FMOD is not initialized! Profiling will fail.");
        }
    }

    private void Update()
    {
        if (hasSaved)
            return;

        timer += Time.deltaTime;
        frameCount++;

        // Sample at specified interval
        if (frameCount % samplingInterval != 0)
            return;

        try
        {
            AudioFrameData sample = CollectMetrics();
            samples.Add(sample);
            totalSamples++;

            if (verboseLogging && totalSamples % 60 == 0) // Log every 60 samples
            {
                Debug.Log($"[AudioProfiler] Sample {totalSamples}: CPU={sample.totalFmodCpu:F2}%, Voices={sample.voices}, Frame={sample.unityFrameMs:F2}ms");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AudioProfiler] Error collecting metrics: {ex.Message}");
        }

        // Check if profiling duration completed
        if (timer >= duration)
        {
            SaveAndQuit();
        }
    }

    private AudioFrameData CollectMetrics()
    {
        AudioFrameData data = new AudioFrameData
        {
            time = Time.time,
            deltaTime = Time.deltaTime,
            unityFrameMs = Time.deltaTime * 1000f
        };

        // FMOD CPU usage (low-level)
        try
        {
            RESULT result = RuntimeManager.CoreSystem.getCPUUsage(out FMOD.CPU_USAGE cpu);
            if (result == RESULT.OK)
            {
                data.fmodCpuDsp = cpu.dsp;
                data.fmodCpuStream = cpu.stream;
                data.fmodCpuUpdate = cpu.update;
                data.totalFmodCpu = cpu.dsp + cpu.stream + cpu.update;
            }
            else
            {
                Debug.LogWarning($"[AudioProfiler] Failed to get CPU usage: {result}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AudioProfiler] Exception getting CPU usage: {ex.Message}");
        }

        // Active voices and channels
        try
        {
            // Get master bus
            RESULT busResult = RuntimeManager.StudioSystem.getBus("bus:/", out Bus masterBus);
            if (busResult == RESULT.OK)
            {
                // Get channel group
                RESULT groupResult = masterBus.getChannelGroup(out ChannelGroup group);
                if (groupResult == RESULT.OK)
                {
                    // Get number of channels
                    RESULT channelResult = group.getNumChannels(out int channelCount);
                    if (channelResult == RESULT.OK)
                    {
                        data.voices = channelCount;
                        data.realChannels = channelCount;
                    }
                }
            }

            // Also get from core system for comparison
            RuntimeManager.CoreSystem.getChannelsPlaying(out int realChannels, out int virtualChannels);
            data.realChannels = realChannels;
            data.virtualChannels = virtualChannels;
            data.voices = realChannels + virtualChannels;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AudioProfiler] Exception getting voice count: {ex.Message}");
        }

        return data;
    }

    private void SaveAndQuit()
    {
        if (hasSaved)
            return;

        hasSaved = true;

        Debug.Log($"[AudioProfiler] Profiling complete. Collected {totalSamples} samples over {timer:F2}s");

        try
        {
            string path = Path.Combine(Application.persistentDataPath, outputFile);
            Debug.Log($"[AudioProfiler] Saving results to: {path}");

            // Ensure directory exists
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AudioMetricsWrapper wrapper = new AudioMetricsWrapper
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                sampleCount = samples.Count,
                totalDuration = timer,
                samplingInterval = samplingInterval,
                samples = samples
            };

            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(path, json);
            
            Debug.Log($"[AudioProfiler] Successfully saved {samples.Count} samples to {path}");
            Debug.Log($"[AudioProfiler] File size: {new FileInfo(path).Length / 1024}KB");

            // Also log summary statistics
            LogSummaryStats();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[AudioProfiler] Failed to save results: {ex.Message}");
            Debug.LogError($"[AudioProfiler] Stack trace: {ex.StackTrace}");
        }

        // Wait a moment before quitting to ensure logs are flushed
        StartCoroutine(QuitAfterDelay(0.5f));
    }

    private void LogSummaryStats()
    {
        if (samples.Count == 0)
        {
            Debug.LogWarning("[AudioProfiler] No samples collected!");
            return;
        }

        float maxCpu = 0f;
        float avgCpu = 0f;
        int maxVoices = 0;
        float maxFrameMs = 0f;

        foreach (AudioFrameData sample in samples)
        {
            maxCpu = Mathf.Max(maxCpu, sample.totalFmodCpu);
            avgCpu += sample.totalFmodCpu;
            maxVoices = Mathf.Max(maxVoices, sample.voices);
            maxFrameMs = Mathf.Max(maxFrameMs, sample.unityFrameMs);
        }

        avgCpu /= samples.Count;

        Debug.Log("=== PROFILING SUMMARY ===");
        Debug.Log($"FMOD CPU: avg={avgCpu:F2}%, max={maxCpu:F2}%");
        Debug.Log($"Voices: max={maxVoices}");
        Debug.Log($"Frame time: max={maxFrameMs:F2}ms");
    }

    private System.Collections.IEnumerator QuitAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        Debug.Log("[AudioProfiler] Quitting application...");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnApplicationQuit()
    {
        if (!hasSaved && samples.Count > 0)
        {
            Debug.LogWarning("[AudioProfiler] Application quitting before save completed. Attempting emergency save...");
            
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "emergency_" + outputFile);
                AudioMetricsWrapper wrapper = new AudioMetricsWrapper
                {
                    timestamp = DateTime.UtcNow.ToString("o"),
                    unityVersion = Application.unityVersion,
                    platform = Application.platform.ToString(),
                    sampleCount = samples.Count,
                    totalDuration = timer,
                    samplingInterval = samplingInterval,
                    samples = samples
                };

                File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
                Debug.Log($"[AudioProfiler] Emergency save completed: {path}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AudioProfiler] Emergency save failed: {ex.Message}");
            }
        }
    }
}