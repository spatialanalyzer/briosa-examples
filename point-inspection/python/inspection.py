"""One complete inspection workflow; synthetic mode is the default."""
from __future__ import annotations

import argparse
import asyncio
import csv
import json
import math
from pathlib import Path
import re
import sys
import time
import uuid

METHODS = [
    "/briosa.UtilityOperations/GetActiveUnits",
    "/briosa.UtilityOperations/GetWorkingFrameProperties",
    "/briosa.AnalysisOperations/GetPointCoordinate",
    "/briosa.AnalysisOperations/GetPointToPointDistance",
]
COLUMNS = "kind,id,x_mm,y_mm,z_mm,actual_mm,nominal_mm,deviation_mm,tolerance_mm,status".split(",")


def load_points(path: Path) -> dict[str, tuple[float, float, float, float]]:
    with path.open(newline="", encoding="utf-8") as stream:
        reader = csv.reader(stream)
        if next(reader, None) != ["name", "x_mm", "y_mm", "z_mm", "tolerance_mm"]:
            raise ValueError("Invalid fixture CSV header")
        points = {}
        for row in reader:
            if len(row) != 5 or not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*", row[0]) or row[0] in points:
                raise ValueError("Expected unique simple point names and five CSV columns")
            values = tuple(float(value) for value in row[1:])
            if not all(math.isfinite(value) for value in values) or values[3] < 0:
                raise ValueError("Coordinates must be finite and tolerances nonnegative")
            points[row[0]] = values
        if not points:
            raise ValueError("Empty fixture")
        return points


def validate_scenario(s, points):
    if s["sa_target"] != "2026.1.0529.7" or s["length_unit"] != "millimeters":
        raise ValueError("This example requires the configured SA target and millimeters")
    if any(not isinstance(s[key], str) or not s[key].strip() for key in ("collection", "group", "frame_collection", "frame")):
        raise ValueError("An explicit collection, group and frame are required")
    for span in s["distances"]:
        if span["first"] not in points or span["second"] not in points or any(
            not isinstance(span[key], (float, int)) or not math.isfinite(span[key]) or span[key] < 0
            for key in ("nominal_mm", "tolerance_mm")
        ):
            raise ValueError("Invalid distance check")


def expected_context(s):
    return {key: s[key] for key in ("length_unit", "frame_collection", "frame")}


class SyntheticMeasurements:
    def __init__(self, scenario, measured):
        self.scenario, self.measured = scenario, measured

    async def context(self):
        return expected_context(self.scenario)

    async def point(self, name):
        return self.measured[name][:3]

    async def distance(self, first, second):
        return math.dist(await self.point(first), await self.point(second))


class ClientMeasurements:
    def __init__(self, client, scenario):
        self.client, self.scenario = client, scenario

    def name(self, name):
        from briosa import PointName
        return PointName(collection_name=self.scenario["collection"], group_name=self.scenario["group"], target_name=name)

    async def context(self):
        units = await self.client.get_active_units()
        frame = await self.client.get_working_frame_properties()
        return {"length_unit": units.length, "frame_collection": frame.collection_name, "frame": frame.frame_name}

    async def point(self, name):
        p = await self.client.get_point_coordinate(self.name(name))
        return p.x_value, p.y_value, p.z_value

    async def distance(self, first, second):
        return (await self.client.get_point_to_point_distance(self.name(first), self.name(second))).magnitude


async def inspect(source, scenario, nominals, mode):
    context = await source.context()
    if context != expected_context(scenario):
        raise ValueError("SA units or working frame do not match the fixture")
    rows = []
    for name, nominal in nominals.items():
        actual = await source.point(name)
        deviation = math.dist(actual, nominal[:3])
        if not math.isfinite(deviation):
            raise ValueError("Invalid coordinate result")
        rows.append(dict(zip(COLUMNS, ["point", name, *actual, None, None, deviation, nominal[3],
                                      "PASS" if deviation <= nominal[3] else "FAIL"])))
    for span in scenario["distances"]:
        actual = await source.distance(span["first"], span["second"])
        if not math.isfinite(actual) or actual < 0:
            raise ValueError("Invalid distance result")
        deviation = abs(actual - span["nominal_mm"])
        rows.append(dict(zip(COLUMNS, ["distance", span["first"] + ":" + span["second"], None, None, None,
                                      actual, span["nominal_mm"], deviation, span["tolerance_mm"],
                                      "PASS" if deviation <= span["tolerance_mm"] else "FAIL"])))
    # Context checks detect common changes; they do not create an atomic snapshot.
    if await source.context() != context:
        raise ValueError("SA context changed during inspection")
    return {"schema_version": 1, "source": mode, "sa_target": scenario["sa_target"], "context": context, "rows": rows}


def save(report, directory: Path):
    if directory.exists():
        raise ValueError("Output must be a new directory")
    directory.parent.mkdir(parents=True, exist_ok=True)
    staging = directory.with_name(directory.name + ".partial-" + uuid.uuid4().hex)
    staging.mkdir()
    (staging / "report.json").write_text(json.dumps(report, indent=2, allow_nan=False) + "\n", encoding="utf-8")
    with (staging / "report.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, lineterminator="\n")
        writer.writerow(COLUMNS)
        for row in report["rows"]:
            writer.writerow("" if row[c] is None else f"{row[c]:.6f}" if isinstance(row[c], (float, int)) else row[c] for c in COLUMNS)
    # Only local file publication is retried when a scanner briefly holds it.
    # No SA operation or complete inspection is replayed.
    for attempt in range(5):
        try:
            staging.rename(directory)
            break
        except PermissionError:
            if attempt == 4:
                raise
            time.sleep(0.1)
    print(f"{report['source']} | {report['context']['length_unit']} | frame={report['context']['frame_collection']}/{report['context']['frame']}")
    for row in report["rows"]:
        print(f"{row['id']:<10} {row['status']:<4} deviation={row['deviation_mm']:.6f} mm")
    return 2 if any(row["status"] == "FAIL" for row in report["rows"]) else 0


async def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--live", action="store_true", help="Attach to a prepared running SA job")
    parser.add_argument("--fixture", type=Path, default=Path("point-inspection/fixture"))
    parser.add_argument("--output", type=Path, default=Path("artifacts/python-report"))
    args = parser.parse_args()
    if args.output.exists():
        raise ValueError("Output must be a new directory")
    nominals = load_points(args.fixture / "nominals.csv")
    scenario = json.loads((args.fixture / "scenario.json").read_text(encoding="utf-8"))
    validate_scenario(scenario, nominals)
    if args.live:
        from briosa import BriosaClient, BriosaClientOptions, BriosaStartOptions
        client = BriosaClient(BriosaClientOptions(command_timeout=10))
        try:
            await client.start(BriosaStartOptions(launch_spatial_analyzer=False))
            snapshot = await client.get_server_snapshot()
            if not snapshot.ready_for_mp or not all(snapshot.supports(method) for method in METHODS):
                raise ValueError("Required operation unavailable or SA not ready")
            report = await inspect(ClientMeasurements(client, scenario), scenario, nominals, "live")
        finally:
            # Stops the owned server/SDK, leaving the prepared SA job open.
            await client.aclose()
    else:
        source = SyntheticMeasurements(scenario, load_points(args.fixture / "measured.csv"))
        report = await inspect(source, scenario, nominals, "synthetic")
    return save(report, args.output)


if __name__ == "__main__":
    try:
        sys.exit(asyncio.run(main()))
    except (Exception, KeyboardInterrupt) as error:
        failure = getattr(error, "failure", None)
        if failure is not None:
            print(f"Operation failed: {failure.kind}; execution={failure.execution_disposition}; "
                  f"recovery={failure.recovery_guidance}; replay={failure.replay_guidance}; safety={failure.replay_safety}", file=sys.stderr)
        else:
            print(f"Inspection stopped ({type(error).__name__}). {error}", file=sys.stderr)
        print("No report produced. If a call was in flight, completion may be unknown; do not automatically retry.", file=sys.stderr)
        sys.exit(1)
