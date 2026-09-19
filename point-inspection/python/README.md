# Read two points with Python client

[Prepare your running SA job](../../docs/setup.md) first.
This example prints two point coordinates and their distance.

## The program

Open [inspection.py](inspection.py) and change the collection, group, and point
names to match your job.

```python
import asyncio

from briosa import BriosaClient, BriosaStartOptions, PointName


async def main():
    # Use the collection, group, and point names from your open SA job.
    first = PointName(collection_name="BriosaDemo", group_name="Points", target_name="P1")
    second = PointName(collection_name="BriosaDemo", group_name="Points", target_name="P2")

    briosa = BriosaClient()
    try:
        await briosa.start(BriosaStartOptions(launch_spatial_analyzer=False))

        units = await briosa.get_active_units()
        print(f"Length unit: {units.length}")

        for point in (first, second):
            coordinates = await briosa.get_point_coordinate(point)
            print(f"{point.target_name}: ({coordinates.x_value:.3f}, {coordinates.y_value:.3f}, {coordinates.z_value:.3f})")

        distance = await briosa.get_point_to_point_distance(first, second)
        print(f"Distance: {distance.magnitude:.3f} {units.length}")
    finally:
        await briosa.aclose()


if __name__ == "__main__":
    asyncio.run(main())
```

## How it works

`PointName` identifies a point by collection, group, and name.
`start(BriosaStartOptions(launch_spatial_analyzer=False))` finds a compatible
installed server and connects to the SA application you already opened.

The loop reads the two points. `get_point_to_point_distance` asks SA to calculate
their distance, while `asyncio.run(main())` runs the asynchronous program.

The `finally` block always closes the owned server and SDK, including when a
call fails. SA stays open. An explicit start is used because entering a default
`BriosaClient` context would also request a new SA application.

## Run it

From the repository root, using Python 3.12:

```powershell
python -m venv point-inspection/python/.venv
./point-inspection/python/.venv/Scripts/python -m pip install -r point-inspection/python/requirements.txt
./point-inspection/python/.venv/Scripts/python point-inspection/python/inspection.py
```

For the setup guide's two points, the distance is **5.000 millimeters**.
With your own points, the output reflects their current coordinates and SA units.
If a call fails, the program stops and displays the error. Fix the cause before
running it again.
