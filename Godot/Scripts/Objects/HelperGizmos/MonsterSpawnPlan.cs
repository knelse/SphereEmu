using System.Collections.Generic;
using Godot;

namespace SphServer.Godot.Scripts.Objects.HelperGizmos;

/// <summary>
/// Built off the Godot thread. Instantiation applies it on the Godot thread
/// </summary>
public sealed class MonsterSpawnPlan
{
    public MonsterSpawnPlan (List<Vector3> regularPositions, List<Vector3> namedPositions)
    {
        RegularPositions = regularPositions;
        NamedPositions = namedPositions;
    }

    public List<Vector3> RegularPositions { get; }
    public List<Vector3> NamedPositions { get; }
}
