# Contributing and validation

The tutorials are small programs for an already-running SpatialAnalyzer job.
Keep the teaching code direct: Briosa calls, ordinary variables and loops,
and language-native resource cleanup. Test fixtures and engineering options
belong under `tests/` and `eng/`.

## Portable checks

From the repository root on Windows x64 with .NET SDK 10.0.401, Node.js 24,
and Python 3.12:

```powershell
./eng/Test.ps1
```

This verifies the published protocol archive, restores locked packages, and
builds the actual examples. The test harness makes temporary source copies
with only the server selection/endpoint changed to its isolated test server.
It then runs those programs through collection and point construction, successful
reads, different units, failed or incomplete calls, incompatible targets/contracts,
and disconnected startup. The test server begins without points, so the programs
must construct them before they can read them.

The harness verifies output values and call order, checks that failed operations
are not replayed, checks client SDK cleanup, and checks that raw gRPC leaves the
external server alive. Test-only connection changes are saved with the evidence
under `artifacts/test-*/`. The tutorial sources contain no test switch or
synthetic measurement path. No real server distribution, SDK, or SA is launched.

The server/client repositories own the broader installation selection and
compatibility suites. These tutorials do not duplicate their bootstrap code.

On 2026-09-19, `eng/Test.ps1` passed all **47 scenarios** using the published
0.2.0 clients and verified 0.7.0 protocol. All C# and TypeScript builds passed.
This evidence covers the tutorial rewrite in issue #7; the earlier workbench's
report and bootstrap checks are retained in Git history rather than presented
as checks of these simpler programs.

## Licensed acceptance

Live results are recorded separately from portable checks in the
[September 19 licensed validation record](evidence/licensed-2026-09-19.md).

With permission to use a licensed installation:

1. Follow [setup](setup.md) using an empty scratch SA job.
2. Run each language client individually and compare its coordinates and
   5-unit distance with SA. Confirm SA remains open after each run.
3. Start and connect Briosa in Control Center. Run the raw gRPC example and
   confirm both the server and SA remain open.
4. Confirm each example created its own collection with P1 and P2. The points
   remain in SA. Stop the Control Center server when finished.
5. Record the exact SA/server/client versions, observed output, and any gaps
   separately from the portable evidence. Do not commit operational logs or
   customer measurements.

The construction and read calls run sequentially without a transaction or rollback.
They do not retry calls. A failed or timed-out write may already have created a
collection or point; inspect SA and Briosa's error details before deciding what
to do next. Portable checks cover collection failure, failure of the second point
write, and an unknown outcome after a point write, asserting no replay or later reads.
