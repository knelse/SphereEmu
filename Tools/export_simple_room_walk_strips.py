#!/usr/bin/env python3
"""Detect opposite openings on simple indoor room kits and export a flat walk strip.

For listed "simple" rooms: find openings on AABB faces at a floor level, pair opposite
mouths, then emit a flat plane spanning opening-to-opening with width = min(mouth widths).

Preview GLB = translucent blue kit + red strip. JSON sidecar stores opening metadata.
"""

from __future__ import annotations

import argparse
import json
from datetime import datetime
from pathlib import Path

import numpy as np
import trimesh
from trimesh.visual.material import PBRMaterial

REPO = Path(__file__).resolve().parents[1]
MODELS = REPO / "Godot" / "Models"

# Curated simple rooms: opposite openings + assumed flat corridor strip.
SIMPLE_ROOMS = [
    "lba_2i",
    "lbc_2a",  # 2 openings on upper deck (above lower pit)
    "lbc_2d",
    "lbm_2b",
    "lbm_2e",
    "lbm_2f",
    "lbm_2g",
    "lbm_2i",
    "lbm_g2f",
]

BLUE = [0.2, 0.55, 0.98, 0.28]
RED = [0.95, 0.15, 0.12, 0.75]


def load_mesh(name: str) -> trimesh.Trimesh:
    for ext in ("glb", "gltf"):
        path = MODELS / f"{name}.{ext}"
        if path.is_file():
            scene = trimesh.load(path, force="scene")
            mesh = scene.to_geometry() if isinstance(scene, trimesh.Scene) else scene
            return trimesh.Trimesh(
                vertices=np.asarray(mesh.vertices, float),
                faces=np.asarray(mesh.faces, int),
                process=False,
            )
    raise FileNotFoundError(name)


def floor_levels(mesh: trimesh.Trimesh, slope_max: float = 30.0):
    v = np.asarray(mesh.vertices, float)
    f = np.asarray(mesh.faces, int)
    tri = v[f]
    nrm = np.cross(tri[:, 1] - tri[:, 0], tri[:, 2] - tri[:, 0])
    lens = np.linalg.norm(nrm, axis=1)
    ok = lens > 1e-8
    nrm = nrm[ok] / lens[ok, None]
    tri = tri[ok]
    area = 0.5 * lens[ok]
    slope = np.degrees(np.arccos(np.clip(np.abs(nrm[:, 1]), 0, 1)))
    up = (nrm[:, 1] > 0.7) & (slope <= slope_max)
    fc = tri[up].mean(1)
    fa = area[up]
    if len(fc) == 0:
        return []
    order = np.argsort(fc[:, 1])
    levels: list[tuple[float, float]] = []
    cur_y = None
    cur_a = 0.0
    n = 0
    for i in order:
        y = float(fc[i, 1])
        if cur_y is None or abs(y - cur_y) > 0.4:
            if cur_y is not None:
                levels.append((cur_y, cur_a))
            cur_y = y
            cur_a = float(fa[i])
            n = 1
        else:
            cur_a += float(fa[i])
            n += 1
            cur_y = (cur_y * (n - 1) + y) / n
    if cur_y is not None:
        levels.append((cur_y, cur_a))
    levels.sort(key=lambda t: -t[1])
    return levels


def pick_floor_y(
    name: str, levels: list[tuple[float, float]], mesh: trimesh.Trimesh
) -> float:
    if not levels:
        bmin, bmax = mesh.bounds
        return float((bmin[1] + bmax[1]) * 0.5)
    if name == "lbc_2a":
        # Upper walkable deck above the deep pit (not the tiny roof lips).
        upper = [lv for lv in levels if lv[0] > -8.0 and lv[1] >= 10.0]
        if upper:
            return max(upper, key=lambda t: t[1])[0]
    return levels[0][0]


def free_slice(
    mesh: trimesh.Trimesh,
    y: float,
    cell: float = 0.25,
    clearance: float = 2.2,
    tol: float = 0.7,
) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    bmin, bmax = mesh.bounds.astype(float)
    xs = np.arange(bmin[0] + cell * 0.5, bmax[0], cell)
    zs = np.arange(bmin[2] + cell * 0.5, bmax[2], cell)
    origins = []
    dirs = []
    coords = []
    for iz, z in enumerate(zs):
        for ix, x in enumerate(xs):
            origins.append([x, y + clearance, z])
            dirs.append([0.0, -1.0, 0.0])
            coords.append((ix, iz))
    locs, idx_ray, _ = mesh.ray.intersects_location(
        np.asarray(origins, float), np.asarray(dirs, float), multiple_hits=False
    )
    free = np.zeros((len(zs), len(xs)), dtype=bool)
    for loc, ir in zip(locs, idx_ray):
        ix, iz = coords[ir]
        if abs(float(loc[1]) - y) < tol:
            free[iz, ix] = True
    return free, xs, zs


def _runs(mask: np.ndarray) -> list[tuple[int, int]]:
    out: list[tuple[int, int]] = []
    start = None
    for i, v in enumerate(mask):
        if v and start is None:
            start = i
        if (not v or i == len(mask) - 1) and start is not None:
            end = i if v and i == len(mask) - 1 else i - 1
            out.append((start, end))
            start = None
    return out


def edge_openings(
    free: np.ndarray, xs: np.ndarray, zs: np.ndarray, min_width: float = 1.2
) -> dict[str, list[dict]]:
    """Openings as free runs on each AABB edge of the floor slice."""
    nz, nx = free.shape
    step_x = float(xs[1] - xs[0]) if len(xs) > 1 else 1.0
    step_z = float(zs[1] - zs[0]) if len(zs) > 1 else 1.0
    ops: dict[str, list[dict]] = {"-X": [], "+X": [], "-Z": [], "+Z": []}

    for label, ix in (("-X", 0), ("+X", nx - 1)):
        for a, b in _runs(free[:, ix]):
            w = (b - a + 1) * step_z
            if w < min_width:
                continue
            c = float(zs[0] + ((a + b) * 0.5) * step_z)
            ops[label].append(
                {
                    "face": label,
                    "axis": "x",
                    "center_u": c,
                    "width": float(w),
                    "u0": float(zs[0] + a * step_z),
                    "u1": float(zs[0] + b * step_z),
                }
            )
    for label, iz in (("-Z", 0), ("+Z", nz - 1)):
        for a, b in _runs(free[iz, :]):
            w = (b - a + 1) * step_x
            if w < min_width:
                continue
            c = float(xs[0] + ((a + b) * 0.5) * step_x)
            ops[label].append(
                {
                    "face": label,
                    "axis": "z",
                    "center_u": c,
                    "width": float(w),
                    "u0": float(xs[0] + a * step_x),
                    "u1": float(xs[0] + b * step_x),
                }
            )
    return ops


def faces_connected(
    free: np.ndarray, a: dict, b: dict, xs: np.ndarray, zs: np.ndarray
) -> bool:
    """True if a free-space path links the two opposite edge openings."""
    nz, nx = free.shape
    step_x = float(xs[1] - xs[0]) if len(xs) > 1 else 1.0
    step_z = float(zs[1] - zs[0]) if len(zs) > 1 else 1.0

    def cells_for(op: dict) -> list[tuple[int, int]]:
        if op["face"] == "-X":
            ix = 0
            z0 = int(round((op["u0"] - zs[0]) / step_z))
            z1 = int(round((op["u1"] - zs[0]) / step_z))
            return [
                (iz, ix) for iz in range(max(0, z0), min(nz, z1 + 1)) if free[iz, ix]
            ]
        if op["face"] == "+X":
            ix = nx - 1
            z0 = int(round((op["u0"] - zs[0]) / step_z))
            z1 = int(round((op["u1"] - zs[0]) / step_z))
            return [
                (iz, ix) for iz in range(max(0, z0), min(nz, z1 + 1)) if free[iz, ix]
            ]
        if op["face"] == "-Z":
            iz = 0
            x0 = int(round((op["u0"] - xs[0]) / step_x))
            x1 = int(round((op["u1"] - xs[0]) / step_x))
            return [
                (iz, ix) for ix in range(max(0, x0), min(nx, x1 + 1)) if free[iz, ix]
            ]
        iz = nz - 1
        x0 = int(round((op["u0"] - xs[0]) / step_x))
        x1 = int(round((op["u1"] - xs[0]) / step_x))
        return [(iz, ix) for ix in range(max(0, x0), min(nx, x1 + 1)) if free[iz, ix]]

    starts = cells_for(a)
    goals = set(cells_for(b))
    if not starts or not goals:
        return False
    from collections import deque

    q = deque(starts)
    seen = set(starts)
    while q:
        z, x = q.popleft()
        if (z, x) in goals:
            return True
        for dz, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            nz_, nx_ = z + dz, x + dx
            if (
                0 <= nz_ < nz
                and 0 <= nx_ < nx
                and free[nz_, nx_]
                and (nz_, nx_) not in seen
            ):
                seen.add((nz_, nx_))
                q.append((nz_, nx_))
    return False


def pair_openings(ops: dict[str, list[dict]], free, xs, zs) -> list[dict]:
    pairs = []
    for fa, fb in (("-X", "+X"), ("-Z", "+Z")):
        for a in ops[fa]:
            for b in ops[fb]:
                # Prefer similar centers / widths.
                if (
                    abs(a["center_u"] - b["center_u"])
                    > max(a["width"], b["width"]) * 0.75
                ):
                    continue
                if not faces_connected(free, a, b, xs, zs):
                    continue
                width = float(min(a["width"], b["width"]))
                center = 0.5 * (a["center_u"] + b["center_u"])
                pairs.append(
                    {
                        "axis": a["axis"],
                        "opening_a": a,
                        "opening_b": b,
                        "width": width,
                        "center_u": center,
                    }
                )
    # Keep best pair per axis (largest width, then closest centers).
    best: dict[str, dict] = {}
    for p in pairs:
        cur = best.get(p["axis"])
        if cur is None:
            best[p["axis"]] = p
            continue
        closer = abs(p["opening_a"]["center_u"] - p["opening_b"]["center_u"])
        cur_closer = abs(cur["opening_a"]["center_u"] - cur["opening_b"]["center_u"])
        if p["width"] > cur["width"] + 0.25 or (
            abs(p["width"] - cur["width"]) <= 0.25 and closer < cur_closer
        ):
            best[p["axis"]] = p
    return list(best.values())


def strip_quad(
    mesh: trimesh.Trimesh, floor_y: float, pair: dict, y_lift: float = 0.0
) -> tuple[np.ndarray, np.ndarray]:
    """Return quad verts + two tris for the walk strip in model-local space."""
    bmin, bmax = mesh.bounds.astype(float)
    half_w = pair["width"] * 0.5
    c = pair["center_u"]
    y = floor_y + y_lift
    if pair["axis"] == "x":
        x0, x1 = float(bmin[0]), float(bmax[0])
        z0, z1 = c - half_w, c + half_w
        verts = np.array(
            [
                [x0, y, z0],
                [x1, y, z0],
                [x1, y, z1],
                [x0, y, z1],
            ],
            dtype=float,
        )
    else:
        z0, z1 = float(bmin[2]), float(bmax[2])
        x0, x1 = c - half_w, c + half_w
        verts = np.array(
            [
                [x0, y, z0],
                [x1, y, z0],
                [x1, y, z1],
                [x0, y, z1],
            ],
            dtype=float,
        )
    faces = np.array([[0, 1, 2], [0, 2, 3]], dtype=int)
    return verts, faces


def pbr(color, name: str) -> PBRMaterial:
    return PBRMaterial(
        name=name,
        baseColorFactor=color,
        metallicFactor=0.0,
        roughnessFactor=0.85,
        alphaMode="BLEND",
        doubleSided=True,
    )


def strip_record(mesh: trimesh.Trimesh, floor_y: float, pair: dict) -> dict:
    """Canonical strip in model-local space (no preview lift) for nav bake merge."""
    verts, _faces = strip_quad(mesh, floor_y, pair, y_lift=0.0)
    # Two upward tris as flat vertex list (v0,v1,v2, v0,v2,v3).
    faces_flat = [
        verts[0].tolist(),
        verts[1].tolist(),
        verts[2].tolist(),
        verts[0].tolist(),
        verts[2].tolist(),
        verts[3].tolist(),
    ]
    return {
        "axis": pair["axis"],
        "floor_y": floor_y,
        "width": pair["width"],
        "center_u": pair["center_u"],
        "opening_a": pair["opening_a"],
        "opening_b": pair["opening_b"],
        "quad": verts.tolist(),
        "faces": faces_flat,
    }


def export_preview(
    name: str, mesh: trimesh.Trimesh, floor_y: float, pairs: list[dict], out_dir: Path
):
    kit = mesh.copy()
    kit.visual = trimesh.visual.TextureVisuals(material=pbr(BLUE, "kit_blue"))
    scene = trimesh.Scene()
    scene.add_geometry(kit, node_name=name, geom_name=name)

    strips_meta = []
    for i, pair in enumerate(pairs):
        verts_prev, faces = strip_quad(mesh, floor_y, pair, y_lift=0.03)
        strip = trimesh.Trimesh(vertices=verts_prev, faces=faces, process=False)
        strip.visual = trimesh.visual.TextureVisuals(material=pbr(RED, f"strip_{i}"))
        label = f"walk_strip_{pair['axis']}_{i}"
        scene.add_geometry(strip, node_name=label, geom_name=label)
        strips_meta.append(strip_record(mesh, floor_y, pair))

    out_glb = out_dir / f"{name}_strip.glb"
    scene.export(str(out_glb))
    meta = {
        "name": name,
        "floor_y": floor_y,
        "strips": strips_meta,
    }
    (out_dir / f"{name}_strip.json").write_text(
        json.dumps(meta, indent=2), encoding="utf-8"
    )
    return out_glb, meta


def write_bake_catalog(results: list[dict], path: Path) -> None:
    """Write Tools/simple_room_walk_strips.json for Godot bake merge."""
    rooms = {}
    for meta in results:
        if not meta.get("ok"):
            continue
        rooms[meta["name"]] = {
            "floor_y": meta["floor_y"],
            "strips": meta["strips"],
        }
    catalog = {
        "version": 1,
        "description": (
            "Local-space flat walk strips for simple indoor rooms. "
            "Always merged on top of regular indoor walkable bake."
        ),
        "rooms": rooms,
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(catalog, indent=2), encoding="utf-8")
    print(f"Wrote bake catalog {path} ({len(rooms)} rooms)")


def process_one(name: str, out_dir: Path) -> dict:
    mesh = load_mesh(name)
    levels = floor_levels(mesh)
    floor_y = pick_floor_y(name, levels, mesh)
    free, xs, zs = free_slice(mesh, floor_y)
    ops = edge_openings(free, xs, zs)
    pairs = pair_openings(ops, free, xs, zs)

    # Fallback for kits with inset +Z / odd mouths (e.g. lbm_g2f): widen search Y.
    if not pairs:
        for dy in (-0.4, 0.4, -0.8, 0.8, 1.2):
            free2, xs2, zs2 = free_slice(mesh, floor_y + dy)
            ops2 = edge_openings(free2, xs2, zs2)
            pairs = pair_openings(ops2, free2, xs2, zs2)
            if pairs:
                floor_y = floor_y + dy
                ops = ops2
                break

    if not pairs:
        return {
            "name": name,
            "ok": False,
            "floor_y": floor_y,
            "openings": ops,
            "error": "no opposite opening pair",
        }

    # Simple rooms: one strip (prefer Z if both — matches most lb* kits).
    if len(pairs) > 1:
        pairs = sorted(pairs, key=lambda p: (-p["width"], p["axis"] != "z"))[:1]

    out_glb, meta = export_preview(name, mesh, floor_y, pairs, out_dir)
    meta["ok"] = True
    meta["glb"] = str(out_glb)
    meta["levels"] = [{"y": y, "area": a} for y, a in levels[:8]]
    return meta


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--out-dir", default="")
    ap.add_argument("--names", nargs="*", default=None)
    args = ap.parse_args()
    names = args.names or SIMPLE_ROOMS
    if args.out_dir:
        out_dir = Path(args.out_dir)
    else:
        now = datetime.now()
        stamp = f"{now.year}-{now.month}-{now.day}_{now.hour:02d}-{now.minute:02d}-{now.second:02d}"
        out_dir = Path(f"D:/1/{stamp}_simple-room-strips")
    out_dir.mkdir(parents=True, exist_ok=True)

    results = []
    ok = fail = 0
    print(f"Exporting {len(names)} simple-room walk strips -> {out_dir}")
    for name in names:
        try:
            meta = process_one(name, out_dir)
        except Exception as exc:  # noqa: BLE001
            meta = {"name": name, "ok": False, "error": str(exc)}
        results.append(meta)
        if meta.get("ok"):
            ok += 1
            s = meta["strips"][0]
            print(
                f"  {name}: ok axis={s['axis']} width={s['width']:.2f} "
                f"center={s['center_u']:.2f} y={meta['floor_y']:.2f}"
            )
        else:
            fail += 1
            print(f"  {name}: FAIL {meta.get('error')}")

    summary = {"ok": ok, "fail": fail, "out": str(out_dir), "results": results}
    (out_dir / "_summary.json").write_text(
        json.dumps(summary, indent=2), encoding="utf-8"
    )
    write_bake_catalog(results, REPO / "Tools" / "simple_room_walk_strips.json")
    print(f"done ok={ok} fail={fail} out={out_dir}")
    return 1 if fail else 0


if __name__ == "__main__":
    raise SystemExit(main())
