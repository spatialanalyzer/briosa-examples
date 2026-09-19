# Prepare SpatialAnalyzer

## Open your job

Use Windows x64 with **SpatialAnalyzer 2026.1.0529.7** and its SDK installed and
licensed. Install the matching Briosa server using the
[Briosa Installer](https://briosa.dev/docs/releases).

Open SA and a job containing two points. In each example, change
`BriosaDemo`, `Points`, `P1`, and `P2` to their collection, group, and point
names in your job.

To reproduce the example output, create these points manually in a scratch job,
using millimeters and the same working frame for creation and reading:

| Collection | Group | Point | X | Y | Z |
| --- | --- | --- | --- | --- | --- |
| BriosaDemo | Points | P1 | 0 | 0 | 0 |
| BriosaDemo | Points | P2 | 3 | 4 | 0 |

Run one example at a time. Use one running SA instance that owns SDK communication,
and leave the job, units, and working frame unchanged while the program runs.

## Language clients

The .NET, TypeScript, and Python examples start their own Briosa server, connect
to the already-open SA application, and clean up when finished. Stop any SDK
session you started in Control Center before running one of these examples.

Server 0.7.0 verifies the activated SDK version, but the connected SA application
still needs operator-provided version evidence. After checking the running SA
version and SDK communication ownership, set these **in the PowerShell terminal
where you will run the example**:

```powershell
$env:Briosa__SpatialAnalyzer__Identity__ConnectedSpatialAnalyzer__OperatorAttestation__Version = '2026.1.0529.7'
$env:Briosa__SpatialAnalyzer__Identity__ConnectedSpatialAnalyzer__OperatorAttestation__Reference = 'local-sa-version-check'
```

The reference labels your actual verification; use a non-sensitive reference of
your own if you keep a verification record. These values apply to processes
launched from this terminal, not every application on the machine. Recheck them
if you change the running SA instance.

The client discovers the matching installed server; no server-path environment
variable is needed. Applications for other SA releases use that target's package
and their own configuration. See [installation selection](https://briosa.dev/docs/deployment/installation-selection)
for explicit per-application choices.

If startup reports an SDK version mismatch or missing identity evidence, follow
the server's [connection setup guide](https://github.com/spatialanalyzer/briosa/blob/main/targets/2026.1.0529.7/docs/operations/control-center.md).
Control Center's saved connection settings apply to servers it launches; they
are not automatically applied to servers launched by the language clients.

## Direct gRPC

For the raw gRPC example, use **Briosa Control Center** from the matching
installation. Configure **Connection setup** with the verified connected SA
version and evidence reference, then select **Start server**, **Start SDK**,
and **Connect**. Wait for **Ready for commands**.

Copy the endpoint shown by Control Center into `GrpcChannel.ForAddress(...)`
in [Program.cs](../point-inspection/grpc/Program.cs). Keep the endpoint local.

The example uses this existing connection and leaves it running. When finished,
use **Stop server** in Control Center; SA remains open.
