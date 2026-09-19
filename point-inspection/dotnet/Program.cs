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
