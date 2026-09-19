import { createBriosaClient, getActiveUnits, getPointCoordinate, getPointToPointDistance } from 'briosa';

// Use the collection, group, and point names from your open SA job.
const first = { collectionName: 'BriosaDemo', groupName: 'Points', targetName: 'P1' };
const second = { collectionName: 'BriosaDemo', groupName: 'Points', targetName: 'P2' };

await using briosa = createBriosaClient();
await briosa.start({ launchSpatialAnalyzer: false });

const units = await getActiveUnits(briosa);
console.log(`Length unit: ${units.length}`);

for (const point of [first, second]) {
  const coordinates = await getPointCoordinate(briosa, { pointName: point });
  console.log(`${point.targetName}: (${coordinates.xValue.toFixed(3)}, ${coordinates.yValue.toFixed(3)}, ${coordinates.zValue.toFixed(3)})`);
}

const distance = await getPointToPointDistance(briosa, { firstPoint: first, secondPoint: second });
console.log(`Distance: ${distance.magnitude.toFixed(3)} ${units.length}`);
