"""Colorize a baked nav preview GLB: each connected nav component gets a distinct color.

Preserves the bake's side-by-side layout: terrain + objects on the left, colored
nav regions on the right (same offsets as the source Navigation meshes).
"""

from __future__ import annotations

import sys
from collections import defaultdict
from pathlib import Path

import numpy as np
import trimesh

# Left half of bake_and_export_single_nav.gd side-by-side exports.
TERRAIN_OBJECT_NODES = (
    "Ground",
    "Objects_plant",
    "Objects_rock",
    "Objects_other",
    "Objects",
    "Terrain",
)

# Right half — nav source meshes to colorize by connected component.
NAV_NODES = ("Navigation", "Navigation_Object")


def color_for_index(i: int) -> np.ndarray:
    hue = (i * 0.61803398875) % 1.0
    h = hue * 6.0
    c = 0.95
    x = c * (1.0 - abs(h % 2.0 - 1.0))
    if 0 <= h < 1:
        r, g, b = c, x, 0.0
    elif 1 <= h < 2:
        r, g, b = x, c, 0.0
    elif 2 <= h < 3:
        r, g, b = 0.0, c, x
    elif 3 <= h < 4:
        r, g, b = 0.0, x, c
    elif 4 <= h < 5:
        r, g, b = x, 0.0, c
    else:
        r, g, b = c, 0.0, x
    m = 0.12
    rgb = np.clip([(r + m) * 255, (g + m) * 255, (b + m) * 255], 0, 255).astype(
        np.uint8
    )
    return np.array([rgb[0], rgb[1], rgb[2], 255], dtype=np.uint8)


def _world_mesh(scene_in: trimesh.Scene, node: str):
    try:
        T, geom = scene_in.graph.get(node)
    except Exception:
        return None
    if geom is None:
        return None
    m = scene_in.geometry[geom]
    if not hasattr(m, "faces") or len(m.faces) == 0:
        return None
    v = np.asarray(m.vertices, dtype=np.float64)
    f = np.asarray(m.faces, dtype=np.int64)
    vw = (T @ np.hstack([v, np.ones((len(v), 1))]).T).T[:, :3]
    return vw, f, m


def _copy_visual(src_mesh: trimesh.Trimesh, dst: trimesh.Trimesh) -> None:
    """Best-effort copy of materials/colors from the bake mesh."""
    try:
        if (
            hasattr(src_mesh.visual, "material")
            and src_mesh.visual.material is not None
        ):
            dst.visual.material = src_mesh.visual.material
        if (
            hasattr(src_mesh.visual, "face_colors")
            and src_mesh.visual.face_colors is not None
        ):
            fc = np.asarray(src_mesh.visual.face_colors)
            if len(fc) == len(dst.faces):
                dst.visual.face_colors = fc.copy()
        elif (
            hasattr(src_mesh.visual, "vertex_colors")
            and src_mesh.visual.vertex_colors is not None
        ):
            vc = np.asarray(src_mesh.visual.vertex_colors)
            if len(vc) == len(dst.vertices):
                dst.visual.vertex_colors = vc.copy()
    except Exception:
        pass


def colorize(src: Path, out: Path) -> int:
    scene_in = trimesh.load(str(src), force="scene")

    parts = []
    for name in NAV_NODES:
        got = _world_mesh(scene_in, name)
        if got is not None:
            parts.append((got[0], got[1]))
    if not parts:
        raise RuntimeError(f"No Navigation meshes in {src}")

    verts = []
    faces = []
    base = 0
    for v, f in parts:
        verts.append(v)
        faces.append(f + base)
        base += len(v)
    V = np.vstack(verts)
    F = np.vstack(faces)

    def vkey(p, prec=3):
        return (
            round(float(p[0]), prec),
            round(float(p[1]), prec),
            round(float(p[2]), prec),
        )

    edge_to_tris: dict = defaultdict(list)
    for ti, face in enumerate(F):
        pts = [vkey(V[face[0]]), vkey(V[face[1]]), vkey(V[face[2]])]
        for a, b in ((0, 1), (1, 2), (2, 0)):
            e = tuple(sorted((pts[a], pts[b])))
            edge_to_tris[e].append(ti)

    parent = list(range(len(F)))

    def find(x):
        while parent[x] != x:
            parent[x] = parent[parent[x]]
            x = parent[x]
        return x

    def union(a, b):
        ra, rb = find(a), find(b)
        if ra != rb:
            parent[rb] = ra

    for tris in edge_to_tris.values():
        for i in range(len(tris)):
            for j in range(i + 1, len(tris)):
                union(tris[i], tris[j])

    areas = (
        np.linalg.norm(
            np.cross(V[F][:, 1] - V[F][:, 0], V[F][:, 2] - V[F][:, 0]), axis=1
        )
        * 0.5
    )
    comp_area: dict = defaultdict(float)
    comp_tris: dict = defaultdict(list)
    for ti in range(len(F)):
        r = find(ti)
        comp_area[r] += float(areas[ti])
        comp_tris[r].append(ti)

    roots = sorted(comp_area.keys(), key=lambda r: -comp_area[r])
    scene_out = trimesh.Scene()

    # Left: terrain + objects (same world frame / side-by-side offset as the bake).
    terrain_added = 0
    for name in TERRAIN_OBJECT_NODES:
        got = _world_mesh(scene_in, name)
        if got is None:
            continue
        vw, f, src_mesh = got
        mesh = trimesh.Trimesh(vertices=vw, faces=f, process=False)
        _copy_visual(src_mesh, mesh)
        scene_out.add_geometry(mesh, node_name=name, geom_name=name)
        terrain_added += 1

    # Also pull any other non-nav geometry (future bake node names).
    known = set(TERRAIN_OBJECT_NODES) | set(NAV_NODES)
    for node in scene_in.graph.nodes_geometry:
        if node in known:
            continue
        got = _world_mesh(scene_in, node)
        if got is None:
            continue
        vw, f, src_mesh = got
        mesh = trimesh.Trimesh(vertices=vw, faces=f, process=False)
        _copy_visual(src_mesh, mesh)
        label = str(node)
        scene_out.add_geometry(mesh, node_name=label, geom_name=label)
        terrain_added += 1

    for i, r in enumerate(roots):
        tris = comp_tris[r]
        used = sorted({int(vi) for ti in tris for vi in F[ti]})
        remap = {old: new for new, old in enumerate(used)}
        sv = V[used]
        sf = np.array(
            [[remap[int(a)], remap[int(b)], remap[int(c)]] for a, b, c in F[tris]],
            dtype=np.int64,
        )
        mesh = trimesh.Trimesh(vertices=sv, faces=sf, process=False)
        rgba = color_for_index(i)
        mesh.visual.face_colors = np.tile(rgba, (len(sf), 1))
        mesh.visual.material = trimesh.visual.material.PBRMaterial(
            name=f"region_{i:03d}",
            baseColorFactor=(rgba / 255.0).tolist(),
            metallicFactor=0.0,
            roughnessFactor=0.85,
        )
        label = f"region_{i:03d}_a{comp_area[r]:.0f}"
        scene_out.add_geometry(mesh, node_name=label, geom_name=label)

    out.parent.mkdir(parents=True, exist_ok=True)
    scene_out.export(str(out))
    if terrain_added == 0:
        print(f"warning: no terrain/object meshes found in {src}; regions-only export")
    return len(roots)


def main():
    if len(sys.argv) < 3:
        print("Usage: colorize_nav_regions.py <src.glb> <out.glb>")
        sys.exit(2)
    src = Path(sys.argv[1])
    out = Path(sys.argv[2])
    n = colorize(src, out)
    print(f"Wrote {out} ({n} regions)")


if __name__ == "__main__":
    main()
