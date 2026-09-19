# Briosa examples guide

Keep these tutorials small enough to read in one sitting. Shared protocol and
client semantics belong to [briosa](https://github.com/spatialanalyzer/briosa),
especially its client-library behavioral contract.

- Start from a GitHub Task and use an issue-number branch.
- Teach basic tasks against an already-running SpatialAnalyzer job.
- Have the introductory examples construct their own two demo points from an
  empty job, then read them. Keep these explicit construction calls simple.
- Prefer direct Briosa calls, ordinary variables and loops, and built-in cleanup.
- Do not add CLI option parsers, synthetic user modes, report frameworks, custom
  measurement interfaces, or copied server discovery/bootstrap implementations.
- Consume published, pinned packages and hash-verified protocol artifacts.
- Generate raw clients with standard protobuf/gRPC tooling. Do not edit generated files.
- Keep execution local, exact-target, and single-tenant. Never replay an MP automatically.
- Check raw protocol result presence and MP success before printing returned values.
- Keep test servers and harness configuration under tests/ and eng/, out of tutorial code.
- Run eng/Test.ps1 for changes affecting examples or their runtime behavior.
- Ordinary CI must not require SA, a license, hardware, vendor documentation, or proprietary binaries.
- Before controlling SA or running licensed integration, obtain explicit permission for the current task.
- Record portable evidence and licensed-SA observations separately; unexecuted validation is not passing.
- Do not commit real customer measurements, SA jobs, or raw operational logs.
- Keep future UI, broader write-back and additional-target work in their own coherent tasks.
