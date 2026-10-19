using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.GameData.Enums;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;

namespace SphServer.Sphere.Game.WorldObject;

public partial class WorldObject
{
    protected virtual void ShowForClient (SphereClient client)
    {
        // st_light3 takes the client down: gMsg gets a -1 container. Phoebe 4 streams dozens
        if (ObjectType == ObjectType.Light_Crystal_Yellow)
        {
            return;
        }

        var packetParts = GetPacketPartsAndUpdateCoordsAndID (client);
        packetParts = ModifyPacketParts (packetParts);
        var packet = PacketPart.GetBytesToWrite (packetParts);
        client.MaybeQueueNetworkPacketSend (packet);
    }

    protected virtual List<PacketPart> GetPacketParts ()
    {
        return PacketPart.LoadDefinedPartsFromFile (ObjectType);
    }

    public List<PacketPart> GetPacketPartsAndUpdateCoordsAndID (SphereClient client)
    {
        var packetParts = GetPacketParts ();
        PacketPart.UpdateCoordinates (packetParts, GlobalTransform.Origin.X, -GlobalTransform.Origin.Y,
            -GlobalTransform.Origin.Z, Angle);
        PacketPart.UpdateEntityId (packetParts, EntityID);
        return packetParts;
    }

    protected virtual List<PacketPart> ModifyPacketParts (List<PacketPart> packetParts)
    {
        PacketPart.UpdateValue (packetParts, "object_type", (int) ObjectType, 10);
        return packetParts;
    }

    protected virtual void ClientInteract (Guid clientID,
        ClientInteractionType interactionType = ClientInteractionType.Unknown)
    {
        SphLogger.Info ($"Client [{clientID}] interacts with [{EntityID}] {ObjectType} -- {interactionType}");
    }
}
