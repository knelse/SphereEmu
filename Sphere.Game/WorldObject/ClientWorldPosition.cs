using Godot;
using SphServer.Client;
using SphServer.Shared.Db.DataModels;

namespace SphServer.Sphere.Game.WorldObject;

/// <summary>
///     Resolves a connected client to Godot world space — the same space as persisted scene placements
///     and <see cref="Node3D.GlobalPosition" />.
/// </summary>
public static class ClientWorldPosition
{
	public static bool TryGetGodotWorldPosition(SphereClient client, out Vector3 worldPosition)
	{
		worldPosition = Vector3.Zero;
		if (client.CurrentCharacter is null)
		{
			return false;
		}

		worldPosition = ResolveGodotWorldPosition(client, client.CurrentCharacter);
		return true;
	}

	public static Vector3 GetGodotWorldPosition(SphereClient client)
	{
		if (!TryGetGodotWorldPosition(client, out var worldPosition))
		{
			return Vector3.Zero;
		}

		return worldPosition;
	}

	private static Vector3 ResolveGodotWorldPosition(SphereClient client, CharacterDbEntry character)
		=> character.Origin;
}
