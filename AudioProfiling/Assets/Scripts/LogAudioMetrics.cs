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

    private float timer = 0f;
    private bool hasSaved = false;

    private readonly List<AudioFrameData> samples = new();

    // ----------------------------
    // Per-frame audio metrics
    // ----------------------------
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

    // ----------------------------
    // JSON wrapper
    // ----------------------------
    [System.Serializable]
    private class AudioMetricsWrapper
    {
        public string timestamp;
        public int sampleCount;
        public List<AudioFrameData> samples;
    }

    private void Update()
    {
        if (hasSaved)
            return;

        timer += Time.deltaTime;

        // ----------------------------
        // Unity frame timing
        // ----------------------------
        float frameMs = Time.deltaTime * 1000f;

        // ----------------------------
        // FMOD CPU (low-level)
        // ----------------------------
        RuntimeManager.CoreSystem.getCPUUsage(out FMOD.CPU_USAGE cpu);

        // ----------------------------
        // Active voices (channels)
        // ----------------------------
        RuntimeManager.StudioSystem.getBus("bus:/", out Bus masterBus);
        masterBus.getChannelGroup(out ChannelGroup group);
        group.getNumChannels(out int channelCount);

        // ----------------------------
        // Store sample
        // ----------------------------
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

        // ----------------------------
        // Finish & save
        // ----------------------------
        if (timer >= duration)
        {
            SaveAndQuit();
        }
    }

    private void SaveAndQuit()
    {
        hasSaved = true;

        string path = Path.Combine(Application.persistentDataPath, outputFile);
        Debug.Log($"[AudioProfiler] Saving results to: {path}");

        var wrapper = new AudioMetricsWrapper
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = samples.Count,
            samples = samples
        };

        File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
        Debug.Log("[AudioProfiler] JSON saved successfully.");

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
