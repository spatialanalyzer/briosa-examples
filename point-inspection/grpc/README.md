# Create and read two points with direct C# gRPC

[Prepare your running SA job](../../docs/setup.md) first and connect it in Control Center.
Start with an empty SA job. This example creates two points, then prints
their coordinates and distance.

## The program

The complete [Program.cs](Program.cs) program is:

```csharp
using Briosa;
using Grpc.Net.Client;

// Start and connect this server in Control Center before running the example.
using var channel = GrpcChannel.ForAddress("http://127.0.0.1:50051");
var discovery = new DiscoveryService.DiscoveryServiceClient(channel);
var utility = new UtilityOperations.UtilityOperationsClient(channel);
var analysis = new AnalysisOperations.AnalysisOperationsClient(channel);
var construction = new ConstructionOperations.ConstructionOperationsClient(channel);

var info = await discovery.GetServerInfoAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
if (info.Version?.SpatialAnalyzerTarget != "2026.1.0529.7" || info.Compatibility?.Major != 1 || !info.ReadyForMp)
    throw new InvalidOperationException("Connect a compatible SA 2026 server in Control Center first.");

// These are generated protobuf types; this project uses no Briosa client package.
var collection = new CollectionName { Name = "BriosaGrpcDemo" };
var first = new PointName { CollectionName = collection.Name, GroupName = "Points", TargetName = "P1" };
var second = new PointName { CollectionName = collection.Name, GroupName = "Points", TargetName = "P2" };

var units = await utility.GetActiveUnitsAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
RequireResult(units.Execution, units.HasLength);
Console.WriteLine($"Length unit: {units.Length}");

var createdCollection = await construction.ConstructCollectionAsync(
    new() { CollectionName = collection, FolderPath = "", MakeDefaultCollection = false }, deadline: DateTime.UtcNow.AddSeconds(10));
RequireResult(createdCollection.Execution);
var createdFirst = await construction.ConstructPointInWorkingCoordinatesAsync(
    new() { PointName = first, WorkingCoordinates = new() { X = 0, Y = 0, Z = 0 } }, deadline: DateTime.UtcNow.AddSeconds(10));
RequireResult(createdFirst.Execution);
var createdSecond = await construction.ConstructPointInWorkingCoordinatesAsync(
    new() { PointName = second, WorkingCoordinates = new() { X = 3, Y = 4, Z = 0 } }, deadline: DateTime.UtcNow.AddSeconds(10));
RequireResult(createdSecond.Execution);

foreach (var point in new[] { first, second })
{
    var coordinates = await analysis.GetPointCoordinateAsync(
        new() { PointName = point }, deadline: DateTime.UtcNow.AddSeconds(10));
    RequireResult(coordinates.Execution, coordinates.HasXValue && coordinates.HasYValue && coordinates.HasZValue);
    Console.WriteLine($"{point.TargetName}: ({coordinates.XValue:F3}, {coordinates.YValue:F3}, {coordinates.ZValue:F3})");
}

var distance = await analysis.GetPointToPointDistanceAsync(
    new() { FirstPoint = first, SecondPoint = second }, deadline: DateTime.UtcNow.AddSeconds(10));
RequireResult(distance.Execution, distance.HasMagnitude);
Console.WriteLine($"Distance: {distance.Magnitude:F3} {units.Length}");

// Raw gRPC returns MP status and optional fields. Language clients check these for you.
static void RequireResult(MpExecutionDetails? execution, bool hasValues = true)
{
    if (execution?.State != MpExecutionState.Succeeded || !execution.HasMpResultCode ||
        execution.MpResultCode != 2 || !hasValues ||
        execution.OutputRetrievals.Any(output => output.State != OutputRetrievalState.Retrieved))
        throw new InvalidOperationException("SA did not return a complete successful result.");
}
```

## How it works

`GrpcChannel` connects to the endpoint shown by Control Center. The generated
service clients expose the protobuf RPCs directly. This project uses standard
gRPC packages and the verified published protocol; it has no Briosa language
client dependency.

The first call checks the server target, compatibility contract, and readiness.
Then the program reads units, creates a collection and two points, and reads
their coordinates and distance, just like the language-client versions.
The construction requests set each field explicitly, including zero coordinates.

Raw responses include MP status and optional output fields. The small
`RequireResult` function prevents a missing value from appearing as zero or a
failed MP from appearing successful. The language clients perform these checks
for you. RPC deadlines bound the wait; failed calls stop the program and are
not retried.

Disposing the channel closes this program's connection. The Control Center
server and its SDK session stay running.

## Run it

Copy the endpoint shown in Control Center into `GrpcChannel.ForAddress(...)`
if it differs from the example. From the repository root, using .NET SDK 10.0.401:

```powershell
./eng/Import-Protocol.ps1
dotnet run --project point-inspection/grpc
```

The distance is **5.000** in SA's current length unit. The program leaves SA
open with both points in its demo collection. To repeat the example, use a fresh
empty job or choose an unused collection name in the source.
If a call fails, the program stops and displays the error; it does not retry.
