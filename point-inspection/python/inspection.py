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
