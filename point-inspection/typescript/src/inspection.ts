import { createBriosaClient, getActiveUnits, getPointCoordinate, getPointToPointDistance } from 'briosa';

// Start with an empty SA job. These names will be created below.
const collection = { name: 'BriosaTypeScriptDemo' };
const first = { collectionName: collection.name, groupName: 'Points', targetName: 'P1' };
const second = { collectionName: collection.name, groupName: 'Points', targetName: 'P2' };

await using briosa = createBriosaClient();
await briosa.start({ launchSpatialAnalyzer: false });

const units = await getActiveUnits(briosa);
console.log(`Length unit: ${units.length}`);

await briosa.constructionOperations.constructCollection({ collectionName: collection });
await briosa.constructionOperations.constructPointInWorkingCoordinates({ pointName: first, workingCoordinates: { x: 0, y: 0, z: 0 } });
await briosa.constructionOperations.constructPointInWorkingCoordinates({ pointName: second, workingCoordinates: { x: 3, y: 4, z: 0 } });

for (const point of [first, second]) {
  const coordinates = await getPointCoordinate(briosa, { pointName: point });
  console.log(`${point.targetName}: (${coordinates.xValue.toFixed(3)}, ${coordinates.yValue.toFixed(3)}, ${coordinates.zValue.toFixed(3)})`);
}

const distance = await getPointToPointDistance(briosa, { firstPoint: first, secondPoint: second });
console.log(`Distance: ${distance.magnitude.toFixed(3)} ${units.length}`);
