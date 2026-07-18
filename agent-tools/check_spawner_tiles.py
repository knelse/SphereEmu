import math, os, re, struct

ROOT = r"d:\SphereDev\SphereSource\SphereEmu"
map_path = os.path.join(ROOT, "Godot", "Terrain", "map.txt")
nav_dir = os.path.join(ROOT, "Godot", "Terrain", "GeneratedNavMeshes")
walk_dir = os.path.join(ROOT, "Godot", "Terrain", "WalkSurfaceData")

wx, wy, wz = -3714.2356, -1702.1195, 3312.9707
origin = (0, 0, 0)
tile_size = 100
radius = 8
GW, RS = 80, 22

local_x = wx - origin[0]
local_z = wz - origin[2]
gx = math.floor(local_x / tile_size)
gz = math.floor(local_z / tile_size)
print(f"Spawner: ({wx}, {wy}, {wz})")
print(f"Grid cell: gx={gx}, gz={gz}")

cells = {}
occ = {}
with open(map_path, "rb") as f:
    data = f.read()
for i in range(0, len(data), RS):
    rec = data[i : i + RS]
    if len(rec) < RS:
        break
    name = rec[:20].split(b"\x00")[0].decode("ascii", "replace").lower()
    v1, v2 = rec[20], rec[21]
    idx = i // RS
    gx_i = GW - (idx % GW) - 1
    gz_i = idx // GW
    if "fill_empt" in name:
        master = "fill_empt_00"
    elif not name.strip():
        continue
    else:
        master = f"{name}_{v1}{v2}"
    o = occ.get(master, 0)
    occ[master] = o + 1
    cells[(gx_i, gz_i)] = (master, o)

print("\nNeighborhood tiles:")
for dgx in range(-1, 2):
    for dgz in range(-1, 2):
        c = cells.get((gx + dgx, gz + dgz))
        print(f"  ({gx+dgx},{gz+dgz}): {c}")

def tile_key(master, o):
    s = re.sub(r"[^a-zA-Z0-9_]", "_", master)
    if s:
        s = s[0].upper() + s[1:]
    return f"{s}_{o:02d}"

keys = set()
min_gx = math.floor((local_x - radius) / tile_size)
max_gx = math.floor((local_x + radius) / tile_size)
min_gz = math.floor((local_z - radius) / tile_size)
max_gz = math.floor((local_z + radius) / tile_size)
for tgx in range(min_gx, max_gx + 1):
    for tgz in range(min_gz, max_gz + 1):
        c = cells.get((tgx, tgz))
        if c:
            keys.add(tile_key(c[0], c[1]))

print("\nTile keys in 8m radius (EnsureTilesLoaded):")
for k in sorted(keys):
    nav = os.path.exists(os.path.join(nav_dir, k + ".res"))
    print(f"  {k}: navmesh={nav}")

chunk_size = 512
cx = math.floor(wx / chunk_size)
cz = math.floor(wz / chunk_size)
print(f"\nWalk atlas chunks (512m), center cx={cx}, cz={cz}:")
for dx in (-1, 0, 1):
    for dz in (-1, 0, 1):
        fn = f"chunk_{cx+dx}_{cz+dz}.bin"
        print(f"  {fn}: {os.path.exists(os.path.join(walk_dir, fn))}")

print(f"\nY analysis:")
print(f"  Spawner Y={wy}")
print(f"  Successful outdoor slots nearby Z~3313 use Y~-160 (delta {abs(wy - (-160)):.0f}m)")
print(f"  WrongLevel threshold = max(15, 7*1.5) = 15m")
print(f"  If nav existed at -160, WrongLevel would fire before NotWalkable on disc pass")

region_rx = math.floor(wx / 500)
region_rz = math.floor(wz / 500)
print(f"\nBakeAllUnder region bucket: ({region_rx}, {region_rz})")
