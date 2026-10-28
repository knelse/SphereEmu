using System.Buffers.Binary;
using Godot;
using SphServer.Client;
using SphServer.Helpers;

namespace SphServer.Server.Simulation;

/// <summary>
/// Channel 3 Teleport: game-frame x, y, z and heading, the arguments of the client's MbcPlayer::Teleport
/// </summary>
public static class PlayerTeleport
{
	public const int PayloadBytes = 16;

	/// <summary>
	///     Places the body, then tells the client, whose own Teleport runs the fade and the room
	///     load. A known continent is stored as given. A dungeon destination with none takes the
	///     nearest city
	/// </summary>
	public static bool Move (SphereClient client, WorldCoords coords, Continents? known = null) =>
		Move (client, new Vector3 ((float) coords.x, (float) coords.y, (float) coords.z), (float) coords.turn,
			known, trackContinent: true);

	/// <summary>
	///     trackContinent writes a known continent, or the nearest city when the destination is a
	///     dungeon. Doors leave it false so a castle interior does not replace the surface continent
	/// </summary>
	public static bool Move (SphereClient client, Vector3 raw, float? yaw, Continents? known = null,
		bool trackContinent = false)
	{
		if (!PlayerMotion.Place (client, raw, yaw, out var placed))
		{
			return false;
		}

		if (trackContinent && client.CurrentCharacter is { } character)
		{
			if (known is { } continent)
			{
				character.LastContinent = continent;
			}
			else if (!SavedCoords.TryContinent (raw.Y, raw.Z, out _)
				&& SavedCoords.TryNearestCityContinent (raw.X, raw.Z, out var inferred))
			{
				character.LastContinent = inferred;
			}
		}

		var heading = PlayerMotion.FindModel (client)?.Rotation.Y ?? yaw ?? 0f;
		client.SendReliable (ConnectionCommand.Teleport, Encode (placed, heading));
		return true;
	}

	public static byte[] Encode (Vector3 raw, float heading)
	{
		var bytes = new byte[PayloadBytes];
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (0, 4), raw.X);
		// Channel 2 draws the body VisualLiftMeters up, so the client lands on the pose it then gets
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (4, 4), raw.Y - SphereClient.VisualLiftMeters);
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (8, 4), raw.Z);
		BinaryPrimitives.WriteSingleLittleEndian (bytes.AsSpan (12, 4), heading);
		return bytes;
	}
}
