import re
from pathlib import Path

t = Path("Godot/Scenes/MainServer.tscn").read_text(encoding="utf-8")
print(
    "with slots", len(re.findall(r"(?m)^BakedSpawnSlots = Array\[Vector3\]\(\[.+", t))
)
print("ERROR named", len(re.findall(r'(?m)^\[node name="ERROR - MonsterSpawner_', t)))
print("spawners", len(re.findall(r'(?m)^\[node name="(?:ERROR - )?MonsterSpawner_', t)))
