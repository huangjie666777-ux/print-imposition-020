# PrintPlanner

Mixed-size label imposition planner for print shops. Packs label instances onto
sheets to reduce paper usage and reports exact cut positions.

## Requirements

- .NET SDK 8.0.421 (see global.json)

## Run

```bash
dotnet run --project src/PrintPlanner.Api
# or
dotnet run --project src/PrintPlanner.Api --urls http://localhost:5080
```

Endpoints:

- `GET /healthz` — health check, returns `{"status":"ok"}`.
- `POST /api/plan` — JSON layout plan.
- `POST /api/plan/svg` — same plan rendered as an SVG preview
  (cut box in blue, bleed box in amber, margins dashed).

## Request format

All dimensions are millimetres and may have decimals. Quantities are positive
integers.

```json
{
  "sheet": {
    "width": 450,
    "height": 320,
    "margins": { "top": 10, "right": 10, "bottom": 10, "left": 10 },
    "spacing": 3,
    "bleed": 2,
    "maxSheets": 5
  },
  "labels": [
    { "id": "SKU-001", "width": 80, "height": 50, "quantity": 6, "allowRotate": true },
    { "id": "SKU-002", "width": 40.5, "height": 30, "quantity": 10, "allowRotate": false }
  ]
}
```

- `margins`: non-printable留白 on each side of the sheet.
- `spacing`: minimum gap kept between any two bleed-expanded boxes
  (horizontally or vertically); no extra gap is added at the sheet edge.
- `bleed`: each cut box is expanded by this amount on all four sides; the
  expanded box must lie fully inside the margins.
- `maxSheets`: upper bound on sheets that may be opened.

Invalid input (non-positive sizes, negative margins/spacing/bleed, duplicate
label ids, `maxSheets < 1`, margins that consume the whole sheet, malformed
JSON) is rejected with `400` and an `errors` list naming each field; no
partial plan is produced.

## Response

```json
{
  "sheets": [
    {
      "index": 1,
      "placements": [
        { "labelId": "SKU-001", "sequence": 1, "x": 12, "y": 12,
          "width": 80, "height": 50, "rotated": false }
      ]
    }
  ],
  "unplaced": [
    { "labelId": "SKU-002", "sequence": 9, "reason": "Sheet limit reached; ..." }
  ],
  "sheetsUsed": 1
}
```

`x`/`y`/`width`/`height` describe the cut box in sheet coordinates
(origin at the sheet's top-left corner). The SVG preview uses the same
coordinates. Instances that cannot fit a single empty sheet and instances
dropped because `maxSheets` was reached are listed in `unplaced` with
distinct reasons; every requested instance is accounted for exactly once.

## Packing approach

Deterministic shelf packing, not a global optimizer:

1. Instances are expanded into cut + bleed boxes and sorted by largest
   dimension (ties keep request order).
2. Each instance is placed on the first already-open sheet with room —
   first fitting shelf, otherwise a new shelf on that sheet — before a new
   sheet is opened, up to `maxSheets`.
3. Both orientations are tried when `allowRotate` is set; rotation swaps
   the cut and bleed boxes together.
4. All geometry uses exact `decimal` arithmetic; nothing is rounded to hide
   overflows or overlaps. The same request always yields the same layout
   (no randomness, no hash-order dependence).

Trade-off: shelves are simple and fast and reuse open sheets first, but can
waste space versus a full 2D bin-packing solver.

## Tests

```bash
dotnet test
```
