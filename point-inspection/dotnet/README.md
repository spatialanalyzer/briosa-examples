# Create and read two points with C# client

[Prepare your running SA job](../../docs/setup.md) first.
Start with an empty SA job. This example creates two points, then prints
their coordinates and distance.

## The program

The complete [Program.cs](Program.cs) program is:

```csharp
using Briosa;

// Start with an empty SA job. These names will be created below.
var collection = new CollectionName { Name = "BriosaDotnetDemo" };
var first = new PointName { CollectionName = collection.Name, GroupName = "Points", TargetName = "P1" };
var second = new PointName { CollectionName = collection.Name, GroupName = "Points", TargetName = "P2" };

await using var briosa = new BriosaClient();
await briosa.StartAsync(new BriosaStartOptions { LaunchSpatialAnalyzer = false });

var units = await briosa.GetActiveUnitsAsync();
Console.WriteLine($"Length unit: {units.Length}");

await briosa.ConstructionOperations.ConstructCollectionAsync(collection);
await briosa.ConstructionOperations.ConstructPointInWorkingCoordinatesAsync(first, new Vector(0, 0, 0));
await briosa.ConstructionOperations.ConstructPointInWorkingCoordinatesAsync(second, new Vector(3, 4, 0));

foreach (var point in new[] { first, second })
{
    var coordinates = await briosa.GetPointCoordinateAsync(point);
    Console.WriteLine($"{point.TargetName}: ({coordinates.XValue:F3}, {coordinates.YValue:F3}, {coordinates.ZValue:F3})");
}

var distance = await briosa.GetPointToPointDistanceAsync(first, second);
Console.WriteLine($"Distance: {distance.Magnitude:F3} {units.Length}");
```

## How it works

`PointName` is a Briosa type identifying a point by collection, group, and name.
`StartAsync` finds and starts a compatible installed Briosa server. Setting
`LaunchSpatialAnalyzer = false` connects to the SA application you already opened.

`GetActiveUnitsAsync` tells us which units the values use.
`ConstructCollectionAsync` creates the demo collection, then two
`ConstructPointInWorkingCoordinatesAsync` calls create P1 and P2. The loop
reads each point's coordinates; `GetPointToPointDistanceAsync` asks SA to
calculate the distance between the two points.

`await using` cleans up the owned server and SDK, including when a call fails.
SA stays open. The client handles compatibility and operation-result checks.

## Run it

From the repository root, using .NET SDK 10.0.401:

```powershell
dotnet run --project point-inspection/dotnet
```

The distance is **5.000** in SA's current length unit. The program leaves SA
open with both points in its demo collection. To repeat the example, use a fresh
empty job or choose an unused collection name in the source.
If a call fails, the program stops and displays the error; it does not retry.
