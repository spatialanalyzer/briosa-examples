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

## Compatibility and package pins

The examples use published client **0.2.0** and the hash-verified Server **0.7.0**
protocol for SA **2026.1.0529.7**. Dependency lockfiles remain exact. Runtime
selection follows behavioral contract **1.0**, independently of that generation
pin; the reviewed Server **0.6.1** build is the narrow legacy exception.

Use the package version recorded in each implementation's lockfile. The portable
acceptance suite consumes the actual NuGet, npm, and PyPI releases.

See [side-by-side applications](docs/side-by-side.md) for automatic discovery,
per-application installation/version/path choices, and migration from shared
path variables. `--discover` explains selection without starting a server or SA.
The raw gRPC implementation has a separately explained
[BCL-only bootstrap](point-inspection/grpc/Bootstrap/README.md).

## Live SA workflow

First prepare the [scratch SA job](point-inspection/fixture/README.md). Leave it
open and unchanged. Use one application against one SA target at a time. These
examples read named points listed in the inspection plan; a project-browser
example can add collection/group enumeration independently.

All four variants discover and own their local Briosa server by default. Append
`--live` to the synthetic commands after preparing the SA job. Use an explicit
per-application selection when needed:

```powershell
dotnet run --project point-inspection/grpc -c Release -- --live --server-version 0.7.0 --output artifacts/grpc-live
```

The raw example validates the selected manifest against live server identity,
checks capabilities, starts an SDK generation, connects to the prepared SA job,
and requires exact SDK/SA identities and readiness. Its RPCs have deadlines;
cleanup stops its guarded SDK generation and owned server, leaving SA open.
The language-client examples express the same sequence through client APIs.

For an external dedicated server with its SDK stopped, raw gRPC also accepts
`--endpoint http://127.0.0.1:50051`. It leaves that server open. An ambiguous SDK
startup response cannot prove generation ownership; inspect the server before
trying again. Local selectors and an external endpoint are mutually exclusive.

Python additionally requires its isolated environment:

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
