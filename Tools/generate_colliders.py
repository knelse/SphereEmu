"""Generate simplistic colliders (convex or concave) for GLB models.

Produces low-poly collision geometry suitable for Godot 4.x navigation:
- Simple / convex-enough models get a single flattened convex hull
  (Godot ConvexPolygonShape3D via the '-convcolonly' import suffix).
- Genuinely concave models (bridges, towers with recesses) get one
  minimalistic concave trimesh built by unioning a V-HACD decomposition
  and decimating the result (Godot ConcavePolygonShape3D via '-colonly').
Both shape kinds are parsed by the NavigationMesh baker as static colliders.

Classification: models with max extent >= MIN_CONCAVE_EXTENT whose mesh
volume fills less than CONCAVE_VOLUME_RATIO of their convex hull volume
are treated as concave; everything else is convex.

Modes:
  preview  original model at origin + collider mesh offset on +X,
           for easy visual inspection (default).
  final    collider node co-located with the model and named with the
           Godot import suffix so the importer creates collision shapes.

Usage:
  python Tools/generate_colliders.py --count 20
  python Tools/generate_colliders.py --names bochka3 cem_tower
  python Tools/generate_colliders.py --mode final --count 20
"""

import argparse
import fnmatch
import sys
from pathlib import Path

import numpy as np
import trimesh
from scipy.optimize import linprog
from scipy.spatial import HalfspaceIntersection
from trimesh.visual.material import PBRMaterial

COLLIDER_COLOR = [0.1, 0.9, 0.3, 0.6]  # translucent green

SINGLE_HULL_FACE_TARGET = 24
CONCAVE_FACE_TARGET = 96
CONCAVE_PART_FACE_TARGET = 16
CONCAVE_VHACD_HULLS = 8
# Mesh volume below this fraction of hull volume => concave collider.
CONCAVE_VOLUME_RATIO = 0.75
# Below this ratio the model is deeply concave (bent planks, arches) and
# goes concave even if it is smaller than MIN_CONCAVE_EXTENT.
DEEP_CONCAVE_VOLUME_RATIO = 0.4
# Or: 90th percentile of surface-sample depth inside the hull, relative to
# model size, above this => concave (robust for open/non-watertight meshes).
CONCAVE_DEPTH_RATIO = 0.12
# Objects smaller than this (max extent, meters) always stay convex:
# they are plain obstacles and concave detail is irrelevant for navigation.
MIN_CONCAVE_EXTENT = 4.0
DEGENERATE_VOLUME = 1e-9

# Hull flattening: collider planes sit where the bulk of the surface is,
# so sparse protrusions (crumbled blocks, handles) only bevel the edges
# instead of inflating the whole profile.
FLATTEN_SURFACE_SAMPLES = 20000
FLATTEN_QUANTILE = 0.96
# Never shrink a plane by more than this fraction of the extent along its
# normal, even if the quantile suggests it (protects thin/sloped shapes).
FLATTEN_MAX_SHRINK_FRAC = 0.3

# Models that never get a collider (vegetation treated as transparent,
# helper objects without real geometry).
SKIP_PATTERNS = (
    "*bush*",
    "*grass*",
    "cam_cube",
    "treeput",
    "fl_*",
    "flower*",
    "kamysh*",
    "pyram*",
    "tn2_fl*",
    "vine*",
)

# Adaptive percent selection: models with fewer source triangles than
# ADAPTIVE_TRIS_THRESHOLD are "simple" and use the lower percent.
# Models below the threshold keep their original mesh as the collider;
# above it, concave ones get the voxel remesh decimated to the percent
# below and convex ones get a flattened hull.
ADAPTIVE_TRIS_THRESHOLD = 150
ADAPTIVE_COMPLEX_PERCENT = 75.0
# Concave colliders are decimated from a dense voxel remesh, not from the
# source mesh, so a percent-of-source target can be absurdly low for simple
# models. Never decimate a concave collider below this many triangles.
CONCAVE_MIN_FACES = 48

# Per-model tweaks: {"shape": "convex" | "concave"} forces the collider kind.
OVERRIDES: dict[str, dict] = {}


def load_merged(path: Path) -> tuple[trimesh.Scene, trimesh.Trimesh]:
    scene = trimesh.load(str(path), force="scene")
    meshes = [
        g for g in scene.dump() if isinstance(g, trimesh.Trimesh) and len(g.faces) > 0
    ]
    if not meshes:
        raise ValueError(f"no triangle geometry in {path.name}")
    merged = trimesh.util.concatenate(meshes)
    return scene, merged


def decimate_hull(hull: trimesh.Trimesh, face_target: int) -> trimesh.Trimesh:
    """Reduce a convex hull to roughly face_target triangles, keeping convexity."""
    if len(hull.faces) <= face_target:
        return hull
    try:
        simplified = hull.simplify_quadric_decimation(face_count=face_target)
        if len(simplified.vertices) >= 4:
            rehulled = simplified.convex_hull
            if rehulled.volume > DEGENERATE_VOLUME:
                return rehulled
    except Exception:
        pass
    return hull


def flatten_hull(merged: trimesh.Trimesh, hull: trimesh.Trimesh) -> trimesh.Trimesh:
    """Shrink hull planes to the surface-area quantile of the mesh.

    Each face plane of the (decimated) hull moves inward until it rests
    against the bulk of the actual surface, so features carrying little
    surface area (rubble on a broken wall, small spikes) stop dictating
    the overall silhouette.
    """
    base = decimate_hull(hull, SINGLE_HULL_FACE_TARGET)
    samples, _ = trimesh.sample.sample_surface(merged, FLATTEN_SURFACE_SAMPLES)

    # Merge near-parallel coplanar triangles into unique planes.
    normals = base.face_normals
    offsets = np.einsum("ij,ij->i", normals, base.triangles_center)
    planes: list[tuple[np.ndarray, float]] = []
    for n, d in zip(normals, offsets):
        if any(np.dot(n, pn) > 0.999 and abs(d - pd) < 1e-4 for pn, pd in planes):
            continue
        planes.append((n, d))

    A = np.array([p[0] for p in planes])
    proj = samples @ A.T  # (samples, planes)
    hi = proj.max(axis=0)
    lo = proj.min(axis=0)
    q = np.quantile(proj, FLATTEN_QUANTILE, axis=0)
    b = np.maximum(q, hi - FLATTEN_MAX_SHRINK_FRAC * (hi - lo))

    # Chebyshev center as a guaranteed interior point.
    norms = np.linalg.norm(A, axis=1, keepdims=True)
    res = linprog(
        c=np.r_[np.zeros(3), -1.0],
        A_ub=np.hstack([A, norms]),
        b_ub=b,
        bounds=[(None, None)] * 3 + [(0, None)],
        method="highs",
    )
    if not res.success or res.x[3] <= 1e-9:
        return base
    try:
        hs = HalfspaceIntersection(np.hstack([A, -b[:, None]]), res.x[:3])
        flat = trimesh.PointCloud(hs.intersections).convex_hull
        if flat.volume > DEGENERATE_VOLUME:
            return decimate_hull(flat, SINGLE_HULL_FACE_TARGET)
    except Exception:
        pass
    return base


def safe_hull(mesh: trimesh.Trimesh) -> trimesh.Trimesh | None:
    try:
        hull = mesh.convex_hull
        if hull.volume > DEGENERATE_VOLUME:
            return hull
    except Exception:
        pass
    return None


def _concave_attempt(merged: trimesh.Trimesh, max_hulls: int) -> trimesh.Trimesh | None:
    raw = trimesh.decomposition.convex_decomposition(
        merged,
        maxConvexHulls=max_hulls,
        resolution=200000,
        maxNumVerticesPerCH=32,
    )
    hulls = []
    for d in raw:
        h = safe_hull(trimesh.Trimesh(vertices=d["vertices"], faces=d["faces"]))
        if h is not None:
            hulls.append(decimate_hull(h, CONCAVE_PART_FACE_TARGET))
    if not hulls:
        return None
    union = trimesh.boolean.union(hulls, engine="manifold")
    union.merge_vertices()
    # fast_simplification stalls above the target on messy unions;
    # iterating squeezes out the coplanar splits the union introduced.
    while len(union.faces) > CONCAVE_FACE_TARGET:
        simplified = union.simplify_quadric_decimation(face_count=CONCAVE_FACE_TARGET)
        simplified.merge_vertices()
        if len(simplified.faces) < 4 or simplified.area <= 0:
            break
        if len(simplified.faces) >= len(union.faces):
            break
        union = simplified
    return union


def build_concave(merged: trimesh.Trimesh) -> trimesh.Trimesh | None:
    """One minimalistic concave trimesh: V-HACD union, then decimation.

    Unioning the convex decomposition (instead of decimating the raw mesh)
    discards thin detail like railings while keeping the walkable shape.
    Retries with fewer hulls when the union refuses to decimate down.
    """
    best = None
    for max_hulls in (CONCAVE_VHACD_HULLS, 6, 4):
        try:
            result = _concave_attempt(merged, max_hulls)
        except Exception:
            result = None
        if result is None:
            continue
        if best is None or len(result.faces) < len(best.faces):
            best = result
        if len(best.faces) <= CONCAVE_FACE_TARGET * 1.5:
            break
    return best


def is_concave(
    merged: trimesh.Trimesh,
    hull: trimesh.Trimesh,
    remeshed: trimesh.Trimesh | None = None,
    voxel_resolution: int = 64,
) -> bool:
    """Classify using the watertight voxel solid, not the raw mesh.

    Raw-mesh volume is meaningless for open geometry (a thin non-watertight
    wall can report ~90% hull fill), so the fill ratio comes from the
    voxel remesh. Doorways/archways are caught via the solid's genus.
    """
    if remeshed is None:
        remeshed = build_voxel(merged, voxel_resolution)
    fill = remeshed.volume / hull.volume
    if fill < DEEP_CONCAVE_VOLUME_RATIO:
        return True
    if float(max(merged.extents)) < MIN_CONCAVE_EXTENT:
        return False
    if fill < CONCAVE_VOLUME_RATIO:
        return True
    genus = remeshed.body_count - remeshed.euler_number / 2
    if genus >= 1:
        return True
    samples, _ = trimesh.sample.sample_surface(merged, 3000)
    depth = trimesh.proximity.ProximityQuery(hull).signed_distance(samples)
    d90 = float(np.quantile(depth, 0.90)) / float(max(merged.extents))
    return d90 > CONCAVE_DEPTH_RATIO


def build_exact(merged: trimesh.Trimesh) -> trimesh.Trimesh:
    """Collider identical to the visual mesh (concave trimesh)."""
    exact = merged.copy()
    exact.merge_vertices()
    exact.remove_unreferenced_vertices()
    return exact


def simplify_to_percent(
    mesh: trimesh.Trimesh,
    percent: float,
    ref_faces: int | None = None,
    min_faces: int = 12,
) -> trimesh.Trimesh:
    """Decimate to `percent`% of ref_faces (defaults to the mesh's own count)."""
    ref = ref_faces if ref_faces is not None else len(mesh.faces)
    # min_faces guards against collapsing into degenerate geometry;
    # 12 is about the least a closed mesh can take.
    target = max(min_faces, int(round(ref * percent / 100.0)))
    while target < len(mesh.faces):
        simplified = mesh.simplify_quadric_decimation(face_count=target)
        simplified.merge_vertices()
        if len(simplified.faces) >= 4 and simplified.area > 0:
            return simplified
        target *= 2
    return mesh.copy()


def _fibonacci_sphere(n: int) -> np.ndarray:
    i = np.arange(n)
    phi = np.pi * (3.0 - np.sqrt(5.0))
    y = 1.0 - 2.0 * (i + 0.5) / n
    r = np.sqrt(1.0 - y * y)
    theta = phi * i
    return np.column_stack([r * np.cos(theta), y, r * np.sin(theta)])


def cull_internal_faces(mesh: trimesh.Trimesh, n_views: int = 256) -> trimesh.Trimesh:
    """Remove faces that are never the first hit from outside viewpoints.

    Rays are cast from a surrounding sphere toward every face (3 targets
    per face). Faces only reachable from inside cavities/shell interiors
    are dropped. The result is an open shell, which is fine for a
    ConcavePolygonShape3D collider.
    """
    try:
        from trimesh.ray.ray_pyembree import RayMeshIntersector

        ray = RayMeshIntersector(mesh)
    except BaseException:
        ray = mesh.ray

    center = mesh.bounds.mean(axis=0)
    radius = float(np.linalg.norm(mesh.extents)) * 1.5
    views = _fibonacci_sphere(n_views) * radius + center

    # Target the centroid and two vertex-weighted points of each triangle
    # so grazing faces aren't missed.
    tris = mesh.triangles
    targets = np.concatenate(
        [
            tris.mean(axis=1),
            tris[:, 0] * 0.6 + tris[:, 1] * 0.2 + tris[:, 2] * 0.2,
            tris[:, 0] * 0.2 + tris[:, 1] * 0.2 + tris[:, 2] * 0.6,
        ]
    )
    n_faces = len(mesh.faces)
    visible = np.zeros(n_faces, dtype=bool)
    for view in views:
        dirs = targets - view
        dirs /= np.linalg.norm(dirs, axis=1)[:, None]
        origins = np.broadcast_to(view, dirs.shape)
        hit = ray.intersects_first(origins, dirs)
        visible[hit[hit >= 0]] = True

    if visible.all() or not visible.any():
        return mesh
    culled = mesh.submesh([np.flatnonzero(visible)], append=True)
    culled.remove_unreferenced_vertices()
    return culled


def build_voxel(merged: trimesh.Trimesh, resolution: int) -> trimesh.Trimesh:
    """Watertight remesh: solid voxelization + marching cubes.

    The result slightly inflates the model (half a voxel) but closes all
    holes, so subsequent decimation degrades gracefully instead of
    punching through open surfaces.
    """
    pitch = float(max(merged.extents)) / resolution
    vox = merged.voxelized(pitch).fill()
    remeshed = vox.marching_cubes
    remeshed.apply_transform(vox.transform)
    remeshed.merge_vertices()
    return remeshed


def build_collider(
    merged: trimesh.Trimesh,
    shape_override: str | None = None,
    strategy: str = "auto",
    voxel_resolution: int = 64,
) -> tuple[trimesh.Trimesh, str, str]:
    """Return (collider mesh, kind 'convex'|'concave', strategy label)."""
    if strategy == "exact":
        exact = build_exact(merged)
        return exact, "concave", "exact"

    if strategy == "voxel":
        remeshed = build_voxel(merged, voxel_resolution)
        return remeshed, "concave", f"voxel res={voxel_resolution}"

    if strategy == "hybrid":
        # Tight flattened hull for convex-enough models (small props),
        # voxel remesh for genuinely concave / walk-under geometry.
        hull = safe_hull(merged)
        if hull is None:
            return merged.bounding_box_oriented.to_mesh(), "convex", "obb"
        remeshed = build_voxel(merged, voxel_resolution)
        if shape_override != "convex" and (
            shape_override == "concave" or is_concave(merged, hull, remeshed)
        ):
            return remeshed, "concave", f"voxel res={voxel_resolution}"
        return flatten_hull(merged, hull), "convex", "hull"

    hull = safe_hull(merged)
    if hull is None:
        # Flat / degenerate geometry: fall back to an oriented bounding box.
        return merged.bounding_box_oriented.to_mesh(), "convex", "obb"

    concave = shape_override == "concave" or (
        shape_override is None and is_concave(merged, hull)
    )
    if concave:
        mesh = build_concave(merged)
        if mesh is not None:
            ratio = abs(merged.volume) / hull.volume
            return mesh, "concave", f"concave (volratio {ratio:.2f})"

    return flatten_hull(merged, hull), "convex", "hull"


def collider_material() -> PBRMaterial:
    return PBRMaterial(
        baseColorFactor=COLLIDER_COLOR,
        metallicFactor=0.0,
        roughnessFactor=1.0,
        alphaMode="BLEND",
        doubleSided=True,
        name="collider_mat",
    )


def export_preview(
    scene: trimesh.Scene,
    merged: trimesh.Trimesh,
    collider: trimesh.Trimesh,
    name: str,
    out_path: Path,
) -> None:
    transform = trimesh.transformations.translation_matrix(
        [merged.extents[0] * 1.25, 0, 0]
    )
    collider = collider.copy()
    collider.visual = trimesh.visual.TextureVisuals(material=collider_material())
    scene.add_geometry(
        collider,
        node_name=f"{name}_col",
        geom_name=f"{name}_col",
        transform=transform,
    )
    scene.export(str(out_path))


def export_final(
    scene: trimesh.Scene,
    collider: trimesh.Trimesh,
    kind: str,
    name: str,
    out_path: Path,
) -> None:
    collider = collider.copy()
    collider.visual = trimesh.visual.TextureVisuals(material=collider_material())
    # Godot import suffixes: '-convcolonly' => ConvexPolygonShape3D,
    # '-colonly' => ConcavePolygonShape3D. Node is replaced on import.
    suffix = "-convcolonly" if kind == "convex" else "-colonly"
    scene.add_geometry(
        collider,
        node_name=f"{name}_col{suffix}",
        geom_name=f"{name}_col{suffix}",
    )
    scene.export(str(out_path))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--models-dir", default="Godot/Models")
    ap.add_argument("--out-dir", default="Godot/Models/Colliders")
    ap.add_argument("--mode", choices=["preview", "final"], default="preview")
    ap.add_argument("--count", type=int, default=None, help="process first N glb files")
    ap.add_argument(
        "--names", nargs="*", default=None, help="specific model names (no extension)"
    )
    ap.add_argument(
        "--strategy",
        choices=["auto", "exact", "voxel", "hybrid"],
        default="auto",
        help="auto: convex/concave heuristics; exact: collider = source mesh; "
        "voxel: watertight voxel remesh of the source; hybrid: flattened "
        "hull for convex models, voxel remesh for concave ones",
    )
    ap.add_argument(
        "--voxel-res",
        type=int,
        default=64,
        help="voxel grid resolution along the longest axis (voxel strategy)",
    )
    ap.add_argument(
        "--percents",
        nargs="*",
        type=float,
        default=None,
        help="also emit <name>_preview_<p> with the collider decimated to p%% "
        "of the original triangle count (preview mode only)",
    )
    ap.add_argument(
        "--cull-internal",
        action="store_true",
        help="drop faces not visible from outside (applied to percent "
        "variants and to colliders below 5000 triangles)",
    )
    ap.add_argument(
        "--adaptive",
        action="store_true",
        help="decimate the collider itself: 10%% of source triangles for "
        "simple models (<150 tris), 50%% for complex ones",
    )
    args = ap.parse_args()

    models_dir = Path(args.models_dir)
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    if args.names:
        files = [models_dir / f"{n}.glb" for n in args.names]
    else:
        files = sorted(models_dir.glob("*.glb"), key=lambda p: p.name.lower())
        files = [
            f
            for f in files
            if not any(fnmatch.fnmatch(f.stem.lower(), pat) for pat in SKIP_PATTERNS)
        ]
        if args.count:
            files = files[: args.count]

    failures = []
    for path in files:
        try:
            over = OVERRIDES.get(path.stem, {})
            scene, merged = load_merged(path)
            if args.adaptive and len(merged.faces) < ADAPTIVE_TRIS_THRESHOLD:
                # Low-poly models keep their original geometry verbatim
                # (concave trimesh collider regardless of shape).
                collider = build_exact(merged)
                kind = "concave"
                strategy = "exact (low poly)"
            else:
                collider, kind, strategy = build_collider(
                    merged,
                    shape_override=over.get("shape"),
                    strategy=args.strategy,
                    voxel_resolution=args.voxel_res,
                )
                if args.adaptive and kind == "concave":
                    collider = simplify_to_percent(
                        collider,
                        ADAPTIVE_COMPLEX_PERCENT,
                        ref_faces=len(merged.faces),
                        min_faces=CONCAVE_MIN_FACES,
                    )
                    strategy += f" @{ADAPTIVE_COMPLEX_PERCENT:.0f}%"
            if args.cull_internal and len(collider.faces) <= 5000:
                collider = cull_internal_faces(collider)
            suffix = "_preview" if args.mode == "preview" else "_collider"
            out_path = out_dir / f"{path.stem}{suffix}.glb"
            if args.mode == "preview":
                export_preview(scene.copy(), merged, collider, path.stem, out_path)
            else:
                export_final(scene, collider, kind, path.stem, out_path)
            extra = ""
            if args.percents and args.mode == "preview":
                counts = []
                # Percent targets are relative to the ORIGINAL model's
                # triangle count so levels are comparable across strategies.
                for p in args.percents:
                    variant = simplify_to_percent(
                        collider, p, ref_faces=len(merged.faces)
                    )
                    if args.cull_internal:
                        variant = cull_internal_faces(variant)
                    label = str(int(p)) if float(p).is_integer() else str(p)
                    export_preview(
                        scene.copy(),
                        merged,
                        variant,
                        path.stem,
                        out_dir / f"{path.stem}_preview_{label}.glb",
                    )
                    counts.append(f"{label}%:{len(variant.faces)}")
                extra = "  " + " ".join(counts)
            print(
                f"{path.stem:16s} src_tris={len(merged.faces):6d} "
                f"col_tris={len(collider.faces):4d} {kind:7s} [{strategy}]{extra}"
            )
        except Exception as exc:
            failures.append((path.name, exc))
            print(f"{path.stem:16s} FAILED: {exc}", file=sys.stderr)

    if failures:
        print(f"\n{len(failures)} failure(s)", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
