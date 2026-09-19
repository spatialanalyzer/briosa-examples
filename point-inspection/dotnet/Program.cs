using Briosa;
using Inspection;

try
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var options = Workbench.Options(args);
    if (options.ContainsKey("--endpoint")) throw new ArgumentException("The .NET client owns its local server; --endpoint is for raw gRPC only.");
    var selection = new BriosaServerSelection
    {
        ExecutablePath = options.GetValueOrDefault("--server-path"),
        InstallationId = options.GetValueOrDefault("--installation-id"),
        Version = options.GetValueOrDefault("--server-version"),
        SearchRoots = options.TryGetValue("--search-root", out var root) ? [root] : [],
        SpatialAnalyzerExecutablePath = options.GetValueOrDefault("--sa-path"),
        AllowPrerelease = options.ContainsKey("--allow-prerelease"),
    };
    if (options.ContainsKey("--discover"))
    {
        var discovered = BriosaInstallations.Discover(selection);
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(discovered, Workbench.Json));
        return discovered.Selected is null ? 1 : 0;
    }
    var fixture = options.GetValueOrDefault("--fixture", "point-inspection/fixture");
    var nominals = Workbench.LoadPoints(Path.Combine(fixture, "nominals.csv"));
    var scenario = Workbench.LoadScenario(fixture, nominals);
    var live = options.ContainsKey("--live");
    var output = options.GetValueOrDefault("--output", "artifacts/dotnet-report");
    if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be a new directory.");
    Report report;
    await using (IMeasurements measurements = live ? new ClientMeasurements(scenario, selection, cancellation.Token) :
        new SyntheticMeasurements(scenario, Workbench.LoadPoints(Path.Combine(fixture, "measured.csv"))))
    {
        if (measurements is ClientMeasurements client) await client.Start();
        report = await Workbench.Inspect(measurements, scenario, nominals, live ? "live" : "synthetic");
    }
    return Workbench.Save(report, output);
}
catch (BriosaOperationException error)
{
    Console.Error.WriteLine($"Operation failed: {error.Kind}; execution={error.ExecutionDisposition}; recovery={error.RecoveryGuidance}; replay={error.ReplayGuidance}; safety={error.ReplaySafety}. No automatic retry.");
    return 1;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Inspection stopped ({error.GetType().Name}). {error.Message}");
    Console.Error.WriteLine("No report produced. If a call was in flight, completion may be unknown; do not automatically retry.");
    return 1;
}

sealed class ClientMeasurements(Scenario scenario, BriosaServerSelection selection, CancellationToken cancellation) : IMeasurements
{
    private readonly BriosaClient _client = new(new BriosaClientOptions { CommandTimeout = TimeSpan.FromSeconds(10) });
    public async Task Start()
    {
        // Attach to the prepared, already-running SA job. Do not launch a blank job.
        await _client.StartAsync(new BriosaStartOptions { LaunchSpatialAnalyzer = false, ServerSelection = selection }, cancellation);
        var snapshot = await _client.GetServerSnapshotAsync(cancellation);
        if (!snapshot.ReadyForMp || Workbench.RequiredMethods.Any(m => !snapshot.Supports(m)))
            throw new InvalidOperationException("Required operation unavailable or SA not ready.");
    }
    private PointName Name(string name) => new() { CollectionName = scenario.Collection, GroupName = scenario.Group, TargetName = name };
    public async Task<Context> ReadContext()
    {
        var units = await _client.GetActiveUnitsAsync(cancellation);
        var frame = await _client.GetWorkingFramePropertiesAsync(cancellation);
        return new(units.Length, frame.CollectionName, frame.FrameName);
    }
    public async Task<(double X, double Y, double Z)> ReadPoint(string name)
    {
        var p = await _client.GetPointCoordinateAsync(Name(name), cancellation);
        return (p.XValue, p.YValue, p.ZValue);
    }
    public async Task<double> ReadDistance(string first, string second) =>
        (await _client.GetPointToPointDistanceAsync(Name(first), Name(second), cancellation)).Magnitude;
    // Client disposal stops the owned server/SDK and leaves SA open.
    public ValueTask DisposeAsync() => _client.DisposeAsync();
}
