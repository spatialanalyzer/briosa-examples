# Briosa examples

Small applications demonstrating integration with SpatialAnalyzer through
[Briosa](https://github.com/spatialanalyzer/briosa).

The first example is a point inspection workbench implemented with direct gRPC,
the .NET client, TypeScript, and Python. All four use the same synthetic fixture
and report contract.

| Example | Start reading here | What it teaches |
| --- | --- | --- |
| Direct C# gRPC | [Program.cs](point-inspection/grpc/Program.cs) | Generated clients, explicit discovery/readiness, lifecycle generation guards, structured errors |
| .NET client | [Program.cs](point-inspection/dotnet/Program.cs) | Async lifecycle, native values, typed operation errors |
| TypeScript client | [inspection.ts](point-inspection/typescript/src/inspection.ts) | Node.js client ownership, cancellation, report export |
| Python client | [inspection.py](point-inspection/python/inspection.py) | Async workflows, data analysis, deterministic reports |

The two C# applications share only [application/report logic](point-inspection/csharp/Inspection.cs).
The raw gRPC project has no dependency on the Briosa .NET client package.

## Run without SpatialAnalyzer

Run all commands from the repository root. Use Windows x64, .NET SDK 10.0.401,
Node.js 24, and Python 3.12. Synthetic mode is the default; it never starts a
Briosa server or contacts SA. Live execution always requires `--live`.

```powershell
# C# using only generated gRPC
./eng/Import-Protocol.ps1
dotnet restore point-inspection/grpc/PointInspection.Grpc.csproj --locked-mode
dotnet run --project point-inspection/grpc -c Release -- --output artifacts/grpc-demo

# C# using the published .NET client
dotnet restore point-inspection/dotnet/PointInspection.csproj --locked-mode
dotnet run --project point-inspection/dotnet -c Release -- --output artifacts/dotnet-demo

# TypeScript using the published npm client
Push-Location point-inspection/typescript
npm ci --ignore-scripts
npm run build
Pop-Location
node point-inspection/typescript/dist/inspection.js --output artifacts/typescript-demo

# Python (synthetic mode works with the standard library alone)
python point-inspection/python/inspection.py --output artifacts/python-demo
```

Expect eight PASS checks and two FAIL checks. This is deliberate: P3 and P8
are out of tolerance. Exit **0** means a complete all-pass report, **2** means
a complete report with failed checks, and **1** means inspection could not
complete. Each output folder contains `report.csv` and `report.json`. Use a new
output folder for each run; existing reports are never overwritten.

`--fixture <folder>` selects another [fixture](point-inspection/fixture/README.md).
Reports show the source (`synthetic` or `live`), units and frame. CSV coordinates
are in millimeters; JSON also records frame identity. No report is published
after a failed call, missing output, or a detected context change.
See the shared [report contract](docs/report-contract.md) for fields and rules.

## Release pairing

| Implementation | Package pin | Server pairing |
| --- | --- | --- |
| Direct gRPC | Hash-verified protocol 0.6.1 | Server 0.6.1, SA 2026.1.0529.7 |
| .NET | `Briosa.2026.1.0529.7` 0.1.1 | Server 0.6.1, SA 2026.1.0529.7 |
| TypeScript | `@spatialanalyzer/briosa-2026.1.0529.7` 0.1.1 via `briosa` alias | Server 0.6.1, SA 2026.1.0529.7 |
| Python | `briosa-2026-1-0529-7` 0.1.1 | Server 0.6.1, SA 2026.1.0529.7 |

All implementations use Server **0.6.1**. Client **0.1.1** validates the exact
server version, source revision, and SA target. Install the matching server
distribution; an unchanged protobuf schema does not make other builds compatible.

References: [Server 0.6.1](https://github.com/spatialanalyzer/briosa/releases/tag/v0.6.1),
[.NET 0.1.1](https://github.com/spatialanalyzer/briosa-dotnet/releases/tag/v0.1.1),
[JS/TS 0.1.1](https://github.com/spatialanalyzer/briosa-js/releases/tag/v0.1.1),
[Python 0.1.1](https://github.com/spatialanalyzer/briosa-py/releases/tag/v0.1.1).

## Live SA workflow

First prepare the [scratch SA job](point-inspection/fixture/README.md). Leave it
open and unchanged. Use one application against one SA target at a time. These
examples read named points listed in the inspection plan; a project-browser
example can add collection/group enumeration independently.

For direct gRPC, start a dedicated released Server 0.6.1 on loopback with its
SDK stopped. Do not connect that server through Control Center first: this
example owns the SDK lifecycle. Then run:

```powershell
dotnet run --project point-inspection/grpc -c Release -- --live --endpoint http://127.0.0.1:50051 --output artifacts/grpc-live
```

The example checks build identity and capabilities, starts an SDK generation,
connects it to the already-running SA application, and requires exact identities
and execution readiness. All calls have deadlines. Cleanup stops only its SDK
generation; the externally started server and SA remain open. If SDK startup
times out before returning its generation, inspect the server before trying
again: the example cannot safely infer ownership from a missing response.

For language-client variants, install client 0.1.1 and the matching Server
0.6.1 distribution, set `BRIOSA_SERVER_PATH` if needed, and append `--live` to
the same console commands. Python additionally requires its isolated environment:

```powershell
python -m venv point-inspection/python/.venv
./point-inspection/python/.venv/Scripts/python -m pip install -r point-inspection/python/requirements.txt
./point-inspection/python/.venv/Scripts/python point-inspection/python/inspection.py --live --output artifacts/python-live
```

Each client owns its local server and SDK, attaches to the prepared SA job,
checks capabilities, and disposes its resources while leaving SA open. A
deadline or caller cancellation does not prove that an in-flight SA operation
was cancelled. The examples never replay an MP call automatically.

## Validation and development

```powershell
./eng/Test.ps1
```

This runs the real generated gRPC/client code against a local test double and
compares all four reports with independently specified results. It also checks
failure paths, missing data, context changes, deadlines, and no automatic replay.
See [validation and licensed acceptance](docs/validation.md). Licensed SA
acceptance has not been executed for this example.

This is the first console milestone tracked by
[briosa#207](https://github.com/spatialanalyzer/briosa/issues/207). SA 2024
qualification, the local TypeScript web interface, and fixture creation/write-back
are follow-up milestones. Shared API semantics remain in the
[Briosa client contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/client-library-behavioral-contract.md).

SpatialAnalyzer and its SDK must be installed and licensed separately for live
execution. They are not included. Portable tests do not require SpatialAnalyzer.

Licensed under Apache-2.0. SpatialAnalyzer, the SA SDK, and their brands remain
Hexagon intellectual property. This repository does not imply Hexagon endorsement.
