# Briosa examples guide

Keep these applications small enough to teach from. Shared protocol and client
semantics belong to [briosa](https://github.com/spatialanalyzer/briosa), especially
its client-library behavioral contract; examples must not redefine them.

- Start from a GitHub Task and use an issue-number branch.
- Consume published, pinned packages and hash-verified protocol artifacts.
- Generate raw clients with standard protobuf/gRPC tooling. Do not edit generated files.
- Synthetic mode must never start SA, a real SDK worker, or a product server.
- Live mode must be explicit, exact-target, local, and single-tenant.
- Preserve presence, execution ambiguity and replay guidance. Never replay an MP automatically.
- Never publish partial reports as completed inspections or overwrite prior reports.
- Keep the synthetic fixture, report contract and expected output equivalent across languages.
- Run `eng/Test.ps1` for changes affecting examples or their runtime behavior.
- Ordinary CI must not require SA, a license, hardware, vendor documentation, or proprietary binaries.
- Before controlling SA or running licensed integration, obtain explicit permission for the current task.
- Record portable evidence and licensed-SA observations separately; unexecuted validation is not passing.
- Do not commit real customer measurements, SA jobs, or raw operational logs.
- Keep future UI, write-back and additional-target work in their own coherent tasks.
