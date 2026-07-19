#!/usr/bin/env python3
"""Place MonsterSpawner nodes at pyram1 ObjectDataJson coords in MainServer.tscn.

Rules:
  - Skip pyram1 within 100m of any cci* placement (Godot/SOURCE_BASIS space).
  - Skip if an existing MonsterSpawner is within 5m.
  - Infer Regular/Named min/max levels from the closest existing spawner within 500m
    that has non-default levels (not 1..3). If none, keep scene defaults (omit props).
"""
from __future__ import annotations

import json
import math
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
OBJECT_DIR = REPO / "Godot" / "Terrain" / "ObjectDataJson"
MAIN_SERVER = REPO / "Godot" / "Scenes" / "MainServer.tscn"

NEAR_SPAWNER_M = 5.0
LEVEL_INFER_M = 500.0
CCI_EXCLUDE_M = 100.0
DEFAULT_MIN, DEFAULT_MAX = 1, 3
# Matches IndoorFieldConfig / IndoorAreaCriteria / OutdoorFieldConfig
INDOOR_Y_MAX = -500.0
DEFAULT_INDOOR_SPAWN_RADIUS_M = 4.0  # IndoorFieldConfig.DefaultSpawnRadiusMeters


def source_to_godot(x: float, y: float, z: float) -> tuple[float, float, float]:
    return (x, -y, -z)


def dist3(a, b) -> float:
    return math.sqrt((a[0] - b[0]) ** 2 + (a[1] - b[1]) ** 2 + (a[2] - b[2]) ** 2)


def load_named_placements(name_pred) -> list[tuple[float, float, float]]:
    out: list[tuple[float, float, float]] = []
    for path in OBJECT_DIR.rglob("*.json"):
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except Exception as ex:
            print(f"WARN skip {path}: {ex}", file=sys.stderr)
            continue
        if not isinstance(data, list):
            continue
        for rec in data:
            if not isinstance(rec, dict):
                continue
            name = str(rec.get("name") or rec.get("object_name") or "").strip().lower()
            if not name_pred(name):
                continue
            try:
                if "coordinates" in rec and isinstance(rec["coordinates"], dict):
                    cd = rec["coordinates"]
                    sx, sy, sz = float(cd["x"]), float(cd["y"]), float(cd["z"])
                else:
                    sx, sy, sz = float(rec["x"]), float(rec["y"]), float(rec["z"])
            except (KeyError, TypeError, ValueError):
                continue
            out.append(source_to_godot(sx, sy, sz))
    return out


def is_default_levels(rmin: int, rmax: int, nmin: int, nmax: int) -> bool:
    return (
        rmin == DEFAULT_MIN
        and rmax == DEFAULT_MAX
        and nmin == DEFAULT_MIN
        and nmax == DEFAULT_MAX
    )


def parse_existing_spawners(text: str) -> list[dict]:
    """Parse MonsterSpawner nodes that are direct children of MonsterSpawners."""
    spawners: list[dict] = []
    # Match spawner header lines only (not Monster_ children).
    header_re = re.compile(
        r'^\[node name="((?:ERROR - )?MonsterSpawner_[^"]+)" parent="MonsterSpawners"[^\]]*\]\s*$'
    )
    transform_re = re.compile(
        r"^transform = Transform3D\([^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,[^,]+,\s*"
        r"([^,]+),\s*([^,]+),\s*([^)]+)\)\s*$"
    )
    level_re = re.compile(r"^(Regular|Named)Monster(Min|Max)Level = (-?\d+)\s*$")
    id_re = re.compile(r"MonsterSpawner_([0-9A-Fa-f]{1,4})_")

    lines = text.splitlines()
    i = 0
    while i < len(lines):
        m = header_re.match(lines[i])
        if not m:
            i += 1
            continue
        name = m.group(1)
        pos = None
        rmin = rmax = nmin = nmax = None
        j = i + 1
        while j < len(lines):
            if lines[j].startswith("[node "):
                break
            tm = transform_re.match(lines[j])
            if tm:
                pos = (float(tm.group(1)), float(tm.group(2)), float(tm.group(3)))
            lm = level_re.match(lines[j])
            if lm:
                kind, which, val = lm.group(1), lm.group(2), int(lm.group(3))
                if kind == "Regular" and which == "Min":
                    rmin = val
                elif kind == "Regular" and which == "Max":
                    rmax = val
                elif kind == "Named" and which == "Min":
                    nmin = val
                elif kind == "Named" and which == "Max":
                    nmax = val
            j += 1
        if pos is not None:
            # Defaults if props omitted in tscn
            rmin = DEFAULT_MIN if rmin is None else rmin
            rmax = DEFAULT_MAX if rmax is None else rmax
            nmin = DEFAULT_MIN if nmin is None else nmin
            nmax = DEFAULT_MAX if nmax is None else nmax
            sid = None
            im = id_re.search(name)
            if im:
                sid = int(im.group(1), 16)
            spawners.append(
                {
                    "name": name,
                    "pos": pos,
                    "levels": (rmin, rmax, nmin, nmax),
                    "non_default": not is_default_levels(rmin, rmax, nmin, nmax),
                    "id": sid,
                }
            )
        i = j
    return spawners


def collect_unique_ids(text: str) -> set[int]:
    return {int(x) for x in re.findall(r"unique_id=(\d+)", text)}


def collect_used_spawner_ids(spawners: list[dict]) -> set[int]:
    return {s["id"] for s in spawners if s["id"] is not None}


def fmt_float(v: float) -> str:
    # Godot-like compact float
    s = f"{v:.6f}".rstrip("0").rstrip(".")
    if s == "-0":
        s = "0"
    return s


def build_node_block(
    name: str,
    unique_id: int,
    gx: float,
    gy: float,
    gz: float,
    levels: tuple[int, int, int, int] | None,
) -> str:
    lines = [
        f'[node name="{name}" parent="MonsterSpawners" unique_id={unique_id} instance=ExtResource("12_tf661")]',
        f"transform = Transform3D(1, 0, 0, 0, 1, 0, 0, 0, 1, {fmt_float(gx)}, {fmt_float(gy)}, {fmt_float(gz)})",
        f'OriginalDisplayName = "{name}"',
    ]
    if gy < INDOOR_Y_MAX:
        lines.append(f"SpawnRadiusMeters = {fmt_float(DEFAULT_INDOOR_SPAWN_RADIUS_M)}")
    if levels is not None:
        rmin, rmax, nmin, nmax = levels
        lines.extend(
            [
                f"RegularMonsterMinLevel = {rmin}",
                f"RegularMonsterMaxLevel = {rmax}",
                f"NamedMonsterMinLevel = {nmin}",
                f"NamedMonsterMaxLevel = {nmax}",
            ]
        )
    lines.append("")
    return "\n".join(lines)


def pick_free_id_run_start(used_ids: set[int], need: int) -> int:
    """Start of the largest contiguous free 16-bit ID run that can hold `need` ids."""
    used = set(used_ids)
    free = [i for i in range(100, 0x10000) if i not in used]
    if not free:
        raise SystemExit("No free spawner hex IDs")
    runs: list[tuple[int, int, int]] = []
    start = prev = free[0]
    for i in free[1:]:
        if i == prev + 1:
            prev = i
            continue
        runs.append((start, prev, prev - start + 1))
        start = prev = i
    runs.append((start, prev, prev - start + 1))
    runs.sort(key=lambda x: -x[2])
    for a, _b, n in runs:
        if n >= need:
            return a
    raise SystemExit(
        f"No free contiguous ID run for {need} spawners (largest={runs[0][2]})"
    )


def find_insert_index(lines: list[str]) -> int:
    """Index of the first top-level node after the MonsterSpawners subtree."""
    start = None
    for i, line in enumerate(lines):
        if line.startswith('[node name="MonsterSpawners"'):
            start = i
            break
    if start is None:
        raise SystemExit("MonsterSpawners node not found in MainServer.tscn")
    for i in range(start + 1, len(lines)):
        if not lines[i].startswith("[node "):
            continue
        # Direct child of root (sibling of MonsterSpawners), not under MonsterSpawners/
        if ' parent="."' in lines[i] or lines[i].endswith(' parent="."]'):
            return i
        # Some scenes use parent="." with unique_id before ]
        if re.search(r'parent="\."', lines[i]):
            return i
    raise SystemExit("Could not find insertion point after MonsterSpawners section")


def main() -> int:
    print("Loading pyram1 placements...")
    pyramids = load_named_placements(lambda n: n == "pyram1")
    print(f"  pyram1 count={len(pyramids)}")

    print("Loading cci* placements...")
    cci = load_named_placements(lambda n: n.startswith("cci"))
    print(f"  cci count={len(cci)}")

    text = MAIN_SERVER.read_text(encoding="utf-8")
    spawners = parse_existing_spawners(text)
    print(f"  existing MonsterSpawners={len(spawners)}")
    non_default = [s for s in spawners if s["non_default"]]
    print(f"  non-default level spawners={len(non_default)}")

    used_ids = collect_used_spawner_ids(spawners)
    unique_ids = collect_unique_ids(text)
    place_state: dict = {}

    # Spatial index: brute force is fine for ~3k spawners × ~1k pyramids.
    placed: list[dict] = []
    skipped_cci = 0
    skipped_near = 0
    inferred = 0
    default_levels = 0

    # Deduplicate pyram1 that share nearly the same Godot position
    seen_q: set[tuple[int, int, int]] = set()
    for gx, gy, gz in pyramids:
        q = (
            int(math.floor(gx * 100)),
            int(math.floor(gy * 100)),
            int(math.floor(gz * 100)),
        )
        if q in seen_q:
            continue
        seen_q.add(q)
        pos = (gx, gy, gz)

        near_cci = False
        for c in cci:
            if dist3(pos, c) <= CCI_EXCLUDE_M:
                near_cci = True
                break
        if near_cci:
            skipped_cci += 1
            continue

        near_spawner = False
        for s in spawners:
            if dist3(pos, s["pos"]) <= NEAR_SPAWNER_M:
                near_spawner = True
                break
        if near_spawner:
            skipped_near += 1
            continue
        # Also avoid clustering new pyram1 placements against each other
        for p in placed:
            if dist3(pos, p["pos"]) <= NEAR_SPAWNER_M:
                near_spawner = True
                break
        if near_spawner:
            skipped_near += 1
            continue

        levels = None
        best_d = LEVEL_INFER_M + 1.0
        best = None
        for s in non_default:
            d = dist3(pos, s["pos"])
            if d <= LEVEL_INFER_M and d < best_d:
                best_d = d
                best = s
        if best is not None:
            levels = best["levels"]
            inferred += 1
        else:
            default_levels += 1

        # Allocate from a contiguous free 16-bit block (never reuse existing IDs).
        if "next_sid" not in place_state:
            place_state["next_sid"] = pick_free_id_run_start(
                used_ids, need=max(1, len(pyramids))
            )
            print(f"  allocating new IDs from 0x{place_state['next_sid']:04X}")
        sid = place_state["next_sid"]
        while sid in used_ids and sid < 0x10000:
            sid += 1
        if sid >= 0x10000:
            raise SystemExit("Out of spawner hex IDs")
        place_state["next_sid"] = sid + 1
        used_ids.add(sid)

        uid = 3_000_000_000 + sid
        while uid in unique_ids:
            uid += 1
        unique_ids.add(uid)

        # Name uses SOURCE coords (floor): source = (gx, -gy, -gz)
        sx, sy, sz = gx, -gy, -gz
        name = f"MonsterSpawner_{sid:04X}_[{math.floor(sx)}]_[{math.floor(sy)}]_[{math.floor(sz)}]"
        placed.append(
            {
                "name": name,
                "pos": pos,
                "unique_id": uid,
                "levels": levels,
                "id": sid,
            }
        )

    print(
        f"To place={len(placed)} skipped_cci={skipped_cci} skipped_near_spawner={skipped_near} "
        f"levels_inferred={inferred} levels_default={default_levels}"
    )

    if not placed:
        print("Nothing to insert.")
        return 0

    blocks = [
        build_node_block(
            p["name"],
            p["unique_id"],
            p["pos"][0],
            p["pos"][1],
            p["pos"][2],
            p["levels"],
        )
        for p in placed
    ]
    insert_blob = "\n".join(blocks)
    if not insert_blob.endswith("\n"):
        insert_blob += "\n"

    lines = text.splitlines(keepends=True)
    # Rebuild with splitlines without keepends for index, then splice
    bare = text.splitlines()
    idx = find_insert_index(bare)
    # Preserve newlines: use keepends list
    bare_keep = text.splitlines(keepends=True)
    # find_insert_index on bare maps 1:1 to bare_keep if file uses \n
    new_text = "".join(bare_keep[:idx]) + insert_blob + "".join(bare_keep[idx:])
    MAIN_SERVER.write_text(new_text, encoding="utf-8")
    print(f"Inserted {len(placed)} spawners into {MAIN_SERVER}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
