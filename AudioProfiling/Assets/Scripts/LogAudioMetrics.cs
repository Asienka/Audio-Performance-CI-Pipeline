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
    private bool isProfilingStarted = false;

    private readonly List<AudioFrameData> samples = new();

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

    [System.Serializable]
    private class AudioMetricsWrapper
    {
        public string timestamp;
        public int sampleCount;
        public List<AudioFrameData> samples;
    }

    private void Awake()
    {
        Debug.Log("[AudioProfiler] Starting initialization...");
        Debug.Log($"[AudioProfiler] Application platform: {Application.platform}");
        Debug.Log($"[AudioProfiler] Is headless: {SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null}");
        
        // Wymuś inicjalizację FMOD przed dodaniem StudioListener
        StartCoroutine(InitializeFMOD());
    }

    private System.Collections.IEnumerator InitializeFMOD()
    {
        Debug.Log("[AudioProfiler] Waiting for FMOD RuntimeManager initialization...");
        
        // Poczekaj 1 frame na automatyczną inicjalizację RuntimeManager
        yield return null;
        
        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogWarning("[AudioProfiler] RuntimeManager not initialized automatically. Forcing initialization...");
            
            // Wymuś inicjalizację poprzez dostęp do CoreSystem
            try
            {
                // To powinno wywołać inicjalizację RuntimeManager
                RuntimeManager.CoreSystem.getVersion(out uint version);
                Debug.Log($"[AudioProfiler] FMOD Core version: {version:X}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AudioProfiler] Failed to force FMOD initialization: {e}");
            }
            
            // Poczekaj kolejny frame
            yield return null;
        }
        
        if (!RuntimeManager.IsInitialized)
        {
            Debug.LogError("[AudioProfiler] FMOD RuntimeManager still NOT initialized!");
            Debug.LogError("[AudioProfiler] This may be due to missing audio device in headless environment.");
            
            // Ostatnia próba - wymuś NOSOUND output
            try
            {
                Debug.Log("[AudioProfiler] Attempting to set NOSOUND output...");
                FMOD.RESULT result = RuntimeManager.CoreSystem.setOutput(FMOD.OUTPUTTYPE.NOSOUND);
                Debug.Log($"[AudioProfiler] setOutput(NOSOUND) result: {result}");
                
                if (result == FMOD.RESULT.OK)
                {
                    // Spróbuj zainicjalizować ponownie
                    yield return null;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AudioProfiler] Failed to set NOSOUND output: {e}");
            }
        }
        
        // Sprawdź końcowy stan
        if (RuntimeManager.IsInitialized)
        {
            Debug.Log("[AudioProfiler] ✓ FMOD initialized successfully!");
            
            // Sprawdź konfigurację
            RuntimeManager.CoreSystem.getOutput(out FMOD.OUTPUTTYPE outputType);
            Debug.Log($"[AudioProfiler] Output type: {outputType}");
            
            RuntimeManager.CoreSystem.getDriver(out int driver);
            Debug.Log($"[AudioProfiler] Driver: {driver}");
            
            RuntimeManager.CoreSystem.getSoftwareFormat(out int sampleRate, out FMOD.SPEAKERMODE speakerMode, out int numRawSpeakers);
            Debug.Log($"[AudioProfiler] Sample rate: {sampleRate}, Speaker mode: {speakerMode}");
            
            // Dodaj StudioListener
            if (FindFirstObjectByType<StudioListener>() == null)
            {
                gameObject.AddComponent<StudioListener>();
                Debug.Log("[AudioProfiler] Added StudioListener");
            }
            
            isProfilingStarted = true;
        }
        else
        {
            Debug.LogError("[AudioProfiler] ✗ FMOD initialization FAILED!");
            Debug.LogError("[AudioProfiler] Aborting profiling. Application will quit.");
            
            // Zapisz pusty plik żeby pipeline nie zawiesił się
            SaveEmptyResults("FMOD initialization failed");
            
            yield return new WaitForSeconds(1f);
            
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }

    private void Update()
    {
        if (hasSaved || !isProfilingStarted)
            return;

        timer += Time.deltaTime;

        if (timer <= Time.deltaTime)
        {
            Debug.Log($"[AudioProfiler] Starting profiling for {duration}s");
        }

        float frameMs = Time.deltaTime * 1000f;

        RuntimeManager.CoreSystem.getCPUUsage(out FMOD.CPU_USAGE cpu);

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
        hasSaved = true;

        Debug.Log($"[AudioProfiler] Collected {samples.Count} samples");

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(dir); 

        string path = Path.Combine(dir, outputFile);

        Debug.Log("[AudioProfiler] Saving results to: " + path);

        var wrapper = new AudioMetricsWrapper
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = samples.Count,
            samples = samples
        };

        try
        {
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(path, json);
            Debug.Log("[AudioProfiler] JSON saved successfully.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError("[AudioProfiler] Failed to save JSON: " + ex);
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void SaveEmptyResults(string errorMessage)
    {
        hasSaved = true;

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, outputFile);

        var wrapper = new AudioMetricsWrapper
        {
            timestamp = System.DateTime.UtcNow.ToString("o"),
            sampleCount = 0,
            samples = new List<AudioFrameData>()
        };

        try
        {
            string json = JsonUtility.ToJson(wrapper, true);
            File.WriteAllText(path, json);
            Debug.Log($"[AudioProfiler] Saved empty results due to: {errorMessage}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[AudioProfiler] Failed to save empty results: {ex}");
        }
    }

    private void OnApplicationQuit()
    {
        if (!hasSaved && samples.Count > 0)
        {
            Debug.LogWarning($"[AudioProfiler] Force-saving {samples.Count} samples on quit");
            SaveAndQuit();
        }
    }
}