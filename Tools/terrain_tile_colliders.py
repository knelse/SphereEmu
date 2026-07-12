"""Add exact-mesh colliders to terrain tile GLBs (Godot/Terrain/Tiles).

Same approach as generate_colliders.py / apply_colliders.py for models.
The collider is the tile mesh itself (Godot's '-col' name suffix =>
visual mesh kept + StaticBody3D child with a ConcavePolygonShape3D built
from the same geometry).

Modes:
  preview  write <name>_preview.glb (original + translucent-green collider
           copy offset on +X) to --out-dir.
  final    rename mesh nodes inside the tile GLB to end in '-col'
           (idempotent, delegates to apply_colliders.process).

Usage:
  python Tools/terrain_tile_colliders.py --names canyon_hr01_00 lake1a_00
  python Tools/terrain_tile_colliders.py --count 10
  python Tools/terrain_tile_colliders.py --mode final
"""

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
from apply_colliders import process as apply_col_suffix
from generate_colliders import build_exact, export_preview, load_merged


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--tiles-dir", default="Godot/Terrain/Tiles")
    ap.add_argument("--out-dir", default="Godot/Terrain/Colliders")
    ap.add_argument("--mode", choices=["preview", "final"], default="preview")
    ap.add_argument("--count", type=int, default=None, help="process first N tiles")
    ap.add_argument(
        "--names", nargs="*", default=None, help="specific tile names (no extension)"
    )
    args = ap.parse_args()

    tiles_dir = Path(args.tiles_dir)
    if args.names:
        files = [tiles_dir / f"{n}.glb" for n in args.names]
    else:
        files = sorted(tiles_dir.glob("*.glb"), key=lambda p: p.name.lower())
        if args.count:
            files = files[: args.count]

    missing = [f for f in files if not f.exists()]
    if missing:
        print(f"missing tiles: {', '.join(f.stem for f in missing)}", file=sys.stderr)
        return 1

    if args.mode == "final":
        patched = skipped = failed = 0
        for path in files:
            try:
                renamed = apply_col_suffix(path, dry_run=False)
            except Exception as exc:
                failed += 1
                print(f"{path.stem:16s} FAILED: {exc}", file=sys.stderr)
                continue
            if renamed:
                patched += 1
            else:
                skipped += 1
        print(f"{patched} patched, {skipped} already patched, {failed} failed")
        return 1 if failed else 0

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    failures = []
    for path in files:
        try:
            scene, merged = load_merged(path)
            collider = build_exact(merged)
            out_path = out_dir / f"{path.stem}_preview.glb"
            export_preview(scene, merged, collider, path.stem, out_path)
            print(
                f"{path.stem:16s} src_tris={len(merged.faces):6d} "
                f"col_tris={len(collider.faces):6d} concave [exact]"
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
