"""Generate Tools/nav_bake_checkpoint_cc.json from the golden bulk plan."""

from __future__ import annotations

import json
from pathlib import Path

PLAN = Path(r"D:/1/2026-7-16 23-21-21 cc-region-verify/bulk_nav_bake_plan.json")
OUT = Path(__file__).resolve().parent / "nav_bake_checkpoint_cc.json"

plan = json.loads(PLAN.read_text(encoding="utf-8"))

status = {}
for g in plan:
    status[g["name"]] = "split_rim_deck"

# Overrides from rim&deck audit (2026-07-16).
status["Cc_2_00_00"] = "merged_rim_deck"
status["Cc_3_hr_occ01"] = "merged_rim_deck"
status["Cc_ph01_occ00"] = "merged_rim_deck"
status["Cc_ph02_occ00"] = "merged_rim_deck"
status["Cc_rd01_occ00"] = "merged_rim_deck"
status["Cc_rd02_occ00"] = "merged_rim_deck"
for n in ("Cc_2_00_03", "Cc_2_00_04", "Cc_2_00_06", "Cc_2_00_08"):
    status[n] = "needs_review_rim_empty_or_sparse"

golden_out = "D:/1/2026-7-16 23-21-21 cc-region-verify"
golden_regions = f"{golden_out}/regions"

checkpoint = {
    "id": "cc-nav-2026-07-16",
    "created": "2026-07-16",
    "purpose": (
        "Golden CC nav bake params + per-group tile lists for verifiable rebakes "
        "while tuning 1-by-1."
    ),
    "golden_out": golden_out,
    "golden_regions": golden_regions,
    "bake_script": "Tools/bake_and_export_single_nav.gd",
    "bulk_script": "Tools/bake_bulk_nav_glbs.ps1",
    "plan_filter": "cc",
    "godot_note": "Resolve via Tools/GodotPath.ps1; prefer *_win64_console.exe",
    "explicit_env": {
        "NAV_EXPERIMENT_ADDFACES": "2",
        "NAV_EXPERIMENT_PRUNE_ISLANDS": "1",
        "NAV_EXPERIMENT_AUTHORED_Y": "1",
        "NAV_EXPERIMENT_REGION_MIN": "14",
        "NAV_EXPERIMENT_SLOPE_DEG": "55",
        "NAV_EXPERIMENT_BUILDING_FILL": "1",
        "NAV_EXPERIMENT_FILL_INCLUDE": "Town_ph00",
    },
    "must_be_unset_or_default": {
        "NAV_EXPERIMENT_WELD": "unset (enabled; set 0 only for debug)",
        "NAV_EXPERIMENT_ARCH_PORTAL": "unset (enabled; !=0)",
        "NAV_EXPERIMENT_GATE_SEAM": "unset (enabled; !=0)",
        "NAV_EXPERIMENT_CASTLE_TERRAIN": "unset (enabled; !=0)",
        "NAV_EXPERIMENT_SPILL": "unset (enabled; !=0)",
        "NAV_EXPERIMENT_XTILE_REACH": "unset/off",
        "DIAG_ALWAYS_CARVE": "unset for golden (1 only for logs)",
    },
    "implicit_defaults_used": {
        "NAV_EXPERIMENT_BRIDGE_GAP": "0.5",
        "NAV_EXPERIMENT_BRIDGE_CLIMB": "BRIDGE_MAX_CLIMB const",
        "NAV_EXPERIMENT_REGION_MERGE": "20.0",
        "NAV_EXPERIMENT_GROUND_CLEARANCE": "2.0",
        "NAV_EXPERIMENT_ARCH_PORTAL_PAD": "2.5",
        "NAV_EXPERIMENT_CASTLE_KEEP_SQM": "80.0",
        "NAV_EXPERIMENT_CASTLE_COURTYARD_CLEARANCE": "10.0",
        "NAV_EXPERIMENT_CASTLE_ROOF_INSET": "12.0",
        "NAV_EXPERIMENT_CASTLE_ROOF_INSET_FRAC": "0.22",
        "NAV_EXPERIMENT_CASTLE_RAMPART_LO": "2.0",
        "NAV_EXPERIMENT_CASTLE_RAMPART_HI": "14.0",
        "NAV_EXPERIMENT_CASTLE_BRIDGE_GAP": "4.0",
        "NAV_EXPERIMENT_CASTLE_BRIDGE_CLIMB": "3.5",
        "NAV_EXPERIMENT_CASTLE_CIRCUIT_GAP": "5.0",
        "NAV_EXPERIMENT_CASTLE_CIRCUIT_CLIMB": "1.5",
        "NAV_EXPERIMENT_CASTLE_DECK_BAND": "2.0",
        "NAV_EXPERIMENT_GATE_PAIR_DIST": "3.5",
        "NAV_EXPERIMENT_GATE_SEAM_DEPTH": "3.0",
        "NAV_EXPERIMENT_GATE_SEAM_WIDTH_PAD": "1.75",
        "NAV_EXPERIMENT_GATE_SEAM_HEIGHT": "3.5",
        "ALWAYS_CARVE_dilate_m": "0.75 (non-preserve_openings)",
        "ALWAYS_CARVE_seal_m": "2.25 (non-preserve_openings)",
    },
    "code_shape_dependencies": [
        "Outdoor/bailey weld lineage veto (tile_edge_roots vs seal_courtyard_roots + UF)",
        "Court-envelope weld veto (_weld_crosses_court_envelope)",
        "Gate-seam carve + weld segment veto (gate_seam_aabbs)",
        "Outer curtain-arch detection (skip portal strip/protect; no preserve_openings)",
        "Portal-forced stitches only for non-outer arches; lineage/gate veto apply",
    ],
    "verify": {
        "split_test": (
            "rim near y_p20 must not share a mesh component with core deck faces "
            "at y >= y_p20+6"
        ),
        "compare_golden": "Diff new GLB against golden_glb / golden_regions_glb for shape",
    },
    "rebake_one_group": {
        "clear_env": "Get-Item Env:NAV_EXPERIMENT_* -EA SilentlyContinue | Remove-Item",
        "set_explicit_env": "Apply explicit_env (+ optional groups[].env_overrides)",
        "1x1_cmd": (
            "godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- "
            "--tile <TILE> --out <OUT>"
        ),
        "2x2_cmd": (
            "godot --path . --headless -s Tools/bake_and_export_single_nav.gd -- "
            "--tile T00 --tile T10 --tile T01 --tile T11 "
            "--combined --combined-name <NAME> --out <OUT>"
        ),
        "bulk_cmd": (
            ".\\Tools\\bake_bulk_nav_glbs.ps1 -Out <OUT> -Filter cc "
            "-Jobs 8 -ColorizeRegions"
        ),
    },
    "param_profile": "cc_baseline_v1",
    "note": (
        "All CC groups share ONE param profile (cc_baseline_v1). "
        "When tuning 1-by-1, put overrides in groups[].env_overrides and bump "
        "that group's param_profile name."
    ),
    "groups": [],
}

for g in plan:
    name = g["name"]
    checkpoint["groups"].append(
        {
            "name": name,
            "kind": g["kind"],
            "family": g["family"],
            "occ": g["occ"],
            "tiles": g["tiles"],
            "map_gx": g.get("gx"),
            "map_gz": g.get("gz"),
            "param_profile": "cc_baseline_v1",
            "env_overrides": {},
            "golden_glb": f"{golden_out}/{name}.glb",
            "golden_regions_glb": f"{golden_regions}/{name}_regions.glb",
            "status_rim_deck": status.get(name, "unknown"),
        }
    )

OUT.write_text(json.dumps(checkpoint, indent=2) + "\n", encoding="utf-8")
counts = {}
for g in checkpoint["groups"]:
    counts[g["status_rim_deck"]] = counts.get(g["status_rim_deck"], 0) + 1
print(f"wrote {OUT} groups={len(checkpoint['groups'])} status={counts}")
