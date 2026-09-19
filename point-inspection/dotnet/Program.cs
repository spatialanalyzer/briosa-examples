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
