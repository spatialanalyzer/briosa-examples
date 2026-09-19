# Read two points with TypeScript client

[Prepare your running SA job](../../docs/setup.md) first.
This example prints two point coordinates and their distance.

## The program

Open [src/inspection.ts](src/inspection.ts) and change the collection, group, and point
names to match your job.

```ts
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
```

## How it works

The point names are plain JavaScript objects. `createBriosaClient` creates the
client; `start({ launchSpatialAnalyzer: false })` finds a compatible installed
server and connects to the SA application you already opened.

Each imported operation takes the client as its first argument. The loop reads
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

For the setup guide's two points, the distance is **5.000 millimeters**.
With your own points, the output reflects their current coordinates and SA units.
If a call fails, the program stops and displays the error. Fix the cause before
running it again.
