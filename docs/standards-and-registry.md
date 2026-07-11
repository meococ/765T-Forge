# Standards packs & drawing registry

## Drawing registry

JSON schema (camelCase):

```json
{
  "projectId": "PROJECT",
  "sheets": [
    {
      "drawingNo": "MTR-DEMO-A101",
      "rev": "A",
      "layout": "A101",
      "title": "...",
      "titleAttrs": { "DWG_NO": "MTR-DEMO-A101" }
    }
  ]
}
```

Tools: `forge_registry_load`, `forge_registry_lookup`.

When loaded, writes to tags `DWG_NO` / `DWG_NO`-like (`DRAWING_NO`, `SHEET_NO`, `SO_HIEU`) **fail closed** if the value is not in the registry (`deny_unknown_drawing_no`).

## Standards pack

Declarative project rules (layers, forbidden layers, drawingNo regex, required titleblock tags). See [`packs/demo-metro/standards.pack.json`](../packs/demo-metro/standards.pack.json).

Tools: `forge_pack_load`, `forge_pack_status`. Preflight merges pack findings.
