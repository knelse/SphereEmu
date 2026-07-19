#!/usr/bin/env python3
"""Renumber pyram1-batch MonsterSpawners onto a contiguous free 16-bit ID block.

Identifies the batch by unique_id >= 3_000_000_000 (from place_pyram1_spawners.py).
Picks the start of the largest free run that fits, rewrites node names + OriginalDisplayName
+ unique_id. Does not touch pre-existing spawners.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

MAIN = Path(__file__).resolve().parents[1] / "Godot" / "Scenes" / "MainServer.tscn"
BATCH_UID_MIN = 3_000_000_000


def main() -> int:
    text = MAIN.read_text(encoding="utf-8")

    header_re = re.compile(
        r'\[node name="((?:ERROR - )?MonsterSpawner_([0-9A-Fa-f]+)_[^"]+)" '
        r'parent="MonsterSpawners"([^\]]*unique_id=)(\d+)([^\]]*)\]'
    )

    pre_hids: set[int] = set()
    batch: list[re.Match[str]] = []
    for m in header_re.finditer(text):
        hid = int(m.group(2), 16)
        uid = int(m.group(4))
        if uid >= BATCH_UID_MIN:
            batch.append(m)
        else:
            pre_hids.add(hid)

    if not batch:
        print("No pyram1-batch spawners found (uid>=3e9).")
        return 0

    old_hids = {int(m.group(2), 16) for m in batch}
    overlap = old_hids & pre_hids
    print(f"batch={len(batch)} pre_hids={len(pre_hids)} current_overlap={len(overlap)}")

    used = set(pre_hids)
    # Find largest contiguous free run that fits batch size (exclude ids < 100 like dump tool).
    free = [i for i in range(100, 0x10000) if i not in used]
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

    need = len(batch)
    pick = next((r for r in runs if r[2] >= need), None)
    if pick is None:
        print("No free contiguous ID run large enough", file=sys.stderr)
        return 1
    new_start = pick[0]
    print(
        f"Renumbering onto 0x{new_start:04X}..0x{new_start + need - 1:04X} (free run 0x{pick[0]:04X}-0x{pick[1]:04X})"
    )

    # Replace from the end so offsets stay valid.
    out = text
    for offset, m in enumerate(reversed(batch)):
        new_hid = new_start + (need - 1 - offset)
        old_name = m.group(1)
        # Strip ERROR - prefix if present for rebuild
        base = old_name
        err = base.startswith("ERROR - ")
        if err:
            base = base[len("ERROR - ") :]
        # MonsterSpawner_XXXX_[...]
        rest = base.split("_", 2)  # ['MonsterSpawner', 'XXXX', '[...]']
        if len(rest) < 3:
            print(f"WARN skip odd name {old_name}", file=sys.stderr)
            continue
        new_base = f"MonsterSpawner_{new_hid:04X}_{rest[2]}"
        new_name = ("ERROR - " + new_base) if err else new_base
        new_uid = BATCH_UID_MIN + new_hid

        old_header = m.group(0)
        new_header = (
            f'[node name="{new_name}" parent="MonsterSpawners"'
            f"{m.group(3)}{new_uid}{m.group(5)}]"
        )
        # Header appears once; also fix OriginalDisplayName for this node block.
        block_start = m.start()
        # Find next [node after this header
        next_node = out.find("\n[node ", block_start + 1)
        block_end = next_node if next_node >= 0 else len(out)
        block = out[block_start:block_end]
        block2 = block.replace(old_header, new_header, 1)
        # OriginalDisplayName may use old name without ERROR prefix
        block2 = block2.replace(
            f'OriginalDisplayName = "{base}"', f'OriginalDisplayName = "{new_base}"'
        )
        block2 = block2.replace(
            f'OriginalDisplayName = "{old_name}"', f'OriginalDisplayName = "{new_name}"'
        )
        out = out[:block_start] + block2 + out[block_end:]

    MAIN.write_text(out, encoding="utf-8")

    # Verify
    text2 = MAIN.read_text(encoding="utf-8")
    pre2: set[int] = set()
    batch2: set[int] = set()
    for m in header_re.finditer(text2):
        hid = int(m.group(2), 16)
        uid = int(m.group(4))
        if uid >= BATCH_UID_MIN:
            batch2.add(hid)
        else:
            pre2.add(hid)
    print(
        f"after: batch={len(batch2)} overlap={len(batch2 & pre2)} range=0x{min(batch2):04X}-0x{max(batch2):04X}"
    )
    if batch2 & pre2:
        print("ERROR: still overlapping", file=sys.stderr)
        return 1
    if len(batch2) != need:
        print(f"ERROR: batch size changed {need} -> {len(batch2)}", file=sys.stderr)
        return 1
    # Contiguous?
    if max(batch2) - min(batch2) + 1 != need or len(batch2) != need:
        print("ERROR: batch not contiguous", file=sys.stderr)
        return 1
    print("OK: no overlap, contiguous free block")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
