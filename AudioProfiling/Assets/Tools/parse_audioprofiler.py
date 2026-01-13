#!/usr/bin/env python3
"""
Audio Profiler Parser
Analyzes Unity FMOD audio performance metrics and validates against thresholds.
"""

import json
import sys
import statistics
from pathlib import Path
from typing import Dict, List, Any, Optional

# ----------------------------
# Default Thresholds
# ----------------------------
DEFAULT_THRESHOLDS = {
    "fmod": {
        "cpu": {
            "dsp": {"max": 20.0, "avg": 10.0},
            "stream": {"max": 5.0, "avg": 2.0},
            "update": {"max": 2.0, "avg": 1.0},
            "total": {"max": 25.0, "avg": 15.0}
        },
        "voices": {
            "max": 64,
            "avg": 32
        }
    },
    "unity": {
        "frame_ms": {
            "max": 33.0,  # ~30 FPS
            "avg": 16.6   # ~60 FPS
        }
    }
}

# ----------------------------
# Helpers
# ----------------------------

def load_json(path: Path) -> Dict[str, Any]:
    """Load and parse JSON file with error handling."""
    if not path.exists():
        print(f"[ERROR] File not found: {path}")
        sys.exit(1)
    
    try:
        with path.open("r", encoding="utf-8") as f:
            return json.load(f)
    except json.JSONDecodeError as e:
        print(f"[ERROR] Invalid JSON in {path}: {e}")
        sys.exit(1)
    except Exception as e:
        print(f"[ERROR] Failed to read {path}: {e}")
        sys.exit(1)


def load_thresholds(script_dir: Path) -> Dict[str, Any]:
    """Load thresholds from file or use defaults."""
    thresholds_path = script_dir / "audio_thresholds.json"
    
    if thresholds_path.exists():
        print(f"[INFO] Loading thresholds from: {thresholds_path}")
        try:
            return load_json(thresholds_path)
        except:
            print("[WARNING] Failed to load thresholds file, using defaults")
            return DEFAULT_THRESHOLDS
    else:
        print("[INFO] No thresholds file found, using defaults")
        print(f"[INFO] To customize, create: {thresholds_path}")
        return DEFAULT_THRESHOLDS


def calculate_stats(values: List[float]) -> Dict[str, float]:
    """Calculate min, max, avg, and percentiles for a list of values."""
    if not values:
        return {"min": 0, "max": 0, "avg": 0, "p95": 0, "p99": 0}
    
    sorted_values = sorted(values)
    return {
        "min": min(values),
        "max": max(values),
        "avg": statistics.mean(values),
        "median": statistics.median(values),
        "p95": sorted_values[int(len(sorted_values) * 0.95)] if len(sorted_values) > 0 else 0,
        "p99": sorted_values[int(len(sorted_values) * 0.99)] if len(sorted_values) > 0 else 0
    }


def safe_get(data: Dict, *keys, default=0):
    """Safely get nested dictionary values."""
    for key in keys:
        if isinstance(data, dict):
            data = data.get(key, default)
        else:
            return default
    return data


# ----------------------------
# Validation
# ----------------------------

class ValidationResult:
    def __init__(self):
        self.errors: List[str] = []
        self.warnings: List[str] = []
        self.info: List[str] = []
    
    def add_error(self, msg: str):
        self.errors.append(msg)
    
    def add_warning(self, msg: str):
        self.warnings.append(msg)
    
    def add_info(self, msg: str):
        self.info.append(msg)
    
    def has_failures(self) -> bool:
        return len(self.errors) > 0


def check_threshold(
    name: str,
    value: float,
    threshold: float,
    stat_type: str,
    result: ValidationResult
):
    """Check if a value exceeds a threshold."""
    if value > threshold:
        result.add_error(f"{name} {stat_type} {value:.2f} > {threshold:.2f}")
    else:
        result.add_info(f"{name} {stat_type} {value:.2f} <= {threshold:.2f} ✓")


def validate_metrics(stats: Dict[str, Dict], thresholds: Dict) -> ValidationResult:
    """Validate collected metrics against thresholds."""
    result = ValidationResult()
    
    # FMOD CPU checks
    fmod_thresholds = thresholds.get("fmod", {}).get("cpu", {})
    
    check_threshold(
        "FMOD DSP CPU",
        stats["fmod_cpu_dsp"]["max"],
        safe_get(fmod_thresholds, "dsp", "max", default=20.0),
        "max",
        result
    )
    
    check_threshold(
        "FMOD DSP CPU",
        stats["fmod_cpu_dsp"]["avg"],
        safe_get(fmod_thresholds, "dsp", "avg", default=10.0),
        "avg",
        result
    )
    
    check_threshold(
        "FMOD Stream CPU",
        stats["fmod_cpu_stream"]["avg"],
        safe_get(fmod_thresholds, "stream", "avg", default=2.0),
        "avg",
        result
    )
    
    check_threshold(
        "FMOD Total CPU",
        stats["fmod_cpu_total"]["max"],
        safe_get(fmod_thresholds, "total", "max", default=25.0),
        "max",
        result
    )
    
    # Voice checks
    voices_thresholds = thresholds.get("fmod", {}).get("voices", {})
    
    avg_threshold = voices_thresholds.get("avg", 32)
    if stats["voices"]["avg"] > avg_threshold:
        result.add_warning(
            f"Voices avg {stats['voices']['avg']:.1f} > {avg_threshold}"
        )
    
    max_threshold = voices_thresholds.get("max", 64)
    if stats["voices"]["max"] > max_threshold:
        result.add_error(
            f"Voices max {stats['voices']['max']} > {max_threshold}"
        )
    
    # Unity frame time checks
    frame_thresholds = thresholds.get("unity", {}).get("frame_ms", {})
    
    avg_threshold = frame_thresholds.get("avg", 16.6)
    if stats["unity_frame_ms"]["avg"] > avg_threshold:
        result.add_warning(
            f"Frame avg {stats['unity_frame_ms']['avg']:.2f}ms > {avg_threshold}ms"
        )
    
    max_threshold = frame_thresholds.get("max", 33.0)
    if stats["unity_frame_ms"]["max"] > max_threshold:
        result.add_error(
            f"Frame max {stats['unity_frame_ms']['max']:.2f}ms > {max_threshold}ms"
        )
    
    return result


# ----------------------------
# Report Generation
# ----------------------------

def generate_report(stats: Dict, metadata: Dict, result: ValidationResult) -> Dict:
    """Generate structured report for output."""
    return {
        "metadata": metadata,
        "statistics": stats,
        "validation": {
            "passed": not result.has_failures(),
            "errors": result.errors,
            "warnings": result.warnings
        }
    }


def print_report(stats: Dict, metadata: Dict, result: ValidationResult):
    """Print human-readable report to console."""
    print("\n" + "="*60)
    print("AUDIO PERFORMANCE REPORT")
    print("="*60)
    
    # Metadata
    if metadata:
        print("\n[METADATA]")
        for key, value in metadata.items():
            print(f"  {key}: {value}")
    
    # Statistics
    print("\n[STATISTICS]")
    for name, values in stats.items():
        print(f"\n{name}:")
        for stat, val in values.items():
            if isinstance(val, float):
                print(f"  {stat}: {val:.2f}")
            else:
                print(f"  {stat}: {val}")
    
    # Validation results
    if result.info:
        print("\n[CHECKS PASSED]")
        for msg in result.info:
            print(f"  ✓ {msg}")
    
    if result.warnings:
        print("\n[WARNINGS]")
        for msg in result.warnings:
            print(f"  ⚠ {msg}")
    
    if result.errors:
        print("\n[FAILURES]")
        for msg in result.errors:
            print(f"  ✗ {msg}")
    
    # Final verdict
    print("\n" + "="*60)
    if result.has_failures():
        print("RESULT: FAIL ❌")
        print("="*60)
    else:
        print("RESULT: PASS ✅")
        print("="*60)


# ----------------------------
# Main Entry Point
# ----------------------------

def main():
    if len(sys.argv) < 2:
        print("Usage: python parse_audioprofiler.py <profiler_output.json>")
        print("\nOptional: Create audio_thresholds.json in the same directory")
        print("to customize performance thresholds.")
        sys.exit(1)

    metrics_path = Path(sys.argv[1])
    script_dir = Path(__file__).parent
    
    # Load data
    print(f"[INFO] Loading metrics from: {metrics_path}")
    metrics = load_json(metrics_path)
    thresholds = load_thresholds(script_dir)
    
    # Extract samples
    samples = metrics.get("samples", [])
    if not samples:
        print("[ERROR] No samples found in profiler output.")
        sys.exit(1)
    
    print(f"[INFO] Processing {len(samples)} samples...")
    
    # Extract metadata
    metadata = {
        "timestamp": metrics.get("timestamp", "unknown"),
        "unity_version": metrics.get("unityVersion", "unknown"),
        "platform": metrics.get("platform", "unknown"),
        "sample_count": metrics.get("sampleCount", len(samples)),
        "total_duration": metrics.get("totalDuration", 0),
        "sampling_interval": metrics.get("samplingInterval", 1)
    }
    
    # Collect metrics
    try:
        dsp_cpu = [s.get("fmodCpuDsp", 0) for s in samples]
        stream_cpu = [s.get("fmodCpuStream", 0) for s in samples]
        update_cpu = [s.get("fmodCpuUpdate", 0) for s in samples]
        total_cpu = [s.get("totalFmodCpu", 0) for s in samples]
        voices = [s.get("voices", 0) for s in samples]
        frame_ms = [s.get("unityFrameMs", 0) for s in samples]
    except KeyError as e:
        print(f"[ERROR] Missing expected field in samples: {e}")
        sys.exit(1)
    
    # Calculate statistics
    stats = {
        "fmod_cpu_dsp": calculate_stats(dsp_cpu),
        "fmod_cpu_stream": calculate_stats(stream_cpu),
        "fmod_cpu_update": calculate_stats(update_cpu),
        "fmod_cpu_total": calculate_stats(total_cpu),
        "voices": calculate_stats(voices),
        "unity_frame_ms": calculate_stats(frame_ms)
    }
    
    # Validate
    result = validate_metrics(stats, thresholds)
    
    # Generate outputs
    print_report(stats, metadata, result)
    
    # Save JSON report
    report = generate_report(stats, metadata, result)
    report_path = Path("report.json")
    
    try:
        with report_path.open("w", encoding="utf-8") as f:
            json.dump(report, f, indent=2)
        print(f"\n[INFO] Detailed report saved to: {report_path}")
    except Exception as e:
        print(f"[WARNING] Failed to save report.json: {e}")
    
    # Exit with appropriate code
    sys.exit(1 if result.has_failures() else 0)


if __name__ == "__main__":
    main()
