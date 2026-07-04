using Godot;

namespace SphServer.Godot.Scripts.Terrain.WalkSurface;

/// <summary>
///     Snapshot of one terrain cell's bake inputs, captured on the Godot main thread.
/// </summary>
internal readonly struct WalkSurfaceCellJob
{
    public WalkSurfaceCellJob(Vector3I cell, int itemId, Basis cellBasis, Vector3 centerLocal)
    {
        Cell = cell;
        ItemId = itemId;
        CellBasis = cellBasis;
        CenterLocal = centerLocal;
    }

    public Vector3I Cell { get; }
    public int ItemId { get; }
    public Basis CellBasis { get; }
    public Vector3 CenterLocal { get; }
}
