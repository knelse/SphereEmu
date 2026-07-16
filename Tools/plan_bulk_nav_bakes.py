"""Plan bulk nav GLB bakes from map.txt for the requested tile families."""

from __future__ import annotations

import json
import re
from collections import Counter, defaultdict
from pathlib import Path

MAP = Path(__file__).resolve().parents[1] / "Godot" / "Terrain" / "map.txt"
RECORD_SIZE = 22
GRID_WIDTH = 80


def tile_group_key(master_name: str, occurrence: int) -> str:
    s = master_name.lower().strip()
    out = []
    for c in s:
        code = ord(c)
        ok = (97 <= code <= 122) or (48 <= code <= 57) or c in "_-."
        out.append(c if ok else "_")
    s = "".join(out)
    if not s:
        return ""
    return s[0].upper() + s[1:] + f"_{occurrence:02d}"


def parse_map():
    raw = MAP.read_bytes()
    cells = []
    next_occ: dict[str, int] = defaultdict(int)
    idx = 0
    offset = 0
    while offset + RECORD_SIZE <= len(raw):
        name_bytes = raw[offset : offset + 20]
        name_len = next((j for j in range(20) if name_bytes[j] == 0), 20)
        name = name_bytes[:name_len].decode("ascii", errors="replace").lower()
        v1, v2 = raw[offset + 20], raw[offset + 21]
        offset += RECORD_SIZE
        if not name or "fill_empt" in name:
            idx += 1
            continue
        master = f"{name}_{v1}{v2}".replace("patch", "Patch")
        gx = GRID_WIDTH - (idx % GRID_WIDTH) - 1
        gz = idx // GRID_WIDTH
        occ = next_occ[master]
        next_occ[master] = occ + 1
        key = tile_group_key(master, occ)
        cells.append(
            {
                "name": name,
                "master": master,
                "variant": f"{v1}{v2}",
                "gx": gx,
                "gz": gz,
                "occ": occ,
                "key": key,
            }
        )
        idx += 1
    return cells


def family_match(name: str, filt: str = "all") -> bool:
    n = name.lower()
    f = (filt or "all").lower()
    if f == "cc":
        return n.startswith("cc")
    if f != "all":
        return n.startswith(f)
    if n.startswith("cc"):
        return True
    if n.startswith("cemetry") or n.startswith("cemetery"):
        return True
    if n.startswith("crater"):
        return True
    if n.startswith("forest2") or n.startswith("forest3"):
        return True
    if re.match(r"^mount1[a-p]$", n):
        return True
    if n.startswith("town"):
        return True
    if re.match(r"^usadba[123]$", n):
        return True
    return False


def pretty_family(name: str) -> str:
    s = name.lower()
    return s[0].upper() + s[1:]


def main():
    import sys

    out = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("bulk_nav_bake_plan.json")
    filt = sys.argv[2] if len(sys.argv) > 2 else "all"
    cells = parse_map()
    matched = [c for c in cells if family_match(c["name"], filt)]
    print(f"filter={filt} matched cells: {len(matched)} / {len(cells)}")

    # Index by (name, gx, gz) and by (name, variant, occ)
    by_pos = {(c["name"], c["gx"], c["gz"]): c for c in matched}
    used = set()  # (name, gx, gz)
    groups = []

    # 2x2: variant 00 at (gx,gz), 01 at (gx+1,gz), 10 at (gx,gz+1), 11 at (gx+1,gz+1)
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
        gname = f"{fam}_occ{occ:02d}"
        groups.append(
            {
                "name": gname,
                "kind": "2x2",
                "family": c["name"],
                "occ": occ,
                "tiles": [corners[v]["key"] for v in ("00", "01", "10", "11")],
                "gx": c["gx"],
                "gz": c["gz"],
            }
        )

    # Remaining → 1x1
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
                "gx": c["gx"],
                "gz": c["gz"],
            }
        )
        used.add(pos)

    groups.sort(key=lambda g: (g["family"], g["occ"], g["name"]))

    n2 = sum(1 for g in groups if g["kind"] == "2x2")
    n1 = len(groups) - n2
    print(f"\nbake groups: {len(groups)}  (2x2={n2}, 1x1={n1})")

    by_fam = Counter(g["family"] for g in groups)
    print("\nGroups by family:")
    for k, v in sorted(by_fam.items()):
        n2f = sum(1 for g in groups if g["family"] == k and g["kind"] == "2x2")
        print(f"  {k:28} total={v:3}  2x2={n2f:3}  1x1={v-n2f:3}")

    print("\nSample 2x2:")
    for g in [x for x in groups if x["kind"] == "2x2"][:12]:
        print(f"  {g['name']:28} {g['tiles']}")

    print("\nSample 1x1:")
    for g in [x for x in groups if x["kind"] == "1x1"][:12]:
        print(f"  {g['name']:28} {g['tiles']}")

    # crater note
    crater_cells = [c for c in cells if "crater" in c["name"]]
    print(f"\ncrater cells on map: {len(crater_cells)} (mesh exists but unused)")

    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(groups, indent=2), encoding="utf-8")
    est = sum(12 if g["kind"] == "2x2" else 4 for g in groups)
    print(f"\nWrote {out}")
    print(f"Estimated bake time: ~{est/60:.0f} min ({len(groups)} jobs)")


if __name__ == "__main__":
    main()
