using Briosa;
using Grpc.Core;
using Grpc.Net.Client;
using Inspection;
using Inspection.Bootstrap;

try
{
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var options = Workbench.Options(args);
    var selection = new BriosaServerSelection
    {
        ExecutablePath = options.GetValueOrDefault("--server-path"),
        InstallationId = options.GetValueOrDefault("--installation-id"),
        Version = options.GetValueOrDefault("--server-version"),
        SearchRoots = options.TryGetValue("--search-root", out var root) ? [root] : [],
        SpatialAnalyzerExecutablePath = options.GetValueOrDefault("--sa-path"),
        AllowPrerelease = options.ContainsKey("--allow-prerelease"),
    };
    selection.Validate();
    if (options.ContainsKey("--endpoint") && (options.Keys.Any(k => k is "--server-path" or
        "--installation-id" or "--server-version" or "--search-root" or "--sa-path" or "--allow-prerelease" or "--discover")))
        throw new ArgumentException("An external endpoint cannot be combined with local installation selection.");
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
    var output = options.GetValueOrDefault("--output", "artifacts/grpc-report");
    if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be a new directory.");
    Report report;
    await using var ownedServer = live && !options.ContainsKey("--endpoint") ? LocalServer.Start(selection) : null;
    await using (IMeasurements measurements = live ?
        new GrpcMeasurements(scenario, ownedServer?.Endpoint.AbsoluteUri ?? options["--endpoint"], cancellation.Token, ownedServer) :
        new SyntheticMeasurements(scenario, Workbench.LoadPoints(Path.Combine(fixture, "measured.csv"))))
    {
        if (measurements is GrpcMeasurements grpc) await grpc.Start();
        report = await Workbench.Inspect(measurements, scenario, nominals, live ? "live" : "synthetic");
    }
    return Workbench.Save(report, output);
}
catch (RpcException error)
{
    // Decode structured detail; never infer replay policy from status text.
    var trailer = error.Trailers.FirstOrDefault(t => t.Key == "briosa-operation-error-bin");
    try
    {
        if (trailer is not null)
        {
            var detail = OperationError.Parser.ParseFrom(trailer.ValueBytes);
            Console.Error.WriteLine($"RPC={error.StatusCode}; kind={detail.Kind}; execution={detail.ExecutionDisposition}; recovery={detail.RecoveryGuidance}; replay={detail.ReplayGuidance}; safety={detail.ReplaySafety}");
        }
        else Console.Error.WriteLine($"RPC={error.StatusCode}; no execution detail; completion may be unknown.");
    }
    catch (Google.Protobuf.InvalidProtocolBufferException)
    {
        Console.Error.WriteLine($"RPC={error.StatusCode}; invalid execution detail; completion may be unknown.");
    }
    Console.Error.WriteLine("No report produced. No automatic retry.");
    return 1;
}
catch (Exception error)
{
    Console.Error.WriteLine($"Inspection stopped ({error.GetType().Name}). {error.Message}");
    Console.Error.WriteLine("No report produced. If a call was in flight, completion may be unknown; do not automatically retry.");
    return 1;
}

sealed class GrpcMeasurements : IMeasurements
{
    private readonly Scenario _scenario;
    private readonly CancellationToken _cancellation;
    private readonly LocalServer? _ownedServer;
    private readonly GrpcChannel _channel;
    private readonly DiscoveryService.DiscoveryServiceClient _discovery;
    private readonly SpatialAnalyzerSdkLifecycle.SpatialAnalyzerSdkLifecycleClient _sdk;
    private readonly UtilityOperations.UtilityOperationsClient _utility;
    private readonly AnalysisOperations.AnalysisOperationsClient _analysis;
    private int? _ownedGeneration;
    private static DateTime Deadline => DateTime.UtcNow.AddSeconds(10);

    public GrpcMeasurements(Scenario scenario, string endpoint, CancellationToken cancellation, LocalServer? ownedServer)
    {
        var uri = new Uri(endpoint);
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.AbsolutePath != "/" || uri.Query != "" || uri.UserInfo != "")
            throw new ArgumentException("Use a local http://127.0.0.1:port endpoint.");
        _scenario = scenario;
        _cancellation = cancellation;
        _ownedServer = ownedServer;
        _channel = GrpcChannel.ForAddress(uri);
        _discovery = new(_channel); _sdk = new(_channel); _utility = new(_channel); _analysis = new(_channel);
    }

    public async Task Start()
    {
        var info = await WaitForDiscovery();
        ValidateServer(info);
        var caps = await _discovery.ListCapabilitiesAsync(new(), deadline: Deadline, cancellationToken: _cancellation);
        if (caps.SpatialAnalyzerTarget != _scenario.SaTarget || caps.ProtocolPackage != "briosa" ||
            Workbench.RequiredMethods.Any(m => !caps.Operations.Any(c => c.FullyQualifiedMethod == m)))
            throw new InvalidOperationException("Required operation unavailable.");
        var state = (await _sdk.GetSpatialAnalyzerSdkStateAsync(new(), deadline: Deadline, cancellationToken: _cancellation)).State;
        if (state is null || state.SdkState != SpatialAnalyzerSdkState.Stopped)
            throw new InvalidOperationException("Use a dedicated server with a stopped SDK; another session may own this one.");
        state = (await _sdk.StartSpatialAnalyzerSdkAsync(new(), deadline: Deadline, cancellationToken: _cancellation)).State;
        if (state is null || !state.HasSdkGeneration || state.SdkGeneration <= 0)
            throw new InvalidDataException("SDK generation missing. Inspect the server before further work.");
        _ownedGeneration = state.SdkGeneration;
        state = (await _sdk.ConnectToSpatialAnalyzerAsync(new() { ExpectedSdkGeneration = _ownedGeneration.Value }, deadline: Deadline, cancellationToken: _cancellation)).State;
        info = await _discovery.GetServerInfoAsync(new(), deadline: Deadline, cancellationToken: _cancellation);
        ValidateServer(info);
        if (state is null || !state.ReadyForMp || state.SdkGeneration != _ownedGeneration || !info.ReadyForMp ||
            info.ActivatedSdkIdentity?.MatchState != RuntimeIdentityMatchState.ExactMatch ||
            info.ConnectedSpatialAnalyzerIdentity?.MatchState != RuntimeIdentityMatchState.ExactMatch)
            throw new InvalidOperationException("Exact identities and execution readiness are required.");
    }

    private async Task<GetServerInfoResponse> WaitForDiscovery()
    {
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(_cancellation);
        startup.CancelAfter(TimeSpan.FromSeconds(30));
        while (true)
        {
            startup.Token.ThrowIfCancellationRequested();
            if (_ownedServer?.HasExited == true) throw new IOException("Owned server exited during startup.");
            try { return await _discovery.GetServerInfoAsync(new(), deadline: Deadline, cancellationToken: startup.Token); }
            catch (RpcException error) when (error.StatusCode == StatusCode.Unavailable && _ownedServer is not null)
            {
                // Read-only readiness polling, before any SDK action. Never retry an MP.
                await Task.Delay(100, startup.Token);
            }
        }
    }

    private void ValidateServer(GetServerInfoResponse info)
    {
        var version = info.Version;
        if (version is null || version.SpatialAnalyzerTarget != _scenario.SaTarget ||
            !ServerReleaseVersion.IsValid(version.BriosaVersion) || version.SourceRevision.Length != 40 ||
            !version.SourceRevision.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ||
            version.ProtocolPackage != "briosa" || info.TargetIsolationMode != TargetIsolationMode.SingleTenant ||
            info.Compatibility is { Major: 0 } ||
            !ServerSelectionPolicy.Compatible(info.Compatibility?.Major ?? 0, info.Compatibility?.Revision ?? 0,
                version.BriosaVersion, version.SourceRevision))
            throw new InvalidOperationException("Server target or behavioral contract is incompatible.");
        if (_ownedServer is { } owned &&
            (version.BriosaVersion != owned.Installation.Version || version.SourceRevision != owned.Installation.SourceRevision ||
             (info.Compatibility?.Major ?? 0) != owned.Installation.ContractMajor ||
             (info.Compatibility?.Revision ?? 0) != owned.Installation.ContractRevision))
            throw new InvalidOperationException("Live server identity differs from the selected installation.");
    }

    private PointName Name(string name) => new() { CollectionName = _scenario.Collection, GroupName = _scenario.Group, TargetName = name };
    private static void Check(MpExecutionDetails? execution, bool present)
    {
        if (!present || execution is null || execution.State != MpExecutionState.Succeeded ||
            !execution.HasMpResultCode || execution.MpResultCode != 2 ||
            execution.OutputRetrievals.Any(o => o.State != OutputRetrievalState.Retrieved))
            throw new InvalidDataException("MP result is incomplete or unsuccessful.");
    }
    public async Task<Context> ReadContext()
    {
        var units = await _utility.GetActiveUnitsAsync(new(), deadline: Deadline, cancellationToken: _cancellation);
        Check(units.Execution, units.HasLength);
        var frame = await _utility.GetWorkingFramePropertiesAsync(new(), deadline: Deadline, cancellationToken: _cancellation);
        Check(frame.Execution, frame.HasFrameName && frame.HasCollectionName);
        return new(units.Length, frame.CollectionName, frame.FrameName);
    }
    public async Task<(double X, double Y, double Z)> ReadPoint(string name)
    {
        var p = await _analysis.GetPointCoordinateAsync(new() { PointName = Name(name) }, deadline: Deadline, cancellationToken: _cancellation);
        Check(p.Execution, p.HasXValue && p.HasYValue && p.HasZValue);
        return (p.XValue, p.YValue, p.ZValue);
    }
    public async Task<double> ReadDistance(string first, string second)
    {
        var d = await _analysis.GetPointToPointDistanceAsync(new() { FirstPoint = Name(first), SecondPoint = Name(second) }, deadline: Deadline, cancellationToken: _cancellation);
        Check(d.Execution, d.HasMagnitude);
        return d.Magnitude;
    }
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ownedGeneration is int generation)
                await _sdk.StopSpatialAnalyzerSdkAsync(new() { ExpectedSdkGeneration = generation }, deadline: Deadline);
        }
        finally { _channel.Dispose(); }
        // External servers and SA remain running; the caller disposes its owned server.
    }
}
