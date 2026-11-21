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
app.MapGrpcService<Construction>();
await app.RunAsync();

sealed class FakeData
{
    public string Case { get; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_CASE") ?? "ok";
    public string Version => Case == "legacy" ? "0.6.1" : Case == "compatible-newer" ? "0.10.1" : "0.10.0";
    public string SourceRevision => Case == "legacy" ? "32a3b56ba4ae31ea5ec6ec3b2aa051eb61c866aa" : new string('a', 40);
    public bool Started { get; set; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_EXTERNAL") == "1";
    public bool Ready { get; set; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_EXTERNAL") == "1" && Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_CASE") != "disconnected";
    public string? Collection { get; set; }
    public string ExpectedCollection { get; } = Environment.GetEnvironmentVariable("BRIOSA_EXAMPLE_TEST_COLLECTION")!;
    public Dictionary<string, Point> Points { get; } = new();
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
    // The two typed failures below copy what Server 0.10.0 (briosa tag v0.10.0) sends,
    // field by field, from targets/2026.1.0529.7/src/Briosa.Server/Services/Outcomes/
    // GrpcOperationOutcomeMapper.cs. The worker generation is the fake's SDK generation:
    // the real server reports the same worker generation in both places.
    const int WorkerGeneration = 7;

    // SA ran the MP and returned a failure code: RequireSuccess, the MP-failure branch
    // (lines 132-152), with the worker's "mp-command-failed" code (Briosa.Worker/Sdk/
    // SpatialAnalyzerSdkAdapter.cs line 122). The server copies SA's code; 3 is the failure
    // code SA returned in the released licensed probe records (targets/2026.1.0529.7/docs/
    // testing/evidence/probe-session-2-2026.1.0529.7-2026-10-25.md).
    public static RpcException MpFailure(FakeOperation operation) => Failure(StatusCode.FailedPrecondition,
        "The SpatialAnalyzer MP command failed.", new OperationError
        {
            OperationId = operation.Id,
            Kind = OperationFailureKind.MpFailure,
            DiagnosticCode = "mp-command-failed",
            ExecutionDisposition = ExecutionDisposition.Completed,
            RecoveryGuidance = RecoveryGuidance.None,
            ReplayGuidance = ReplayGuidance.DoNotReplay,
            ReplaySafety = operation.ReplaySafety,
            WorkerGeneration = WorkerGeneration,
            MpExecution = new()
            {
                State = MpExecutionState.Failed,
                MpResultCode = 3,
                OutputRetrievals = { operation.Outputs.Select(f => new OutputRetrievalDetails { FieldName = f, State = OutputRetrievalState.NotAttempted }) }
            }
        });

    // The worker process exited during the MP, so the reply is lost and the outcome is
    // unknown: CreateTransportFailure, the WorkerFailure branch (lines 412-423), with the
    // supervisor's "worker-exited-during-execution" code (Briosa.Server/Workers/
    // WorkerProcessSupervisor.cs lines 898-917). Replay guidance follows the operation's
    // reviewed replay safety (ReplayAfterAmbiguousCompletion, lines 728-732). The real
    // supervisor also retires the worker generation; this fake keeps its SDK state.
    public static RpcException WorkerFailure(FakeOperation operation) => Failure(StatusCode.Unavailable,
        "The SpatialAnalyzer worker failed and requires explicit recovery.", new OperationError
        {
            OperationId = operation.Id,
            Kind = OperationFailureKind.WorkerFailure,
            DiagnosticCode = "worker-exited-during-execution",
            ExecutionDisposition = ExecutionDisposition.StartedOutcomeUnknown,
            RecoveryGuidance = RecoveryGuidance.WorkerReplacement,
            ReplayGuidance = operation.ReplaySafety == ReplaySafety.Safe ? ReplayGuidance.MayReplay : ReplayGuidance.ReconcileBeforeReplay,
            ReplaySafety = operation.ReplaySafety,
            WorkerGeneration = WorkerGeneration
        });

    static RpcException Failure(StatusCode status, string detail, OperationError error) =>
        new(new Status(status, detail), new Metadata { { "briosa-operation-error-bin", error.ToByteArray() } });

    // The fake's other rejections (here and the InvalidArgument, AlreadyExists and generation
    // checks below) are test assertions with no OperationError trailer and no Server 0.10.0
    // counterpart. No scenario expects them: they make an unexpected request fail the run.
    public void RequireReady() { if (!Ready) throw new RpcException(new Status(StatusCode.FailedPrecondition, "Not ready")); }
    public Point Point(PointName name, FakeOperation operation)
    {
        if (name.CollectionName != Collection || name.GroupName != "Points" || !Points.TryGetValue(name.TargetName, out var p))
            throw MpFailure(operation);
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
        Compatibility = data.Case == "legacy" ? null : new CompatibilityContract { Major = data.Case == "wrong-contract" ? 2u : 3u, Revision = 0 },
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
        // Shaped like the packaged standard profile. Each row copies the real server's
        // reviewed classification: no risk flags, quick, no recorded validation gap.
        var response = new ListCapabilitiesResponse { SpatialAnalyzerTarget = "2026.1.0529.7", ProtocolPackage = "briosa", AdmissionProfile = "standard", PolicyFingerprint = "sha256:" + new string('c', 64) };
        var ids = new[] { "utility_operations.get_active_units", "utility_operations.get_working_frame_properties", "analysis_operations.get_point_coordinate", "analysis_operations.get_point_to_point_distance", "construction_operations.construct_collection", "construction_operations.construct_point_in_working_coordinates" };
        string[] methods = ["/briosa.UtilityOperations/GetActiveUnits", "/briosa.UtilityOperations/GetWorkingFrameProperties",
            "/briosa.AnalysisOperations/GetPointCoordinate", "/briosa.AnalysisOperations/GetPointToPointDistance",
            "/briosa.ConstructionOperations/ConstructCollection", "/briosa.ConstructionOperations/ConstructPointInWorkingCoordinates"];
        for (int i = 0; i < methods.Length; i++)
        {
            if (data.Case == "unsupported" && i == 2) continue;
            var split = methods[i].Split('/');
            response.Operations.Add(new OperationCapability
            {
                OperationId = ids[i], GrpcService = split[1], Rpc = split[2], FullyQualifiedMethod = methods[i],
                Effect = i >= 4 ? OperationEffect.Mutating : OperationEffect.ReadOnly,
                ReplaySafety = i >= 4 ? ReplaySafety.Unsafe : ReplaySafety.Safe,
                ExecutionScope = i >= 4 ? OperationExecutionScope.GlobalStateMutation : OperationExecutionScope.GlobalStateRead,
                DurationClass = OperationDurationClass.Quick,
                ValidationStatus = OperationValidationStatus.NoRecordedGap,
                Admission = OperationAdmission.Admitted
            });
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
        return Task.FromResult(new GetActiveUnitsResult { Length = data.Case == "inches" ? "Inches" : "Millimeters", Angular = "degrees", Temperature = "Celsius", Execution = FakeData.Success("length", "angular", "temperature") });
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
                throw FakeData.MpFailure(FakeOperation.GetPointCoordinate);
            case "unknown":
                throw FakeData.WorkerFailure(FakeOperation.GetPointCoordinate);
            // The caller's own 10-second deadline ends the call. As with Server 0.10.0, the
            // caller then receives DeadlineExceeded without the server's typed detail.
            case "deadline": await Task.Delay(TimeSpan.FromSeconds(15), context.CancellationToken); break;
        }
        var p = data.Point(request.PointName, FakeOperation.GetPointCoordinate);
        var response = new GetPointCoordinateResult
        {
            XValue = p.X,
            YValue = p.Y,
            ZValue = p.Z,
            VectorRepresentation = FakeData.Vector(p),
            Execution = FakeData.Success("vector_representation", "x_value", "y_value", "z_value")
        };
        // Malformed OK results with no Server 0.10.0 counterpart. They test the result checks
        // in the clients and the raw example. Server 0.10.0 sends a typed error instead:
        // DataLoss/OutputRetrievalFailure for a missing output (mapper lines 176-193) or a
        // non-finite value (lines 154-163), and FailedPrecondition/MpFailure for a code other
        // than 2 or 4 (lines 132-152).
        if (data.Case == "missing-output") response.ClearZValue();
        if (data.Case == "mp-result-failure") response.Execution.MpResultCode = 1;
        if (data.Case == "nonfinite") response.XValue = double.NaN;
        return response;
    }
    public override Task<GetPointToPointDistanceResult> GetPointToPointDistance(GetPointToPointDistanceRequest request, ServerCallContext context)
    {
        data.RequireReady(); data.Log("distance");
        var a = data.Point(request.FirstPoint, FakeOperation.GetPointToPointDistance);
        var b = data.Point(request.SecondPoint, FakeOperation.GetPointToPointDistance);
        var x = b.X - a.X; var y = b.Y - a.Y; var z = b.Z - a.Z;
        var response = new GetPointToPointDistanceResult
        {
            XValue = x,
            YValue = y,
            ZValue = z,
            VectorRepresentation = new() { X = x, Y = y, Z = z },
            Magnitude = Math.Sqrt(x * x + y * y + z * z),
            Execution = FakeData.Success("vector_representation", "x_value", "y_value", "z_value", "magnitude")
        };
        // MP code 4: OK with its outputs, reported as a partial success (contract major 3).
        if (data.Case == "partial-success")
        {
            response.Execution.State = MpExecutionState.PartialSuccess;
            response.Execution.MpResultCode = 4;
        }
        return Task.FromResult(response);
    }
}

sealed class Construction(FakeData data) : ConstructionOperations.ConstructionOperationsBase
{
    public override Task<ConstructCollectionResult> ConstructCollection(ConstructCollectionRequest request, ServerCallContext context)
    {
        data.RequireReady(); data.Log("collection.create");
        if (data.Case == "collection-failure")
            throw FakeData.MpFailure(FakeOperation.ConstructCollection);
        if (request.CollectionName?.Name != data.ExpectedCollection || data.Collection is not null ||
            !request.HasFolderPath || request.FolderPath != "" || !request.HasMakeDefaultCollection || request.MakeDefaultCollection)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Unexpected collection or changed working collection"));
        data.Collection = request.CollectionName.Name;
        return Task.FromResult(new ConstructCollectionResult { Execution = FakeData.Success() });
    }

    public override Task<ConstructPointInWorkingCoordinatesResult> ConstructPointInWorkingCoordinates(ConstructPointInWorkingCoordinatesRequest request, ServerCallContext context)
    {
        data.RequireReady(); data.Log("point.create");
        var name = request.PointName;
        var xyz = request.WorkingCoordinates;
        if (name is null || name.CollectionName != data.Collection || name.GroupName != "Points" ||
            xyz is null || !xyz.HasX || !xyz.HasY || !xyz.HasZ)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Missing point identity or coordinates"));
        if (data.Case == "second-write-failure" && data.Points.Count == 1)
            throw FakeData.MpFailure(FakeOperation.ConstructPointInWorkingCoordinates);
        if (!data.Points.TryAdd(name.TargetName, new(name.TargetName, xyz.X, xyz.Y, xyz.Z)))
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Point already created"));
        // Simulate a committed write whose reply is lost: it must not be replayed.
        if (data.Case == "write-unknown")
            throw FakeData.WorkerFailure(FakeOperation.ConstructPointInWorkingCoordinates);
        var result = new ConstructPointInWorkingCoordinatesResult { Execution = FakeData.Success() };
        // A malformed OK result (code 3 reported as Succeeded) with no Server 0.10.0
        // counterpart; the server sends FailedPrecondition/MpFailure for code 3.
        if (data.Case == "write-result-failure") result.Execution.MpResultCode = 3;
        return Task.FromResult(result);
    }
}

sealed record Point(string Name, double X, double Y, double Z);

// Each operation's reviewed replay safety and output fields, as Server 0.10.0 registers
// them (the Descriptor and OutputContracts of each <Name>Operation.cs under
// targets/2026.1.0529.7/src/Briosa.Server/Operations/).
sealed record FakeOperation(string Id, ReplaySafety ReplaySafety, string[] Outputs)
{
    public static readonly FakeOperation GetPointCoordinate = new("analysis_operations.get_point_coordinate", ReplaySafety.Safe,
        ["vector_representation", "x_value", "y_value", "z_value"]);
    public static readonly FakeOperation GetPointToPointDistance = new("analysis_operations.get_point_to_point_distance", ReplaySafety.Safe,
        ["vector_representation", "x_value", "y_value", "z_value", "magnitude"]);
    public static readonly FakeOperation ConstructCollection = new("construction_operations.construct_collection", ReplaySafety.Unsafe, []);
    public static readonly FakeOperation ConstructPointInWorkingCoordinates = new("construction_operations.construct_point_in_working_coordinates", ReplaySafety.Unsafe, []);
}
