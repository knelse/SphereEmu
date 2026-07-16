# CC nav bake checkpoint (`cc-nav-2026-07-16`)

Machine-readable source of truth: [`nav_bake_checkpoint_cc.json`](nav_bake_checkpoint_cc.json).

Use this when rebaking or tuning CC tiles one-by-one so results stay comparable to the golden set.

## Golden output

- GLBs: `D:/1/2026-7-16 23-21-21 cc-region-verify/`
- Colorized regions: `D:/1/2026-7-16 23-21-21 cc-region-verify/regions/`
- Plan: `.../bulk_nav_bake_plan.json` (also embedded as `groups[]` in the JSON checkpoint)

## Shared param profile: `cc_baseline_v1`

All 38 CC groups use the **same** env profile today. Per-tile overrides go in `groups[].env_overrides` later.

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
4. **Outer curtain-arch** skip (no portal strip/protect; solid carve)

Tuning params without this code will not match golden topology.

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
