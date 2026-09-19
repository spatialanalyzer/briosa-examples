# Briosa examples

Create two points, read their coordinates, and measure their distance in a running
[SpatialAnalyzer](https://hexagon.com/products/spatialanalyzer) job with
[Briosa](https://github.com/spatialanalyzer/briosa).

Choose your language. Each example is a single short program that:

1. Connects to your open SA job.
2. Reads the active length unit.
3. Creates a collection and two points in the current working frame.
4. Prints their coordinates and asks SA for the distance between them.

| Use | Tutorial | Complete program |
| --- | --- | --- |
| .NET | [C# client](point-inspection/dotnet/README.md) | [Program.cs](point-inspection/dotnet/Program.cs) |
| JavaScript / TypeScript | [TypeScript client](point-inspection/typescript/README.md) | [inspection.ts](point-inspection/typescript/src/inspection.ts) |
| Python | [Python client](point-inspection/python/README.md) | [inspection.py](point-inspection/python/inspection.py) |
| Direct gRPC | [C# with generated gRPC](point-inspection/grpc/README.md) | [Program.cs](point-inspection/grpc/Program.cs) |

## Before you start

[Prepare SpatialAnalyzer](docs/setup.md), then follow one tutorial.
These examples use SA **2026.1.0529.7**, published client **0.2.0** packages,
and the Server **0.7.0** protocol. Install the matching Briosa server with the
[Briosa Installer](https://briosa.dev/docs/releases). The language clients find
a compatible installation automatically.

Start with an empty SA job. Each example creates P1 at (0, 0, 0) and P2 at
(3, 4, 0) in its own demo collection; no manual point setup is needed.

With SA using inches, the output is:

```text
Length unit: Inches
P1: (0.000, 0.000, 0.000)
P2: (3.000, 4.000, 0.000)
Distance: 5.000 Inches
```

The programs leave SA open with the created points. Values use SA's active units
and working frame. Run one example at a time and leave the job unchanged while
it runs. To repeat the same example, use a fresh empty job or change its collection
name to an unused name.

## Development

Build and test instructions are in [contributing](docs/validation.md).
Automated checks use a test server internally; the tutorial programs always
connect to Briosa. See the validation record for portable and licensed results.

Licensed under Apache-2.0. SpatialAnalyzer, its SDK, and their brands remain
Hexagon intellectual property and require a separate installation and license.
