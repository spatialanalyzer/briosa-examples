# Inspection report contract

This is the example application's report format. It does not define Briosa's
protocol or client behavior. All four implementations use schema version **1**.

Each completed inspection creates a new directory containing `report.json` and
`report.csv`. Existing output is refused. A failed inspection does not publish
that directory; a filesystem failure can leave a `.partial-*` staging directory.

## JSON

The top-level fields are:

| Field | Meaning |
| --- | --- |
| `schema_version` | Integer `1` |
| `source` | `synthetic` for fixture measurements or `live` for gRPC/client calls; this is an execution mode, not validation evidence |
| `sa_target` | Exact configured SA release, initially `2026.1.0529.7` |
| `context` | `length_unit`, `frame_collection`, and `frame` |
| `rows` | Point rows in nominal CSV order, then distance rows in scenario order |

Every row has the same fields as the CSV header below. Coordinates, distances,
deviations, and tolerances are in millimeters. Inapplicable values are JSON `null`.
JSON preserves numeric precision; it never writes NaN or infinity.

## CSV and acceptance rules

```text
kind,id,x_mm,y_mm,z_mm,actual_mm,nominal_mm,deviation_mm,tolerance_mm,status
```

- A `point` row uses the point name as `id`, records measured X/Y/Z, and leaves
  `actual_mm` and `nominal_mm` empty. Its deviation is the Euclidean distance
  from the nominal coordinates in the fixture CSV.
- A `distance` row uses `first:second` as `id` and leaves X/Y/Z empty. Live mode
  obtains `actual_mm` from SA's point-to-point distance operation; `nominal_mm`
  comes from the scenario. Deviation is their absolute difference.
- `status` is `PASS` when deviation is at most the nonnegative tolerance, or
  `FAIL` otherwise. The comparison uses full precision before formatting.
- CSV numeric fields use a decimal point and six decimal places. Empty fields
  correspond to JSON `null`. Neither format substitutes zero for missing data.

The fixture deliberately produces ten checks: eight pass and two fail. See
[`expected.csv`](../point-inspection/fixture/expected.csv). This teaching rule is
not a GD&T or measurement-uncertainty acceptance model.

Exit code **0** means a complete report with all checks passing, **2** means a
complete report with failed checks, and **1** means the inspection did not
complete. Before/after unit and frame checks detect some context changes; the
report is not an atomic snapshot or proof that the job was unchanged throughout.
