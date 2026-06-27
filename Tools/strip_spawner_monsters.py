"""One-off script: remove monster child nodes from MainServer.tscn spawners and Cats."""

from __future__ import annotations

import sys
from pathlib import Path

DEFAULT_PATH = (
    Path(__file__).resolve().parents[1] / "Godot" / "Scenes" / "MainServer.tscn"
)


def strip_monsters(path: Path) -> None:
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    out: list[str] = []
    removed_monsters = 0
    cleared_arrays = 0
    i = 0
    while i < len(lines):
        line = lines[i]
        if line.startswith("[node "):
            if (
                'parent="MonsterSpawners/MonsterSpawner_' in line
                and 'instance=ExtResource("7_wg6ka")' in line
            ):
                removed_monsters += 1
                i += 1
                continue
            if 'parent="Cats"' in line and 'instance=ExtResource("7_wg6ka")' in line:
                removed_monsters += 1
                i += 1
                while i < len(lines) and not lines[i].startswith("[node "):
                    i += 1
                continue

        if line.startswith("RegularMonsters = [NodePath("):
            out.append("RegularMonsters = []\n")
            cleared_arrays += 1
            i += 1
            continue
        if line.startswith("NamedMonsters = [NodePath("):
            out.append("NamedMonsters = []\n")
            cleared_arrays += 1
            i += 1
            continue

        out.append(line)
        i += 1

    path.write_text("".join(out), encoding="utf-8", newline="\n")
    print(
        f"Removed {removed_monsters} monster nodes, cleared {cleared_arrays} spawner arrays, "
        f"output lines: {len(out)} (was {len(lines)})"
    )


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PATH
    strip_monsters(target)
