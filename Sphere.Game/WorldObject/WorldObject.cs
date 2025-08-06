using System;
using System.Collections.Generic;
using Godot;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Server.Config;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

public partial class WorldObject : Node3D
{
	[Export] public int Angle { get; set; }
	[Export] public ushort ID { get; set; }
	[Export] public ObjectType ObjectType { get; set; } = ObjectType.Unknown;

	public override void _Ready ()
	{
		if (ID == 0)
		{
			ID = WorldObjectIndex.New();
		}

		ActiveNodes.Add(GetInstanceId(), this);
		ActiveWorldObjects.Add(ID, this);

		var area3D = new Area3D();
		area3D.CollisionLayer = 1;
		area3D.CollisionMask = 2;
		
		var collisionShape3d = new CollisionShape3D();
		collisionShape3d.Shape = new SphereShape3D
		{
			Radius = ServerConfig.AppConfig.ObjectVisibilityDistance
		};
		
		area3D.AddChild(collisionShape3d);
		area3D.BodyEntered += body =>
		{
			// layer mask should only allow clients here, so we assume it's a client

			var clientNode = body.GetParent();
			if (clientNode is not SphereClient client)
			{
				SphLogger.Info($"WorldObject {Name}: collision enter by {body.Name} which is not a SphereClient");
				return;
			}

			ShowForClient(client);
		};
		
		area3D.BodyExited += body =>
		{
			// layer mask should only allow clients here, so we assume it's a client

			var clientNode = body.GetParent();
			if (clientNode is not SphereClient client)
			{
				SphLogger.Info($"WorldObject {Name}: collision exit by {body.Name} which is not a SphereClient");
				return;
			}
			
			client.MaybeQueueNetworkPacketSend(CommonPackets.DespawnEntity(ID));
		};
		AddChild(area3D);
	}

	public void ShowForClient (SphereClient client)
	{
		var packetParts = GetPacketPartsAndUpdateCoordsAndID(client);
		packetParts = ModifyPacketParts(packetParts);
		var packet = PostprocessPacketBytes(PacketPart.GetBytesToWrite(packetParts));
		client.MaybeQueueNetworkPacketSend(packet);
	}

	protected virtual List<PacketPart> GetPacketParts ()
	{
		return PacketPart.LoadDefinedPartsFromFile(ObjectType);
	}

	public List<PacketPart> GetPacketPartsAndUpdateCoordsAndID (SphereClient client)
	{
		var packetParts = GetPacketParts();
		PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, GlobalTransform.Origin.Y,
		    GlobalTransform.Origin.Z, Angle);
		var localId = client.GetLocalObjectId(ID);
		PacketPart.UpdateEntityId(packetParts, localId);

		return packetParts;
	}

	protected virtual List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
	{
		return packetParts;
	}

	protected virtual byte[] PostprocessPacketBytes (byte[] packet)
	{
		return packet;
	}

	protected virtual void ClientInteract (ushort clientID,
		ClientInteractionType interactionType = ClientInteractionType.Unknown)
	{
		SphLogger.Info($"Client [{clientID:X4}] interacts with [{ID}] {ObjectType} -- {interactionType}");
	}
}
