using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

public partial class WorldObject
{
	protected virtual void ShowForClient(SphereClient client)
	{
		var packetParts = GetPacketPartsAndUpdateCoordsAndID(client);
		packetParts = ModifyPacketParts(packetParts);
		var packet = PostprocessPacketBytes(PacketPart.GetBytesToWrite(packetParts));
		client.MaybeQueueNetworkPacketSend(packet);
	}

	protected virtual List<PacketPart> GetPacketParts()
	{
		return PacketPart.LoadDefinedPartsFromFile(ObjectType);
	}

	public List<PacketPart> GetPacketPartsAndUpdateCoordsAndID(SphereClient client)
	{
		var packetParts = GetPacketParts();
		PacketPart.UpdateCoordinates(packetParts, GlobalTransform.Origin.X, -GlobalTransform.Origin.Y,
			-GlobalTransform.Origin.Z, Angle);
		var localId = client.GetLocalObjectId(ID);
		PacketPart.UpdateEntityId(packetParts, localId);
		return packetParts;
	}

	protected virtual List<PacketPart> ModifyPacketParts(List<PacketPart> packetParts)
	{
		PacketPart.UpdateValue(packetParts, "object_type", (int)ObjectType, 10);
		return packetParts;
	}

	protected virtual byte[] PostprocessPacketBytes(byte[] packet)
	{
		return packet;
	}

	protected virtual void ClientInteract(ushort clientID,
		ClientInteractionType interactionType = ClientInteractionType.Unknown)
	{
		SphLogger.Info($"Client [{clientID:X4}] interacts with [{ID}] {ObjectType} -- {interactionType}");
	}
}
