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

This verifies the hash-pinned protocol archive, restores locked packages, and
builds the actual examples. The test harness makes temporary source copies
with only the server selection/endpoint changed to its isolated test server.
It then runs those programs through collection and point construction, successful
reads, different units, a partial success (MP code 4), failed or incomplete calls,
incompatible targets/contracts, and disconnected startup. The test server begins
without points, so the programs must construct them before they can read them.

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

## Contract major 3 release

These tutorials use the `0.5.0` .NET, Python, and JavaScript clients for
SA 2026.1.0529.7 and the Server `0.10.0` protocol. They require behavioral
compatibility major 3. The direct gRPC example verifies that contract before MP
work; the language clients perform their own server selection and compatibility
checks. The `wrong-contract` scenario advertises major 2 and verifies rejection.
The test server also reports the major-3 admission profile and each operation's
reviewed classification, copied from the real server's `standard` profile. The
clients read both at startup.

### Published clients and the protocol checkpoint

The language tutorials pin the published 0.5.0 packages: NuGet
`Briosa.2026.1.0529.7`, npm `@spatialanalyzer/briosa-2026.1.0529.7` (installed
as `briosa`), and PyPI `briosa-2026-1-0529-7`. Server 0.10.0 is not released
yet, so the protocol pin is still the frozen checkpoint `0.10.0-dev.1` of
[briosa#363](https://github.com/spatialanalyzer/briosa/pull/363), which has no
download URL. Build it first:

```powershell
./eng/Build-ProtocolCheckpoint.ps1
./eng/Test.ps1
```

The script clones briosa at the pinned source commit and builds the archive
into `artifacts/`, the way the client CI rebuilds the checkpoint. It needs Git
and buf 1.72.0. `eng/Import-Protocol.ps1` then verifies the archive against the
pinned SHA-256.

| Input | Built from | Download |
| --- | --- | --- |
| Protocol checkpoint `0.10.0-dev.1` | briosa `2960705eb3152646513dca2b5d2043cd7cfffb75` | None until the release |
| .NET client 0.5.0 | briosa-dotnet `bc72244454c436441cabbaad1ae4e8ca519a5a9e` | [NuGet](https://api.nuget.org/v3-flatcontainer/briosa.2026.1.0529.7/0.5.0/briosa.2026.1.0529.7.0.5.0.nupkg) |
| JavaScript client 0.5.0 | briosa-js `b94fce3f6e95b3e15f99f9d65f1710cc8c8d3119` | [npm](https://registry.npmjs.org/@spatialanalyzer/briosa-2026.1.0529.7/-/briosa-2026.1.0529.7-0.5.0.tgz) |
| Python client 0.5.0 | briosa-py `cae6e535ebfd89fd6c4ba280c741ec5f7fd04a0d` | [PyPI](https://files.pythonhosted.org/packages/07/97/fbe56e4590c2af58291c8732cd3c4aca07de278a08c3726b75dc0b0f975e/briosa_2026_1_0529_7-0.5.0-py3-none-any.whl) |

The 2026-11-20 run used these files:

```text
0bf2b7f216a8d1040bd416b4566cf6c53a13f398ec81657dc91b674e0f45cae3  briosa-protocol-0.10.0-dev.1-sa-2026.1.0529.7.zip
adc0de5a274034d7bf393502095ed620a49e0e1a467b1263a86d192f47a38d29  briosa.2026.1.0529.7.0.5.0.nupkg
7d0583ed9b6ec489d3f93af74df877a01856138913952e7e0ca098a3344105cd  briosa-2026.1.0529.7-0.5.0.tgz
f2ecb2aaaf4d0b5de24910986f9d778453c982f5a82ae2031213b0d62edc6064  briosa_2026_1_0529_7-0.5.0-py3-none-any.whl
```

The archive matches the checkpoint recorded in briosa#363. The three packages
match the registry files and SHA-256 values that
[briosa#381](https://github.com/spatialanalyzer/briosa/pull/381) pins in
`compatibility/retained-clients.json`.

On 2026-11-20, `eng/Test.ps1` passed all **51 scenarios** with the published
packages and the checkpoint archive. The `partial-success` case, new with
contract major 3, answers the distance read with MP code 4: the clients return
it as a success, and the raw gRPC example reports it and continues. All three C#
builds and the TypeScript build completed without warnings or errors. This is
portable fake-server evidence. The 0.10.0 release archive still needs its own
run, and licensed execution has not been recorded for these pins. Earlier runs
against source builds of the client pull requests are recorded in
[briosa-examples#16](https://github.com/spatialanalyzer/briosa-examples/pull/16).

The [shared behavioral contract](https://github.com/spatialanalyzer/briosa/blob/v0.10.0/docs/architecture/client-library-behavioral-contract.md)
defines overload and lifecycle behavior. Tutorial calls remain sequential and
stop on failure; they do not automatically retry an uncertain write.

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
