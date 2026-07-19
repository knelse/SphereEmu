# Indoor walkable checkpoint — cci (`indoor-walkable-cci-2026-07-18-pre-lb`)

Machine-readable: [`indoor_walkable_checkpoint_cci.json`](indoor_walkable_checkpoint_cci.json).

**Scope:** `cci*` dungeon clusters (pre-lb* restore).  
**Golden:** `D:/1/2026-7-18_21-57-38_indoor-cci-pre-lb` (42 clusters).

## Pipeline (pre-lb*)

- Upward faces, slope ≤ 70° (flip downward windings)
- **No** outer-shell carve, weld, or simple-room strips
- Still uses **float64** membership keys (so kits are not dropped)

## Bulk

Auto policy: labels starting with `cci` get `--pre-lb-walkable` (no force switch needed).

```powershell
.\Tools\export_all_indoor_clusters.ps1 -LabelPrefix cci -SkipManifestRebuild -Jobs 6
```

### Production NavigationMesh

```powershell
.\Tools\export_all_indoor_clusters.ps1 -LabelPrefix cci -WriteNavRes -SkipPreviewGlb -SkipManifestRebuild -Jobs 6
```

Writes `Godot/Terrain/GeneratedIndoorNavMeshes/cluster_*.res` + `index.json` (loaded by `TerrainNavMeshRuntime` alongside outdoor tiles). Full outdoor+indoor bake: editor **Bake terrain navigation** (`TerrainNavigationBaker`, `BakeIndoorNav=true`).

Do not apply the lb* shell/weld/strips path to cci without bumping this profile (`-ForceLbPipeline` only for intentional experiments).
