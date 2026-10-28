using Godot;
using SphServer.Client;
using SphServer.Helpers;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Server.Simulation;

/// <summary>
/// Channel 3 UseObject: the actor's guid, then the door or portal guid, as Guid.ToByteArray
/// </summary>
public static class ObjectUse
{
	public const int PayloadBytes = 32;

	// Client canUseFromHere: the door and the player under 6 m apart on every axis
	public const float ReachMeters = 6f;

	// A door or portal marker at special point n has its landing at n + 1000, which is the second
	// id TeleportObject::receiveItemData reads
	private const int LandingPointOffset = 1000;

	public static bool TryDecode (byte[] payload, out Guid source, out Guid target)
	{
		source = Guid.Empty;
		target = Guid.Empty;
		if (payload is null || payload.Length < PayloadBytes)
		{
			return false;
		}

		source = new Guid (payload.AsSpan (0, 16));
		target = new Guid (payload.AsSpan (16, 16));
		return true;
	}

	/// <summary>
	/// A rejected use leaves the body where it is and sends nothing back
	/// </summary>
	public static void Apply (Guid playerId, byte[] payload)
	{
		if (ActiveClients.Get (playerId) is not { } client || !PlayerMotion.IsInGame (client))
		{
			return;
		}

		if (!TryDecode (payload, out var source, out var target))
		{
			Reject (playerId, $"payload {payload?.Length ?? 0}B");
			return;
		}

		if (source != client.EntityID)
		{
			Reject (playerId, $"source {source:N} is not the sender");
			return;
		}

		if (client.CurrentCharacter is not { CurrentHP: > 0 })
		{
			Reject (playerId, "dead");
			return;
		}

		if (ActiveWorldObjects.Get (target) is not { } obj || !GodotObject.IsInstanceValid (obj) || !obj.IsInsideTree ())
		{
			Reject (playerId, $"no target {target:N}");
			return;
		}

		var body = PlayerMotion.FindModel (client);
		var from = body?.GlobalPosition ?? client.GlobalPosition;
		var offset = (from - obj.GlobalPosition).Abs ();
		var distance = Math.Max (offset.X, Math.Max (offset.Y, offset.Z));
		if (distance >= ReachMeters)
		{
			Reject (playerId, $"{obj.ObjectType} {target:N} at {distance:0.0} m on one axis");
			return;
		}

		if (!TryExit (obj, out var exit, out var yaw))
		{
			Reject (playerId, $"{obj.ObjectType} {target:N} has no exit");
			return;
		}

		if (!PlayerTeleport.Move (client, exit, yaw))
		{
			Reject (playerId, $"{obj.ObjectType} {target:N} placement failed");
			return;
		}

		if (obj is TeleportWithTarget
			&& client.CurrentCharacter is { } character
			&& SavedCoords.TryNearestCityContinent (exit.X, exit.Z, out var continent))
		{
			character.LastContinent = continent;
		}

		SphLogger.Info ($"USE {playerId:N}: {obj.ObjectType} {target:N} to ({exit.X:0.0},{exit.Y:0.0},{exit.Z:0.0})");
	}

	/// <summary>
	/// The exit in the game frame. A door target uses its angle when one is set, and a door without
	/// a target and a portal land on their special point, heading included
	/// </summary>
	private static bool TryExit (WorldObject obj, out Vector3 exit, out float? yaw)
	{
		exit = default;
		yaw = null;
		switch (obj)
		{
			case Door { HasTarget: true } door:
				exit = new Vector3 ((float) door.TargetX, (float) door.TargetY, (float) door.TargetZ);
				if (door.HasTargetAngle)
				{
					yaw = (float) door.TargetAngle;
				}

				return true;
			case Door door:
				return TryLanding (door.DoorID, out exit, out yaw);
			case TeleportWithTarget portal:
				return TryLanding (portal.SubtypeID, out exit, out yaw);
			default:
				return false;
		}
	}

	private static bool TryLanding (int marker, out Vector3 exit, out float? yaw)
	{
		exit = default;
		yaw = null;
		if (SphereServer.ServerNode?.Motion is not { } stream
			|| !stream.TryGetSpecialPoint (marker + LandingPointOffset, out var point))
		{
			return false;
		}

		exit = point.Position;
		yaw = point.Heading;
		return true;
	}

	private static void Reject (Guid playerId, string reason)
	{
		SphLogger.Info ($"USE {playerId:N}: rejected, {reason}");
	}
}
