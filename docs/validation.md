# Validation and live acceptance

Portable validation uses an explicitly named synthetic test server. It has no
COM, SpatialAnalyzer, SDK, hardware, or license dependency. Its executable is
named `Briosa.Server.exe` so the published clients and raw bootstrap can launch
it through explicit per-start selection. The harness creates disposable portable
manifests around this test double. Never register or install it as a product.

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

The raw bootstrap passes the same 24 server-owned selection fixtures as the
clients. Process scenarios also check inert discovery, invalid explicit choices
without environment fallback, a newer compatible server, the exact legacy
exception, contract rejection, selected-manifest/runtime mismatch before SDK
startup, and preservation of external server ownership.

On 2026-09-19, the full portable suite passed **73 scenarios** using the public
SA 2026 client **0.2.0** packages from NuGet, npm, and PyPI and the verified
Server **0.7.0** protocol artifact. The bootstrap also passed all **24** shared
selection vectors, native permission-descriptor checks, and missing explicit-path
isolation. Compatible newer server identities in this suite are simulations in
the synthetic test double, not claims that a future server release was tested.
The [server compatibility matrix](https://github.com/spatialanalyzer/briosa/blob/main/compatibility/matrix.json)
separately records actual published client/server combinations.

## Licensed acceptance — not executed

This example currently has no licensed-SA validation record. Before an agent
controls SA, obtain explicit permission for the current task under the
[Briosa repository guide](https://github.com/spatialanalyzer/briosa/blob/main/AGENTS.md).

Use the published package versions and selected compatible server recorded by
the release acceptance evidence:

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
