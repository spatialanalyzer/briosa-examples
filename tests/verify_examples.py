"""Engineering-only checks for the tutorials. Never launches SpatialAnalyzer."""
import argparse
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]

# The typed failures the test server sends, copied from Server 0.10.0 (see
# tests/FakeServer/Program.cs): (gRPC status, status detail, kind, diagnostic code,
# execution disposition, recovery guidance, MP details as TypeScript prints them).
MP_FAILURE = ("FailedPrecondition", "The SpatialAnalyzer MP command failed.", "mpFailure",
              "mp-command-failed", "completed", "none",
              ["mpExecution: {", "state: 'failed'", "mpResultCode: 3"])
WORKER_FAILURE = ("Unavailable", "The SpatialAnalyzer worker failed and requires explicit recovery.",
                  "workerFailure", "worker-exited-during-execution", "startedOutcomeUnknown",
                  "workerReplacement", ["mpExecution: null"])
# case: (operation, failure, replay safety, replay guidance)
OPERATION_FAILURES = {
    "missing-point": ("analysis_operations.get_point_coordinate", MP_FAILURE, "safe", "doNotReplay"),
    "unknown": ("analysis_operations.get_point_coordinate", WORKER_FAILURE, "safe", "mayReplay"),
    "collection-failure": ("construction_operations.construct_collection", MP_FAILURE, "unsafe", "doNotReplay"),
    "second-write-failure": ("construction_operations.construct_point_in_working_coordinates",
                             MP_FAILURE, "unsafe", "doNotReplay"),
    "write-unknown": ("construction_operations.construct_point_in_working_coordinates",
                      WORKER_FAILURE, "unsafe", "reconcileBeforeReplay"),
}
PYTHON_STATUS = {"FailedPrecondition": "FAILED_PRECONDITION", "Unavailable": "UNAVAILABLE"}
# The other failing cases are rejected by the program or its client before any MP failure.
CONNECT_FIRST = "System.InvalidOperationException: Connect a compatible SA 2026 server in Control Center first."
NOT_SUCCESSFUL = "System.InvalidOperationException: SA did not return a successful result."
DOTNET_PROTOCOL = "Briosa.BriosaProtocolException: Briosa returned an invalid protocol value ({})."
DOTNET_COMPATIBILITY = "Briosa.BriosaCompatibilityException: The Briosa runtime is incompatible with this client ({})."
PYTHON_COMPATIBILITY = ("briosa.errors.BriosaCompatibilityError: The Briosa server is incompatible with this client "
                        "(server-installation-identity-mismatch).")
TYPESCRIPT_COMPATIBILITY = ("BriosaCompatibilityError: Briosa compatibility check failed: "
                            "server-installation-identity-mismatch.")
CLIENT_ERRORS = {
    "grpc": {"wrong-target": CONNECT_FIRST, "wrong-contract": CONNECT_FIRST, "disconnected": CONNECT_FIRST,
             "missing-output": NOT_SUCCESSFUL, "mp-result-failure": NOT_SUCCESSFUL,
             "write-result-failure": NOT_SUCCESSFUL,
             "deadline": 'Grpc.Core.RpcException: Status(StatusCode="DeadlineExceeded"'},
    "dotnet": {"wrong-target": DOTNET_COMPATIBILITY.format("server-installation-target-mismatch"),
               "wrong-contract": DOTNET_COMPATIBILITY.format("server-installation-contract-mismatch"),
               "disconnected": DOTNET_PROTOCOL.format("startup-readiness-not-established"),
               "missing-output": DOTNET_PROTOCOL.format("required-output-missing:z_value")},
    "python": {"wrong-target": PYTHON_COMPATIBILITY, "wrong-contract": PYTHON_COMPATIBILITY,
               "disconnected": "briosa.errors.BriosaLifecycleError: Briosa lifecycle operation is unavailable "
                               "(startup-readiness-not-established).",
               "missing-output": "briosa.errors.BriosaProtocolError: Briosa returned invalid protocol data "
                                 "(required-output-missing:z_value)."},
    "typescript": {"wrong-target": TYPESCRIPT_COMPATIBILITY, "wrong-contract": TYPESCRIPT_COMPATIBILITY,
                   "disconnected": "BriosaProtocolError: Briosa returned invalid protocol data "
                                   "(startup-readiness-not-established).",
                   "missing-output": "BriosaProtocolError: Briosa returned invalid protocol data "
                                     "(required-output-missing:zValue)."},
}


def expected_report(name, case):
    """Text a failing program must print: the typed details its error reports."""
    if case not in OPERATION_FAILURES:
        return [CLIENT_ERRORS[name][case]]
    operation, failure, safety, replay = OPERATION_FAILURES[case]
    status, detail, kind, code, disposition, recovery, mp_details = failure
    rpc_status = f'Grpc.Core.RpcException: Status(StatusCode="{status}", Detail="{detail}")'
    if name == "grpc":
        # The raw example does not decode the error trailer; it reports the gRPC status.
        return [rpc_status]
    if name == "dotnet":
        return [f"Briosa.BriosaOperationException: Briosa operation '{operation}' failed ({code}).", rpc_status]
    if name == "python":
        return [f"briosa.errors.BriosaOperationError: Briosa operation '{operation}' failed ({code}).",
                f"status = StatusCode.{PYTHON_STATUS[status]}", f'details = "{detail}"']
    # Node prints every typed field of the uncaught BriosaOperationError.
    return [f"BriosaOperationError: Briosa operation failed ({code}).", f"operationId: '{operation}'",
            f"kind: '{kind}'", f"diagnosticCode: '{code}'", f"executionDisposition: '{disposition}'",
            f"recoveryGuidance: '{recovery}'", f"replayGuidance: '{replay}'", f"replaySafety: '{safety}'",
            *mp_details]


def run_checked(command, cwd):
    result = subprocess.run(command, cwd=cwd, capture_output=True, text=True, timeout=120)
    if result.returncode:
        raise AssertionError(f"{command}\n{result.stdout}\n{result.stderr}")


def replace_once(path, before, after):
    source = path.read_text(encoding="utf-8")
    assert source.count(before) == 1, f"Test connection replacement is no longer unique: {path}"
    path.write_text(source.replace(before, after), encoding="utf-8")


def wait_for_port(process, port):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        assert process.poll() is None, "Test server exited"
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.1):
                return
        except OSError:
            time.sleep(0.05)
    raise AssertionError("Test server did not start")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--node", default="node")
    parser.add_argument("--python", default=str(ROOT / "point-inspection/python/.venv/Scripts/python.exe"))
    args = parser.parse_args()
    evidence = ROOT / "artifacts" / ("test-" + uuid.uuid4().hex)
    samples = evidence / "samples"
    samples.mkdir(parents=True)

    # Keep all test wiring here. The only changes to copied teaching programs
    # are explicit test-server selection / endpoint; no production fallback.
    for name in ("Directory.Build.props", "global.json"):
        shutil.copy2(ROOT / name, samples / name)
    ignore = shutil.ignore_patterns("bin", "obj", "node_modules", "dist", ".venv", "__pycache__")
    for name in ("point-inspection", "protocol"):
        shutil.copytree(ROOT / name, samples / name, ignore=ignore)
    shutil.copytree(ROOT / "artifacts/protocol", samples / "artifacts/protocol")
    shutil.copytree(ROOT / "point-inspection/typescript/node_modules",
                    samples / "point-inspection/typescript/node_modules")

    payload = evidence / "server"
    shutil.copytree(ROOT / "tests/FakeServer/bin/Release/net10.0", payload)
    executable = payload / "Briosa.Server.exe"
    assert executable.is_file()
    (payload / "Briosa.Worker.exe").write_bytes(b"Test placeholder; never executed.")
    (payload / "manifest.json").write_text(json.dumps({
        "schemaVersion": 3,
        "artifactName": "briosa-0.10.0-sa-2026.1.0529.7-win-x64",
        "briosaVersion": "0.10.0", "sourceRevision": "a" * 40,
        "spatialAnalyzerTarget": "2026.1.0529.7", "runtimeIdentifier": "win-x64",
        "protocolPackage": "briosa", "spatialAnalyzerBundled": False,
        "compatibility": {"major": 3, "revision": 0},
    }), encoding="utf-8")
    server_path = executable.as_posix()
    with socket.socket() as sock:
        sock.bind(("127.0.0.1", 0))
        port = sock.getsockname()[1]

    replace_once(samples / "point-inspection/dotnet/Program.cs",
                 "LaunchSpatialAnalyzer = false",
                 f'LaunchSpatialAnalyzer = false, ServerSelection = new BriosaServerSelection {{ ExecutablePath = "{server_path}" }}')
    replace_once(samples / "point-inspection/typescript/src/inspection.ts",
                 "launchSpatialAnalyzer: false",
                 f"launchSpatialAnalyzer: false, serverSelection: {{ executablePath: {json.dumps(server_path)} }}")
    replace_once(samples / "point-inspection/python/inspection.py",
                 "from briosa import BriosaClient, BriosaStartOptions, CollectionName, PointName, Vector",
                 "from briosa import BriosaClient, BriosaStartOptions, BriosaServerSelection, CollectionName, PointName, Vector")
    replace_once(samples / "point-inspection/python/inspection.py",
                 "launch_spatial_analyzer=False",
                 f"launch_spatial_analyzer=False, server_selection=BriosaServerSelection(executable_path={server_path!r})")
    replace_once(samples / "point-inspection/grpc/Program.cs",
                 "http://127.0.0.1:50051", f"http://127.0.0.1:{port}")

    for project in ("grpc/PointInspection.Grpc.csproj", "dotnet/PointInspection.csproj"):
        run_checked([args.dotnet, "build", f"point-inspection/{project}", "-c", "Release",
                     "-p:RestoreLockedMode=true"], samples)
    run_checked([args.node, "node_modules/typescript/bin/tsc", "-p", "tsconfig.json"],
                samples / "point-inspection/typescript")
    commands = {
        "grpc": [args.dotnet, "point-inspection/grpc/bin/Release/net10.0/PointInspection.Grpc.dll"],
        "dotnet": [args.dotnet, "point-inspection/dotnet/bin/Release/net10.0/PointInspection.dll"],
        "typescript": [args.node, "point-inspection/typescript/dist/inspection.js"],
        "python": [args.python, "point-inspection/python/inspection.py"],
    }
    completed = []
    collections = {"grpc": "BriosaGrpcDemo", "dotnet": "BriosaDotnetDemo",
                   "typescript": "BriosaTypeScriptDemo", "python": "BriosaPythonDemo"}
    for name, command in commands.items():
        cases = ["ok", "inches", "partial-success", "wrong-target", "wrong-contract",
                 "disconnected", "missing-point", "unknown", "missing-output",
                 "collection-failure", "write-unknown", "second-write-failure"]
        if name == "grpc":
            cases += ["mp-result-failure", "deadline", "write-result-failure"]
        for case in cases:
            calls_path = evidence / f"{name}-{case}.calls"
            env = os.environ.copy()
            env.pop("BRIOSA_SERVER_PATH", None)
            env.update(BRIOSA_EXAMPLE_TEST_CASE=case, BRIOSA_EXAMPLE_TEST_LOG=str(calls_path),
                       BRIOSA_EXAMPLE_TEST_COLLECTION=collections[name],
                       BRIOSA_EXAMPLE_TEST_EXTERNAL="1" if name == "grpc" else "0",
                       DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1")
            if Path(args.dotnet).is_absolute():
                env["DOTNET_ROOT"] = str(Path(args.dotnet).parent)
            process = None
            server_output = None
            try:
                if name == "grpc":
                    server_output = (evidence / f"{name}-{case}.server-output").open("w")
                    process = subprocess.Popen([str(executable), f"--Briosa:Endpoint:Port={port}"],
                                               env=env, stdout=server_output, stderr=server_output,
                                               creationflags=subprocess.CREATE_NO_WINDOW)
                    wait_for_port(process, port)
                result = subprocess.run(command, cwd=samples, env=env, capture_output=True,
                                        text=True, timeout=50)
                (evidence / f"{name}-{case}.output").write_text(result.stdout + result.stderr, encoding="utf-8")
                calls = calls_path.read_text().splitlines() if calls_path.exists() else []
                if case in ("ok", "inches", "partial-success"):
                    unit = "Inches" if case == "inches" else "Millimeters"
                    expected = [f"Length unit: {unit}", "P1: (0.000, 0.000, 0.000)",
                                "P2: (3.000, 4.000, 0.000)", f"Distance: 5.000 {unit}"]
                    if case == "partial-success" and name == "grpc":
                        # The clients return MP code 4 as a success; the raw example reports it.
                        expected.insert(3, "SA reported a partial success (MP code 4).")
                    assert result.returncode == 0, result.stderr
                    assert result.stdout.splitlines() == expected, result.stdout
                    assert [call for call in calls if not call.startswith("sdk.")] == ["units", "collection.create", "point.create", "point.create", "point", "point", "distance"], calls
                else:
                    assert result.returncode != 0, f"{name}/{case} should stop"
                    assert "Distance:" not in result.stdout, result.stdout
                    report = result.stdout + result.stderr
                    for text in expected_report(name, case):
                        assert text in report, f"{name}/{case} did not report {text!r}:\n{report}"
                    if case in ("wrong-target", "wrong-contract", "disconnected"):
                        assert "units" not in calls and "point" not in calls, calls
                        assert not any(call.endswith(".create") for call in calls), calls
                    elif case in ("collection-failure", "write-unknown", "second-write-failure", "write-result-failure"):
                        assert calls.count("collection.create") == 1, calls
                        expected_writes = 0 if case == "collection-failure" else 2 if case == "second-write-failure" else 1
                        assert calls.count("point.create") == expected_writes, f"Write was retried or skipped: {calls}"
                        assert "point" not in calls, "Reads continued after a failed write"
                    else:
                        assert calls.count("point") == 1, f"Failed call was replayed: {calls}"
                        assert "P1:" not in result.stdout, "Missing or failed point was printed as data"
                    if case in ("wrong-target", "wrong-contract"):
                        assert "sdk.start" not in calls, calls
                if name == "grpc":
                    assert process.poll() is None, "Example stopped the external server"
                    assert not any(call.startswith("sdk.") for call in calls), calls
                elif "sdk.start" in calls:
                    assert calls.count("sdk.stop") == 1, f"Owned SDK was not cleaned up: {calls}"
                completed.append({"implementation": name, "case": case, "result": "passed"})
                print(f"PASS {name}/{case}", flush=True)
            finally:
                if process is not None:
                    process.terminate()
                    process.wait(timeout=10)
                if server_output is not None:
                    server_output.close()
    (evidence / "summary.json").write_text(json.dumps(completed, indent=2) + "\n", encoding="utf-8")
    print(f"{len(completed)} scenarios passed. Evidence: {evidence}")


if __name__ == "__main__":
    main()
