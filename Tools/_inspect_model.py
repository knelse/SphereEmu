"""Ad-hoc: render a model (original only) from 4 azimuths for inspection."""

import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import trimesh  # noqa: E402
from mpl_toolkits.mplot3d.art3d import Poly3DCollection  # noqa: E402

name = sys.argv[1]
mesh = trimesh.util.concatenate(
    [g for g in trimesh.load(f"Godot/Models/{name}.glb", force="scene").dump()]
)
mesh.vertices = mesh.vertices[:, [0, 2, 1]]  # Y-up -> Z-up for matplotlib

fig = plt.figure(figsize=(16, 5))
for i, (elev, azim) in enumerate([(20, -60), (20, 30), (20, 120), (70, -60)]):
    ax = fig.add_subplot(1, 4, i + 1, projection="3d")
    pc = Poly3DCollection(
        mesh.vertices[mesh.faces],
        alpha=0.9,
        facecolor="steelblue",
        edgecolor="k",
        linewidths=0.15,
    )
    ax.add_collection3d(pc)
    c = mesh.bounds.mean(axis=0)
    r = float(max(mesh.extents)) / 2 * 1.05
    ax.set_xlim(c[0] - r, c[0] + r)
    ax.set_ylim(c[1] - r, c[1] + r)
    ax.set_zlim(c[2] - r, c[2] + r)
    ax.set_axis_off()
    ax.view_init(elev=elev, azim=azim)
    ax.set_title(f"elev={elev} azim={azim}", fontsize=8)
fig.suptitle(name)
fig.tight_layout()
out = Path(f"Godot/Models/Colliders/_inspect_{name}.png")
fig.savefig(out, dpi=100)
print(out)
