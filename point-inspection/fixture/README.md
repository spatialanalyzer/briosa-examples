# Synthetic inspection fixture

All coordinates in this folder were created for this example. No customer or
vendor job data is included. The points represent a flat 100 x 50 mm fixture.
`nominals.csv` is the inspection plan; `measured.csv` supplies deliberately
imperfect coordinates. P3 and P8 fail the 0.2 mm 3D deviation threshold. P6 is
exactly on the threshold and passes. Both distance checks pass.

The report contains ten checks: eight pass and two fail. Exit code **2** means
inspection completed with out-of-tolerance checks, not a communication failure.
`expected.csv` is the independently specified output, rounded to six decimal
places. Comparisons use full precision before formatting.

## Prepare a live SA job

This procedure is provided for manual validation and has **not yet been run
against licensed SA** for this example.

1. Use a scratch job in SpatialAnalyzer **2026.1.0529.7**.
2. Select millimeters as the length unit.
3. Create a collection called `BriosaDemo`, a point group called `Measured`,
   and an identity frame called `InspectionFrame` in that collection.
4. Set `BriosaDemo/InspectionFrame` as the working frame.
5. Import the first four columns of `measured.csv` into `Measured`, mapping
   them to point name, X, Y, Z and skipping the header. The tolerance column
   is example metadata; do not import it as a coordinate.
6. Check all eight point names and coordinates, and save your scratch job.

Use SA's import dialog for the format and column mapping supported by your
installed release. The examples attach to the running job and read it; they
do not import, replace, save, or close it.

The configured units literal is `millimeters`. If your licensed run returns a
different literal, record it and review the mapping rather than silently
assuming the same scale. All coordinates must be expressed in the same frame
as the nominal CSV. A matching frame name alone cannot prove its transform:
the operator must verify the identity-frame setup.

Run one example at a time. Leave the job, point data, units, and frame unchanged
until the report completes. Before/after context reads detect some changes;
they do not isolate a multi-call snapshot or detect every edit.

## Customize

Copy the folder, edit the CSV files and `scenario.json`, then pass `--fixture`
with your new folder. Names in these teaching CSV files are deliberately
restricted to a letter followed by letters, numbers, or underscores. A general
CSV importer and broader SA name support are outside this example's scope.

`tolerance_mm` is a Euclidean deviation threshold, inclusive at its boundary.
It is not a GD&T tolerance zone or an uncertainty-aware acceptance rule.
Distance checks compare the absolute difference between SA's returned distance
and the configured nominal length. Missing points or failed calls stop the
inspection; they are never recorded as zero or PASS.
