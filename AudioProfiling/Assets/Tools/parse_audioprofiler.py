import json
import sys
import statistics
from pathlib import Path

# ----------------------------
# Helpers
# ----------------------------

def load_json(path: Path):
    if not path.exists():
        print(f"[ERROR] File not found: {path}")
        sys.exit(1)
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)

def stat(values):
    return {
        "min": min(values),
        "max": max(values),
        "avg": statistics.mean(values)
    }

# ----------------------------
# Entry
# ----------------------------

if len(sys.argv) < 2:
    print("Usage: python parse_profiler.py <profiler_output.json>")
    sys.exit(1)

metrics_path = Path(sys.argv[1])
thresholds_path = Path(__file__).parent / "thresholds.json"

metrics = load_json(metrics_path)
thresholds = load_json(thresholds_path)

samples = metrics.get("samples", [])
if not samples:
    print("[ERROR] No samples found in profiler output.")
    sys.exit(1)

# ----------------------------
# Collect metrics
# ----------------------------

dsp_cpu = [s["fmodCpuDsp"] for s in samples]
stream_cpu = [s["fmodCpuStream"] for s in samples]
total_cpu = [s["totalFmodCpu"] for s in samples]
voices = [s["voices"] for s in samples]
frame_ms = [s["unityFrameMs"] for s in samples]

stats = {
    "fmod_cpu_dsp": stat(dsp_cpu),
    "fmod_cpu_stream": stat(stream_cpu),
    "fmod_cpu_total": stat(total_cpu),
    "voices": stat(voices),
    "unity_frame_ms": stat(frame_ms)
}

# ----------------------------
# Evaluation
# ----------------------------

errors = []
warnings = []

def check_max(name, value, limit):
    if value > limit:
        errors.append(f"{name} max {value:.2f} > {limit}")

def check_avg(name, value, limit):
    if value > limit:
        errors.append(f"{name} avg {value:.2f} > {limit}")

# FMOD CPU
check_max(
    "FMOD DSP CPU",
    stats["fmod_cpu_dsp"]["max"],
    thresholds["fmod"]["cpu"]["dsp"]["max"]
)

check_avg(
    "FMOD Stream CPU",
    stats["fmod_cpu_stream"]["avg"],
    thresholds["fmod"]["cpu"]["stream"]["avg"]
)

check_max(
    "FMOD Total CPU",
    stats["fmod_cpu_total"]["max"],
    thresholds["fmod"]["cpu"]["total"]["max"]
)

# Voices
if stats["voices"]["avg"] > thresholds["fmod"]["voices"]["avg"]:
    warnings.append(
        f"Voices avg {stats['voices']['avg']:.1f} > {thresholds['fmod']['voices']['avg']}"
    )

if stats["voices"]["max"] > thresholds["fmod"]["voices"]["max"]:
    errors.append(
        f"Voices max {stats['voices']['max']} > {thresholds['fmod']['voices']['max']}"
    )

# Unity frame time
if stats["unity_frame_ms"]["avg"] > thresholds["unity"]["frame_ms"]["avg"]:
    warnings.append(
        f"Frame avg {stats['unity_frame_ms']['avg']:.2f}ms > {thresholds['unity']['frame_ms']['avg']}ms"
    )

if stats["unity_frame_ms"]["max"] > thresholds["unity"]["frame_ms"]["max"]:
    errors.append(
        f"Frame max {stats['unity_frame_ms']['max']:.2f}ms > {thresholds['unity']['frame_ms']['max']}ms"
    )

# ----------------------------
# Report
# ----------------------------

print("\n=== AUDIO PERFORMANCE REPORT ===")
for k, v in stats.items():
    print(f"{k}: avg={v['avg']:.2f} max={v['max']:.2f}")

if warnings:
    print("\n[WARNINGS]")
    for w in warnings:
        print(" -", w)

if errors:
    print("\n[FAIL]")
    for e in errors:
        print(" -", e)
    sys.exit(1)

print("\n[PASS] Audio performance within thresholds.")
sys.exit(0)
