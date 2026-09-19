# Create and read two points with TypeScript client

[Prepare your running SA job](../../docs/setup.md) first.
Start with an empty SA job. This example creates two points, then prints
their coordinates and distance.

## The program

The complete [src/inspection.ts](src/inspection.ts) program is:

```ts
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
```

## How it works

The point names are plain JavaScript objects. `createBriosaClient` creates the
client; `start({ launchSpatialAnalyzer: false })` finds a compatible installed
server and connects to the SA application you already opened.

`constructionOperations.constructCollection` creates the demo collection.
Two `constructPointInWorkingCoordinates` calls create P1 and P2.
Each imported read operation takes the client as its first argument. The loop reads
each point, then `getPointToPointDistance` asks SA to calculate their distance.
`toFixed(3)` only formats the console output.

`await using` cleans up the owned server and SDK, even if a call fails. SA stays
open. The client handles compatibility and operation-result checks.

## Run it

From the repository root, using Node.js 24:

```powershell
cd point-inspection/typescript
npm ci
npm run build
node dist/inspection.js
```

The distance is **5.000** in SA's current length unit. The program leaves SA
open with both points in its demo collection. To repeat the example, use a fresh
empty job or choose an unused collection name in the source.
If a call fails, the program stops and displays the error; it does not retry.
