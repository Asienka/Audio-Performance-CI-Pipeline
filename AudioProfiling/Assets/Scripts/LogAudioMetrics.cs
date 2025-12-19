using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FMODUnity;
using FMOD;                // for CPU_USAGE and ChannelGroup
using FMOD.Studio;         // for Bus
using Debug = UnityEngine.Debug;
using FmodDebug = FMOD.Debug;

public class LogAudioMetrics : MonoBehaviour
{
    [Header("Profiling Settings")]
    public float duration = 1f;
    public string outputFile = "profiler_output.json";

    private float timer = 0f;
    private bool hasSaved = false;

    private readonly List<AudioFrameData> samples = new();

    private struct AudioFrameData
    {
        public float time;
        public float unityFrameMs;
        public float fmodCpuDsp;
        public float fmodCpuStream;
        public float totalFmodCpu;
        public int voices;
    }

    private void Start()
    {
        Debug.Log("[Perf] Audio metrics logging started.");
        Debug.Log($"[Perf] Output file: {Path.Combine(Application.dataPath, outputFile)}");

        // Safety force save in case Update() fails
        Invoke(nameof(ForceSave), duration + 10f);
    }

    private void Update()
    {
        timer += Time.deltaTime;
        float frameTimeMs = Time.deltaTime * 1000f;

        // Get FMOD CPU usage
        RuntimeManager.StudioSystem.getCPUUsage(out _, out var studio);
        float dspCpu = GetFloatMember(studio, "dsp");
        float streamCpu = GetFloatMember(studio, "stream");

        // Get voice count
        RuntimeManager.StudioSystem.getBus("bus:/", out Bus masterBus);
        masterBus.getChannelGroup(out var group);
        group.getNumChannels(out var voiceCount);

        samples.Add(new AudioFrameData
        {
            time = Time.time,
            unityFrameMs = frameTimeMs,
            fmodCpuDsp = dspCpu,
            fmodCpuStream = streamCpu,
            totalFmodCpu = dspCpu + streamCpu,
            voices = voiceCount
        });

        if (!hasSaved && timer >= duration)
        {
            hasSaved = true;
            SaveJson();
            Invoke(nameof(Quit), 1f);
        }

        // Optional: debug every 10 samples
        if (samples.Count % 10 == 0)
            Debug.Log($"[Perf] Collected {samples.Count} samples");
    }

    private void ForceSave()
    {
        if (samples.Count == 0)
        {
            Debug.LogWarning("[Perf] No samples collected, forcing save anyway.");
        }
        SaveJson();
        Invoke(nameof(Quit), 0.5f);
    }

    private void SaveJson()
    {
        string path = Path.Combine(Application.dataPath, outputFile);
        Debug.Log("[Perf] Saving JSON to: " + path);

        var wrapper = new
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = samples.Count,
            samples = samples
        };

        File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
        Debug.Log("[Perf] JSON saved successfully.");
    }

    private float GetFloatMember(object obj, string name)
    {
        if (obj == null) return 0f;
        var t = obj.GetType();
        var prop = t.GetProperty(name);
        if (prop != null)
        {
            var val = prop.GetValue(obj);
            if (val is float f) return f;
            if (val is double d) return (float)d;
            if (val is int i) return i;
        }
        var field = t.GetField(name);
        if (field != null)
        {
            var val = field.GetValue(obj);
            if (val is float f2) return f2;
            if (val is double d2) return (float)d2;
            if (val is int i2) return i2;
        }
        return 0f;
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
