using Godot;

namespace SphServer.Sphere.Game.WorldObject;

internal static partial class GlbVisualGrounding
{
    // The probe spans the network anchor and a jump, so a node at the anchor finds the surface it
    // stands over
    private const float ProbeAboveMeters = 1f;

    private const float ProbeBelowMeters = 4f;

    // A distant node's cell is not streamed, so a miss keeps the last lift, and the overlay refresh
    // and each nav step re-probe only where the node has moved
    private static readonly Dictionary<ulong, (Vector2 Xz, float Offset)> GroundOffsetCache = new ();

    private const float ReprobeMoveMetersSq = 0.0625f;

    /// <summary>
    /// The lift from the surface under a node to its pose, negative when the node sits above the
    /// ground, so a model grounded at the node origin drops onto the surface it stands over
    /// </summary>
    public static float GroundOffsetUnderNode (Node3D node)
    {
        var position = node.GlobalPosition;
        var id = node.GetInstanceId ();
        var cached = GroundOffsetCache.TryGetValue (id, out var entry);
        if (cached && entry.Xz.DistanceSquaredTo (new Vector2 (position.X, position.Z)) < ReprobeMoveMetersSq)
        {
            return entry.Offset;
        }

        if (node.GetWorld3D ()?.DirectSpaceState is not { } space)
        {
            return cached ? entry.Offset : 0f;
        }

        var from = position + Vector3.Up * ProbeAboveMeters;
        var to = position - Vector3.Up * ProbeBelowMeters;
        var query = PhysicsRayQueryParameters3D.Create (from, to);
        query.CollisionMask = 1;
        var hit = space.IntersectRay (query);
        if (hit.Count == 0)
        {
            return cached ? entry.Offset : 0f;
        }

        if (GroundOffsetCache.Count > 4096)
        {
            GroundOffsetCache.Clear ();
        }

        var offset = Mathf.Clamp (hit["position"].AsVector3 ().Y - position.Y, -ProbeBelowMeters, ProbeAboveMeters);
        GroundOffsetCache[id] = (new Vector2 (position.X, position.Z), offset);
        return offset;
    }
}
