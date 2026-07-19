#!/usr/bin/env python3
"""Export one translucent-blue GLB per unique indoor room / base-tile model.

Uses the same name criteria as indoor_area_criteria / build_indoor_cluster_manifest
(cci*, lb* except lbridge*, *_in, rd_island*, rd_r1..5, rd_rh, room1, tn4_hotel)
for placements that appear underground (Godot Y < -500).

Does not export world clusters — just the kit mesh itself, see-through.
"""

from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path

import trimesh
from trimesh.visual.material import PBRMaterial

REPO = Path(__file__).resolve().parents[1]
OBJECT_DATA = REPO / "Godot" / "Terrain" / "ObjectDataJson"
MODELS = REPO / "Godot" / "Models"

# Match Tools/export_nearby_objects_glb.gd _blue_translucent_material
BLUE = [0.2, 0.55, 0.98, 0.28]


def is_indoor_name(name: str) -> bool:
    n = name.strip().lower()
    if not n or n == "empty":
        return False
    if n == "lbridge" or n.startswith("lbridge"):
        return False
    if n.endswith("_in"):
        return True
    if n.startswith("cci"):
        return True
    if n.startswith("lb"):
        return True
    if n.startswith("rd_island"):
        return True
    if n in (
        "rd_r1",
        "rd_r2",
        "rd_r3",
        "rd_r4",
        "rd_r5",
        "rd_rh",
        "room1",
        "tn4_hotel",
    ):
        return True
    return False


def collect_unique_names() -> list[str]:
    names: set[str] = set()
    for path in OBJECT_DATA.rglob("*.json"):
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        if not isinstance(data, list):
            continue
        for it in data:
            if not isinstance(it, dict):
                continue
            name = str(it.get("name", "")).strip().lower()
            if not is_indoor_name(name):
                continue
            # ObjectDataJson.y -> Godot Y via SOURCE_BASIS (y -> -y)
            godot_y = -float(it.get("y", 0.0))
            if godot_y >= -500.0:
                continue
            names.add(name)
    return sorted(names)


def resolve_model(name: str) -> Path | None:
    for ext in ("glb", "gltf"):
        cand = MODELS / f"{name}.{ext}"
        if cand.is_file():
            return cand
    return None


def blue_material() -> PBRMaterial:
    return PBRMaterial(
        name="indoor_tile_blue",
        baseColorFactor=BLUE,
        metallicFactor=0.0,
        roughnessFactor=0.85,
        alphaMode="BLEND",
        doubleSided=True,
    )


def export_one(src: Path, out_path: Path) -> tuple[int, int]:
    loaded = trimesh.load(src, force="scene")
    if isinstance(loaded, trimesh.Scene):
        if not loaded.graph.nodes_geometry:
            raise RuntimeError("empty scene")
        mesh = loaded.to_geometry()
    else:
        mesh = loaded
    if mesh is None or len(getattr(mesh, "vertices", [])) == 0:
        raise RuntimeError("no geometry")
    mesh = mesh.copy()
    mesh.visual = trimesh.visual.TextureVisuals(material=blue_material())
    scene = trimesh.Scene()
    scene.add_geometry(mesh, node_name=src.stem, geom_name=src.stem)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    scene.export(str(out_path))
    return len(mesh.vertices), len(mesh.faces)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--out-dir",
        default="",
        help="output folder (default: D:/1/<stamp>_indoor-room-tiles)",
    )
    ap.add_argument("--limit", type=int, default=0, help="export only first N names")
    args = ap.parse_args()

    names = collect_unique_names()
    if args.limit > 0:
        names = names[: args.limit]

    if args.out_dir:
        out_dir = Path(args.out_dir)
    else:
        now = datetime.now()
        stamp = f"{now.year}-{now.month}-{now.day}_{now.hour:02d}-{now.minute:02d}-{now.second:02d}"
        out_dir = Path(f"D:/1/{stamp}_indoor-room-tiles")

    out_dir.mkdir(parents=True, exist_ok=True)
    ok = fail = 0
    log_lines = [f"names={len(names)} out={out_dir}"]
    print(f"Exporting {len(names)} indoor room tile models -> {out_dir}")

    for name in names:
        src = resolve_model(name)
        dest = out_dir / f"{name}.glb"
        if src is None:
            fail += 1
            msg = f"{name}: MISSING model"
            print(msg)
            log_lines.append(msg)
            continue
        try:
            nv, nf = export_one(src, dest)
            ok += 1
            msg = f"{name}: ok verts={nv} faces={nf}"
            print(msg)
            log_lines.append(msg)
        except Exception as exc:  # noqa: BLE001 — batch export continues
            fail += 1
            msg = f"{name}: FAIL {exc}"
            print(msg)
            log_lines.append(msg)

    summary = f"done ok={ok} fail={fail} out={out_dir}"
    log_lines.append(summary)
    (out_dir / "_export_log.txt").write_text(
        "\n".join(log_lines) + "\n", encoding="utf-8"
    )
    print(summary)
    return 1 if fail else 0


if __name__ == "__main__":
    raise SystemExit(main())
