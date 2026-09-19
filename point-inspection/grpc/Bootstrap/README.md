# Direct gRPC bootstrap

These BCL-only helpers locate and validate a local distribution and own the
server process. They contain no language-client package dependency. Application
RPCs in `../Program.cs` use standard generated protobuf/gRPC types directly.

The Registry/receipt reader, SemVer ordering, and selector are an example-local
snapshot of the .NET implementation at commit
`ea91dfe` in `spatialanalyzer/briosa-dotnet`, adapted only to this namespace
and an example-specific identity. Their semantics belong to the
[server-owned contract](https://github.com/spatialanalyzer/briosa/blob/main/docs/architecture/installation-selection-and-compatibility.md).
Do not trim receipt, integrity, scope, or runtime identity checks to simplify a
demo. The small application flow is separate from this bootstrap plumbing.

`tests/Bootstrap` runs the same server-owned selection vectors as the three
clients and checks native Windows permission rules without changing any ACL.
The fixture is pinned to server source
`4303a3322074869b35a3f16f9e35484a7bd5c830` (Server 0.7.0) and is not generated here.

Automatic discovery launches nothing. Explicit paths never fall back. Selection
is revalidated before launch and stays fixed until disposal. Cleanup affects
only the process created by this example; SDK ownership remains guarded by
generation in the raw RPC flow. No SDK registration or SA application switching
takes place here.
