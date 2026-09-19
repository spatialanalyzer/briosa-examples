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
