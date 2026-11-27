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
a test server that reports another SA target or contract, and disconnected
startup. The test server begins without points, so the programs must construct
them before they can read them.

The harness verifies output values and call order, checks that failed operations
are not replayed, checks client SDK cleanup, and checks that raw gRPC leaves the
external server alive. For each failing case it also checks the uncaught error
the program ends with, so a generic or wrapping exception or a different failure
does not pass; see [what the harness checks](#what-the-harness-checks-in-a-failing-case).
Test-only connection changes are saved with the evidence
under `artifacts/test-*/`. The tutorial sources contain no test switch or
synthetic measurement path. No real server distribution, SDK, or SA is launched.

The server/client repositories own the broader installation selection and
compatibility suites. These tutorials do not duplicate their bootstrap code.

On 2026-09-19, `eng/Test.ps1` passed all **47 scenarios** using the published
0.2.0 clients and verified 0.7.0 protocol. All C# and TypeScript builds passed.
This evidence covers the tutorial rewrite in issue #7; the earlier workbench's
report and bootstrap checks are retained in Git history rather than presented
as checks of these simpler programs.

## Server 0.10.1 and supported clients

These tutorials use the `0.5.0` .NET, Python, and JavaScript clients for
SA 2026.1.0529.7 and the Server `0.10.1` protocol. The direct gRPC example
checks the server's target, compatibility and readiness before any MP call. The
language clients select a compatible installation and verify the running server.

In the `wrong-target` case, the test server reports SA 2024.1.0508.5. In
`wrong-contract`, it reports an incompatible value in its discovery response.
The raw gRPC example rejects either response. For the language clients, the
installation manifest the harness writes beside the test server advertises a
compatible Server 0.10.0 installation for SA 2026.1.0529.7. Each client starts
that test server, compares the identity it reports with the manifest, and stops
before it starts the SDK or calls an MP. It reports
`server-installation-target-mismatch` or `server-installation-contract-mismatch`
(.NET), or `server-installation-identity-mismatch` (Python and TypeScript). No
server rejects anything in these cases.

The test server reports the `standard` admission profile with a capability list
that is a subset of a real server's: only the five operations the tutorials call,
all of which it implements. Each row copies that operation's reviewed
classification from Server 0.10.0. The policy fingerprint is synthetic
(`sha256:` followed by 64 `c` characters), not the real profile's fingerprint.
The clients read the profile and the list at startup.

### What the test server simulates

For each operation failure, the test server sends the gRPC status and typed
`OperationError` that Server 0.10.0 sends on the same path. The values are copied
from its [outcome mapper](https://github.com/spatialanalyzer/briosa/blob/v0.10.0/targets/2026.1.0529.7/src/Briosa.Server/Services/Outcomes/GrpcOperationOutcomeMapper.cs)
and the [outcome contract](https://github.com/spatialanalyzer/briosa/blob/v0.10.0/docs/architecture/execution-outcomes-and-recovery.md),
with each operation's reviewed replay safety and output fields.

The test server reports version 0.10.0 and a synthetic source revision. The
mismatch cases also alter the reported SA target or compatibility metadata.
Server 0.10.1 sends the same shapes on these paths: its
[outcome mapper](https://github.com/spatialanalyzer/briosa/blob/v0.10.1/targets/2026.1.0529.7/src/Briosa.Server/Services/Outcomes/GrpcOperationOutcomeMapper.cs)
adds a policy-denial detail only to a policy-denied failure, which no case
simulates. The test server leaves the new message fields on
`GetServerInfoResponse`, `OperationCapability` and `OperationError` unset. It
also omits the configured SA interaction mode, which decodes as `UNSPECIFIED`.

| Case | Server 0.10.0 path | What the test server sends |
| --- | --- | --- |
| `missing-point`, `collection-failure`, `second-write-failure` | SA ran the MP and returned a failure code | `FailedPrecondition`, `MpFailure`, `mp-command-failed`, `Completed`, recovery `None`, `DoNotReplay`; MP state `Failed` with code 3, each output `NotAttempted` |
| `unknown` (point read), `write-unknown` (point write) | The worker exited during the MP, so the reply was lost | `Unavailable`, `WorkerFailure`, `worker-exited-during-execution`, `StartedOutcomeUnknown`, `WorkerReplacement`; `MayReplay` for the safe read, `ReconcileBeforeReplay` for the unsafe write |
| `deadline` (raw gRPC) | The caller's deadline elapses | No reply within the example's 10-second deadline. As with Server 0.10.0, the caller gets `DeadlineExceeded` without typed detail |
| `missing-output`; `mp-result-failure` and `write-result-failure` (raw gRPC) | None | An `OK` result with an absent output, or with code 1 or 3 reported as `Succeeded`. Server 0.10.0 sends a typed `DataLoss`/`OutputRetrievalFailure` or `FailedPrecondition`/`MpFailure` error instead. These cases test the result checks in the clients and the raw example |
| `nonfinite`: implemented in the test server, not run by the harness | None | An `OK` point read whose `x_value` is NaN. Server 0.10.0 sends a typed `DataLoss`/`OutputRetrievalFailure` error instead |

Code 3 is the MP failure code SA returned in the server's released licensed probe
records; the server reports whichever code SA returns. Some behavior is not
modelled. After a simulated worker failure the test server keeps its SDK state,
whereas Server 0.10.0 also retires the worker generation until an explicit
recovery request; the tutorials stop at the failure, so no case reads that state.
The test server's other rejections (a call before readiness, an unexpected
request, a repeated point write) are test assertions without a typed error, and
no case expects them. There is no policy-denial case. The test server logs each
MP call before its readiness check, and in the `wrong-target`, `wrong-contract`
and `disconnected` cases the harness fails if a program attempted any MP
operation at all.

### What the harness checks in a failing case

Each failing program must end with an uncaught error. The harness reads the
report its runtime prints on stderr and compares its structure, not a substring
anywhere in the output. The outermost, uncaught error must be the expected one:
a program that wraps it in another exception fails, as does another error type,
message, gRPC status or printed field value.

For an operation failure (`missing-point`, `unknown`, `collection-failure`,
`second-write-failure`, `write-unknown`), these are the fields each tutorial
prints and the harness checks. Nothing a tutorial does not print is checked.

| Tutorial | What the harness compares | Checked | Not printed, so not checked |
| --- | --- | --- | --- |
| .NET client | The first line of the unhandled-exception report and each `--->` inner exception line | `BriosaOperationException` with the operation ID and diagnostic code in its message, and exactly one inner exception: the `RpcException` with the gRPC status code and detail | Kind, execution disposition, recovery guidance, replay guidance, replay safety, MP details, worker generation |
| Python client | Every exception in the traceback and the chain marker between them | The last exception is `BriosaOperationError` with the operation ID and diagnostic code, raised directly from an `AioRpcError` with the gRPC status code and details, and the traceback holds no other exception. The `AioRpcError`'s `debug_error_string` line, gRPC's own diagnostic, is skipped | Kind, execution disposition, recovery guidance, replay guidance, replay safety, MP details, worker generation |
| TypeScript client | The header line of Node's uncaught-error report and the error's printed properties, exactly, ignoring only whitespace | `BriosaOperationError` with the diagnostic code in its message, and exactly these properties in this order: operation ID, kind, diagnostic code, execution disposition, recovery guidance, replay guidance, replay safety and MP details. The MP details are the state, the exact MP result code, and each retrieval entry's field name, state and diagnostic code, in order; for a worker failure they are `null` | gRPC status code and detail, worker generation |
| Raw gRPC example | The first line of the unhandled-exception report and each `--->` inner exception line | Exactly one exception: the `RpcException` with the gRPC status code and detail | Every `OperationError` field: the example does not decode the error trailer |

For the other failing cases the harness compares the same structure with one
expected error: its type and message, which for the clients includes the
diagnostic code, and for TypeScript a single `diagnosticCode` property. The
.NET and raw gRPC errors must have no inner exception, and the Python error must
be the only exception in the traceback. In the raw gRPC `deadline` case the error
is an `RpcException` with `DeadlineExceeded` and the empty detail Grpc.Net.Client
reports when the caller's deadline ends the call.

The `unknown` read carries `MayReplay`, and every program still stops without
repeating it.

### Published packages and the protocol archive

Everything the tutorials consume is a published release file. The language
tutorials pin the 0.5.0 packages: NuGet `Briosa.2026.1.0529.7`, npm
`@spatialanalyzer/briosa-2026.1.0529.7` (installed as `briosa`), and PyPI
`briosa-2026-1-0529-7`. `protocol/protocol.lock.json` pins the Server 0.10.1
protocol archive. `eng/Import-Protocol.ps1` first refuses an `artifacts/` that is
a link. Then, unless `artifacts/` already holds the archive, it downloads it from
the GitHub release. Before building, the script checks the archive's SHA-256,
validates the manifest's version, source revision, SA target and compatibility
metadata, and verifies every file hash in the manifest. It
empties `artifacts/protocol-package` and `artifacts/protocol` before it extracts
and copies, and then checks that `artifacts/protocol` holds exactly the
manifest's proto files, so no file from an earlier import remains. It clears only
those two folders, and refuses if either one is a link.

The import cannot delete its own archive. It holds the archive open for the
whole import, shared only for reading, and hashes and extracts it through that
handle, so no path can delete, rename or change it meanwhile. Before it clears
either folder, it compares the real location Windows reports for the open
archive and for each folder, with every junction, symbolic link, short name and
substituted drive resolved, and refuses an archive inside either folder.
`eng/Test.ps1` repeats the import in a scratch copy holding planted stale files
and fails if one survives. It then puts the archive inside each folder, passes
it through a directory junction, and fails unless the import refuses it before
clearing either folder and the archive is still there.

| Input | Built from | Download |
| --- | --- | --- |
| Protocol archive 0.10.1 | briosa `d947d95ca6e7ff1ecece94cdcb218724f151a807` (tag `v0.10.1`) | [GitHub release](https://github.com/spatialanalyzer/briosa/releases/download/v0.10.1/briosa-protocol-0.10.1-sa-2026.1.0529.7.zip) |
| .NET client 0.5.0 | briosa-dotnet `bc72244454c436441cabbaad1ae4e8ca519a5a9e` | [NuGet](https://api.nuget.org/v3-flatcontainer/briosa.2026.1.0529.7/0.5.0/briosa.2026.1.0529.7.0.5.0.nupkg) |
| JavaScript client 0.5.0 | briosa-js `b94fce3f6e95b3e15f99f9d65f1710cc8c8d3119` | [npm](https://registry.npmjs.org/@spatialanalyzer/briosa-2026.1.0529.7/-/briosa-2026.1.0529.7-0.5.0.tgz) |
| Python client 0.5.0 | briosa-py `cae6e535ebfd89fd6c4ba280c741ec5f7fd04a0d` | [PyPI](https://files.pythonhosted.org/packages/07/97/fbe56e4590c2af58291c8732cd3c4aca07de278a08c3726b75dc0b0f975e/briosa_2026_1_0529_7-0.5.0-py3-none-any.whl) |

The SHA-256 values of those files:

```text
1a58ff916a3a32014c34cf3b399bf6a4f9b8f988b6d3d52e134b1debdb66b21a  briosa-protocol-0.10.1-sa-2026.1.0529.7.zip
adc0de5a274034d7bf393502095ed620a49e0e1a467b1263a86d192f47a38d29  briosa.2026.1.0529.7.0.5.0.nupkg
7d0583ed9b6ec489d3f93af74df877a01856138913952e7e0ca098a3344105cd  briosa-2026.1.0529.7-0.5.0.tgz
f2ecb2aaaf4d0b5de24910986f9d778453c982f5a82ae2031213b0d62edc6064  briosa_2026_1_0529_7-0.5.0-py3-none-any.whl
```

The archive's SHA-256 is the digest GitHub lists for that release asset and the
value in the release's `.zip.sha256` file. The
three packages match the registry files and SHA-256 values that
[briosa#381](https://github.com/spatialanalyzer/briosa/pull/381) pins in
`compatibility/retained-clients.json`.

On 2026-11-21 (UTC), `eng/Test.ps1` passed all **51 scenarios** with the
published packages and the 0.10.0 archive. The `partial-success` case returns
MP code 4 from the distance read. The language clients treat it as success, and
the raw gRPC example reports the code and continues. All three C#
builds and the TypeScript build completed without warnings or errors. This is
portable fake-server evidence; licensed execution has not been recorded for
these pins. After the review follow-up (the clean protocol import and its
stale-file check, the Server 0.10.0 failure shapes and the typed-detail checks),
an offline local run of `eng/Test.ps1` on 2026-11-21 (UTC) again passed all
51 scenarios, with no build warnings or errors. After the second review
follow-up (an import that cannot delete its own archive, the uncaught-error
checks, and the check that no MP is attempted in the refused cases), another
offline local run on 2026-11-21 (UTC) passed the import self-checks and all 51
scenarios, with no build warnings or errors.

On 2026-11-27 (UTC), after the protocol pin moved to the Server 0.10.1 release
archive, an offline local run of `eng/Test.ps1` passed the import self-checks
and all 51 scenarios. All three C# builds and the TypeScript build completed
without warnings or errors. The run used locally cached copies of the pinned
packages and a hash-verified copy of the release asset, not the download or the
registries. It is portable fake-server evidence; licensed execution has not been
recorded for these pins.

Earlier runs against source builds of the client pull requests and against the
frozen protocol checkpoint `0.10.0-dev.1`
([briosa#363](https://github.com/spatialanalyzer/briosa/pull/363)) are recorded
in [briosa-examples#16](https://github.com/spatialanalyzer/briosa-examples/pull/16).
Each of the 31 files the 0.10.0 release manifest lists has the same SHA-256 as in
that checkpoint, including the descriptor set
(`aa938e86fc62627d58994953398da3b7dc6a202f5f3c90e778bbddccda0261f8`). Only the
manifest's name, version and source revision differ.

The 0.10.1 archive lists the same 31 files. Four differ from 0.10.0:
`proto/briosa/discovery.proto`, `proto/briosa/operation_outcomes.proto`, the
descriptor set (now
`6f0f5a430d6bb5583982a41f16035b4cce4446dfcb498b36bf0069f3700b4a04`) and
`compatibility/contract.json`. Server 0.10.1 continues to support clients 0.5.0
and 0.5.1 for the matching SA target.
The proto changes only add messages, enums and fields: lifecycle bounds and the
configured SA interaction mode in `GetServerInfoResponse`, request options and a
policy-denial detail in `OperationCapability`, and a policy-denial detail in
`OperationError`. The 0.5.0 clients ignore them. Of the four programs, only the
raw gRPC example compiles the imported protocol, as does the test server; both
build against 0.10.1 without a source change. The language clients carry their
own generated code.

The [shared behavioral contract](https://github.com/spatialanalyzer/briosa/blob/v0.10.1/docs/architecture/client-library-behavioral-contract.md)
defines overload and lifecycle behavior. Tutorial calls remain sequential and
stop on failure; they do not automatically retry an uncertain write.

## Licensed acceptance

Live results are recorded separately from portable checks in the
[September 19 licensed validation record](evidence/licensed-2026-09-19.md).
That record covers Server 0.7.0 and the 0.2.0 clients. Nothing in `eng/Test.ps1`
runs against SpatialAnalyzer, and no licensed run is recorded for the current pins.

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
