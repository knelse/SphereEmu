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
	{
		// Prefer the live Node3D pose when the client is in the scene tree — Character.Origin can lag a
		// packet behind (or still hold pre-login DB values) while GlobalPosition is kept current by
		// SphereClient.UpdateCoordinatesInWorld. Fall back to Origin when the node is not in the tree yet.
		if (client.IsInsideTree())
		{
			return client.GlobalPosition;
		}

		return character.Origin;
	}
}
