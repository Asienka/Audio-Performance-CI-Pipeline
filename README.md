<div align="center">

# Audio Performance CI Pipeline

**Automated performance regression detection for Unity games. Headless build → stress test → FMOD profiling → threshold validation. Blocks PRs that exceed CPU/voice/frame-time budgets.**

[![Status: Active](https://img.shields.io/badge/status-active-brightgreen)]()
[![Unity 2023.1.22f1](https://img.shields.io/badge/Unity-2023.1.22f1-blue?logo=unity)]()
[![FMOD](https://img.shields.io/badge/audio-FMOD-orange)]()
[![Python 3.11](https://img.shields.io/badge/python-3.11-blue?logo=python)]()
[![GitHub Actions](https://img.shields.io/badge/CI-GitHub%20Actions-2088FF?logo=github-actions)]()
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)]()

**TL;DR** — Push to `main` or open a PR. GitHub Actions builds the Unity project headless, runs an audio stress test, collects per-frame FMOD + Unity metrics, parses them in Python, and fails the build if anything crosses the defined thresholds. No manual profiling. No "wydaje mi się wolniejsze". Numbers or it didn't happen.

</div>

---

## What this is

A CI pipeline that catches performance regressions in Unity games **before** they ship. Built originally for audio profiling (FMOD + Unity integration), but the pattern generalizes to any per-frame metrics you want to gate.

In game development, performance testing is usually manual. Someone plays the build, says "wydaje mi się wolniejsze", dev opens Profiler, compares to last week. That's slow, subjective, and happens after the damage is done.

This pipeline does it automated, on every PR, with hard thresholds. Frame time goes from 16ms to 22ms? Build fails. FMOD DSP CPU spikes from 8% to 25%? Build fails. Voices average over 128? Build fails. Numbers don't lie.

---

## Quick start

### Prerequisites

- GitHub account
- Unity 2023.1.22f1 (Personal license is enough)
- FMOD Studio project (or replace with any audio middleware)

### Setup

1. **Add Unity license to GitHub secrets:**
   - `UNITY_EMAIL` — your Unity ID email
   - `UNITY_PASSWORD` — your Unity ID password
   - `UNITY_LICENSE` — your `.ulf` license file content (base64)

2. **Configure the stress test:**
   - Open `AudioProfiling/Assets/Scenes/Profiler.unity`
   - Add your FMOD events to `StressTestAudioEvents.eventsToStress`
   - Adjust `instancesPerBurst`, `burstInterval`, `totalTestDuration`

3. **Set your performance budgets** in `AudioProfiling/Assets/Tools/audio_thresholds.json`

4. **Push to `main` or open a PR.** Watch the pipeline run.

### What the pipeline does

```
┌──────────────────────────────────────────────────────────────┐
│  GitHub Actions Pipeline                                    │
│                                                              │
│  Job 1: build (ubuntu-latest)                               │
│  └─► Checkout                                               │
│  └─► game-ci/unity-builder@v4 (build StandaloneWindows64)   │
│  └─► Upload artifact: Build-Executable                      │
│                                                              │
│  Job 2: profile (windows-latest)                             │
│  └─► Download Build-Executable                              │
│  └─► Run AudioProfiling.exe -batchmode -nographics          │
│  └─► Collect profiler_output.json (next to .exe)            │
│  └─► Upload artifact: Profiler-Logs                         │
│                                                              │
│  Job 3: analyze (ubuntu-latest)                              │
│  └─► Download Profiler-Logs                                 │
│  └─► python parse_audioprofiler.py profiler_output.json     │
│  └─► Compare to audio_thresholds.json                       │
│  └─► Exit 0 (pass) or 1 (fail)                              │
└──────────────────────────────────────────────────────────────┘
```

**Why three jobs?** Build is slow and OS-agnostic. Profiling must run on Windows (Unity runtime constraint for the target build). Analysis is OS-agnostic. Splitting them gives you proper artifact caching, parallelization where possible, and clear failure attribution.

---

## Architecture

### What gets measured

Per frame, for `duration` seconds (default 10s):

| Metric | Source | Why it matters |
|---|---|---|
| `unityFrameMs` | `Time.deltaTime * 1000` | Overall frame budget |
| `fmodCpuDsp` | `FMOD.CoreSystem.getCPUUsage().dsp` | DSP mixing cost |
| `fmodCpuStream` | `FMOD.CoreSystem.getCPUUsage().stream` | Streaming cost |
| `fmodCpuUpdate` | `FMOD.CoreSystem.getCPUUsage().update` | System update cost |
| `totalFmodCpu` | Sum of the above | Total FMOD CPU load |
| `voices` | `ChannelGroup.getNumChannels()` | Active voices (memory + CPU) |

All saved as JSON. One file per run. One decision per run: pass or fail.

### How validation works

`parse_audioprofiler.py` reads the JSON, computes `min`/`max`/`avg` per metric, compares to `audio_thresholds.json`. Anything in `errors` fails the build. Anything in `warnings` is logged but doesn't fail.

```python
# From audio_thresholds.json
{
  "fmod": {
    "cpu": {
      "dsp": { "max": 15.0 },          # peak DSP CPU, fail if > 15%
      "stream": { "avg": 10.0 },      # avg stream CPU, fail if > 10%
      "total": { "max": 20.0 }        # peak total FMOD CPU, fail if > 20%
    },
    "voices": {
      "avg": 128,                     # warning if avg > 128
      "max": 256                      # fail if max > 256
    }
  },
  "unity": {
    "frame_ms": {
      "avg": 25.0,                    # warning if avg > 25ms
      "max": 40.0                     # fail if max > 40ms
    }
  }
}
```

**Current threshold values are placeholders.** Tune them for your target platform and game.

---

## Repository layout

```
.
├── .github/workflows/
│   └── audio-ci.yml              # GitHub Actions pipeline (3 jobs)
│
├── AudioProfiling/               # Unity project
│   ├── Assets/
│   │   ├── Editor/               # Custom Editor scripts (if any)
│   │   ├── FMODProject/          # FMOD Studio project
│   │   ├── Plugins/              # FMOD Unity integration
│   │   ├── Scenes/
│   │   │   └── Profiler.unity    # Scene with the stress test + profiler
│   │   ├── Scripts/
│   │   │   ├── LogAudioMetrics.cs    # Per-frame FMOD + Unity metrics collection
│   │   │   └── StressTestAudioEvents.cs  # Generates audio load
│   │   ├── StreamingAssets/     # FMOD banks (gitignored)
│   │   └── Tools/
│   │       ├── parse_audioprofiler.py   # Validation script
│   │       └── audio_thresholds.json    # Performance budgets
│   ├── Library/                  # Unity cache (gitignored)
│   ├── Packages/                 # Unity packages manifest
│   ├── ProjectSettings/          # Unity project settings
│   └── UserSettings/             # Unity user settings
│
└── README.md
```

---

## Design choices

These are the decisions that aren't obvious from a quick scan. If something in the repo looks weird, it's probably because of one of these.

**Headless FMOD initialization with NOSOUND fallback.** Running FMOD in a CI environment without audio hardware is non-trivial. The first init attempt often fails because there's no audio device. The script tries the normal init, then falls back to `OUTPUTTYPE.NOSOUND`, which gives FMOD a valid but silent output. This works on both Windows and Linux runners. Without this fallback, every CI run crashes at startup.

**Editor vs Build branching in `LogAudioMetrics.cs`.** The profiler has to know how to quit cleanly in both contexts. In the Unity Editor, you call `UnityEditor.EditorApplication.isPlaying = false`. In a standalone build, you call `Application.Quit()`. Preprocessor directives (`#if UNITY_EDITOR`) handle this without runtime checks. Cleaner and avoids "Editor assembly not found" errors in builds.

**Force-save on `OnApplicationQuit()`.** If the build is killed before the duration timer completes (timeout, memory pressure, whatever), `hasSaved` is still false. The `OnApplicationQuit` callback forces a save of whatever samples were collected. Better to have partial data than no data when debugging.

**Three job pipeline instead of one.** The build (job 1) is slow but OS-agnostic. The profile run (job 2) must happen on Windows because Unity's Windows Standalone build expects to run on Windows. The analysis (job 3) is pure Python, runs anywhere. Splitting them means: faster builds (only rebuild when needed), proper OS targeting, and you can re-run analysis on a stored artifact without re-building.

**`game-ci/unity-builder` instead of custom Docker.** Writing a custom Unity Docker image is a project unto itself. `game-ci/unity-builder` is maintained by the community, handles license activation, supports multiple Unity versions, and outputs build artifacts in a standard location. Don't reinvent this.

**Artifact retention over Git LFS for build output.** Build artifacts (the .exe) are large (50-200MB) and binary. Storing them in Git LFS bloats the repo. GitHub Actions artifacts are the right place for them: ephemeral, per-run, downloadable for debugging. Default 90-day retention is fine for debugging; lower it if you want to save storage.

**Threshold-based validation, not baseline comparison.** The pipeline compares to absolute thresholds (e.g., "FMOD DSP CPU must stay under 15%"), not to "previous build was 8%, this build is 12%". Absolute thresholds are simpler to reason about, easier to set ("frame budget is 16.67ms for 60 FPS, period"), and don't break when you change platforms or have a bad baseline. Baseline comparison is a future enhancement for teams that need more nuance.

**Stress test uses random event selection.** `Random.Range(0, eventsToStress.Length)` simulates real gameplay where audio events don't fire in a fixed pattern. A deterministic stress test (always events[0], events[1], events[2]...) would optimize for specific code paths and miss issues that only appear with varied loads.

---

## Running locally

### Unity Editor

Open `AudioProfiling/` as a Unity project. Load `Scenes/Profiler.unity`. Press Play. The profiler runs for `duration` seconds, saves JSON next to the project root, then quits.

### Standalone build

After building (e.g., via Unity Editor's Build Settings), run the .exe with `-batchmode -nographics`:

```bash
AudioProfiling.exe -batchmode -nographics -logFile ./unity.log
```

The profiler_output.json appears next to the executable.

### Validation script

```bash
# From repo root
python AudioProfiling/Assets/Tools/parse_audioprofiler.py path/to/profiler_output.json
```

Exit code 0 = pass, exit code 1 = fail. Thresholds loaded from `audio_thresholds.json` next to the script.

---

## What I ran into

The rough edges that took figuring out, in case anyone else hits them.

**FMOD failing to initialize in headless mode.** First version of the script assumed FMOD would just work. It didn't, because the CI runner has no audio device. The error message ("OutputType None returned ERR_INITIALIZE") is unhelpful. The fix is the NOSOUND output fallback. The pattern is: try normal init → if it fails, try NOSOUND → if that fails, save empty results and exit cleanly. Never crash silently.

**`LogAudioMetrics` not saving before application quit.** First version relied entirely on the `duration` timer to trigger save. But the timer-based save happens in `Update()`, which doesn't run after the duration ends and the script calls `Application.Quit()`. Race condition. The fix: `hasSaved` flag + `OnApplicationQuit()` callback that force-saves if there are unsaved samples. Always provide a fallback path for "we're shutting down, do what you can."

**Build artifact too large for GitHub release.** Tried attaching the .exe to GitHub Releases. Hit the 2GB limit for some build configurations. Switched to GitHub Actions artifacts: ephemeral, no size limit per artifact, automatic cleanup. The lesson: don't use Releases for CI artifacts; use artifacts for CI artifacts.

**Job 2 (profile) timing out on long stress tests.** Default GitHub Actions job timeout is 360 minutes. Most builds don't hit that, but 30+ minute stress tests occasionally do. The fix: explicit `timeout-minutes` in the job definition, set to a reasonable upper bound (e.g., 60 minutes). If you hit it, you have bigger problems than a timeout.

**`game-ci/unity-builder` license activation flaky on first run.** The action sometimes fails to activate the license on the first attempt. The workaround is a retry step. Recent versions of the action handle this better, but if you see intermittent license errors, check the action's changelog.

**Profiler JSON from Unity is not real JSON.** Unity's `JsonUtility.ToJson` produces technically valid JSON, but the structure (nested generic types, wrapper classes) is awkward to parse. The `parse_audioprofiler.py` script has to know the exact structure. If you add fields to `AudioFrameData` or `AudioMetricsWrapper`, update the Python parser. There's no schema enforcement. The fix for the future: use a proper schema (JSON Schema, Pydantic, or Protocol Buffers).

---

## Contributing

This is a personal portfolio project, but if you spot a bug in the Unity scripts, the Python parser, or the GitHub Actions workflow — PRs are welcome. The easiest way to start is to fork, set up the Unity + FMOD environment, and try running the pipeline locally.

---

## License

MIT
