"""Merge chunked world nav preview GLBs into one file.

Usage:
  python Tools/merge_world_nav_glb.py --chunks D:/1/world_chunk_*.glb --out D:/1/world_nav_preview.glb
"""

from __future__ import annotations

import argparse
import glob
import sys
from pathlib import Path

import trimesh


def merge_glb(paths: list[Path], out_path: Path) -> None:
    if not paths:
        raise SystemExit("No input GLB files matched.")

    combined = trimesh.Scene()
    for path in paths:
        print(f"Loading {path}...")
        scene = trimesh.load(str(path), force="scene")
        if not isinstance(scene, trimesh.Scene):
            raise SystemExit(f"Expected scene in {path}, got {type(scene)}")
        for node_name in scene.graph.nodes_geometry:
            transform, geom_name = scene.graph.get(node_name)
            geom = scene.geometry[geom_name]
            prefix = path.stem
            combined.add_geometry(
                geom,
                transform=transform,
                geom_name=f"{prefix}_{geom_name}",
                node_name=f"{prefix}_{node_name}",
            )

    out_path.parent.mkdir(parents=True, exist_ok=True)
    print(f"Writing {out_path}...")
    combined.export(str(out_path))
    size_mb = out_path.stat().st_size / (1024 * 1024)
    print(f"Done: {out_path} ({size_mb:.1f} MB, {len(paths)} chunks)")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--chunks",
        nargs="+",
        required=True,
        help="Chunk GLB paths (glob patterns allowed)",
    )
    parser.add_argument(
        "--out",
        required=True,
        help="Merged output GLB path",
    )
    args = parser.parse_args()

    paths: list[Path] = []
    for pattern in args.chunks:
        matches = sorted(Path(p) for p in glob.glob(pattern))
        paths.extend(matches)
    paths = sorted(set(paths))

    merge_glb(paths, Path(args.out))


if __name__ == "__main__":
    main()
