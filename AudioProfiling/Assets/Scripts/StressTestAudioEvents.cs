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

    private float timer;
    private float burstTimer;

    private readonly List<EventInstance> activeInstances = new();

    private void Update()
    {
        timer += Time.deltaTime;

        if (timer < startDelay)
            return;

        if (timer >= totalTestDuration + startDelay)
        {
            CleanupInstances();
            enabled = false;
            Debug.Log("[StressTest] Finished.");
            return;
        }

        burstTimer += Time.deltaTime;
        if (burstTimer >= burstInterval)
        {
            burstTimer = 0f;
            TriggerBurst();
        }
    }

    private void TriggerBurst()
    {
        if (eventsToStress == null || eventsToStress.Length == 0)
            return;

        for (int i = 0; i < instancesPerBurst; i++)
        {
            var evt = eventsToStress[Random.Range(0, eventsToStress.Length)];
            var instance = RuntimeManager.CreateInstance(evt);

            instance.start();
            activeInstances.Add(instance);

            StartCoroutine(ReleaseAfterSeconds(instance, instanceLifetime));
        }
    }

    private System.Collections.IEnumerator ReleaseAfterSeconds(EventInstance instance, float delay)
    {
        yield return new WaitForSeconds(delay);

        instance.stop(FMOD.Studio.STOP_MODE.ALLOWFADEOUT);
        instance.release();
        activeInstances.Remove(instance);
    }

    private void CleanupInstances()
    {
        foreach (var inst in activeInstances)
        {
            inst.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            inst.release();
        }
        activeInstances.Clear();
    }
}
