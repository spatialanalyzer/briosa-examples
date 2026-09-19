// Test double only: no COM, SDK, SpatialAnalyzer launch, or vendor dependencies.
using Briosa;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;

if (Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_CASE") is null)
    throw new InvalidOperationException("This is a test double. The test harness must select its test scenario.");
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(
    builder.Configuration.GetValue<int?>("Briosa:Endpoint:Port") ?? 50051,
    o => o.Protocols = HttpProtocols.Http2));
builder.Services.AddGrpc();
builder.Services.AddSingleton<FakeData>();
var app = builder.Build();
app.MapGrpcService<Discovery>();
app.MapGrpcService<Sdk>();
app.MapGrpcService<Application>();
app.MapGrpcService<Utility>();
app.MapGrpcService<Analysis>();
await app.RunAsync();

sealed class FakeData
{
    public string Case { get; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_CASE") ?? "ok";
    public string Version => Case == "legacy" ? "0.6.1" : Case == "compatible-newer" ? "0.7.1" : "0.7.0";
    public string SourceRevision => Case == "legacy" ? "32a3b56ba4ae31ea5ec6ec3b2aa051eb61c866aa" : new string('a', 40);
    public bool Started { get; set; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_EXTERNAL") == "1";
    public bool Ready { get; set; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_EXTERNAL") == "1" && Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_CASE") != "disconnected";
    private readonly Dictionary<string, Point> _points = new()
    {
        ["P1"] = new("P1", 0, 0, 0),
        ["P2"] = new("P2", 3, 4, 0),
    };
    public void Log(string method)
    {
        var path = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_LOG");
        if (path is not null) File.AppendAllText(path, method + "\n");
    }
    public SpatialAnalyzerSdkLifecycleState State => new()
    {
        StateRevision = 1,
        SdkGeneration = 7,
        ApplicationGeneration = 11,
        SdkState = Ready ? SpatialAnalyzerSdkState.Ready : Started ? SpatialAnalyzerSdkState.Running : SpatialAnalyzerSdkState.Stopped,
        ConnectionState = Ready ? SpatialAnalyzerConnectionState.Connected : SpatialAnalyzerConnectionState.Disconnected,
        ExecutionReadinessState = Ready ? SpatialAnalyzerExecutionReadinessState.ExecutionReady : SpatialAnalyzerExecutionReadinessState.Unverified,
        RecoveryState = SpatialAnalyzerSdkRecoveryState.NotRequired,
        ReadyForMp = Ready
    };
    public static MpExecutionDetails Success(params string[] fields) => new()
    {
        State = MpExecutionState.Succeeded,
        MpResultCode = 2,
        OutputRetrievals = { fields.Select(f => new OutputRetrievalDetails { FieldName = f, State = OutputRetrievalState.Retrieved }) }
    };
    public static RpcException Failure(string operation, OperationFailureKind kind, ExecutionDisposition disposition, StatusCode status)
    {
        var detail = new OperationError
        {
            OperationId = operation,
            Kind = kind,
            DiagnosticCode = "synthetic-failure",
            ExecutionDisposition = disposition,
            RecoveryGuidance = RecoveryGuidance.OperatorInterventionRequired,
            ReplayGuidance = ReplayGuidance.DoNotReplay,
            ReplaySafety = ReplaySafety.Unknown
        };
        return new RpcException(new Status(status, "Synthetic failure"), new Metadata { { "briosa-operation-error-bin", detail.ToByteArray() } });
    }
    public void RequireReady() { if (!Ready) throw new RpcException(new Status(StatusCode.FailedPrecondition, "Not ready")); }
    public Point Point(PointName name)
    {
        if (name.CollectionName != "BriosaDemo" || name.GroupName != "Points" || !_points.TryGetValue(name.TargetName, out var p))
            throw Failure("analysis_operations.get_point_coordinate", OperationFailureKind.MpFailure, ExecutionDisposition.Completed, StatusCode.FailedPrecondition);
        return p;
    }
    public static Vector Vector(Point p) => new() { X = p.X, Y = p.Y, Z = p.Z };
}

sealed class Discovery(FakeData data) : DiscoveryService.DiscoveryServiceBase
{
    public override Task<GetServerInfoResponse> GetServerInfo(GetServerInfoRequest request, ServerCallContext context) => Task.FromResult(new GetServerInfoResponse
    {
        Version = new VersionCoordinates
        {
            BriosaVersion = data.Case == "wrong-version" ? "99.0.0" : data.Version,
            SourceRevision = data.Case == "wrong-source" ? new string('b', 40) : data.SourceRevision,
            ProtocolPackage = "briosa",
            SpatialAnalyzerTarget = data.Case == "wrong-target" ? "2024.1.0508.5" : "2026.1.0529.7"
        },
        Compatibility = data.Case == "legacy" ? null : new CompatibilityContract { Major = data.Case == "wrong-contract" ? 2u : 1u, Revision = 0 },
        ReadyForMp = data.Ready,
        TargetIsolationMode = TargetIsolationMode.SingleTenant,
        WorkerState = data.Ready ? WorkerRuntimeState.Ready : WorkerRuntimeState.Stopped,
        SpatialAnalyzerConnectionState = data.State.ConnectionState,
        SpatialAnalyzerExecutionReadinessState = data.State.ExecutionReadinessState,
        ActivatedSdkIdentity = new() { Version = "2026.1.0529.7", Source = RuntimeIdentityEvidenceSource.RuntimeVerification, MatchState = RuntimeIdentityMatchState.ExactMatch },
        ConnectedSpatialAnalyzerIdentity = new() { Version = "2026.1.0529.7", Source = RuntimeIdentityEvidenceSource.RuntimeVerification, MatchState = RuntimeIdentityMatchState.ExactMatch }
    });
    public override Task<ListCapabilitiesResponse> ListCapabilities(ListCapabilitiesRequest request, ServerCallContext context)
    {
        var response = new ListCapabilitiesResponse { SpatialAnalyzerTarget = "2026.1.0529.7", ProtocolPackage = "briosa" };
        var ids = new[] { "utility_operations.get_active_units", "utility_operations.get_working_frame_properties", "analysis_operations.get_point_coordinate", "analysis_operations.get_point_to_point_distance" };
        string[] methods = ["/briosa.UtilityOperations/GetActiveUnits", "/briosa.UtilityOperations/GetWorkingFrameProperties",
            "/briosa.AnalysisOperations/GetPointCoordinate", "/briosa.AnalysisOperations/GetPointToPointDistance"];
        for (int i = 0; i < methods.Length; i++)
        {
            if (data.Case == "unsupported" && i == 2) continue;
            var split = methods[i].Split('/');
            response.Operations.Add(new OperationCapability { OperationId = ids[i], GrpcService = split[1], Rpc = split[2], FullyQualifiedMethod = methods[i], Effect = OperationEffect.ReadOnly, ReplaySafety = ReplaySafety.Unknown, ExecutionScope = OperationExecutionScope.GlobalStateRead });
        }
        return Task.FromResult(response);
    }
}
sealed class Application : SpatialAnalyzerLifecycle.SpatialAnalyzerLifecycleBase
{
    public override Task<GetSpatialAnalyzerStateResponse> GetSpatialAnalyzerState(GetSpatialAnalyzerStateRequest request, ServerCallContext context) => Task.FromResult(new GetSpatialAnalyzerStateResponse
    {
        State = new() { StateRevision = 1, ApplicationGeneration = 11, ApplicationState = SpatialAnalyzerApplicationState.Running, Ownership = SpatialAnalyzerOwnership.External }
    });
}
sealed class Sdk(FakeData data) : SpatialAnalyzerSdkLifecycle.SpatialAnalyzerSdkLifecycleBase
{
    public override Task<GetSpatialAnalyzerSdkStateResponse> GetSpatialAnalyzerSdkState(GetSpatialAnalyzerSdkStateRequest request, ServerCallContext context) => Task.FromResult(new GetSpatialAnalyzerSdkStateResponse { State = data.State });
    public override Task<StartSpatialAnalyzerSdkResponse> StartSpatialAnalyzerSdk(StartSpatialAnalyzerSdkRequest request, ServerCallContext context)
    { data.Log("sdk.start"); data.Started = true; return Task.FromResult(new StartSpatialAnalyzerSdkResponse { State = data.State }); }
    public override Task<ConnectToSpatialAnalyzerResponse> ConnectToSpatialAnalyzer(ConnectToSpatialAnalyzerRequest request, ServerCallContext context)
    {
        data.Log("sdk.connect");
        if (request.ExpectedSdkGeneration != 7) throw new RpcException(new Status(StatusCode.FailedPrecondition, "Wrong generation"));
        data.Ready = data.Case != "disconnected";
        return Task.FromResult(new ConnectToSpatialAnalyzerResponse { State = data.State });
    }
    public override Task<StopSpatialAnalyzerSdkResponse> StopSpatialAnalyzerSdk(StopSpatialAnalyzerSdkRequest request, ServerCallContext context)
    {
        data.Log("sdk.stop");
        if (request.ExpectedSdkGeneration != 7) throw new RpcException(new Status(StatusCode.FailedPrecondition, "Wrong generation"));
        data.Started = false; data.Ready = false;
        return Task.FromResult(new StopSpatialAnalyzerSdkResponse { State = data.State });
    }
}
sealed class Utility(FakeData data) : UtilityOperations.UtilityOperationsBase
{
    public override Task<GetActiveUnitsResult> GetActiveUnits(GetActiveUnitsRequest request, ServerCallContext context)
    {
        data.RequireReady(); data.Log("units");
        return Task.FromResult(new GetActiveUnitsResult { Length = data.Case == "inches" ? "inches" : "millimeters", Angular = "degrees", Temperature = "Celsius", Execution = FakeData.Success("length", "angular", "temperature") });
    }

}
sealed class Analysis(FakeData data) : AnalysisOperations.AnalysisOperationsBase
{
    public override async Task<GetPointCoordinateResult> GetPointCoordinate(GetPointCoordinateRequest request, ServerCallContext context)
    {
        data.RequireReady(); data.Log("point");
        switch (data.Case)
        {
            case "mp-failure":
            case "missing-point":
                throw FakeData.Failure("analysis_operations.get_point_coordinate", OperationFailureKind.MpFailure, ExecutionDisposition.Completed, StatusCode.FailedPrecondition);
            case "unknown":
                throw FakeData.Failure("analysis_operations.get_point_coordinate", OperationFailureKind.WorkerFailure, ExecutionDisposition.StartedOutcomeUnknown, StatusCode.Unavailable);
            case "deadline": await Task.Delay(TimeSpan.FromSeconds(15), context.CancellationToken); break;
        }
        var p = data.Point(request.PointName);
        var response = new GetPointCoordinateResult
        {
            XValue = p.X,
            YValue = p.Y,
            ZValue = p.Z,
            VectorRepresentation = FakeData.Vector(p),
            Execution = FakeData.Success("vector_representation", "x_value", "y_value", "z_value")
        };
        if (data.Case == "missing-output") response.ClearZValue();
        if (data.Case == "mp-result-failure") response.Execution.MpResultCode = 1;
        if (data.Case == "nonfinite") response.XValue = double.NaN;
        return response;
    }
    public override Task<GetPointToPointDistanceResult> GetPointToPointDistance(GetPointToPointDistanceRequest request, ServerCallContext context)
    {
        data.RequireReady(); data.Log("distance");
        var a = data.Point(request.FirstPoint); var b = data.Point(request.SecondPoint);
        var x = b.X - a.X; var y = b.Y - a.Y; var z = b.Z - a.Z;
        return Task.FromResult(new GetPointToPointDistanceResult
        {
            XValue = x,
            YValue = y,
            ZValue = z,
            VectorRepresentation = new() { X = x, Y = y, Z = z },
            Magnitude = Math.Sqrt(x * x + y * y + z * z),
            Execution = FakeData.Success("vector_representation", "x_value", "y_value", "z_value", "magnitude")
        });
    }
}

sealed record Point(string Name, double X, double Y, double Z);
