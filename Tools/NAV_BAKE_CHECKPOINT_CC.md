# CC nav bake checkpoint (`cc-nav-2026-07-17-r025`)

Machine-readable source of truth: [`nav_bake_checkpoint_cc.json`](nav_bake_checkpoint_cc.json).

Use this when rebaking or tuning CC tiles one-by-one so results stay comparable to the golden set.

## Golden output

- GLBs: `D:/1/2026-7-17 15-22-44 cc-bulk-agent-r025/`
- Colorized regions: `D:/1/2026-7-17 15-22-44 cc-bulk-agent-r025/regions/`
- Verified rebake (38/38 WriteRes, matches golden): `D:/1/2026-7-17 18-09-05 cc-bulk-restore-r025/`
- Prior (pre–0.25 radius): `D:/1/2026-7-16 23-21-21 cc-region-verify/`
- Plan: `.../bulk_nav_bake_plan.json` (also embedded as `groups[]` in the JSON checkpoint)

## Shared param profile: `cc_baseline_v1`

Most CC groups use this env profile. Tuned tiles may bump `groups[].param_profile` and add `env_overrides` / `code_shape`.

### Agent size

| | Value |
|--|--|
| `AGENT_RADIUS` / `AgentRadius` | `0.25` m |
| Effective corridor width | ~`0.5` m (2× radius after Recast erosion) |

Bake script baseline: `Tools/bake_and_export_single_nav.gd` = **git HEAD + `AGENT_RADIUS=0.25`**.  
`Cc_1_00_05` also needs the east-arch code shape below. Do **not** enable castle-wide OUTER→entrance promote (regress `Cc_2_hr_occ00` ramparts).

### Explicit env (must set)

| Var | Value |
|-----|-------|
| `NAV_EXPERIMENT_ADDFACES` | `2` |
| `NAV_EXPERIMENT_PRUNE_ISLANDS` | `1` |
| `NAV_EXPERIMENT_AUTHORED_Y` | `1` |
| `NAV_EXPERIMENT_REGION_MIN` | `14` |
| `NAV_EXPERIMENT_SLOPE_DEG` | `55` |
| `NAV_EXPERIMENT_BUILDING_FILL` | `1` |
| `NAV_EXPERIMENT_FILL_INCLUDE` | `Town_ph00` |

### Must be unset (defaults ON)

Clear leftover experiment vars before rebake:

```powershell
Get-Item Env:NAV_EXPERIMENT_* -ErrorAction SilentlyContinue | Remove-Item
```

Then set only the explicit table above. Especially do **not** leave these forced off unless debugging:

- `NAV_EXPERIMENT_WELD=0`
- `NAV_EXPERIMENT_ARCH_PORTAL=0`
- `NAV_EXPERIMENT_GATE_SEAM=0`
- `NAV_EXPERIMENT_CASTLE_TERRAIN=0`

### Implicit defaults (unset → code defaults)

See `implicit_defaults_used` in the JSON (castle keep/bridge/circuit, gate seam depth/pad, arch portal pad, ALWAYS_CARVE dilate/seal, etc.).

## Code shape (not env) — required for same topology

Shape also depends on current `bake_and_export_single_nav.gd` behavior:

1. Outdoor/bailey **weld lineage veto**
2. **Court-envelope** weld veto
3. **Gate-seam** carve + weld veto
4. **Outer curtain-arch** skip (no portal strip/protect; solid carve) — except entrance kits on tuned tiles
5. **`AGENT_RADIUS=0.25`**

Tuning params without this code will not match golden topology.

## Per-tile accepted profiles

### `Cc_1_00_05` → `cc_1_00_05_east_arch_v1`

- **Env:** same as `cc_baseline_v1` (no `env_overrides`).
- **Accepted out:** `D:/1/2026-7-17 16-27-36 cc105-reg4/` (8 regions; matches golden `15-22-44 cc-bulk-agent-r025`).
- **Prior:** `D:/1/2026-7-17 15-14-37 cc105-tight-corridor/`.
- **Issue fixed:** east arch incomplete land carve under `cc22`; keep a **0.5 m** ground corridor via `cc95` without a protect-cell walkable carpet.
- **Code shape (do not regress):** see `groups[Cc_1_00_05].code_shape` in the JSON — entrance-arch portal (`cc95`), pier-wing ALWAYS_CARVE (`cc22`), strip-only walkables, `AGENT_RADIUS=0.25`.

### `Cc_1_00_06` → `cc_1_00_06_keep_arch_v1`

- **Env:** `cc_baseline_v1` plus:
  - `NAV_EXPERIMENT_ENTRANCE_ARCH_KITS=cc22`
  - `NAV_EXPERIMENT_ENTRANCE_CORRIDOR_WIDTH=2.5`
  - `NAV_EXPERIMENT_ENTRANCE_BRIDGE_LIFT=2.5`
  - `NAV_EXPERIMENT_ARCH_PORTAL_PAD=3.0`
- **Accepted out:** `D:/1/2026-7-17 18-20-40 cc106-reg4/` (`Cc_1_00_06.glb` + `regions/`).
- **Issue fixed:** keep arch (`cc22`, no `cc95`) sealed at keep grade; open a thin mouth without wiping pier footprints (wide protect had zeroed ALWAYS_CARVE jobs).
- **Code shape (do not regress):** see `groups[Cc_1_00_06].code_shape` in the JSON — entrance `cc22`, deferred castle wall, thin protect, pier solid outside lateral column, keep-grade bridge lift, strip restamp.

### `Cc_1_00_09` → `cc_1_00_09_undercroft_v1`

- **Env:** `cc_baseline_v1` plus:
  - `NAV_EXPERIMENT_CASTLE_BRIDGE_GAP=6.0`
  - `NAV_EXPERIMENT_ALWAYS_CARVE_MAX_AREA=8`
  - `NAV_EXPERIMENT_ALWAYS_CARVE_DILATE_MAX_AREA=8`
- **Accepted out:** `D:/1/2026-7-17 20-29-24 cc109-undercroft-v2/` (`Cc_1_00_09.glb` + `regions/`).
- **Issue fixed:** keep undercroft sealed by dense `cc08`/`cc37` ALWAYS_CARVE dilate (25 m² default too loose for `cc*` shells vs `hrz*`).
- **Code shape:** same undercroft carve/weld path as `Cc_1_hr_occ02`; tighter area caps only. Outer curtain arches stay OUTER-skipped on this profile.

### `Cc_1_hr_occ02` → `cc_1_hr_occ02_archways_undercroft_v1`

- **Env:** `cc_baseline_v1` plus:
  - `NAV_EXPERIMENT_ENTRANCE_ARCH_KITS=cc17,cc19,cc21,cc22,cc54,hrt_tgate1,hrt_wall1,hrt_wall3,hrz14,hrz16,hrz17,hrz19,hrz21,hrz22,hrz54`
  - `NAV_EXPERIMENT_ENTRANCE_CORRIDOR_WIDTH=2.5`
  - `NAV_EXPERIMENT_ENTRANCE_BRIDGE_LIFT=2.5`
  - `NAV_EXPERIMENT_ARCH_PORTAL_PAD=3.0`
  - `NAV_EXPERIMENT_CASTLE_BRIDGE_GAP=6.0`
- **Accepted out:** `D:/1/2026-7-17 19-25-45 cc1hr02-undercroft-final/` (`Cc_1_hr_occ02.glb` + `regions/`).
- **Issue fixed:** open archway mouths on this 2×2; reduce keep-undercroft sealing from ALWAYS_CARVE dilate/room-fill on large shells (`hrz08`/`hrz23`); stitch deep-bailey grade islands (not portal-only).
- **Do not:** default the full archway list as entrance globally (regress `Cc_2_hr` outer mouths/ramparts).
- **Code shape (do not regress):** see `groups[Cc_1_hr_occ02].code_shape` in the JSON — archway consts, large-kit ALWAYS_CARVE skip, shell peel / small-hole fill, deep-bailey grade welds.

### `cc_archways_v1` (shared archway mouth profile)

Used by: `Cc_1_hr_occ03`, `Cc_2_00_01`, `Cc_2_00_03`, `Cc_2_00_08`, `Cc_2_00_09`, `Cc_ph01_occ00`, `Cc_rd03_occ02`.

- **Env:** `cc_baseline_v1` plus full archway `ENTRANCE_ARCH_KITS` list, corridor `2.5`, bridge lift `2.5`, portal pad `3.0`, `CASTLE_BRIDGE_GAP=6.0` (same kit list as `Cc_1_hr_occ02`).
- **Do not** default that allowlist globally (regress `Cc_2_hr` outer mouths/ramparts).
- **Not used on:** `Cc_2_00_05`, `Cc_2_hr_occ00` — full list set `outer_skipped=0` and fused field↔bailey (1–2 regions). Those stay on `cc_baseline_v1` (OUTER skip): accepted `D:/1/2026-7-17 20-36-35 cc2-iso-baseline/` (`Cc_2_00_05`=5 regions, `Cc_2_hr_occ00`=7).
- **Accepted out (archway groups):** `D:/1/2026-7-17 20-31-24 cc-archway-batch/` (per-group `accepted_*` in JSON).

## Region colorize

`Tools/colorize_nav_regions.py` (used by `-ColorizeRegions`) writes `regions/*_regions.glb` with the bake’s **side-by-side** layout:

- Left: `Ground` + `Objects_*` (terrain + objects)
- Right: colored connected nav components

## Godot editor / project bake

`TerrainNavigationBaker` (“Bake terrain navigation”) no longer runs an in-process Recast carve.
It shells out to:

```powershell
.\Tools\bake_bulk_nav_glbs.ps1 -CheckpointJson Tools/nav_bake_checkpoint_cc.json -WriteRes ...
```

which applies `explicit_env` + per-group `env_overrides` / `param_profile`, then runs
`Tools/bake_and_export_single_nav.gd` with `--bake-only` (production `.res` only; no preview GLBs).
2×2 combined for towns/CC blocks. Optional inspector fields: `BakeOnlyTileGroupKey` (`-Tile`),
`BakeFilter`, `CheckpointJsonPath`, `MaxConcurrentBakeJobs`. Use orchestrator `-ExportGlb` only when
you want side-by-side preview GLBs.

## Rebake one group

```powershell
. .\Tools\GodotPath.ps1
$godot = (Resolve-GodotExecutable) -replace '_win64\.exe$','_win64_console.exe'
if (-not (Test-Path $godot)) { $godot = Resolve-GodotExecutable }

Get-Item Env:NAV_EXPERIMENT_* -ErrorAction SilentlyContinue | Remove-Item
$env:NAV_EXPERIMENT_ADDFACES="2"
$env:NAV_EXPERIMENT_PRUNE_ISLANDS="1"
$env:NAV_EXPERIMENT_AUTHORED_Y="1"
$env:NAV_EXPERIMENT_REGION_MIN="14"
$env:NAV_EXPERIMENT_SLOPE_DEG="55"
$env:NAV_EXPERIMENT_BUILDING_FILL="1"
$env:NAV_EXPERIMENT_FILL_INCLUDE="Town_ph00"

$out = "D:/1/_rebake_check"
New-Item -ItemType Directory -Force -Path $out | Out-Null

# 1x1 example
& $godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- `
  --tile Cc_1_00_03 --out $out

# 2x2 example
& $godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- `
  --tile Cc_2_hr_00_00 --tile Cc_2_hr_10_00 --tile Cc_2_hr_01_00 --tile Cc_2_hr_11_00 `
  --combined --combined-name Cc_2_hr_occ00 --out $out

# Full CC (WriteRes + side-by-side regions)
.\Tools\bake_bulk_nav_glbs.ps1 -Out $out -Filter cc -Jobs 8 -WriteRes -ColorizeRegions
```

Tile lists for every group are in `nav_bake_checkpoint_cc.json` → `groups[].tiles`.

## Status snapshot (rim ↔ elevated deck)

| Status | Groups |
|--------|--------|
| `split_rim_deck` | All `Cc_1_00_*`, all `Cc_1_hr_*`, most `Cc_2_00_*`, all `Cc_2_hr_*`, `Cc_3_hr_occ00`, `Cc_ph00`, all `Cc_rd03_*` |
| `merged_rim_deck` | `Cc_2_00_00`, `Cc_3_hr_occ01`, `Cc_ph01_occ00`, `Cc_ph02_occ00`, `Cc_rd01_occ00`, `Cc_rd02_occ00` |
| `needs_review_rim_empty_or_sparse` | `Cc_2_00_03`, `Cc_2_00_04`, `Cc_2_00_06`, `Cc_2_00_08` |

## Tuning workflow

1. Pick one `groups[].name`.
2. Rebake with `cc_baseline_v1` → confirm matches golden (regions / rim-deck split).
3. Change **one** param via `env_overrides` (or a temporary env var).
4. Rebake to a new out dir; compare to golden.
5. Record keeper overrides back into the JSON under that group + rename `param_profile` if it diverges from baseline.
