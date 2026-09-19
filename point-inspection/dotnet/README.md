# Read two points with C# client

[Prepare your running SA job](../../docs/setup.md) first.
This example prints two point coordinates and their distance.

## The program

Open [Program.cs](Program.cs) and change the collection, group, and point
names to match your job.

```csharp
using Briosa;

// Use the collection, group, and point names from your open SA job.
var first = new PointName { CollectionName = "BriosaDemo", GroupName = "Points", TargetName = "P1" };
var second = new PointName { CollectionName = "BriosaDemo", GroupName = "Points", TargetName = "P2" };

await using var briosa = new BriosaClient();
await briosa.StartAsync(new BriosaStartOptions { LaunchSpatialAnalyzer = false });

var units = await briosa.GetActiveUnitsAsync();
Console.WriteLine($"Length unit: {units.Length}");

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

`GetActiveUnitsAsync` tells us which units the returned values use. The loop
reads each point's coordinates; `GetPointToPointDistanceAsync` asks SA to
calculate the distance between the two points.

`await using` cleans up the owned server and SDK, including when a call fails.
SA stays open. The client handles compatibility and operation-result checks.

## Run it

From the repository root, using .NET SDK 10.0.401:

```powershell
dotnet run --project point-inspection/dotnet
```

For the setup guide's two points, the distance is **5.000 millimeters**.
With your own points, the output reflects their current coordinates and SA units.
If a call fails, the program stops and displays the error. Fix the cause before
running it again.
