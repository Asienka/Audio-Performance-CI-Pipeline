using System.Collections.Generic;
using System.IO;
using UnityEngine;
using FMODUnity;
using FMOD;                // for CPU_USAGE and ChannelGroup
using FMOD.Studio;         // for Bus

public class LogAudioMetrics : MonoBehaviour
{
    [Header("Profiling Settings")]
    public float duration = 10f;
    public string outputFile = "profiler_output.json";

    private float timer = 0f;
    private List<AudioFrameData> samples = new List<AudioFrameData>();

    struct AudioFrameData
    {
        public float time;
        public float unityFrameMs;
        public float fmodCpuDsp;
        public float fmodCpuStream;
        public float totalFmodCpu;
        public int voices;
    }

    void Start()
    {
        UnityEngine.Debug.Log("[Perf] Audio metrics logging started.");
    }

    void Update()
    {
        timer += Time.deltaTime;

        // Unity frame time (ms)
        float frameTimeMs = Time.deltaTime * 1000f;

        // Query FMOD CPU usage. Use out-var to match whatever signature
        // the installed FMOD API provides, then read the numeric fields
        // (`dsp` and `stream`) via reflection so this code compiles
        // regardless of the exact CPU_USAGE type (FMOD or FMOD.Studio).
        RuntimeManager.StudioSystem.getCPUUsage(out var core, out var studio);

        float dspCpu = GetFloatMember(studio, "dsp");
        float streamCpu = GetFloatMember(studio, "stream");

        // FMOD voice count
        RuntimeManager.StudioSystem.getBus("bus:/", out FMOD.Studio.Bus masterBus);
        masterBus.getChannelGroup(out FMOD.ChannelGroup group);
        group.getNumChannels(out int voiceCount);

        samples.Add(new AudioFrameData
        {
            time = Time.time,
            unityFrameMs = frameTimeMs,
            fmodCpuDsp = dspCpu,
            fmodCpuStream = streamCpu,
            totalFmodCpu = dspCpu + streamCpu,
            voices = voiceCount
        });

        if (timer >= duration)
        {
            SaveJson();
            UnityEngine.Debug.Log("[Perf] Metrics saved. Quitting.");
            Application.Quit();
        }
    }

    private void SaveJson()
    {
        string path = Path.Combine(Application.dataPath, "..", outputFile);

        var wrapper = new
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = samples.Count,
            samples = samples
        };

        File.WriteAllText(path, JsonUtility.ToJson(wrapper, true));
        UnityEngine.Debug.Log("[Perf] JSON saved to: " + path);
    }

    // Reflection helper: read a float field or property named `name` from an object.
    // This lets the code work with either `FMOD.CPU_USAGE` or `FMOD.Studio.CPU_USAGE`
    // depending on which type the installed FMOD assembly exposes.
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
}
