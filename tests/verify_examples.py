"""Engineering-only checks for the tutorials. Never launches SpatialAnalyzer."""
import argparse
from collections import namedtuple
import json
import os
from pathlib import Path
import re
import shutil
import socket
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]

# The typed failures the test server sends, copied from Server 0.10.0 (see
# tests/FakeServer/Program.cs). mp_result_code is None for a failure without MP details.
Failure = namedtuple("Failure", "status detail kind code disposition recovery mp_result_code")
MP_FAILURE = Failure("FailedPrecondition", "The SpatialAnalyzer MP command failed.", "mpFailure",
                     "mp-command-failed", "completed", "none", 3)
WORKER_FAILURE = Failure("Unavailable", "The SpatialAnalyzer worker failed and requires explicit recovery.",
                         "workerFailure", "worker-exited-during-execution", "startedOutcomeUnknown",
                         "workerReplacement", None)
# Each operation's declared output fields, in order (FakeOperation in tests/FakeServer/Program.cs).
# For an MP failure, Server 0.10.0 reports one NotAttempted retrieval per output, in this order.
OUTPUTS = {
    "analysis_operations.get_point_coordinate": ["vector_representation", "x_value", "y_value", "z_value"],
    "construction_operations.construct_collection": [],
    "construction_operations.construct_point_in_working_coordinates": [],
}
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
                            "server-installation-identity-mismatch.", "server-installation-identity-mismatch")
CLIENT_ERRORS = {
    # Grpc.Net.Client ends a call at the caller's deadline with an empty detail.
    "grpc": {"wrong-target": CONNECT_FIRST, "wrong-contract": CONNECT_FIRST, "disconnected": CONNECT_FIRST,
             "missing-output": NOT_SUCCESSFUL, "mp-result-failure": NOT_SUCCESSFUL,
             "write-result-failure": NOT_SUCCESSFUL,
             "deadline": 'Grpc.Core.RpcException: Status(StatusCode="DeadlineExceeded", Detail="")'},
    "dotnet": {"wrong-target": DOTNET_COMPATIBILITY.format("server-installation-target-mismatch"),
               "wrong-contract": DOTNET_COMPATIBILITY.format("server-installation-contract-mismatch"),
               "disconnected": DOTNET_PROTOCOL.format("startup-readiness-not-established"),
               "missing-output": DOTNET_PROTOCOL.format("required-output-missing:z_value")},
    "python": {"wrong-target": PYTHON_COMPATIBILITY, "wrong-contract": PYTHON_COMPATIBILITY,
               "disconnected": "briosa.errors.BriosaLifecycleError: Briosa lifecycle operation is unavailable "
                               "(startup-readiness-not-established).",
               "missing-output": "briosa.errors.BriosaProtocolError: Briosa returned invalid protocol data "
                                 "(required-output-missing:z_value)."},
    # (header line, diagnosticCode property)
    "typescript": {"wrong-target": TYPESCRIPT_COMPATIBILITY, "wrong-contract": TYPESCRIPT_COMPATIBILITY,
                   "disconnected": ("BriosaProtocolError: Briosa returned invalid protocol data "
                                    "(startup-readiness-not-established).", "startup-readiness-not-established"),
                   "missing-output": ("BriosaProtocolError: Briosa returned invalid protocol data "
                                      "(required-output-missing:zValue).", "required-output-missing:zValue")},
}
PYTHON_CAUSE = "The above exception was the direct cause of the following exception:"
PYTHON_CHAIN = re.compile("\n\n(" + re.escape(PYTHON_CAUSE) +
                          "|During handling of the above exception, another exception occurred:)\n\n")


def js_literal(value):
    """A value as Node's util.inspect prints it, without whitespace."""
    if value is None:
        return "null"
    if isinstance(value, str):
        return f"'{value}'"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, list):
        return "[" + ",".join(map(js_literal, value)) + "]"
    return "{" + ",".join(f"{key}:{js_literal(item)}" for key, item in value.items()) + "}"


def without_whitespace(text):
    """Printed JavaScript values with the whitespace outside strings removed."""
    return "".join(re.findall(r"""'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*"|[^\s'"]+""", text))


def terminal_error(name, stderr):
    """The uncaught error a program ended with, as its runtime reports it on stderr.

    .NET: the exceptions of the unhandled-exception report, outermost first. Python: the
    exceptions of the traceback, first raised first, and the chain markers between them,
    leaving out gRPC's debug_error_string line. Node: the header line and the printed own
    properties of the uncaught error. None if stderr does not hold such a report.
    """
    lines = stderr.splitlines()
    if name in ("grpc", "dotnet"):
        prefix = "Unhandled exception. "
        # Exactly one report: a second one means the program failed again afterwards.
        if not lines or not lines[0].startswith(prefix) or sum(line.startswith(prefix) for line in lines) != 1:
            return None
        return [lines[0][len(prefix):]] + [line[len(" ---> "):] for line in lines if line.startswith(" ---> ")]
    if name == "python":
        parts = PYTHON_CHAIN.split(stderr.rstrip("\n"))
        exceptions = []
        for part in parts[::2]:
            report = part.splitlines()
            if not report or report[0] != "Traceback (most recent call last):":
                return None
            # The exception follows the indented frames.
            start = next((i for i, line in enumerate(report) if i and not line.startswith(" ")), None)
            if start is None:
                return None
            exceptions.append("\n".join(line for line in report[start:]
                                        if not line.startswith("\tdebug_error_string = ")))
        return exceptions, parts[1::2]
    blocks = stderr.rstrip("\n").split("\n\n")
    if len(blocks) < 2 or not re.fullmatch(r"Node\.js v\d+\.\d+\.\d+", blocks[-1]):
        return None
    error = blocks[-2].splitlines()
    frames = 1
    while frames < len(error) and error[frames].startswith("    at "):
        frames += 1
    # The last stack frame opens the error's own properties.
    if frames == 1 or not error[frames - 1].endswith(" {") or error[-1] != "}":
        return None
    return error[0], without_whitespace("{" + "\n".join(error[frames:]))


def expected_error(name, case):
    """The uncaught error a failing program must end with, in terminal_error's form."""
    if case not in OPERATION_FAILURES:
        error = CLIENT_ERRORS[name][case]
        if name == "python":
            return [error], []
        if name == "typescript":
            return error[0], js_literal({"diagnosticCode": error[1]})
        return [error]
    operation, failure, safety, replay = OPERATION_FAILURES[case]
    rpc = f'Grpc.Core.RpcException: Status(StatusCode="{failure.status}", Detail="{failure.detail}")'
    message = f"Briosa operation '{operation}' failed ({failure.code})."
    if name == "grpc":
        # The raw example does not decode the error trailer; the gRPC error ends the program.
        return [rpc]
    if name == "dotnet":
        return ["Briosa.BriosaOperationException: " + message, rpc]
    if name == "python":
        cause = ("grpc.aio._call.AioRpcError: <AioRpcError of RPC that terminated with:\n"
                 f"\tstatus = StatusCode.{PYTHON_STATUS[failure.status]}\n\tdetails = \"{failure.detail}\"\n>")
        return [cause, "briosa.errors.BriosaOperationError: " + message], [PYTHON_CAUSE]
    mp_execution = None if failure.mp_result_code is None else {
        "state": "failed", "mpResultCode": failure.mp_result_code,
        "outputRetrievals": [{"fieldName": field, "state": "notAttempted", "diagnosticCode": None}
                             for field in OUTPUTS[operation]]}
    # Node prints every typed field of the uncaught BriosaOperationError, in this order.
    return f"BriosaOperationError: Briosa operation failed ({failure.code}).", js_literal({
        "operationId": operation, "kind": failure.kind, "diagnosticCode": failure.code,
        "executionDisposition": failure.disposition, "recoveryGuidance": failure.recovery,
        "replayGuidance": replay, "replaySafety": safety, "mpExecution": mp_execution})


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
                    error, expected = terminal_error(name, result.stderr), expected_error(name, case)
                    assert error == expected, (f"{name}/{case} did not end with the expected uncaught error.\n"
                                               f"Expected: {expected!r}\nFound: {error!r}\n{result.stderr}")
                    if case in ("wrong-target", "wrong-contract", "disconnected"):
                        # The test server logs every MP call it receives, even before readiness.
                        assert [call for call in calls if not call.startswith("sdk.")] == [], f"An MP was attempted: {calls}"
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
