from pathlib import Path
import re

text = Path(
    r"d:/SphereDev/SphereSource/SphereEmu/Godot/Scenes/MainServer.tscn"
).read_text(encoding="utf-8")
m = re.search(
    r'\[node name="AlchemyMaterialSpawners".*?(?=\[node name="MonsterSpawners")',
    text,
    re.S,
)
block = m.group(0) if m else ""
ok = miss = 0
for n in re.split(r'\n(?=\[node name="AlchemyMaterial)', block):
    nm = re.search(r'\[node name="([^"]+)"', n)
    if not nm or nm.group(1) == "AlchemyMaterialSpawners":
        continue
    slots = 0
    if "BakedSpawnSlots" in n:
        slots = len(
            re.findall(
                r"Vector3\(", n.split("BakedSpawnSlots", 1)[1].split("\n[node", 1)[0]
            )
        )
    err = "HasBakeError = true" in n
    detail_m = re.search(r'BakeErrorDetail = "([^"]*)"', n)
    if slots > 0 and not err:
        ok += 1
    else:
        miss += 1
        print(
            f"{nm.group(1)} slots={slots} err={err} {detail_m.group(1) if detail_m else ''}"
        )
print(f"ok={ok} missing={miss}")
