# E08 — Publish overwrite guard

## Setup
- Existing PDF at the target `outputPath`.

## Call
`forge_plot_publish` with `overwriteAcknowledged=false`

## Expect
- `Ok=false`
- Error code indicates overwrite not acknowledged
- Existing PDF unchanged
