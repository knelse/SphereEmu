"""Render preview GLBs to PNG contact sheets for quick visual verification."""

import sys
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np
import trimesh
from mpl_toolkits.mplot3d.art3d import Poly3DCollection


def plot_mesh(ax, mesh, color, alpha):
    tris = mesh.vertices[mesh.faces]
    pc = Poly3DCollection(
        tris, alpha=alpha, facecolor=color, edgecolor="k", linewidths=0.2
    )
    ax.add_collection3d(pc)


def render(path: Path, ax):
    scene = trimesh.load(str(path), force="scene")
    bounds_min = np.full(3, np.inf)
    bounds_max = np.full(3, -np.inf)
    for name, geom in scene.geometry.items():
        if not isinstance(geom, trimesh.Trimesh):
            continue
        for node in scene.graph.nodes_geometry:
            transform, gname = scene.graph[node]
            if gname != name:
                continue
            m = geom.copy()
            m.apply_transform(transform)
            # glTF is Y-up; matplotlib 3D uses Z as vertical, so swap Y/Z.
            m.vertices = m.vertices[:, [0, 2, 1]]
            is_col = "_col" in node or "_col" in name
            plot_mesh(
                ax, m, "limegreen" if is_col else "steelblue", 0.45 if is_col else 0.9
            )
            bounds_min = np.minimum(bounds_min, m.bounds[0])
            bounds_max = np.maximum(bounds_max, m.bounds[1])
    center = (bounds_min + bounds_max) / 2
    radius = float(max(bounds_max - bounds_min)) / 2 * 1.1 + 1e-6
    ax.set_xlim(center[0] - radius, center[0] + radius)
    ax.set_ylim(center[1] - radius, center[1] + radius)
    ax.set_zlim(center[2] - radius, center[2] + radius)
    ax.set_title(path.stem.replace("_preview", ""), fontsize=8)
    ax.set_axis_off()
    ax.view_init(elev=25, azim=-60)


def main():
    out_dir = Path("Godot/Models/Colliders")
    files = sorted(out_dir.glob("*_preview.glb"), key=lambda p: p.name.lower())
    if len(sys.argv) > 1:
        files = [f for f in files if f.stem.replace("_preview", "") in sys.argv[1:]]
    cols = 4
    rows = (len(files) + cols - 1) // cols
    fig = plt.figure(figsize=(cols * 4, rows * 3.2))
    for i, f in enumerate(files):
        ax = fig.add_subplot(rows, cols, i + 1, projection="3d")
        # trimesh Y-up -> plot with Z vertical by swapping axes at draw time
        render(f, ax)
    fig.tight_layout()
    out = out_dir / "_contact_sheet.png"
    fig.savefig(out, dpi=90)
    print(f"saved {out}")


if __name__ == "__main__":
    main()
