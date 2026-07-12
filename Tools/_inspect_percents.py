"""Ad-hoc: grid of collider decimation levels (rows=models, cols=percent)."""

import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import trimesh  # noqa: E402
from mpl_toolkits.mplot3d.art3d import Poly3DCollection  # noqa: E402

names = sys.argv[1:]
levels = ["100", "75", "66", "50", "25", "10"]

fig = plt.figure(figsize=(len(levels) * 3, len(names) * 2.6))
for r, name in enumerate(names):
    for c, level in enumerate(levels):
        suffix = "" if level == "100" else f"_{level}"
        path = Path(f"Godot/Models/Colliders/{name}_preview{suffix}.glb")
        scene = trimesh.load(str(path), force="scene")
        col = None
        for node in scene.graph.nodes_geometry:
            transform, gname = scene.graph[node]
            if "_col" not in node and "_col" not in gname:
                continue
            m = scene.geometry[gname].copy()
            m.apply_transform(transform)
            m.vertices = m.vertices[:, [0, 2, 1]]
            col = m
        ax = fig.add_subplot(
            len(names), len(levels), r * len(levels) + c + 1, projection="3d"
        )
        pc = Poly3DCollection(
            col.vertices[col.faces],
            alpha=0.55,
            facecolor="limegreen",
            edgecolor="k",
            linewidths=0.2,
        )
        ax.add_collection3d(pc)
        cen = col.bounds.mean(axis=0)
        rad = float(max(col.extents)) / 2 * 1.05
        ax.set_xlim(cen[0] - rad, cen[0] + rad)
        ax.set_ylim(cen[1] - rad, cen[1] + rad)
        ax.set_zlim(cen[2] - rad, cen[2] + rad)
        ax.set_axis_off()
        ax.view_init(elev=20, azim=-60)
        ax.set_title(f"{name} {level}% ({len(col.faces)} tris)", fontsize=7)
fig.tight_layout()
out = Path("Godot/Models/Colliders/_percent_sheet.png")
fig.savefig(out, dpi=100)
print(out)
