"""Embed trimesh colliders into GLB models via Godot's '-col' name suffix.

Renames every mesh-bearing node inside the GLB JSON chunk to end in '-col',
which makes Godot's importer keep the visual mesh and add a StaticBody3D
child with a ConcavePolygonShape3D built from the same geometry. The edit
only touches the JSON chunk; binary buffers stay byte-identical.

Skips the same vegetation/helper models as generate_colliders.py.
Idempotent: nodes already ending in '-col' are left alone.

Usage:
  python Tools/apply_colliders.py                 # all models
  python Tools/apply_colliders.py --names bochka3 # specific models
  python Tools/apply_colliders.py --dry-run
"""

import argparse
import fnmatch
import json
import struct
import sys
from pathlib import Path

from generate_colliders import SKIP_PATTERNS

GLB_MAGIC = 0x46546C67
CHUNK_JSON = 0x4E4F534A
CHUNK_BIN = 0x004E4942


def read_glb(data: bytes) -> list[tuple[int, bytes]]:
    magic, version, _length = struct.unpack_from("<III", data, 0)
    if magic != GLB_MAGIC or version != 2:
        raise ValueError("not a GLB v2 file")
    chunks = []
    offset = 12
    while offset < len(data):
        clen, ctype = struct.unpack_from("<II", data, offset)
        offset += 8
        chunks.append((ctype, data[offset : offset + clen]))
        offset += clen
    return chunks


def write_glb(chunks: list[tuple[int, bytes]]) -> bytes:
    out = b""
    for ctype, payload in chunks:
        pad = (4 - len(payload) % 4) % 4
        payload = payload + (b" " if ctype == CHUNK_JSON else b"\x00") * pad
        out += struct.pack("<II", len(payload), ctype) + payload
    return struct.pack("<III", GLB_MAGIC, 2, 12 + len(out)) + out


def add_col_suffix(gltf: dict) -> int:
    renamed = 0
    for i, node in enumerate(gltf.get("nodes", [])):
        if "mesh" not in node:
            continue
        name = node.get("name") or f"Mesh{i}"
        if name.endswith("-col"):
            continue
        node["name"] = f"{name}-col"
        renamed += 1
    return renamed


def process(path: Path, dry_run: bool) -> int:
    data = path.read_bytes()
    chunks = read_glb(data)
    out_chunks = []
    renamed = 0
    for ctype, payload in chunks:
        if ctype == CHUNK_JSON:
            gltf = json.loads(payload)
            renamed = add_col_suffix(gltf)
            payload = json.dumps(gltf, separators=(",", ":")).encode("utf-8")
        out_chunks.append((ctype, payload))
    if renamed and not dry_run:
        path.write_bytes(write_glb(out_chunks))
    return renamed


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--models-dir", default="Godot/Models")
    ap.add_argument("--names", nargs="*", default=None)
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    models_dir = Path(args.models_dir)
    if args.names:
        files = [models_dir / f"{n}.glb" for n in args.names]
    else:
        files = sorted(models_dir.glob("*.glb"), key=lambda p: p.name.lower())
        files = [
            f
            for f in files
            if not any(fnmatch.fnmatch(f.stem.lower(), pat) for pat in SKIP_PATTERNS)
        ]

    changed = skipped = failed = 0
    for path in files:
        try:
            renamed = process(path, args.dry_run)
        except Exception as exc:
            failed += 1
            print(f"{path.stem:16s} FAILED: {exc}", file=sys.stderr)
            continue
        if renamed:
            changed += 1
        else:
            skipped += 1
    print(
        f"{changed} updated, {skipped} unchanged, {failed} failed ({len(files)} total)"
    )
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
