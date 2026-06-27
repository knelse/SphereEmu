"""One-off: set SpawningEnabled=true on spawners within 100m of CityCenter TP points."""

from __future__ import annotations

import re
import sys
from pathlib import Path

DEFAULT_PATH = (
    Path(__file__).resolve().parents[1] / "Godot" / "Scenes" / "MainServer.tscn"
)
RADIUS_METERS = 100.0

# SavedCoords TeleportPoints[..][CityCenter], mapped to Godot: (x, -y, -z)
CITY_CENTERS_GODOT: list[tuple[float, float, float]] = [
    (2614.0, -157.8, -1293.0),  # Hyperion / Shipston
    (1882.0, -155.6, 407.0),  # Hyperion / Bangville
    (2292.0, -155.3, 2386.0),  # Hyperion / Torvil
    (422.0, -153.3, 1284.0),  # Hyperion / Sanpool
    (-2723.59, -404.75, -2110.18),  # Haron / Nomrad
    (1872.19, -402.77, -3293.8),  # Haron / Gifes
    (-1993.0, 104.5, -457.0),  # Phoebe / Umrad
    (-3397.0, 340.0, 813.0),  # Rodos / Ankhelm
]

TRANSFORM_RE = re.compile(
    r"Transform3D\("
    r"[^,]+,[^,]+,[^,]+,"
    r"[^,]+,[^,]+,[^,]+,"
    r"[^,]+,[^,]+,[^,]+,"
    r"\s*([-\d.]+),\s*([-\d.]+),\s*([-\d.]+)\s*\)"
)
SPAWNER_NODE_RE = re.compile(
    r'^\[node name="MonsterSpawner_[^"]+" parent="MonsterSpawners"'
)
SPAWNING_ENABLED_LINE = "SpawningEnabled = true\n"


def distance_squared(
    a: tuple[float, float, float], b: tuple[float, float, float]
) -> float:
    return sum((a[i] - b[i]) ** 2 for i in range(3))


def parse_transform(line: str) -> tuple[float, float, float] | None:
    match = TRANSFORM_RE.search(line)
    if not match:
        return None
    return float(match.group(1)), float(match.group(2)), float(match.group(3))


def is_near_city_center(position: tuple[float, float, float]) -> bool:
    radius_sq = RADIUS_METERS * RADIUS_METERS
    return any(
        distance_squared(position, center) <= radius_sq for center in CITY_CENTERS_GODOT
    )


def enable_town_spawners(path: Path) -> None:
    lines = path.read_text(encoding="utf-8").splitlines(keepends=True)
    out: list[str] = []
    enabled = 0
    scanned = 0
    i = 0

    while i < len(lines):
        line = lines[i]
        if SPAWNER_NODE_RE.match(line):
            scanned += 1
            block = [line]
            i += 1
            position: tuple[float, float, float] | None = None

            while i < len(lines) and not lines[i].startswith("[node "):
                block.append(lines[i])
                if lines[i].startswith("transform = "):
                    position = parse_transform(lines[i])
                i += 1

            if position is not None and is_near_city_center(position):
                enabled += 1
                updated_block: list[str] = []
                has_spawning_enabled = False
                for block_line in block:
                    if block_line.startswith("SpawningEnabled = "):
                        updated_block.append(SPAWNING_ENABLED_LINE)
                        has_spawning_enabled = True
                    else:
                        updated_block.append(block_line)

                if not has_spawning_enabled:
                    insert_at = len(updated_block)
                    for idx, block_line in enumerate(updated_block):
                        if block_line.startswith("RegularMonsters = "):
                            insert_at = idx + 1
                            break
                    updated_block.insert(insert_at, SPAWNING_ENABLED_LINE)

                out.extend(updated_block)
            else:
                out.extend(block)
            continue

        out.append(line)
        i += 1

    path.write_text("".join(out), encoding="utf-8", newline="\n")
    print(
        f"Scanned {scanned} spawners, enabled {enabled} within {RADIUS_METERS:.0f}m of city centers "
        f"({len(CITY_CENTERS_GODOT)} centers)"
    )


if __name__ == "__main__":
    target = Path(sys.argv[1]) if len(sys.argv) > 1 else DEFAULT_PATH
    enable_town_spawners(target)
