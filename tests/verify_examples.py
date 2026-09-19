"""Run actual packaged clients against a local synthetic gRPC server. Never uses SA."""
import argparse
import csv
import json
import math
import os
from pathlib import Path
import shutil
import socket
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[1]
FIXTURE = ROOT / "point-inspection/fixture"


def wait_for_port(process, port):
    deadline = time.monotonic() + 15
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AssertionError("Synthetic server exited")
        try:
            with socket.create_connection(("127.0.0.1", port), timeout=0.1):
                return
        except OSError:
            time.sleep(0.05)
    raise AssertionError("Synthetic server did not start")


def verify_report(directory, expected_source):
    expected = (FIXTURE / "expected.csv").read_text(encoding="utf-8").replace("\r\n", "\n")
    actual = (directory / "report.csv").read_text(encoding="utf-8").replace("\r\n", "\n")
    assert actual == expected, f"CSV differs: {directory}"
    report = json.loads((directory / "report.json").read_text(encoding="utf-8"))
    assert report["source"] == expected_source
    assert report["schema_version"] == 1 and report["sa_target"] == "2026.1.0529.7"
    assert report["context"] == {"length_unit": "millimeters", "frame_collection": "BriosaDemo", "frame": "InspectionFrame"}
    expected_rows = list(csv.DictReader(expected.splitlines()))
    assert len(report["rows"]) == len(expected_rows)
    for actual_row, expected_row in zip(report["rows"], expected_rows):
        for key, value in expected_row.items():
            if value == "": assert actual_row[key] is None
            elif key in ("kind", "id", "status"): assert actual_row[key] == value
            else: assert math.isclose(actual_row[key], float(value), abs_tol=1e-6)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--node", default="node")
    parser.add_argument("--python", default=str(ROOT / "point-inspection/python/.venv/Scripts/python.exe"))
    parser.add_argument("--case", action="append", help="Select test cases; default runs full matrix")
    args = parser.parse_args()
    os.chdir(ROOT)
    run = ROOT / "artifacts" / ("test-" + uuid.uuid4().hex)
    run.mkdir(parents=True)
    commands = {
        "grpc": [args.dotnet, "point-inspection/grpc/bin/Release/net10.0/PointInspection.Grpc.dll"],
        "dotnet": [args.dotnet, "point-inspection/dotnet/bin/Release/net10.0/PointInspection.dll"],
        "typescript": [args.node, "point-inspection/typescript/dist/inspection.js"],
        "python": [args.python, "point-inspection/python/inspection.py"],
    }
    server = ROOT / "tests/FakeServer/bin/Release/net10.0/Briosa.Server.exe"
    assert server.is_file(), "Build tests/FakeServer first"
    cases = args.case or ["synthetic", "ok", "compatible-newer", "legacy", "wrong-version", "wrong-source", "wrong-contract", "unsupported", "disconnected", "wrong-units",
                          "changed-frame", "missing-point", "mp-failure", "unknown", "missing-output", "nonfinite", "deadline", "invalid-fixture"]
    completed = []
    for name, command in commands.items():
        for case in cases:
            output = run / f"{name}-{case}"
            log = run / f"{name}-{case}.calls"
            env = os.environ.copy()
            env.pop("BRIOSA_SERVER_PATH", None)
            env.update(BRIOSA_EXAMPLE_TEST_FIXTURE=str(FIXTURE),
                       BRIOSA_EXAMPLE_TEST_LOG=str(log), BRIOSA_EXAMPLE_TEST_CASE=case)
            cmd = command + ["--output", str(output)]
            process = None
            server_log = None
            if case == "invalid-fixture":
                malformed = run / f"{name}-invalid-input"
                shutil.copytree(FIXTURE, malformed)
                with (malformed / "nominals.csv").open("a") as f: f.write("P1,0,0,0,0.2\n")
                cmd += ["--fixture", str(malformed)]
            elif case != "synthetic":
                # Only the synthetic server is copied. This is never registered or installed.
                version = "0.6.1" if case == "legacy" else "0.7.1" if case == "compatible-newer" else "0.7.0"
                source = "32a3b56ba4ae31ea5ec6ec3b2aa051eb61c866aa" if case == "legacy" else "a" * 40
                payload = run / f"{name}-{case}-server"
                shutil.copytree(server.parent, payload)
                # Presence evidence only; the synthetic server never executes this file.
                (payload / "Briosa.Worker.exe").write_bytes(b"Synthetic test placeholder; not executable.")
                manifest = {
                    "schemaVersion": 2 if case == "legacy" else 3,
                    "artifactName": f"briosa-{version}-sa-2026.1.0529.7-win-x64",
                    "briosaVersion": version, "sourceRevision": source,
                    "spatialAnalyzerTarget": "2026.1.0529.7", "runtimeIdentifier": "win-x64",
                    "protocolPackage": "briosa", "spatialAnalyzerBundled": False,
                }
                if case != "legacy": manifest["compatibility"] = {"major": 1, "revision": 0}
                (payload / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")
                cmd += ["--live", "--server-path", str(payload / "Briosa.Server.exe")]
            try:
                if case == "ok":
                    discovery = subprocess.run(command + ["--discover", "--server-path", str(payload / "Briosa.Server.exe")],
                                               env=env, text=True, capture_output=True, timeout=45)
                    assert discovery.returncode == 0, discovery.stderr
                    assert json.loads(discovery.stdout)["selected"]["version"] == "0.7.0"
                    assert not log.exists(), "Read-only discovery contacted the server"
                    invalid_env = env.copy()
                    invalid_env["BRIOSA_SERVER_PATH"] = str(payload / "Briosa.Server.exe")
                    invalid = subprocess.run(command + ["--live", "--server-path", str(run / "missing/Briosa.Server.exe"),
                                                       "--output", str(run / f"{name}-invalid-choice")],
                                             env=invalid_env, text=True, capture_output=True, timeout=45)
                    assert invalid.returncode == 1 and not log.exists(), "Invalid explicit choice fell back to the environment"
                result = subprocess.run(cmd, env=env, text=True, capture_output=True, timeout=50)
                (run / f"{name}-{case}.output").write_text(result.stdout + result.stderr, encoding="utf-8")
                wanted = 2 if case in ("synthetic", "ok", "compatible-newer", "legacy") else 1
                assert result.returncode == wanted, f"{name}/{case}: exit {result.returncode}, expected {wanted}\n{result.stdout}\n{result.stderr}"
                calls = log.read_text().splitlines() if log.exists() else []
                if wanted == 2:
                    verify_report(output, "synthetic" if case == "synthetic" else "live")
                    if case in ("ok", "compatible-newer", "legacy"):
                        assert calls.count("point") == 8 and calls.count("distance") == 2
                    if case == "synthetic":
                        original = (output / "report.csv").read_bytes()
                        again = subprocess.run(cmd, env=env, text=True, capture_output=True, timeout=15)
                        assert again.returncode == 1, "Existing output was accepted"
                        assert (output / "report.csv").read_bytes() == original, "Existing report was overwritten"
                else:
                    assert not output.exists(), "Failure published a report"
                    if case in ("unknown", "missing-point", "mp-failure", "deadline", "missing-output", "nonfinite"):
                        assert calls.count("point") == 1, "Failed operation was retried or failure path never reached"
                    if case in ("wrong-version", "wrong-source", "wrong-contract", "unsupported", "disconnected", "wrong-units"):
                        assert "point" not in calls, "Data read before admission/context checks"
                    if case in ("wrong-version", "wrong-source", "wrong-contract"):
                        assert "sdk.start" not in calls, "SDK activated before installation identity admission"
                    if case == "unknown":
                        assert "unknown" in result.stderr.lower(), "Completion ambiguity was lost"
                if "sdk.start" in calls:
                    assert calls.count("sdk.stop") == 1, "Owned SDK generation was not stopped exactly once"
                completed.append({"implementation": name, "case": case, "result": "passed"})
                print(f"PASS {name}/{case}", flush=True)
            finally:
                if process is not None:
                    process.terminate()
                    process.wait(timeout=10)
                if server_log is not None: server_log.close()
    # The direct-gRPC application can also attach to an explicitly supplied
    # dedicated endpoint. It must leave that external server alive.
    if args.case is None or "ok" in args.case:
        with socket.socket() as sock:
            sock.bind(("127.0.0.1", 0))
            port = sock.getsockname()[1]
        env = os.environ.copy()
        env.update(BRIOSA_EXAMPLE_TEST_FIXTURE=str(FIXTURE), BRIOSA_EXAMPLE_TEST_CASE="ok")
        env.pop("BRIOSA_SERVER_PATH", None)
        external_log = (run / "grpc-external.server-log").open("w")
        process = subprocess.Popen([str(server), f"--Briosa:Endpoint:Port={port}"], env=env,
                                   stdout=external_log, stderr=external_log, creationflags=subprocess.CREATE_NO_WINDOW)
        try:
            wait_for_port(process, port)
            output = run / "grpc-external"
            result = subprocess.run(commands["grpc"] + ["--live", "--endpoint", f"http://127.0.0.1:{port}", "--output", str(output)],
                                    env=env, text=True, capture_output=True, timeout=50)
            assert result.returncode == 2, result.stderr
            verify_report(output, "live")
            assert process.poll() is None, "External server ownership was lost"
            completed.append({"implementation": "grpc-external", "case": "ok", "result": "passed"})
        finally:
            process.terminate()
            process.wait(timeout=10)
            external_log.close()
    (run / "summary.json").write_text(json.dumps(completed, indent=2) + "\n")
    print(f"{len(completed)} scenarios passed. Evidence: {run}")


if __name__ == "__main__":
    main()
