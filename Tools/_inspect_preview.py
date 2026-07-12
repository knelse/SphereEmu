"""Ad-hoc: multi-angle render of a preview GLB.

Original model is blue, collider is green.
"""

import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import trimesh  # noqa: E402
from mpl_toolkits.mplot3d.art3d import Poly3DCollection  # noqa: E402

name = sys.argv[1]
path = Path(f"Godot/Models/Colliders/{name}_preview.glb")
scene = trimesh.load(str(path), force="scene")

meshes = []
for node in scene.graph.nodes_geometry:
    transform, gname = scene.graph[node]
    geom = scene.geometry[gname]
    if not isinstance(geom, trimesh.Trimesh):
        continue
    m = geom.copy()
    m.apply_transform(transform)
    m.vertices = m.vertices[:, [0, 2, 1]]
    meshes.append((m, "_col" in node or "_col" in gname))

bmin = np.min([m.bounds[0] for m, _ in meshes], axis=0)
bmax = np.max([m.bounds[1] for m, _ in meshes], axis=0)
c = (bmin + bmax) / 2
r = float(max(bmax - bmin)) / 2 * 1.05

fig = plt.figure(figsize=(16, 5))
for i, (elev, azim) in enumerate([(15, -60), (15, 30), (15, 120), (65, -45)]):
    ax = fig.add_subplot(1, 4, i + 1, projection="3d")
    for m, is_col in meshes:
        pc = Poly3DCollection(
            m.vertices[m.faces],
            alpha=0.45 if is_col else 0.85,
            facecolor="limegreen" if is_col else "steelblue",
            edgecolor="k",
            linewidths=0.2,
        )
        ax.add_collection3d(pc)
    ax.set_xlim(c[0] - r, c[0] + r)
    ax.set_ylim(c[1] - r, c[1] + r)
    ax.set_zlim(c[2] - r, c[2] + r)
    ax.set_axis_off()
    ax.view_init(elev=elev, azim=azim)
fig.suptitle(name)
fig.tight_layout()
out = Path(f"Godot/Models/Colliders/_inspect_{name}_preview.png")
fig.savefig(out, dpi=100)
print(out)
