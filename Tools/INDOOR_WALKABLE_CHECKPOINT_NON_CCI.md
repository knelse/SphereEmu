# Indoor walkable checkpoint — non-cci (`indoor-walkable-non-cci-2026-07-18-pre-keyfix`)

Machine-readable: [`indoor_walkable_checkpoint_non_cci.json`](indoor_walkable_checkpoint_non_cci.json).

**Scope:** non-`cci*` clusters (`lb*`, `rd_*`, `*_in`, …).  
**Golden:** `D:/1/2026-7-18_18-47-51_indoor-clusters` (first 30 clusters).

Freeze these settings when rebaking labyrinth / non-cci indoor walkables. Walkable constants match the shared indoor pipeline; this checkpoint records the **pre–cci key-fix** golden set so non-cci results stay comparable.

## Pipeline

1. Closed outer shell exclude  
2. Inward / wing / covered-upper keep (slope ≤ 80°)  
3. Same-floor weld (`WELD_XZ` 0.45–3.5 m)  
4. Always-on simple-room strips (`Tools/simple_room_walk_strips.json`)

## Bulk

Auto policy: non-`cci*` labels use the latest lb* pipeline (shell/weld/strips). Mixed exports route per label.

```powershell
.\Tools\export_all_indoor_clusters.ps1 -MaxClusters 30 -SkipManifestRebuild -Jobs 6
```

### Production NavigationMesh

```powershell
.\Tools\export_all_indoor_clusters.ps1 -WriteNavRes -SkipPreviewGlb -SkipManifestRebuild -Jobs 6
```

Writes `Godot/Terrain/GeneratedIndoorNavMeshes/` (auto `cci*`→pre-lb, else lb* latest). Full outdoor+indoor bake: editor **Bake terrain navigation**.

See JSON for full constants and verify notes. For **cci*** clusters use [`INDOOR_WALKABLE_CHECKPOINT_CCI.md`](INDOOR_WALKABLE_CHECKPOINT_CCI.md).
