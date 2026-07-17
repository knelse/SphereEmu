"""Rank planned nav bake groups by object placement count (bake cell bucketing)."""

from __future__ import annotations

import json
import math
import sys
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MAP = ROOT / "Godot" / "Terrain" / "map.txt"
OBJECT_DIR = ROOT / "Godot" / "Terrain" / "ObjectDataJson"
RECORD_SIZE = 22
GRID_WIDTH = 80
TILE_SIZE = 100.0
OBJECT_ORIGIN_SHIFT = (4000.0, 0.0, 4000.0)

# Import planner helpers
sys.path.insert(0, str(Path(__file__).resolve().parent))
from plan_bulk_nav_bakes import family_match, parse_map, pretty_family  # noqa: E402


def object_cell(x: float, y: float, z: float) -> tuple[int, int]:
    """Mirror bake_and_export_single_nav.gd _index_placements_file pivot bucketing."""
    # SOURCE_BASIS * (x,y,z) = (x, -y, -z)
    px, py, pz = x, -y, -z
    # Ry(-90°) * pos = (-pz, py, px)
    ox = -pz + OBJECT_ORIGIN_SHIFT[0]
    oz = px + OBJECT_ORIGIN_SHIFT[2]
    return int(math.floor(ox / TILE_SIZE)), int(math.floor(oz / TILE_SIZE))


def load_counts_by_cell() -> dict[tuple[int, int], int]:
    counts: dict[tuple[int, int], int] = defaultdict(int)
    files = list(OBJECT_DIR.rglob("*.json"))
    for path in files:
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except Exception as exc:
            print(f"  skip {path.name}: {exc}")
            continue
        if not isinstance(data, list):
            continue
        for item in data:
            if not isinstance(item, dict):
                continue
            name = str(item.get("name", item.get("object_name", ""))).lower()
            if not name or name == "empty":
                continue
            if isinstance(item.get("coordinates"), dict):
                cd = item["coordinates"]
                x = float(cd.get("x", 0.0))
                y = float(cd.get("y", 0.0))
                z = float(cd.get("z", 0.0))
            else:
                x = float(item.get("x", 0.0))
                y = float(item.get("y", 0.0))
                z = float(item.get("z", 0.0))
            cell = object_cell(x, y, z)
            counts[cell] += 1
    return counts


def plan_groups(filt: str = "all") -> list[dict]:
    cells = parse_map()
    matched = [c for c in cells if family_match(c["name"], filt)]
    by_pos = {(c["name"], c["gx"], c["gz"]): c for c in matched}
    used = set()
    groups = []

    for c in sorted(matched, key=lambda x: (x["name"], x["gz"], x["gx"])):
        pos = (c["name"], c["gx"], c["gz"])
        if pos in used or c["variant"] != "00":
            continue
        needed = {
            "00": (c["gx"], c["gz"]),
            "01": (c["gx"] + 1, c["gz"]),
            "10": (c["gx"], c["gz"] + 1),
            "11": (c["gx"] + 1, c["gz"] + 1),
        }
        corners = {}
        ok = True
        for var, (x, z) in needed.items():
            cell = by_pos.get((c["name"], x, z))
            if cell is None or cell["variant"] != var:
                ok = False
                break
            corners[var] = cell
        if not ok:
            continue
        for var, (x, z) in needed.items():
            used.add((c["name"], x, z))
        occ = corners["00"]["occ"]
        fam = pretty_family(c["name"])
        groups.append(
            {
                "name": f"{fam}_occ{occ:02d}",
                "kind": "2x2",
                "family": c["name"],
                "occ": occ,
                "tiles": [corners[v]["key"] for v in ("00", "01", "10", "11")],
                "cells": [
                    (corners[v]["gx"], corners[v]["gz"])
                    for v in ("00", "01", "10", "11")
                ],
                "gx": c["gx"],
                "gz": c["gz"],
            }
        )

    for c in matched:
        pos = (c["name"], c["gx"], c["gz"])
        if pos in used:
            continue
        groups.append(
            {
                "name": c["key"],
                "kind": "1x1",
                "family": c["name"],
                "occ": c["occ"],
                "tiles": [c["key"]],
                "cells": [(c["gx"], c["gz"])],
                "gx": c["gx"],
                "gz": c["gz"],
            }
        )
        used.add(pos)
    return groups


def main() -> None:
    top_n = int(sys.argv[1]) if len(sys.argv) > 1 else 30
    out_plan = (
        Path(sys.argv[2]) if len(sys.argv) > 2 else Path("Tools/_top_object_plan.json")
    )
    out_csv = Path(sys.argv[3]) if len(sys.argv) > 3 else out_plan.with_suffix(".csv")

    print("Indexing object placements...")
    counts = load_counts_by_cell()
    print(f"  cells with objects: {len(counts)}  total objects: {sum(counts.values())}")

    print("Planning groups (filter=all)...")
    groups = plan_groups("all")
    ranked = []
    for g in groups:
        fam = g["family"].lower()
        if fam.startswith("cc") or fam.startswith("town"):
            continue
        n = sum(counts.get((gx, gz), 0) for gx, gz in g["cells"])
        ranked.append((n, g))
    ranked.sort(key=lambda t: (-t[0], t[1]["family"], t[1]["name"]))

    print(f"\nEligible groups (excl cc/town): {len(ranked)}")
    print(f"Top {top_n}:")
    top = ranked[:top_n]
    for i, (n, g) in enumerate(top, 1):
        print(
            f"  {i:2}. {g['name']:28} {g['kind']} objects={n:6} tiles={len(g['tiles'])}"
        )

    plan = []
    for n, g in top:
        plan.append(
            {
                "name": g["name"],
                "kind": g["kind"],
                "family": g["family"],
                "occ": g["occ"],
                "tiles": g["tiles"],
                "gx": g["gx"],
                "gz": g["gz"],
                "object_count": n,
            }
        )
    out_plan.parent.mkdir(parents=True, exist_ok=True)
    out_plan.write_text(json.dumps(plan, indent=2), encoding="utf-8")
    lines = ["rank,name,kind,family,occ,object_count,tiles"]
    for i, item in enumerate(plan, 1):
        lines.append(
            f"{i},{item['name']},{item['kind']},{item['family']},{item['occ']},"
            f"{item['object_count']},\"{'|'.join(item['tiles'])}\""
        )
    out_csv.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"\nWrote {out_plan}")
    print(f"Wrote {out_csv}")


if __name__ == "__main__":
    main()
