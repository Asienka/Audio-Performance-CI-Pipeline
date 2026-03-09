Audio Performance CI Pipeline

Automated CI pipeline designed to validate runtime audio performance in a Unity project.
The pipeline builds the project in headless mode, runs automated performance tests, and analyzes collected metrics to detect performance regressions.

============================

Project Goal

In game development, performance testing is often manual and time-consuming.
The goal of this project was to create a reproducible automated workflow that validates runtime performance and detects regressions early during development.

============================

Pipeline Overview

The CI pipeline performs the following steps:

- Build Unity project in headless mode
- Run automated runtime audio stress tests
- Collect structured performance metrics in JSON format
- Validate results against defined performance thresholds
- Report potential regressions during CI execution

The pipeline is implemented using GitHub Actions and runs automatically on repository updates.

============================
### Pipeline Architecture

Developer Push
      ↓
GitHub Actions Workflow
      ↓
Headless Unity Build
      ↓
Automated Runtime Test Execution
      ↓
JSON Metrics Collection
      ↓
Threshold-Based Validation
      ↓
CI Result (Pass / Fail)

============================

Technologies

GitHub Actions
Unity (headless builds)
FMOD
JSON metrics logging
Key Features
automated build and runtime execution
structured performance metrics collection
threshold-based performance validation
reproducible CI testing workflow
debugging of middleware initialization in CI environment

=======================

Example Metrics Output

Performance metrics are exported in structured JSON format and analyzed during CI execution.


Example:

{
  "cpu_usage": 42,
  "audio_sources_active": 128,
  "frame_time_ms": 16.7
}

===========================

