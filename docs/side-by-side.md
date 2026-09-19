# Side-by-side applications

A client package identifies its exact SpatialAnalyzer target. The server
selection belongs to one application's startup. There is no shared environment
variable or machine-wide "active Briosa" switch.

For example, two independent applications can use these settings:

| Application | Client product | Selection |
| --- | --- | --- |
| Inspection for SA 2026 | `Briosa.2026.1.0529.7`, `@spatialanalyzer/briosa-2026.1.0529.7`, or `briosa-2026-1-0529-7` | Automatic compatible stable release, or its own installation ID |
| Legacy integration for SA 2024 | The corresponding `2024.1.0508.5` product | A separate automatic selection or explicit server path |

Keep Python targets in separate virtual environments and Node targets in
separate applications. Package versions such as 0.2.0 are independent of the
SA release and Briosa server version.

The Point Inspection Workbench itself remains an SA 2026 example. This table
demonstrates deployment selection; it does not qualify the inspection workflow
for SA 2024 or transfer SA 2026 licensed observations to that target.

## Inspect selection before live work

All four programs accept the same discovery arguments:

```powershell
dotnet run --project point-inspection/grpc -c Release -- --discover
dotnet run --project point-inspection/dotnet -c Release -- --discover --server-version 0.7.0
node point-inspection/typescript/dist/inspection.js --discover --search-root "D:\Briosa Packages"
./point-inspection/python/.venv/Scripts/python point-inspection/python/inspection.py --discover --installation-id "<id from discovery>"
```

Discovery prints explicitly requested paths, metadata, and rejection reasons.
It launches no server, SDK, or SA process. A successful selection exits 0; an
unresolved selection exits 1. Do not include private paths from these reports
in ordinary application logs.

For a portable distribution, pass the actual absolute executable path:

```powershell
dotnet run --project point-inspection/grpc -c Release -- --live --server-path "D:\Briosa\2026\Briosa.Server.exe" --output artifacts/inspection-2026
```

Other options are `--server-version`, `--installation-id`, `--search-root`,
`--allow-prerelease`, and `--sa-path`. An installation ID and server path are
mutually exclusive. Invalid explicit choices never fall back. The sample
attaches to a prepared SA job and does not launch SA; `--sa-path` demonstrates
how to supply exact application evidence if you adapt the startup phase to
request application launch.

Without selectors, each application picks the highest compatible stable
server for its own target. Equal-version conflicts fail as ambiguous. The
Registry index is a lookup aid over committed receipts; use Installer 0.3.0's
registration rescan for existing custom stores, or pass an explicit search root.

## Compatibility and ownership

The client generation artifact stays pinned for repeatable builds. Runtime
admission requires contract major 1 with a sufficient revision, plus exact SA
target and selected-manifest provenance. Server 0.6.1 is the single reviewed
legacy exception. Existing client 0.1.x packages retain their exact build pins.

The selected distribution stays fixed until the session ends. Recovery never
silently selects an upgraded installation. SDK/SA identity and bounded
execution readiness remain independent checks. The examples never change
COM registration, close an unrelated SA process, or automatically replay an MP.

Several installed releases do not establish safe concurrent execution.
Windows SDK registration and the SA instance owning communication ports still
determine which SDK/application pair can execute. Run the examples sequentially
against one prepared job and establish the intended exact SDK registration
before each target's licensed acceptance.

## Direct gRPC

The raw example's [bootstrap](../point-inspection/grpc/Bootstrap/README.md) reads
installation evidence and owns its server process. Its application code uses
only standard generated gRPC clients, with explicit capability, readiness,
generation, and error checks.

`--endpoint http://127.0.0.1:port` remains available for an externally started,
dedicated loopback server whose SDK is stopped. It cannot be combined with
local installation selectors. Cleanup stops only the SDK generation the example
started and leaves that external server running.

The language-client variants demonstrate the same discovery contract with the
idiomatic startup options. They need no per-machine path variable. Existing
scripts should replace `BRIOSA_SERVER_PATH` with `--server-path`; the new client
line's legacy environment opt-in is deliberately not enabled by these examples.
