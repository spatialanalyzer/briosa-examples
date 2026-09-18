# Portable validation — September 18, 2026

The four console examples passed **56 portable scenarios**: fourteen cases for
each of direct C# gRPC, the .NET client, TypeScript and Python.

| Case | Direct gRPC | .NET | TypeScript | Python |
| --- | --- | --- | --- | --- |
| Synthetic fixture and expected reports | Pass | Pass | Pass | Pass |
| Actual transport/client calls against the fake server | Pass | Pass | Pass | Pass |
| Wrong server identity | Pass | Pass | Pass | Pass |
| Unavailable operation | Pass | Pass | Pass | Pass |
| Disconnected/not-ready SDK | Pass | Pass | Pass | Pass |
| Incorrect length unit | Pass | Pass | Pass | Pass |
| Working frame changes during inspection | Pass | Pass | Pass | Pass |
| Missing point | Pass | Pass | Pass | Pass |
| MP failure | Pass | Pass | Pass | Pass |
| Unknown execution outcome | Pass | Pass | Pass | Pass |
| Missing scalar output | Pass | Pass | Pass | Pass |
| Nonfinite coordinate | Pass | Pass | Pass | Pass |
| Deadline while awaiting a point | Pass | Pass | Pass | Pass |
| Invalid fixture input | Pass | Pass | Pass | Pass |

Success scenarios compare every CSV field with the committed expected output
and check JSON numeric values to 1e-6 mm. They also verify that a subsequent run
cannot overwrite an existing report. Failure scenarios check that no report is
published. The harness checks exactly one point request for injected operation
failures (no replay), and exactly one SDK stop for an observed SDK start.

The local environment used Windows, .NET SDK 10.0.401, Node.js 24.21.0 and
Python 3.12. Packages were restored from NuGet, npm and PyPI. Both C# example
projects built with zero warnings and errors; TypeScript compiled in strict mode.

The final run uses published client **0.1.1** packages and the hash-verified
Server **0.6.1** protocol. The test double advertises the same Server 0.6.1
version/source identity for all four implementations; compatibility checks are
not bypassed. Separate clean consumers verified normal NuGet, npm and PyPI
installation of client 0.1.1 for both SA targets. These portable checks do not
establish licensed runtime validation.

No SpatialAnalyzer application, SDK, COM connection, measurement hardware or
license was used. The licensed acceptance procedure remains unexecuted.

Reproduce with `./eng/Test.ps1`. Per-run calls, console output, reports and the
machine-readable summary are written under `artifacts/test-*` and uploaded by CI.
