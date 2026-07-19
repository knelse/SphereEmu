#!/usr/bin/env python3
"""Scan ObjectDataJson for indoor base tiles, cluster by mesh proximity, write export manifest.

Clusters union only when shell meshes are within MESH_GRACE_M (default 5m).
AABB proximity is only a candidate filter; links require sampled mesh vertices within grace
(so large oriented AABBs don't falsely glue neighbouring dungeons).
"""

from __future__ import annotations

import json
import math
import sys
import time
from collections import Counter
from pathlib import Path

import numpy as np

try:
    import trimesh
except ImportError as e:  # pragma: no cover
    raise SystemExit("trimesh required: pip install trimesh") from e

try:
    from scipy.spatial import cKDTree
except ImportError as e:  # pragma: no cover
    raise SystemExit("scipy required: pip install scipy") from e

REPO = Path(__file__).resolve().parents[1]
ROOT = REPO / "Godot" / "Terrain" / "ObjectDataJson"
MODELS = REPO / "Godot" / "Models"
OUT = Path(r"D:/1/indoor-clusters-manifest.json")
MEMBERS_DIR = Path(r"D:/1/indoor-cluster-members")

## Max gap between shell meshes to treat kits as one dungeon.
MESH_GRACE_M = 5.0
## Export radius pad beyond farthest mesh AABB corner from cluster center.
PAD_M = 20.0
SAMPLE_VERTS = 64
SOURCE_BASIS = np.array(
    [[1.0, 0.0, 0.0], [0.0, -1.0, 0.0], [0.0, 0.0, -1.0]],
    dtype=np.float64,
)


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


def godot_basis_from_euler_yxz(euler: np.ndarray) -> np.ndarray:
    ex, ey, ez = float(euler[0]), float(euler[1]), float(euler[2])
    cx, sx = math.cos(ex), math.sin(ex)
    cy, sy = math.cos(ey), math.sin(ey)
    cz, sz = math.cos(ez), math.sin(ez)
    bx = np.array([[1.0, 0.0, 0.0], [0.0, cx, -sx], [0.0, sx, cx]], dtype=np.float64)
    by = np.array([[cy, 0.0, sy], [0.0, 1.0, 0.0], [-sy, 0.0, cy]], dtype=np.float64)
    bz = np.array([[cz, -sz, 0.0], [sz, cz, 0.0], [0.0, 0.0, 1.0]], dtype=np.float64)
    return by @ bx @ bz


def placement_basis(pitch: float, yaw: float, roll: float) -> np.ndarray:
    euler = np.array([pitch, -yaw, roll], dtype=np.float64)
    return SOURCE_BASIS @ godot_basis_from_euler_yxz(euler) @ SOURCE_BASIS


def load_local_mesh(object_name: str, cache: dict) -> dict | None:
    """Cache local AABB + evenly sampled vertices for surface-distance checks."""
    if object_name in cache:
        return cache[object_name]
    path = None
    for ext in ("glb", "gltf"):
        cand = MODELS / f"{object_name}.{ext}"
        if cand.is_file():
            path = cand
            break
    if path is None:
        cache[object_name] = None
        return None
    try:
        scene = trimesh.load(path, force="scene")
        if isinstance(scene, trimesh.Scene):
            if not scene.graph.nodes_geometry:
                cache[object_name] = None
                return None
            mesh = scene.to_geometry()
        else:
            mesh = scene
        if mesh is None or len(getattr(mesh, "vertices", [])) == 0:
            cache[object_name] = None
            return None
        verts = np.asarray(mesh.vertices, dtype=np.float64)
        bounds = np.asarray(mesh.bounds, dtype=np.float64)
        if len(verts) > SAMPLE_VERTS:
            idx = np.linspace(0, len(verts) - 1, SAMPLE_VERTS, dtype=np.int64)
            samples = verts[idx]
        else:
            samples = verts
        entry = {
            "amin": bounds[0].copy(),
            "amax": bounds[1].copy(),
            "samples": samples,
        }
        cache[object_name] = entry
        return entry
    except Exception:
        cache[object_name] = None
        return None


def world_aabb(
    local_min: np.ndarray,
    local_max: np.ndarray,
    basis: np.ndarray,
    origin: np.ndarray,
) -> tuple[np.ndarray, np.ndarray]:
    corners = np.array(
        [
            [local_min[0], local_min[1], local_min[2]],
            [local_min[0], local_min[1], local_max[2]],
            [local_min[0], local_max[1], local_min[2]],
            [local_min[0], local_max[1], local_max[2]],
            [local_max[0], local_min[1], local_min[2]],
            [local_max[0], local_min[1], local_max[2]],
            [local_max[0], local_max[1], local_min[2]],
            [local_max[0], local_max[1], local_max[2]],
        ],
        dtype=np.float64,
    )
    world = corners @ basis.T + origin
    return world.min(axis=0), world.max(axis=0)


def aabb_distance(
    amin: np.ndarray, amax: np.ndarray, bmin: np.ndarray, bmax: np.ndarray
) -> float:
    dx = max(0.0, float(amin[0] - bmax[0]), float(bmin[0] - amax[0]))
    dy = max(0.0, float(amin[1] - bmax[1]), float(bmin[1] - amax[1]))
    dz = max(0.0, float(amin[2] - bmax[2]), float(bmin[2] - amax[2]))
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def member_key(p: dict) -> str:
    x, y, z = p["pos"]
    return (
        f"{p['name']}|{x:.3f}|{y:.3f}|{z:.3f}|"
        f"{p['pitch']:.4f}|{p['yaw']:.4f}|{p['roll']:.4f}"
    )


def main() -> int:
    t0 = time.time()
    placements: list[dict] = []
    seen: set = set()
    # Root-level .json only. Subfolders (mazes/, rooms/, …) ignored until re-enabled.
    for path in sorted(ROOT.glob("*.json")):
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
        except Exception:
            continue
        if not isinstance(data, list):
            continue
        for item in data:
            if not isinstance(item, dict):
                continue
            name = str(item.get("name", "")).lower()
            if not is_indoor_name(name):
                continue
            sx = float(item.get("x", 0))
            sy = float(item.get("y", 0))
            sz = float(item.get("z", 0))
            gx, gy, gz = sx, -sy, -sz
            if gy >= -500:
                continue
            pitch = float(item.get("pitch", 0))
            yaw = float(item.get("yaw", 0))
            roll = float(item.get("roll", 0))
            if isinstance(item.get("rotation_euler"), dict):
                rd = item["rotation_euler"]
                pitch = float(rd.get("pitch", pitch))
                yaw = float(rd.get("yaw", yaw))
                roll = float(rd.get("roll", roll))
            key = (
                name,
                round(gx, 3),
                round(gy, 3),
                round(gz, 3),
                round(pitch, 4),
                round(yaw, 4),
                round(roll, 4),
            )
            if key in seen:
                continue
            seen.add(key)
            placements.append(
                {
                    "name": name,
                    "pos": (gx, gy, gz),
                    "pitch": pitch,
                    "yaw": yaw,
                    "roll": roll,
                }
            )

    n = len(placements)
    print(f"tiles={n} loading mesh samples...", flush=True)
    mesh_cache: dict = {}
    world_bounds: list[tuple[np.ndarray, np.ndarray] | None] = [None] * n
    world_samples: list[np.ndarray | None] = [None] * n
    missing_models = 0
    for i, p in enumerate(placements):
        local = load_local_mesh(p["name"], mesh_cache)
        origin = np.asarray(p["pos"], dtype=np.float64)
        basis = placement_basis(p["pitch"], p["yaw"], p["roll"])
        if local is None:
            missing_models += 1
            world_bounds[i] = (origin - 0.5, origin + 0.5)
            world_samples[i] = origin.reshape(1, 3)
            continue
        world_bounds[i] = world_aabb(local["amin"], local["amax"], basis, origin)
        world_samples[i] = local["samples"] @ basis.T + origin
        if (i + 1) % 1000 == 0:
            print(f"  meshes {i+1}/{n}", flush=True)

    loaded = sum(1 for v in mesh_cache.values() if v is not None)
    print(
        f"models cached={len(mesh_cache)} loaded={loaded} missing_placements={missing_models}",
        flush=True,
    )

    parent = list(range(n))

    def find(i: int) -> int:
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(a: int, b: int) -> None:
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    max_half = 1.0
    centers = np.zeros((n, 3), dtype=np.float64)
    for i, wb in enumerate(world_bounds):
        assert wb is not None
        amin, amax = wb
        centers[i] = 0.5 * (amin + amax)
        half = 0.5 * float(np.max(amax - amin))
        if half > max_half:
            max_half = half
    cell = max(10.0, min(40.0, max_half))
    search_r = max_half + MESH_GRACE_M + cell
    neighbor_cells = int(math.ceil(search_r / cell))

    buckets: dict[tuple[int, int, int], list[int]] = {}
    for i, c in enumerate(centers):
        key = (
            int(math.floor(c[0] / cell)),
            int(math.floor(c[1] / cell)),
            int(math.floor(c[2] / cell)),
        )
        buckets.setdefault(key, []).append(i)

    print(
        f"clustering mesh_grace={MESH_GRACE_M}m (vertex-confirmed) cell={cell:.1f} "
        f"neighbor_cells={neighbor_cells} max_half_extent={max_half:.1f}",
        flush=True,
    )
    comparisons = 0
    aabb_hits = 0
    links = 0
    grace = MESH_GRACE_M
    for key, idxs in buckets.items():
        for dx in range(-neighbor_cells, neighbor_cells + 1):
            for dy in range(-neighbor_cells, neighbor_cells + 1):
                for dz in range(-neighbor_cells, neighbor_cells + 1):
                    nk = (key[0] + dx, key[1] + dy, key[2] + dz)
                    if nk not in buckets:
                        continue
                    for i in idxs:
                        amin, amax = world_bounds[i]  # type: ignore[misc]
                        sa = world_samples[i]
                        assert sa is not None
                        for j in buckets[nk]:
                            if j <= i:
                                continue
                            bmin, bmax = world_bounds[j]  # type: ignore[misc]
                            comparisons += 1
                            if aabb_distance(amin, amax, bmin, bmax) > grace:
                                continue
                            aabb_hits += 1
                            sb = world_samples[j]
                            assert sb is not None
                            # Bidirectional nearest — either cloud may be denser on contact face.
                            d1 = float(cKDTree(sb).query(sa, k=1)[0].min())
                            d2 = float(cKDTree(sa).query(sb, k=1)[0].min())
                            if min(d1, d2) <= grace:
                                if find(i) != find(j):
                                    links += 1
                                union(i, j)

    clusters: dict[int, list[int]] = {}
    for i in range(n):
        clusters.setdefault(find(i), []).append(i)
    ordered = sorted(clusters.values(), key=lambda c: -len(c))

    MEMBERS_DIR.mkdir(parents=True, exist_ok=True)
    # Clear old member files so stale ids don't linger.
    for old in MEMBERS_DIR.glob("cluster_*.json"):
        old.unlink()

    manifest = []
    for k, c in enumerate(ordered):
        cid = k + 1
        cx = float(np.mean([centers[i][0] for i in c]))
        cy = float(np.mean([centers[i][1] for i in c]))
        cz = float(np.mean([centers[i][2] for i in c]))
        maxd = 0.0
        for i in c:
            amin, amax = world_bounds[i]  # type: ignore[misc]
            for corner in (
                (amin[0], amin[1], amin[2]),
                (amin[0], amin[1], amax[2]),
                (amin[0], amax[1], amin[2]),
                (amin[0], amax[1], amax[2]),
                (amax[0], amin[1], amin[2]),
                (amax[0], amin[1], amax[2]),
                (amax[0], amax[1], amin[2]),
                (amax[0], amax[1], amax[2]),
            ):
                d = math.sqrt(
                    (corner[0] - cx) ** 2
                    + (corner[1] - cy) ** 2
                    + (corner[2] - cz) ** 2
                )
                maxd = max(maxd, d)
        radius = max(40.0, maxd + PAD_M)
        names: dict[str, int] = {}
        for i in c:
            names[placements[i]["name"]] = names.get(placements[i]["name"], 0) + 1
        top = sorted(names.items(), key=lambda x: -x[1])[:5]
        label = top[0][0] if top else "cluster"
        prefixes = Counter(placements[i]["name"][:3] for i in c)
        members = [member_key(placements[i]) for i in c]
        members_path = MEMBERS_DIR / f"cluster_{cid:03d}.json"
        members_path.write_text(
            json.dumps({"id": cid, "count": len(members), "keys": members}, indent=2),
            encoding="utf-8",
        )
        manifest.append(
            {
                "id": cid,
                "count": len(c),
                "center": [round(cx, 3), round(cy, 3), round(cz, 3)],
                "radius": round(radius, 1),
                "span": round(maxd, 1),
                "label": label,
                "top_names": dict(top),
                "prefixes": dict(prefixes.most_common()),
                "members": str(members_path).replace("\\", "/"),
            }
        )

    OUT.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "link_mode": "mesh_vertex",
        "mesh_grace_m": MESH_GRACE_M,
        "pad_m": PAD_M,
        "sample_verts": SAMPLE_VERTS,
        "tiles": n,
        "unique_models": len(mesh_cache),
        "models_loaded": loaded,
        "missing_model_placements": missing_models,
        "aabb_candidate_hits": aabb_hits,
        "mesh_links": links,
        "comparisons": comparisons,
        "clusters": manifest,
    }
    OUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    elapsed = time.time() - t0
    print(f"wrote {OUT}")
    print(f"members dir {MEMBERS_DIR}")
    print(
        f"tiles={n} clusters={len(manifest)} aabb_hits={aabb_hits} "
        f"mesh_links={links} elapsed_s={elapsed:.1f}"
    )
    print(
        f"radii min={min(c['radius'] for c in manifest)} "
        f"max={max(c['radius'] for c in manifest)}"
    )
    print("largest clusters:")
    for c in manifest[:12]:
        print(
            f"  id={c['id']:3d} n={c['count']:4d} r={c['radius']:.0f} "
            f"label={c['label']} prefixes={c['prefixes']}"
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
