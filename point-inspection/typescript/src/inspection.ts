import { readFile, mkdir, writeFile, rename, access } from 'node:fs/promises';
import { join, dirname } from 'node:path';
import { randomUUID } from 'node:crypto';
import { setTimeout as delay } from 'node:timers/promises';
import {
  createBriosaClient, discoverInstallations, getActiveUnits, getWorkingFrameProperties,
  getPointCoordinate, getPointToPointDistance, BriosaOperationError,
  type BriosaClient, type BriosaServerSelection,
} from 'briosa';

type XYZ = [number, number, number];
type Point = { xyz: XYZ; tolerance: number };
type Context = { length_unit: string; frame_collection: string; frame: string };
type Scenario = Context & {sa_target: string; collection: string; group: string;
  distances: {first: string; second: string; nominal_mm: number; tolerance_mm: number}[]};
type Row = {kind: string; id: string; x_mm: number | null; y_mm: number | null; z_mm: number | null;
  actual_mm: number | null; nominal_mm: number | null; deviation_mm: number; tolerance_mm: number; status: string};
type Report = {schema_version: number; source: string; sa_target: string; context: Context; rows: Row[]};
interface Measurements {
  context(): Promise<Context>;
  point(name: string): Promise<XYZ>;
  distance(first: string, second: string): Promise<number>;
}
const methods = ['/briosa.UtilityOperations/GetActiveUnits', '/briosa.UtilityOperations/GetWorkingFrameProperties',
  '/briosa.AnalysisOperations/GetPointCoordinate', '/briosa.AnalysisOperations/GetPointToPointDistance'];
const columns: (keyof Row)[] = ['kind', 'id', 'x_mm', 'y_mm', 'z_mm', 'actual_mm', 'nominal_mm', 'deviation_mm', 'tolerance_mm', 'status'];

async function loadPoints(path: string): Promise<Map<string, Point>> {
  const lines = (await readFile(path, 'utf8')).trimEnd().split(/\r?\n/);
  if (lines.shift() !== 'name,x_mm,y_mm,z_mm,tolerance_mm' || !lines.length) throw new Error('Invalid fixture CSV header or empty fixture');
  const points = new Map<string, Point>();
  for (const line of lines) {
    const fields = line.split(',');
    if (fields.length !== 5 || !/^[A-Za-z][A-Za-z0-9_]*$/.test(fields[0]) || points.has(fields[0]))
      throw new Error('Expected unique simple point names and five CSV columns');
    const values = fields.slice(1).map(v => v.trim() === '' ? NaN : Number(v));
    if (values.some(v => !Number.isFinite(v)) || values[3] < 0) throw new Error('Coordinates must be finite and tolerances nonnegative');
    points.set(fields[0], {xyz: [values[0], values[1], values[2]], tolerance: values[3]});
  }
  return points;
}

function validate(s: Scenario, points: Map<string, Point>) {
  if (s.sa_target !== '2026.1.0529.7' || s.length_unit !== 'millimeters' ||
      [s.collection, s.group, s.frame_collection, s.frame].some(v => typeof v !== 'string' || !v.trim()) || !Array.isArray(s.distances))
    throw new Error('This example requires the configured SA target, millimeters and an explicit frame');
  for (const span of s.distances) {
    if (!points.has(span.first) || !points.has(span.second) ||
        [span.nominal_mm, span.tolerance_mm].some(v => !Number.isFinite(v) || v < 0)) throw new Error('Invalid distance check');
  }
}

function expectedContext(s: Scenario): Context {
  return {length_unit: s.length_unit, frame_collection: s.frame_collection, frame: s.frame};
}
function checkContext(actual: Context, s: Scenario) {
  if (actual.length_unit !== s.length_unit || actual.frame_collection !== s.frame_collection || actual.frame !== s.frame)
    throw new Error('SA units or working frame do not match the fixture');
}

class SyntheticMeasurements implements Measurements {
  constructor(private s: Scenario, private points: Map<string, Point>) {}
  async context() { return expectedContext(this.s); }
  async point(name: string): Promise<XYZ> {
    const p = this.points.get(name);
    if (!p) throw new Error('Missing synthetic point');
    return p.xyz;
  }
  async distance(first: string, second: string) {
    const a = await this.point(first), b = await this.point(second);
    return Math.hypot(...a.map((v, i) => v - b[i]));
  }
}

class ClientMeasurements implements Measurements {
  constructor(private client: BriosaClient, private s: Scenario, private signal: AbortSignal) {}
  private name(name: string) { return {collectionName: this.s.collection, groupName: this.s.group, targetName: name}; }
  async context(): Promise<Context> {
    const units = await getActiveUnits(this.client, {signal: this.signal});
    const frame = await getWorkingFrameProperties(this.client, {signal: this.signal});
    return {length_unit: units.length, frame_collection: frame.collectionName, frame: frame.frameName};
  }
  async point(name: string): Promise<XYZ> {
    const p = await getPointCoordinate(this.client, {pointName: this.name(name)}, {signal: this.signal});
    return [p.xValue, p.yValue, p.zValue];
  }
  async distance(first: string, second: string) {
    return (await getPointToPointDistance(this.client, {firstPoint: this.name(first), secondPoint: this.name(second)}, {signal: this.signal})).magnitude;
  }
}

async function inspect(source: Measurements, s: Scenario, nominals: Map<string, Point>, mode: string): Promise<Report> {
  const context = await source.context();
  checkContext(context, s);
  const rows: Row[] = [];
  for (const [name, nominal] of nominals) {
    const actual = await source.point(name);
    const deviation = Math.hypot(...actual.map((v, i) => v - nominal.xyz[i]));
    if (!Number.isFinite(deviation)) throw new Error('Invalid coordinate result');
    rows.push({kind: 'point', id: name, x_mm: actual[0], y_mm: actual[1], z_mm: actual[2], actual_mm: null,
      nominal_mm: null, deviation_mm: deviation, tolerance_mm: nominal.tolerance, status: deviation <= nominal.tolerance ? 'PASS' : 'FAIL'});
  }
  for (const span of s.distances) {
    // The live source asks SA to calculate the distance.
    const actual = await source.distance(span.first, span.second);
    if (!Number.isFinite(actual) || actual < 0) throw new Error('Invalid distance result');
    const deviation = Math.abs(actual - span.nominal_mm);
    rows.push({kind: 'distance', id: span.first + ':' + span.second, x_mm: null, y_mm: null, z_mm: null,
      actual_mm: actual, nominal_mm: span.nominal_mm, deviation_mm: deviation, tolerance_mm: span.tolerance_mm,
      status: deviation <= span.tolerance_mm ? 'PASS' : 'FAIL'});
  }
  checkContext(await source.context(), s);
  return {schema_version: 1, source: mode, sa_target: s.sa_target, context, rows};
}

async function save(report: Report, directory: string) {
  try { await access(directory); throw new Error('Output must be a new directory'); }
  catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
  await mkdir(dirname(directory), {recursive: true});
  const staging = directory + '.partial-' + randomUUID();
  await mkdir(staging);
  await writeFile(join(staging, 'report.json'), JSON.stringify(report, null, 2) + '\n');
  const lines = [columns.join(','), ...report.rows.map(row => columns.map(c =>
    row[c] === null ? '' : typeof row[c] === 'number' ? (row[c] as number).toFixed(6) : row[c]).join(','))];
  await writeFile(join(staging, 'report.csv'), lines.join('\n') + '\n');
  // Windows scanners can briefly hold a just-written file open. Only this local
  // filesystem operation is retried; no SA call or inspection is ever replayed.
  for (let attempt = 0; ; attempt++) {
    try { await rename(staging, directory); break; }
    catch (error) {
      if (attempt >= 4 || !['EPERM', 'EBUSY', 'EACCES'].includes((error as NodeJS.ErrnoException).code ?? '')) throw error;
      await delay(100);
    }
  }
  console.log(`${report.source} | ${report.context.length_unit} | frame=${report.context.frame_collection}/${report.context.frame}`);
  for (const row of report.rows) console.log(`${row.id.padEnd(10)} ${row.status.padEnd(4)} deviation=${row.deviation_mm.toFixed(6)} mm`);
  return report.rows.some(r => r.status === 'FAIL') ? 2 : 0;
}

async function main() {
  const args = process.argv.slice(2), options = new Map<string, string>();
  for (let i = 0; i < args.length; i++) {
    if (options.has(args[i])) throw new Error('Duplicate argument');
    if (['--live', '--discover', '--allow-prerelease'].includes(args[i])) options.set(args[i], 'true');
    else if (['--fixture', '--output', '--server-path', '--installation-id', '--server-version', '--search-root', '--sa-path'].includes(args[i]) && i + 1 < args.length)
      options.set(args[i], args[++i]);
    else throw new Error('Use --live, --discover, --fixture, --output, --server-path, --installation-id, --server-version, --search-root, --sa-path, --allow-prerelease');
  }
  const serverSelection: BriosaServerSelection = {
    ...(options.has('--server-path') ? {executablePath: options.get('--server-path')!} : {}),
    ...(options.has('--installation-id') ? {installationId: options.get('--installation-id')!} : {}),
    ...(options.has('--server-version') ? {version: options.get('--server-version')!} : {}),
    ...(options.has('--search-root') ? {searchRoots: [options.get('--search-root')!]} : {}),
    ...(options.has('--sa-path') ? {spatialAnalyzerExecutablePath: options.get('--sa-path')!} : {}),
    allowPrerelease: options.has('--allow-prerelease'),
  };
  if (options.has('--discover')) {
    const report = await discoverInstallations(serverSelection);
    console.log(JSON.stringify(report, null, 2));
    return report.selected ? 0 : 1;
  }
  const fixture = options.get('--fixture') ?? 'point-inspection/fixture';
  const output = options.get('--output') ?? 'artifacts/typescript-report';
  try { await access(output); throw new Error('Output must be a new directory'); }
  catch (error) { if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw error; }
  const nominals = await loadPoints(join(fixture, 'nominals.csv'));
  const scenario: Scenario = JSON.parse(await readFile(join(fixture, 'scenario.json'), 'utf8'));
  validate(scenario, nominals);
  let report: Report;
  if (options.has('--live')) {
    const client = createBriosaClient({commandTimeoutMs: 10000});
    const controller = new AbortController();
    const interrupt = () => controller.abort();
    process.once('SIGINT', interrupt);
    try {
      await client.start({launchSpatialAnalyzer: false, serverSelection, signal: controller.signal});
      const snapshot = await client.getServerSnapshot({signal: controller.signal});
      if (!snapshot.readyForMp || !methods.every(m => snapshot.supports(m))) throw new Error('Required operation unavailable or SA not ready');
      report = await inspect(new ClientMeasurements(client, scenario, controller.signal), scenario, nominals, 'live');
    } finally {
      process.removeListener('SIGINT', interrupt);
      await client[Symbol.asyncDispose]();
    }
  } else report = await inspect(new SyntheticMeasurements(scenario, await loadPoints(join(fixture, 'measured.csv'))), scenario, nominals, 'synthetic');
  return save(report, output);
}

try { process.exitCode = await main(); }
catch (error) {
  if (error instanceof BriosaOperationError) {
    console.error(`Operation failed: ${error.kind}; execution=${error.executionDisposition}; recovery=${error.recoveryGuidance}; replay=${error.replayGuidance}; safety=${error.replaySafety}`);
  } else console.error(`Inspection stopped (${error instanceof Error ? error.name : 'Error'}). ${error instanceof Error ? error.message : ''}`);
  console.error('No report produced. If a call was in flight, completion may be unknown; do not automatically retry.');
  process.exitCode = 1;
}
