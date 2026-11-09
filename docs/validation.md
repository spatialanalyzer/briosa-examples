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

## Contract major 3 release

These tutorials use the `0.5.0` .NET, Python, and JavaScript clients for
SA 2026.1.0529.7 and the Server `0.10.0` protocol. They require behavioral
compatibility major 3. The direct gRPC example verifies that contract before MP
work; the language clients perform their own server selection and compatibility
checks. The `wrong-contract` scenario advertises major 2 and verifies rejection.
The test server also reports the major-3 admission profile and each operation's
reviewed classification, copied from the real server's `standard` profile. The
clients read both at startup.

### Candidate phase

Until Server 0.10.0 and the 0.5.0 clients are published, the pins point at
unpublished inputs: the frozen protocol checkpoint `0.10.0-dev.1` and the client
candidates of [briosa#363](https://github.com/spatialanalyzer/briosa/pull/363).
None of them is published, and no binary is committed. Build them first:

```powershell
./eng/Build-Candidates.ps1
./eng/Test.ps1
```

The script clones each pinned commit and builds it into `artifacts/`, the way
the client CI rebuilds the checkpoint and briosa's retained-client gate packs a
source candidate. It also needs Git, buf 1.72.0, and the .NET SDK that
briosa-dotnet pins (10.0.302); pass `-Dotnet` when the default `dotnet` lacks
it. `nuget.config` maps only `Briosa.2026.1.0529.7` to the local feed and keeps
that package out of the user-wide NuGet cache. The TypeScript tutorial installs
the npm tarball by path, and the Python requirements add the wheel folder with
`--find-links`.

| Input | Repository | Commit |
| --- | --- | --- |
| Protocol checkpoint `0.10.0-dev.1` | briosa | `2960705eb3152646513dca2b5d2043cd7cfffb75` |
| .NET client 0.5.0 | briosa-dotnet ([#46](https://github.com/spatialanalyzer/briosa-dotnet/pull/46)) | `3823369030dc0a286d698a4795b9442b4c7f9bf1` |
| JavaScript client 0.5.0 | briosa-js ([#46](https://github.com/spatialanalyzer/briosa-js/pull/46)) | `feda30f3bd0bdae8cf9b02ae307146de259e2167` |
| Python client 0.5.0 | briosa-py ([#46](https://github.com/spatialanalyzer/briosa-py/pull/46)) | `6a04ff934d554be0767bea997f9517676674b8a4` |

The 2026-11-09 run used these files:

```text
0bf2b7f216a8d1040bd416b4566cf6c53a13f398ec81657dc91b674e0f45cae3  briosa-protocol-0.10.0-dev.1-sa-2026.1.0529.7.zip
6a24be5b3582102f3365f33daa1a3d487f7bed8f0f5d8012f8b394cfb817e9fe  Briosa.2026.1.0529.7.0.5.0.nupkg
aa3909ec164f5276157c86eeef422bf5dfd34018f6bd7e01c159103e79ca5cf7  spatialanalyzer-briosa-2026.1.0529.7-0.5.0.tgz
e63f83524a2d6968af032075587cebdbdcde90110c3958395c230bc6403f2123  briosa_2026_1_0529_7-0.5.0-py3-none-any.whl
```

The archive matches the checkpoint recorded in briosa#363. The archive, the
tarball, and the wheel rebuild byte for byte. NuGet packing writes timestamps
and a random part name, so a rebuilt .NET candidate has a new hash: re-lock the
.NET tutorial once with
`dotnet restore point-inspection/dotnet/PointInspection.csproj --force-evaluate`.

On 2026-11-09, `eng/Test.ps1` passed all **47 scenarios** with these candidate
pins and the checkpoint archive, verified against the checkpoint SHA-256. All
three C# builds and the TypeScript build completed without warnings or errors.
This is portable fake-server evidence for unpublished candidates. The published
0.5.0 packages and the 0.10.0 release archive still need their own run, and
licensed execution has not been recorded for these pins.

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
