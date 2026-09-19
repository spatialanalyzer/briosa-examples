# Read two points with Direct C# gRPC

[Prepare your running SA job](../../docs/setup.md) first and connect it in Control Center.
This example prints two point coordinates and their distance.

## The program

Open [Program.cs](Program.cs) and change the collection, group, and point
names to match your job. Also copy your Control Center endpoint into the channel address.

```csharp
using Briosa;
using Grpc.Net.Client;

// Start and connect this server in Control Center before running the example.
using var channel = GrpcChannel.ForAddress("http://127.0.0.1:50051");
var discovery = new DiscoveryService.DiscoveryServiceClient(channel);
var utility = new UtilityOperations.UtilityOperationsClient(channel);
var analysis = new AnalysisOperations.AnalysisOperationsClient(channel);

var info = await discovery.GetServerInfoAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
if (info.Version?.SpatialAnalyzerTarget != "2026.1.0529.7" || info.Compatibility?.Major != 1 || !info.ReadyForMp)
    throw new InvalidOperationException("Connect a compatible SA 2026 server in Control Center first.");

// These are generated protobuf types; this project uses no Briosa client package.
var first = new PointName { CollectionName = "BriosaDemo", GroupName = "Points", TargetName = "P1" };
var second = new PointName { CollectionName = "BriosaDemo", GroupName = "Points", TargetName = "P2" };

var units = await utility.GetActiveUnitsAsync(new(), deadline: DateTime.UtcNow.AddSeconds(10));
RequireResult(units.Execution, units.HasLength);
Console.WriteLine($"Length unit: {units.Length}");

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
static void RequireResult(MpExecutionDetails? execution, bool hasValues)
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
Then the program reads units, loops over two points, and asks SA for their
distance, just like the language-client versions.

Raw responses include MP status and optional output fields. The small
`RequireResult` function prevents a missing value from appearing as zero or a
failed MP from appearing successful. The language clients perform these checks
for you. RPC deadlines bound the wait; failed calls stop the program and are
not retried.

Disposing the channel closes this program's connection. The Control Center
server and its SDK session stay running.

## Run it

From the repository root, using .NET SDK 10.0.401:

```powershell
./eng/Import-Protocol.ps1
dotnet run --project point-inspection/grpc
```

For the setup guide's two points, the distance is **5.000 millimeters**.
With your own points, the output reflects their current coordinates and SA units.
If a call fails, the program stops and displays the error. Fix the cause before
running it again.
