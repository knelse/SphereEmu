using Godot;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Godot.Scripts.Terrain;
using SphServer.Shared.BitStream;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Godot.Tools;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Sphere.Game.WorldObject;
using static Stat;

namespace SphServer.Client;

public partial class SphereClient
{
	private CharacterBody3D? clientModel;

	/// <summary>
	/// g_ground code 5: the water plane is at or below the feet, and within 30 units
	/// </summary>
	public bool IsUnderwater { get; private set; }

	// Breath from the IsUnderwater rising edge; a tick starts another while it stays set
	private const double UnderwaterBreathSeconds = 6;
	private int underwaterTickGeneration;
	private bool underwaterTickArmed;
	private SceneTreeTimer? underwaterTickTimer;

	// SceneTreeTimer is refcounted; a local dies before Timeout
	private SceneTreeTimer? playerRespawnTimer;

	private void EnsureClientModel ()
	{
		clientModel ??= NodeChildTools.FindFirstChildOfType<CharacterBody3D> (this, "ClientModel");
	}

	public void UpdateCoordinatesInWorld ()
	{
		var transform = Transform;
		transform.Origin = CurrentCharacter!.Origin;
		Transform = transform;
		UpdateUnderwater ();
		WorldObjectVisibilityManager.RefreshRegistration (this);
	}

	/// <summary>
	/// Stored Y and Z are negated client axes, same as the ping write
	/// </summary>
	public void UpdateUnderwater ()
	{
		var character = CurrentCharacter;
		IsUnderwater = character is not null
					   && TerrainWater.IsUnderwater (character.X, -character.Y, -character.Z);
		SyncUnderwaterBreath ();
	}

	public void DisarmUnderwaterBreath ()
	{
		if (!underwaterTickArmed && underwaterTickTimer is null)
		{
			return;
		}

		underwaterTickArmed = false;
		underwaterTickTimer = null;
		underwaterTickGeneration++;
	}

	private void SyncUnderwaterBreath ()
	{
		var character = CurrentCharacter;
		if (!IsUnderwater || character is null || character.CurrentHP <= 0)
		{
			DisarmUnderwaterBreath ();
			return;
		}

		if (underwaterTickArmed)
		{
			return;
		}

		ArmUnderwaterBreath ();
	}

	private void ArmUnderwaterBreath ()
	{
		if (GetTree () is null)
		{
			return;
		}

		underwaterTickGeneration++;
		var generation = underwaterTickGeneration;
		underwaterTickArmed = true;
		underwaterTickTimer = GetTree ().CreateTimer (UnderwaterBreathSeconds);
		underwaterTickTimer.Timeout += () => OnUnderwaterBreath (generation);
	}

	private void OnUnderwaterBreath (int generation)
	{
		if (!GodotObject.IsInstanceValid (this) || generation != underwaterTickGeneration)
		{
			return;
		}

		underwaterTickArmed = false;
		underwaterTickTimer = null;
		var character = CurrentCharacter;
		if (!IsUnderwater || character is null || character.CurrentHP <= 0)
		{
			return;
		}

		var damage = (character.MaxHP + 5) / 6;
		if (damage <= 0)
		{
			return;
		}

		EnqueueClientEvent (new CharacterHealthChangeEvent (character.EntityId, -damage,
			Origin: DamageOriginSpecial.НехваткаВоздуха));
		if (character.CurrentHP > damage)
		{
			ArmUnderwaterBreath ();
		}
	}

	public void InitializeInteractions ()
	{
		SphLogger.Info ($"Initializing client interactions. Client ID: {EntityID}");

		if (clientModel is null)
		{
			SphLogger.Error ($"Cannot initialize client interactions: client model is null. Client ID: {EntityID}");
			return;
		}

		clientModel.ProcessMode = ProcessModeEnum.Inherit;
		clientModel.CollisionLayer = 2;
		clientModel.CollisionMask = 0b11;

		// Collision comes from base._Ready, which waits until the client is in game
		base._Ready ();
	}

	/// <summary>
	/// Delay stands in for city placement
	/// </summary>
	public void SchedulePlayerRespawn (float delaySeconds = 5f)
	{
		if (IsAdminDebugDummy || GetTree () is null)
		{
			return;
		}

		playerRespawnTimer = GetTree ().CreateTimer (delaySeconds);
		playerRespawnTimer.Timeout += ReviveAfterDeath;
	}

	public void ReviveAfterDeath ()
	{
		if (CurrentCharacter is null || CurrentCharacter.CurrentHP > 0)
		{
			return;
		}

		var respawnHp = (ushort) Math.Min (100, (int) CurrentCharacter.MaxHP);
		if (respawnHp == 0)
		{
			return;
		}

		CurrentCharacter.CurrentHP = respawnHp;

		var x = (float) CurrentCharacter.X;
		var y = (float) -CurrentCharacter.Y;
		var z = (float) -CurrentCharacter.Z;
		var yaw = (float) CurrentCharacter.Angle;

		// hit_type 0 flags 6 clears the WaitReinc latch
		MaybeQueueNetworkPacketSend (
			CommonPackets.BuildPlayerReceiveHit (EntityID, hitType: 0, hpDelta: 0, secondDelta: 0, flags: 6));
		// cmd 15 restarts PrgMove at this pose
		MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerSetCoordinates (EntityID, x, y, z, yaw));
		MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerSetCurrentHp (EntityID, respawnHp));
		MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerSetStat (EntityID, (byte) HpCurrent, respawnHp));
		MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerSetStat (EntityID, (byte) MpCurrent, CurrentCharacter.CurrentMP));
		MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerPlayEffect (EntityID, 758));
		NetworkedStatsUpdater.Update (CurrentCharacter, refreshPeers: false);
		BroadcastHpToVisibleClients (respawnHp, respawnHp, includeMax: true);
		SaveCharacter ();
		SyncUnderwaterBreath ();

		SphLogger.Info (
			$"Player respawn after death. Client ID: {EntityID}, SetCoordinates, effect 758, HP={respawnHp}.");
	}
}
