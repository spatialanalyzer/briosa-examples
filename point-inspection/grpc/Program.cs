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
