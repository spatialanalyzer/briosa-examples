# Create and read two points with Python client

[Prepare your running SA job](../../docs/setup.md) first.
Start with an empty SA job. This example creates two points, then prints
their coordinates and distance.

## The program

The complete [inspection.py](inspection.py) program is:

```python
import asyncio

from briosa import BriosaClient, BriosaStartOptions, CollectionName, PointName, Vector


async def main():
    # Start with an empty SA job. These names will be created below.
    collection = CollectionName(name="BriosaPythonDemo")
    first = PointName(collection_name=collection.name, group_name="Points", target_name="P1")
    second = PointName(collection_name=collection.name, group_name="Points", target_name="P2")

    briosa = BriosaClient()
    try:
        await briosa.start(BriosaStartOptions(launch_spatial_analyzer=False))

        units = await briosa.get_active_units()
        print(f"Length unit: {units.length}")

        await briosa.construction_operations.construct_collection(collection)
        await briosa.construction_operations.construct_point_in_working_coordinates(first, Vector(0, 0, 0))
        await briosa.construction_operations.construct_point_in_working_coordinates(second, Vector(3, 4, 0))

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

`construction_operations.construct_collection` creates the demo collection.
Two `construct_point_in_working_coordinates` calls create P1 and P2.
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

The distance is **5.000** in SA's current length unit. The program leaves SA
open with both points in its demo collection. To repeat the example, use a fresh
empty job or choose an unused collection name in the source.
If a call fails, the program stops and displays the error; it does not retry.
