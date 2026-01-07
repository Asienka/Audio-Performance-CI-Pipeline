using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class StressTestAudioEvents : MonoBehaviour
{
    [Header("FMOD Events To Stress Test")]
    [Tooltip("List of FMOD events to be triggered during the stress test.")]
    [SerializeField] private EventReference[] eventsToStress;

    [Header("Burst Settings")]
    [Tooltip("Number of simultaneous event instances triggered per burst.")]
    [SerializeField] private int instancesPerBurst = 10;

    [Tooltip("Interval (seconds) between bursts.")]
    [SerializeField] private float burstInterval = 0.5f;

    [Header("Event Selection")]
    [Tooltip("If enabled, events are chosen randomly from the list.")]
    [SerializeField] private bool randomizeEventSelection = true;

    [Tooltip("Used if random selection is disabled.")]
    [SerializeField] private int fixedEventIndex = 0;

    [Header("FMOD Parameter Randomization")]
    [Tooltip("Enable randomizing a parameter on each instance.")]
    [SerializeField] private bool randomizeParameters = false;

    [Tooltip("FMOD parameter name to modify.")]
    [SerializeField] private string parameterName = "";

    [Tooltip("Minimum random parameter value.")]
    [SerializeField] private float parameterMin = 0f;

    [Tooltip("Maximum random parameter value.")]
    [SerializeField] private float parameterMax = 1f;

    [Header("Timing Controls")]
    [Tooltip("Total duration of the test (seconds).")]
    [SerializeField] private float totalTestDuration = 10f;

    [Tooltip("Delay before the stress test begins (seconds).")]
    [SerializeField] private float startDelay = 0f;

    private float timer = 0f;
    private float burstTimer = 0f;

    private readonly List<EventInstance> activeInstances = new List<EventInstance>();

    private void Update()
    {
        timer += Time.deltaTime;

        // Wait for delayed start
        if (timer < startDelay)
            return;

        burstTimer += Time.deltaTime;

        // End the test
        if (timer >= totalTestDuration + startDelay)
        {
            Debug.Log("[StressTest] Test finished. Releasing FMOD instances…");
            CleanupInstances();
            enabled = false;
            return;
        }

        // Trigger bursts
        if (burstTimer >= burstInterval)
        {
            burstTimer = 0f;
            TriggerBurst();
        }
    }

    private void TriggerBurst()
    {
        if (eventsToStress == null || eventsToStress.Length == 0)
        {
            Debug.LogWarning("[StressTest] No FMOD events assigned.");
            return;
        }

        for (int i = 0; i < instancesPerBurst; i++)
        {
            EventReference evt = SelectEvent();

            EventInstance instance = RuntimeManager.CreateInstance(evt);

            if (randomizeParameters && !string.IsNullOrEmpty(parameterName))
            {
                float paramValue = Random.Range(parameterMin, parameterMax);
                instance.setParameterByName(parameterName, paramValue);
            }

            instance.start();
            // Release differently depending on environment
#if UNITY_EDITOR
            instance.release();
#else
            // In headless/CI builds: wait a few frames before releasing to ensure FMOD processes it
            StartCoroutine(ReleaseAfterFrames(instance, 5));
#endif

            activeInstances.Add(instance);
        }

        Debug.Log($"[StressTest] Burst triggered → {instancesPerBurst} instances.");
    }

    private EventReference SelectEvent()
    {
        if (randomizeEventSelection)
        {
            return eventsToStress[Random.Range(0, eventsToStress.Length)];
        }

        int idx = Mathf.Clamp(fixedEventIndex, 0, eventsToStress.Length - 1);
        return eventsToStress[idx];
    }

    private void CleanupInstances()
    {
        foreach (var inst in activeInstances)
        {
            inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
        }

        activeInstances.Clear();
    }
    
    private System.Collections.IEnumerator ReleaseAfterFrames(EventInstance inst, int frames)
    {
        for (int i = 0; i < frames; i++)
            yield return null;
        inst.release();
    }
}

