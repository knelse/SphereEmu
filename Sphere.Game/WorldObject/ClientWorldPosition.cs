using Godot;
using SphServer.Client;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
/// Godot world space, the same space as persisted scene placements
/// </summary>
public static class ClientWorldPosition
{
    public static bool TryGetGodotWorldPosition (SphereClient client, out Vector3 worldPosition)
    {
        worldPosition = Vector3.Zero;
        if (client.CurrentCharacter is null)
        {
            return false;
        }

        worldPosition = ResolveGodotWorldPosition (client, client.CurrentCharacter);
        return true;
    }

    public static Vector3 GetGodotWorldPosition (SphereClient client)
    {
        if (!TryGetGodotWorldPosition (client, out var worldPosition))
        {
            return Vector3.Zero;
        }

        return worldPosition;
    }

    private static Vector3 ResolveGodotWorldPosition (SphereClient client, CharacterDbEntry character)
    {
        // Origin can lag a packet or still hold pre-login values, so an in-tree node uses
        // GlobalPosition
        if (client.IsInsideTree ())
        {
            return client.GlobalPosition;
        }

        return character.Origin;
    }
}
