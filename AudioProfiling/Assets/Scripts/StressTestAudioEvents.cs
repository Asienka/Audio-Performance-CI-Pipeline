using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using FMODUnity;
using FMOD.Studio;

public class StressTestAudioEvents : MonoBehaviour
{
    [Header("FMOD Events To Stress Test")]
    [SerializeField] private EventReference[] eventsToStress;

    [Header("Burst Settings")]
    [SerializeField] private int instancesPerBurst = 10;
    [SerializeField] private float burstInterval = 0.5f;

    [Header("Timing")]
    [SerializeField] private float totalTestDuration = 10f;
    [SerializeField] private float startDelay = 0f;

    [Header("Instance Lifetime")]
    [SerializeField] private float instanceLifetime = 2f;
    
    [Header("Randomization")]
    [SerializeField] private bool randomizePosition = true;
    [SerializeField] private float positionRadius = 10f;
    [SerializeField] private bool randomizeLifetime = false;
    [SerializeField] private Vector2 lifetimeRange = new Vector2(1f, 3f);

    [Header("Debug")]
    [SerializeField] private bool logVerbose = false;

    private float timer;
    private float burstTimer;
    private bool testComplete;
    private int totalInstancesCreated;
    private int totalInstancesReleased;

    private readonly List<EventInstance> activeInstances = new();
    private readonly Queue<EventInstance> instancesToRelease = new();

    private void Start()
    {
        if (eventsToStress == null || eventsToStress.Length == 0)
        {
            Debug.LogError("[StressTest] No events to stress test! Please assign FMOD events.");
            enabled = false;
            return;
        }

        Debug.Log($"[StressTest] Starting stress test with {eventsToStress.Length} event(s)");
        Debug.Log($"[StressTest] Duration: {totalTestDuration}s, Burst: {instancesPerBurst} instances every {burstInterval}s");
    }

    private void Update()
    {
        if (testComplete)
            return;

        timer += Time.deltaTime;

        // Wait for start delay
        if (timer < startDelay)
            return;

        // Check if test is complete
        if (timer >= totalTestDuration + startDelay)
        {
            FinishTest();
            return;
        }

        // Trigger bursts
        burstTimer += Time.deltaTime;
        if (burstTimer >= burstInterval)
        {
            burstTimer = 0f;
            TriggerBurst();
        }

        // Process instance releases
        ProcessInstanceReleases();
    }

    private void TriggerBurst()
    {
        if (eventsToStress == null || eventsToStress.Length == 0)
            return;

        int successCount = 0;
        int failCount = 0;

        for (int i = 0; i < instancesPerBurst; i++)
        {
            EventReference evt = eventsToStress[Random.Range(0, eventsToStress.Length)];
            
            if (evt.IsNull)
            {
                Debug.LogWarning($"[StressTest] Event at index {i} is null, skipping");
                failCount++;
                continue;
            }

            try
            {
                EventInstance instance = RuntimeManager.CreateInstance(evt);

                // Set 3D position if randomization is enabled
                if (randomizePosition)
                {
                    Vector3 randomPos = Random.insideUnitSphere * positionRadius;
                    RuntimeManager.AttachInstanceToGameObject(instance, gameObject);
                    instance.set3DAttributes(RuntimeUtils.To3DAttributes(transform.position + randomPos));
                }

                FMOD.RESULT result = instance.start();
                
                if (result != FMOD.RESULT.OK)
                {
                    Debug.LogWarning($"[StressTest] Failed to start instance: {result}");
                    instance.release();
                    failCount++;
                    continue;
                }

                activeInstances.Add(instance);
                totalInstancesCreated++;
                successCount++;

                // Schedule release
                float lifetime = instanceLifetime;
                if (randomizeLifetime)
                {
                    lifetime = Random.Range(lifetimeRange.x, lifetimeRange.y);
                }
                
                StartCoroutine(ReleaseAfterSeconds(instance, lifetime));
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[StressTest] Exception creating instance: {ex.Message}");
                failCount++;
            }
        }

        if (logVerbose)
        {
            Debug.Log($"[StressTest] Burst complete: {successCount} success, {failCount} fail, {activeInstances.Count} active");
        }
    }

    private IEnumerator ReleaseAfterSeconds(EventInstance instance, float delay)
    {
        yield return new WaitForSeconds(delay);

        if (testComplete)
        {
            // Test already finished, cleanup handled elsewhere
            yield break;
        }

        instancesToRelease.Enqueue(instance);
    }

    private void ProcessInstanceReleases()
    {
        while (instancesToRelease.Count > 0)
        {
            EventInstance instance = instancesToRelease.Dequeue();
            ReleaseInstance(instance);
        }
    }

    private void ReleaseInstance(EventInstance instance)
    {
        try
        {
            instance.getPlaybackState(out PLAYBACK_STATE state);
            
            if (state != PLAYBACK_STATE.STOPPED)
            {
                instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
            }
            
            instance.release();
            activeInstances.Remove(instance);
            totalInstancesReleased++;

            if (logVerbose)
            {
                Debug.Log($"[StressTest] Released instance. Active: {activeInstances.Count}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[StressTest] Exception releasing instance: {ex.Message}");
        }
    }

    private void FinishTest()
    {
        testComplete = true;
        
        Debug.Log($"[StressTest] Test complete. Created: {totalInstancesCreated}, Released: {totalInstancesReleased}, Active: {activeInstances.Count}");
        
        CleanupInstances();
        enabled = false;
        
        Debug.Log("[StressTest] Finished and cleaned up.");
    }

    private void CleanupInstances()
    {
        Debug.Log($"[StressTest] Cleaning up {activeInstances.Count} remaining instances...");
        
        foreach (EventInstance inst in activeInstances)
        {
            try
            {
                inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
                inst.release();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[StressTest] Exception during cleanup: {ex.Message}");
            }
        }
        
        activeInstances.Clear();
        instancesToRelease.Clear();
        
        Debug.Log("[StressTest] Cleanup complete.");
    }

    private void OnDestroy()
    {
        if (!testComplete)
        {
            CleanupInstances();
        }
    }

    private void OnDisable()
    {
        if (!testComplete)
        {
            CleanupInstances();
        }
    }
}