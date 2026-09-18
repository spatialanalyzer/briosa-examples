# Validation and live acceptance

Portable validation uses an explicitly named synthetic test server. It has no
COM, SpatialAnalyzer, SDK, hardware, or license dependency. Its executable is
named `Briosa.Server.exe` only so the published clients can launch it through
their documented `BRIOSA_SERVER_PATH` override. Never install this test double
as a product or leave that override set after a test.

The harness starts the test double in child-process environments. It exercises
the actual generated gRPC calls and published language clients, rather than
substituting private client internals. It verifies matching reports and failure
behavior. It does not establish licensed-SA compatibility or validate COM.

Run `./eng/Test.ps1` from the repository root on Windows. The script imports a
hash-verified protocol artifact, restores locked packages, builds the examples,
and runs the matrix. Test evidence goes to a new `artifacts/test-*` folder.

Covered scenarios include synthetic reports, transport-backed reports, server
identity mismatch, unavailable capability, failed readiness, incorrect units,
frame changes, missing points, MP failure, unknown completion, missing result
fields, nonfinite coordinates, deadlines, and invalid fixture input. Failed
calls are not automatically replayed. Failed inspections do not publish reports.

## Licensed acceptance — not executed

This example currently has no licensed-SA validation record. Before an agent
controls SA, obtain explicit permission for the current task under the
[Briosa repository guide](https://github.com/spatialanalyzer/briosa/blob/main/AGENTS.md).

Use the documented Server 0.6.1 / client 0.1.1 pairing:

1. Record the exact installed SA, SDK, server, protocol and client versions.
2. Prepare the [synthetic job](../point-inspection/fixture/README.md).
3. Verify there are no competing Briosa or experimental SDK clients.
4. Run each example separately with `--live` and a fresh output directory.
5. Compare CSV with `expected.csv`: P3/P8 fail; the other eight checks pass.
6. Verify the units and working-frame information matches the prepared job.
7. Verify application shutdown leaves SA and the scratch job open.
8. Remove one scratch point and repeat: expect exit 1 and no report.
9. Change the working frame or length unit and repeat: expect rejection before
   point inspection. Restore the scratch job between cases.
10. Record observations and limitations separately for each exact SA target.

Do not test destructive recovery, kill an SA process, or attach multiple SDK
clients as part of this acceptance procedure. Portable tests cover ambiguous
completion. A live timeout requires operator reconciliation before another run.

## Follow-up scope

The initial example targets SA 2026.1.0529.7. The published SA 2024 client and
server products require their own example package pins and licensed validation;
changing a version string does not qualify that target. A local TypeScript web
interface and fixture creation/write-back remain separate follow-up milestones.
