using Godot;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Godot.Tools;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.Networking.DataModel.Serializers;
using static Stat;

namespace SphServer.Client;

public partial class SphereClient
{
	internal Area3D? BroadcastArea3D { get; private set; }

	private void EnsureBroadcastArea ()
	{
		if (BroadcastArea3D is not null)
		{
			return;
		}

		var area = NodeChildTools.FindFirstChildOfType<Area3D> (this);
		if (area is not null && NodeChildTools.FindFirstChildOfType<CollisionShape3D> (area) is not null)
		{
			BroadcastArea3D = area;
		}
	}

	protected override void ShowForClient (SphereClient client)
	{
		if (client.GetInstanceId () == GetInstanceId ())
		{
			return;
		}

		if (CurrentCharacter is null)
		{
			return;
		}

		SphLogger.Info ($"Showing for other client: {client.EntityID}. Client ID: {EntityID}");
		var origin = GlobalTransform.Origin;
		client.MaybeQueueNetworkPacketSend (
			new CharacterDbEntrySerializer (CurrentCharacter).ToSpawnSnapshotByteArray (
				EntityID, origin.X, -origin.Y, -origin.Z, CurrentCharacter.Angle));
		client.MaybeQueueNetworkPacketSend (
			CommonPackets.BuildSetWornGearPacket (EntityID, CharacterWornLook.ToWearPattern (CurrentCharacter)));
	}

	/// <summary>
	/// SetWornGear (region 6) is what Image waits on after the first region 61
	/// </summary>
	public override void BroadcastAppearanceRefreshToVisibleClients ()
	{
		if (CurrentCharacter is null)
		{
			return;
		}

		var wear = CharacterWornLook.ToWearPattern (CurrentCharacter);
		ForEachVisibleClient (viewer =>
		{
			viewer.MaybeQueueNetworkPacketSend (CommonPackets.BuildSetWornGearPacket (EntityID, wear));
		});
	}

	/// <summary>
	/// Peer ContMan ApplyHp + absolute HpCurrent (and optional HpMax) for visible clients
	/// </summary>
	public void BroadcastHpToVisibleClients (int healthDiff, int absoluteHp, bool includeMax = false)
	{
		var maxHp = CurrentCharacter?.MaxHP ?? 0;
		ForEachVisibleClient (viewer =>
		{
			viewer.MaybeQueueNetworkPacketSend (
				CommonPackets.BuildPlayerApplyHpDelta (EntityID, EntityID, healthDiff));
			viewer.MaybeQueueNetworkPacketSend (
				CommonPackets.BuildPlayerWriteIndexedStat (EntityID, (byte) HpCurrent, absoluteHp));
			if (includeMax)
			{
				viewer.MaybeQueueNetworkPacketSend (
					CommonPackets.BuildPlayerWriteIndexedStat (EntityID, (byte) HpMax, maxHp));
			}
		});
	}

	/// <summary>
	/// WriteIndexedStat, because FULL_SPAWN does not refresh an already-spawned peer
	/// </summary>
	public void BroadcastNameplateRefreshToVisibleClients ()
	{
		if (CurrentCharacter is null)
		{
			return;
		}

		var character = CurrentCharacter;
		var titleLevel = character.TitleMinusOne % 60;
		var degreeLevel = character.DegreeMinusOne % 60;
		var titleRebirth = character.TitleMinusOne / 60;
		var degreeRebirth = character.DegreeMinusOne / 60;

		ForEachVisibleClient (viewer =>
		{
			var packets = new List<byte[]> (9);
			CommonPackets.AppendPlayerNameplateStatPackets (packets, EntityID,
				character.CurrentHP, character.MaxHP, (int) character.Karma,
				titleLevel, degreeLevel, titleRebirth, degreeRebirth,
				(int) character.Guild, character.GuildLevelMinusOne);
			foreach (var packet in packets)
			{
				viewer.MaybeQueueNetworkPacketSend (packet);
			}
		});
	}

	/// <summary>
	/// Classic Manager SetClan frame for the name and rank
	/// </summary>
	public void BroadcastClanRefreshToVisibleClients ()
	{
		if (CurrentCharacter is null)
		{
			return;
		}

		var clanName = CurrentCharacter.Clan?.Name;
		if (string.IsNullOrEmpty (clanName)
			|| CurrentCharacter.Clan?.Id == ClanDbEntry.DefaultClanDbEntry.Id)
		{
			return;
		}

		var rank = (int) CurrentCharacter.ClanRank;
		MaybeQueueNetworkPacketSend (
			CommonPackets.BuildClanRankPacket (EntityID, clanName, rank));

		ForEachVisibleClient (viewer =>
		{
			viewer.MaybeQueueNetworkPacketSend (
				CommonPackets.BuildClanRankPacket (EntityID, clanName, rank));
		});
	}
}
