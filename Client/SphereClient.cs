using Godot;
using SphServer.Client.Networking;
using SphServer.Client.Networking.GameplayLogic.Stats;
using SphServer.Client.State;
using SphServer.Packets;
using SphServer.Server.Broadcast;
using SphServer.Server.Config;
using SphServer.Server.Debug.Parser;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Godot.Tools;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;
using SphServer.System;
using static Stat;

namespace SphServer.Client;

public partial class SphereClient : WorldObject
{
	public readonly ClientStateManager ClientStateManager = new(false);
	private ClientConnection clientConnection = null!;
	private ClientEvents clientEvents = null!;

	private CharacterBody3D? clientModel;
	public CharacterDbEntry? CurrentCharacter;
	private ClientState currentState = ClientState.I_AM_BREAD;
	private bool isExiting;
	public ushort localId;
	/// <summary>Admin UI only — no TCP; skip network/disconnect. See <c>AdminDebugDummyClient</c>.</summary>
	public bool IsAdminDebugDummy { get; private set; }
	private PlayerDbEntry? playerDbEntry;
	private int selectedCharacterIndex;
	private StreamPeerTcp streamPeerTcp = null!;

	internal Area3D? BroadcastArea3D { get; private set; }

	public override async void _PhysicsProcess(double delta)
	{
		if (IsAdminDebugDummy)
		{
			return;
		}

		if (streamPeerTcp.GetStatus() != StreamPeerSocket.Status.Connected)
		{
			RemoveClient();
			return;
		}

		clientModel ??= NodeChildTools.FindFirstChildOfType<CharacterBody3D>(this, "ClientModel");
		if (BroadcastArea3D is null)
		{
			var area = NodeChildTools.FindFirstChildOfType<Area3D>(this);
			if (area is not null && NodeChildTools.FindFirstChildOfType<CollisionShape3D>(area) is not null)
			{
				BroadcastArea3D = area;
			}
		}

		await clientConnection.Process(delta);
		await clientEvents.HandleEventsAsync();
	}

	public override void _Ready()
	{
		// TODO: client logs in separate files
		SphLogger.Info($"New client connected. Client ID: {localId:X4}");

		// Task.Run(() =>
		// {
		//     while (true)
		//     {
		//         var input = Console.ReadLine();
		//         try
		//         {
		//             if (CurrentCharacter is null)
		//             {
		//                 Console.WriteLine("Character is null");
		//                 continue;
		//             }
		//
		//             var parser = ConsoleCommandParser.Get(CurrentCharacter);
		//             var result = parser.Parse(input);
		//         }
		//         catch (Exception ex)
		//         {
		//             Console.WriteLine(ex.Message);
		//         }
		//     }
		// });
	}

	public SphereClient Setup(StreamPeerTcp streamPeer, ushort id)
	{
		clientEvents = new ClientEvents(this);
		clientConnection = new ClientConnection(streamPeer, id, this);
		localId = id;
		streamPeerTcp = streamPeer;

		return this;
	}

	/// <summary>No-TCP client for admin UI debugging. Pair with <c>AdminDebugDummyClient</c>.</summary>
	public SphereClient SetupAdminDebugDummy(ushort id)
	{
		IsAdminDebugDummy = true;
		localId = id;
		clientEvents = new ClientEvents(this);
		return this;
	}

	public void EnqueueClientEvent(ClientQueuedEvent clientEvent)
	{
		clientEvents.Enqueue(clientEvent);
	}

	public void SetPlayerDbEntry(PlayerDbEntry? entry)
	{
		playerDbEntry = entry;
	}

	public CharacterDbEntry? GetSelecterCharacter()
	{
		return CurrentCharacter;
	}

	/// <summary>Write the character back to the database.</summary>
	public void SaveCharacter()
	{
		// Characters are [BsonRef], so they live in their own collection and the player row only
		// holds references — updating the player alone leaves the character document untouched.
		if (CurrentCharacter is not null)
		{
			if (CurrentCharacter.Id == 0)
			{
				SphLogger.Error($"SaveCharacter: character Id is 0, Insert instead. Client ID: {localId:X4}");
				CurrentCharacter.Id = DbConnection.Characters.Insert(CurrentCharacter);
			}
			else if (!DbConnection.Characters.Update(CurrentCharacter))
			{
				// Update returns false when the row is missing (common after a thin BsonRef stub).
				SphLogger.Warning(
					$"SaveCharacter: Characters.Update({CurrentCharacter.Id}) missed, Upsert. " +
					$"Client ID: {localId:X4}");
				DbConnection.Characters.Upsert(CurrentCharacter);
			}
		}

		if (playerDbEntry is not null)
		{
			DbConnection.Players.Update(playerDbEntry);
		}

		DbConnection.Checkpoint();
	}

	public void SetSelectedCharacterIndex(int index)
	{
		selectedCharacterIndex = index;
		try
		{
			var listed = playerDbEntry!.Characters[index];
			// BsonRef Include can hand back a thin stub; reload the Characters row so Base*/Items
			// match LiteDB (same fix as AdminDebugDummyClient).
			var character = listed.Id != 0
				? DbConnection.Characters.Query()
					.Include(["$.Clan"])
					.Where(c => c.Id == listed.Id)
					.FirstOrDefault()
				: null;
			if (character is null)
			{
				character = listed;
				SphLogger.Warning(
					$"SetSelectedCharacterIndex: no Characters row for id {listed.Id}, using list stub. " +
					$"Client ID: {localId:X4}");
			}
			else
			{
				playerDbEntry.Characters[index] = character;
			}

			CurrentCharacter = character;
			CurrentCharacter.ClientIndex = localId;
			CurrentCharacter.ClientLocalId = localId;
			UpdateCharacterForDebugMode();
			ClientStateEvents.RaiseCharacterChanged(localId);
		}
		catch (Exception ex)
		{
			SphLogger.Error($"Unable to select character at index: {index}. Client ID: {localId:X4}.", ex);
		}
	}

	public void CreatePlayerCharacter(CharacterDbEntry newCharacter, int index)
	{
		try
		{
			DbConnection.Characters.Insert(newCharacter);
			playerDbEntry!.Characters.Insert(index, newCharacter);
			DbConnection.Players.Update(playerDbEntry!);
		}
		catch (Exception ex)
		{
			SphLogger.Error($"Unable to create character at index: {index}. Client ID: {localId:X4}.", ex);
		}
	}

	public void DeletePlayerCharacter(int index)
	{
		// TODO: move to db entry
		try
		{
			var characterToDelete = playerDbEntry!.Characters[index];
			var id = characterToDelete.Id;
			var name = characterToDelete.Name;
			SphLogger.Info($"Delete character [{index}] - [{name}]. Client ID: {localId:X4}");
			playerDbEntry!.Characters.RemoveAt(index);
			DbConnection.Players.Update(playerDbEntry);
			DbConnection.Characters.Delete(id);

			// TODO: reinit session after delete
			RemoveClient();
		}
		catch (Exception ex)
		{
			SphLogger.Error($"Unable to delete character at index: {index}. Client ID: {localId:X4}.", ex);
		}
	}

	public void RemoveClient()
	{
		if (isExiting)
		{
			return;
		}

		SphLogger.Info($"Client disconnected. Client ID: {localId:X4}");

		isExiting = true;
		SaveCharacter();
		DbConnection.Checkpoint();
		if (!IsAdminDebugDummy)
		{
			clientConnection.Close();
		}

		ActiveClients.Remove(localId);
		ConsoleCommandParser.Invalidate(localId);
		ActiveNodes.Remove(GetInstanceId());

		if (playerDbEntry is not null)
		{
			ActiveWorldObjects.LoggedInClients.Remove(playerDbEntry.Login, out _);
		}

		PlayerCountBroadcast.OnClientLeftWorld();

		QueueFree();
	}

	public void MaybeQueueNetworkPacketSend(byte[] packet)
	{
		if (IsAdminDebugDummy)
		{
			return;
		}

		clientConnection.MaybeScheduleNetworkPacketSend(packet);
	}

	public ushort GetLocalObjectId(int id)
	{
		// TODO: implement
		return (ushort)id;
	}

	public ushort GetGlobalObjectId(int id)
	{
		// TODO: implement
		return (ushort)id;
	}

	public static ushort GetLocalObjectId(ushort clientId, int id)
	{
		// TODO: implement
		return (ushort)id;
	}

	public void UpdateCoordinatesInWorld()
	{
		var transform = Transform;
		transform.Origin = CurrentCharacter!.Origin;
		Transform = transform;
		WorldObjectVisibilityManager.RefreshRegistration(this);
	}

	public void InitializeInteractions()
	{
		SphLogger.Info($"Initializing client interactions. Client ID: {localId:X4}");

		if (clientModel is null)
		{
			SphLogger.Error($"Cannot initialize client interactions: client model is null. Client ID: {localId:X4}");
			return;
		}

		clientModel.ProcessMode = ProcessModeEnum.Inherit;
		clientModel.CollisionLayer = 2;
		clientModel.CollisionMask = 0b11;

		// WorldObject.ID
		ID = localId;

		// base.Ready sets up collision. For clients, we only want that to happen when they're ready for interactions
		// aka when they're in game
		base._Ready();
	}

	public string GetIpAddressAndPort()
	{
		if (IsAdminDebugDummy)
		{
			return "debug:0";
		}

		return streamPeerTcp.GetConnectedHost() + ':' + streamPeerTcp.GetConnectedPort();
	}

	public string GetIpAddressWithoutPort()
	{
		if (IsAdminDebugDummy)
		{
			return "debug";
		}

		return streamPeerTcp.GetConnectedHost();
	}

	public string? GetLogin()
	{
		return playerDbEntry?.Login;
	}

	protected override void ShowForClient(SphereClient client)
	{
		if (client.GetInstanceId() == GetInstanceId())
		{
			// do not show for itself
			return;
		}

		SphLogger.Info($"Showing for other client: {client.localId:X4}. Client ID: {localId:X4}");
		base.ShowForClient(client);
	}

	/// <summary>
	///     In-place gear look via _player SetWornGear (region 6), plus classic entity_character
	///     re-show (no despawn) so peers that never started Image still refresh.
	/// </summary>
	public override void BroadcastAppearanceRefreshToVisibleClients()
	{
		if (CurrentCharacter is null)
		{
			return;
		}

		var wear = CharacterWornLook.ToWearPattern(CurrentCharacter);
		ForEachVisibleClient(viewer =>
		{
			var entityId = viewer.GetLocalObjectId(ID);
			viewer.MaybeQueueNetworkPacketSend(CommonPackets.BuildSetWornGearPacket(entityId, wear));
			ShowForClient(viewer);
		});
	}

	/// <summary>
	///     Self: classic 08C0 HP bar + Manager SystemMessage mode2 (gMsg 120 chat).
	///     Viewers: ContMan ApplyHp (ShowKill float) then WriteIndexedStat absolute HP
	///     so the nameplate bar is correct even if ContMan is ignored.
	/// </summary>
	public void BroadcastApplyHpDelta(int hpDelta)
	{
		if (CurrentCharacter is null || hpDelta == 0)
		{
			return;
		}

		var selfId = CurrentCharacter.ClientIndex;
		var character = CurrentCharacter;
		NetworkedStatsUpdater.Update(character, refreshPeers: false);

		// SendSys2-compatible: mode2 sprintf gMsg(120) with signed delta.
		var chatColor = hpDelta > 0
			? 0x32B496u // pack_rgb24(50, 180, 150) heal
			: 0xB46432u; // pack_rgb24(180, 100, 50) damage
		MaybeQueueNetworkPacketSend(
			CommonPackets.BuildPlayerSystemChatSprintf(selfId, gmsgId: 120, (short)hpDelta, chatColor));

		ForEachVisibleClient(viewer =>
		{
			var entityId = viewer.GetLocalObjectId(ID);
			// ContMan first (popup + optional delta), then absolute WriteIndexedStat wins for bar.
			viewer.MaybeQueueNetworkPacketSend(
				CommonPackets.BuildPlayerApplyHpDelta(entityId, entityId, hpDelta));
			viewer.MaybeQueueNetworkPacketSend(
				CommonPackets.BuildPlayerWriteIndexedStat(entityId, (byte)HpCurrent, character.CurrentHP));
			viewer.MaybeQueueNetworkPacketSend(
				CommonPackets.BuildPlayerWriteIndexedStat(entityId, (byte)HpMax, character.MaxHP));
		});
	}

	/// <summary>
	///     Pushes nameplate fields to viewers via TradeMan WriteIndexedStat.
	///     FULL_SPAWN re-show does not update an already-spawned peer entity.
	///     Clan name is separate: <see cref="BroadcastClanRefreshToVisibleClients"/>.
	/// </summary>
	public void BroadcastNameplateRefreshToVisibleClients()
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

		ForEachVisibleClient(viewer =>
		{
			var entityId = viewer.GetLocalObjectId(ID);
			var packets = new List<byte[]>(9);
			CommonPackets.AppendPlayerNameplateStatPackets(packets, entityId,
				character.CurrentHP, character.MaxHP, (int)character.Karma,
				titleLevel, degreeLevel, titleRebirth, degreeRebirth,
				(int)character.Guild, character.GuildLevelMinusOne);
			foreach (var packet in packets)
			{
				viewer.MaybeQueueNetworkPacketSend(packet);
			}
		});
	}

	/// <summary>
	///     Pushes clan name + rank to self and every viewer (Manager SetClan-style classic frame).
	/// </summary>
	public void BroadcastClanRefreshToVisibleClients()
	{
		if (CurrentCharacter is null)
		{
			return;
		}

		var clanName = CurrentCharacter.Clan?.Name;
		if (string.IsNullOrEmpty(clanName)
			|| CurrentCharacter.Clan?.Id == ClanDbEntry.DefaultClanDbEntry.Id)
		{
			return;
		}

		var rank = (int)CurrentCharacter.ClanRank;
		MaybeQueueNetworkPacketSend(
			CommonPackets.BuildClanRankPacket(CurrentCharacter.ClientIndex, clanName, rank));

		ForEachVisibleClient(viewer =>
		{
			var entityId = viewer.GetLocalObjectId(ID);
			viewer.MaybeQueueNetworkPacketSend(
				CommonPackets.BuildClanRankPacket(entityId, clanName, rank));
		});
	}

	private void UpdateCharacterForDebugMode()
	{
		// TODO: move to db entry
		if (!ServerConfig.AppConfig.DebugMode)
		{
			SphLogger.Info($"Skipping debug mode update (debug mode off). Client ID: {localId:X4}");
			return;
		}

		if (CurrentCharacter is null)
		{
			SphLogger.Info($"Skipping debug mode update (character is null). Client ID: {localId:X4}");
			return;
		}

		CurrentCharacter.X = ServerConfig.AppConfig.Spawn_X;
		CurrentCharacter.Y = -ServerConfig.AppConfig.Spawn_Y;
		CurrentCharacter.Z = -ServerConfig.AppConfig.Spawn_Z;
		CurrentCharacter.Angle = ServerConfig.AppConfig.Spawn_Angle;
		CurrentCharacter.Money = ServerConfig.AppConfig.Spawn_Money;
	}

	protected override List<PacketPart> GetPacketParts()
	{
		return PacketPart.LoadDefinedWithOverride("entity_character");
	}

	protected override List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		var character = CurrentCharacter!;
		// Same nine-byte look block as character-list / CheckWeapon (boots…helmet).
		CharacterWornLook.Apply(character);

		var nameBytes = SphEncoding.Win1251.GetBytes(character.Name);
		PacketPart.UpdateValue(packetParts, "character_name_length", nameBytes.Length, 8);
		PacketPart.UpdateValue(packetParts, "character_name", character.Name);

		PacketPart.UpdateValue(packetParts, "face_model", character.FaceType, 8);
		PacketPart.UpdateValue(packetParts, "hair_model", character.HairStyle, 8);
		PacketPart.UpdateValue(packetParts, "hair_color_model", character.HairColor, 8);
		PacketPart.UpdateValue(packetParts, "tattoo_model", character.Tattoo, 8);

		PacketPart.UpdateValue(packetParts, "current_hp", character.CurrentHP, 14);
		PacketPart.UpdateValue(packetParts, "max_hp", character.MaxHP, 14);
		PacketPart.UpdateValue(packetParts, "is_female", character.IsGenderFemale ? 1 : 0, 1);
		PacketPart.UpdateValue(packetParts, "karma", (int)character.Karma, 3);
		PacketPart.UpdateValue(packetParts, "title_level", character.TitleMinusOne % 60, 6);
		PacketPart.UpdateValue(packetParts, "degree_level", character.DegreeMinusOne % 60, 6);
		PacketPart.UpdateValue(packetParts, "guild", (int)character.Guild, 4);
		PacketPart.UpdateValue(packetParts, "guild_level", character.GuildLevelMinusOne, 4);
		PacketPart.UpdateValue(packetParts, "rebirth_title", character.TitleMinusOne / 60, 2);
		PacketPart.UpdateValue(packetParts, "rebirth_degree", character.DegreeMinusOne / 60, 2);

		// 7-bit look codes; empty slots are ASCII '0' (retail), not zero.
		const byte emptyLook = (byte)'0';
		PacketPart.UpdateValue(packetParts, "weapon_model", 0, 7);
		PacketPart.UpdateValue(packetParts, "look_pad_0", 0, 7);
		PacketPart.UpdateValue(packetParts, "shoes_model", character.BootModelId & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "pants_model", character.PantsModelId & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "armor_model", character.ArmorModelId & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "robe_model", character.RobeModelId & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "gloves_model", character.GlovesModelId & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "shield_model", character.ShieldModelId & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "look_extra_1", emptyLook & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "look_extra_2", emptyLook & 0x7F, 7);
		PacketPart.UpdateValue(packetParts, "helmet_model", character.HelmetModelId & 0x7F, 7);

		return packetParts;
	}
}
