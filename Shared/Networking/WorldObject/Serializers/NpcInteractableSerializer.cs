using System.Collections.Generic;
using SphServer.Client;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db.DataModels;
using SphServer.Sphere.Game;

namespace SphServer.Shared.Networking.WorldObject.Serializers;

public class NpcInteractableSerializer (NpcInteractable npcInteractable)
{
    public byte[] ShowItemList (ushort clientId)
    {
        var localId = SphereClient.GetLocalObjectId(clientId, npcInteractable.ID);
        var stream = SphBitStream.GetWriteBitStream();
        
        // return;
        stream.WriteUInt16(localId);
        stream.WriteByte(0, 2);
        stream.WriteUInt16((ushort) npcInteractable.ObjectType, 10);
        stream.WriteByte(0, 1);
        // interaction
        stream.WriteByte(0x0A, 8);
        // open container
        stream.WriteUInt16(0x0103, 16);
        stream.WriteByte(0, 8);
        
        var itemSeparator = (ushort) 0b110000000001010;
        
        var packetBytes = new List<byte>();
        for (var i = 0; i < npcInteractable.GetMaxItemsOnSale(); i++)
        {
            var item = npcInteractable.ItemsOnSale[i];
            var slotId = i + 1;
            stream.WriteUInt16(itemSeparator, 15);
            stream.WriteByte((byte) slotId, 8);
        
            var itemLocalId = SphereClient.GetLocalObjectId(clientId, item.Id);
            stream.WriteUInt16(itemLocalId);
            stream.WriteBytes([0x00, 0x00, 0x00, 0x00, 0x00], 5, true);
            stream.WriteUInt32((uint) item.VendorCost, 32);
            // 74 seems to be max amount vendor can display
            if (slotId % 28 != 0 && slotId != 74)
            {
                continue;
            }
        
            // split
            var packetPiece2 = Packet.ToByteArray(stream.GetStreamData(), 3);
            packetPiece2[^1] = 0;
            packetBytes.AddRange(packetPiece2);
            stream.CutStream(0, 0);
            if (i == npcInteractable.ItemsOnSale.Count - 1)
            {
                break;
            }
        
            stream.WriteUInt16(localId);
            stream.WriteByte(0, 2);
            stream.WriteUInt16((ushort) npcInteractable.ObjectType, 10);
            stream.WriteByte(0, 2);
        }
        
        stream.WriteByte(0x3F, 7);
        stream.WriteUInt16(clientId);
        stream.WriteUInt32(0x62A34008);
        stream.WriteByte(0x0, 5);
        stream.WriteUInt16(localId);
        stream.WriteByte(0x0, 7);
        var streamData = stream.GetStreamData();
        streamData[^1] = 0;
        var packet = Packet.ToByteArray(streamData, 3);
        packetBytes.AddRange(packet);
        return packetBytes.ToArray();
    }

    public byte[] ShowItemContents (ushort clientId)
    {
        var stream = SphBitStream.GetWriteBitStream();
        var packetList = new List<byte>();
        for (var i = 0; i < npcInteractable.GetMaxItemsOnSale(); i++)
        {
            var item = npcInteractable.ItemsOnSale[i];
            WriteItemPacketToStream(clientId, item, stream);
            // if (i > 0 && i % 5 == 0)
            // {
            // live splits items into batches of 5 and client seems to break if we send more than 10 at a time
            // but for now we'll send one at a time so we don't have to stitch them properly
            // if (stream.Bit != 0)
            // {
            //     // 1s would be left at the end if we don't fill
            //     stream.WriteByte(0, 8 - stream.Bit);
            // }
            if (stream.Bit != 0)
            {
                stream.WriteByte(0, 8 - stream.Bit);
            }
        
            var packet = Packet.ToByteArray(stream.GetStreamData(), 3);
            stream.CutStream(0, 0);
            // Client.TryFindClientByIdAndSendData(clientId, packet);
            packetList.AddRange(packet);
            continue;
            // }
            //
            // if (i != ItemsOnSale.Count - 1)
            // {
            //     var delimiter =
            //         GameObjectDataHelper.WeaponsAndArmor.Contains(item.GameObjectType) ||
            //         item.ObjectType is ObjectType.MantraBookSmall or ObjectType.MantraBookLarge
            //             or ObjectType.MantraBookGreat
            //             ? 0x7F
            //             : 0x7E;
            //     stream.WriteByte((byte) delimiter);
            // }
        }

        return packetList.ToArray();

        // if (stream.Bit != 0)
        // {
        //     // 1s would be left at the end if we don't fill
        //     stream.WriteByte(0, 8 - stream.Bit);
        // }
        //
        // var packet = Packet.ToByteArray(stream.GetStreamData(), 3);
        // Client.TryFindClientByIdAndSendData(clientId, packet);
    }

    private void WriteItemPacketToStream (ushort clientId, ItemDbEntry itemDbEntry, BitStreams.BitStream stream)
    {
        var actualObjectType = itemDbEntry.ObjectType == ObjectType.Unknown
            ? itemDbEntry.GameObjectType.GetPacketObjectType()
            : itemDbEntry.ObjectType;
        var packetParts = PacketPart.LoadDefinedPartsFromFile(actualObjectType);
        PacketPart.UpdateCoordinates(packetParts, 1000000, 0, 0, 0);
        var localId = SphereClient.GetLocalObjectId(clientId, itemDbEntry.Id);
        PacketPart.UpdateEntityId(packetParts, localId);
        PacketPart.UpdateValue(packetParts, "object_type", (int) actualObjectType, 10);
        PacketPart.UpdateValue(packetParts, "game_object_id", itemDbEntry.GameId, 14);
        PacketPart.UpdateValue(packetParts, "container_id", itemDbEntry.ParentContainerId ?? 0xFF00, 16);
        if (itemDbEntry.ItemCount > 1)
        {
            PacketPart.UpdateValue(packetParts, "count", itemDbEntry.ItemCount, 15);
        }
        
        if (itemDbEntry.Suffix != ItemSuffix.None)
        {
            PacketPart.UpdateValue(packetParts, "__hasSuffix", 0, 1);
            var suffixLengthValue = (int) itemDbEntry.Suffix < 7 ? 0 : 1;
            PacketPart.UpdateValue(packetParts, "suffix_length", suffixLengthValue, 2);
            var suffixLength = suffixLengthValue == 0 ? 3 : 7;
            PacketPart.UpdateValue(packetParts, "suffix",
                GameObjectDataHelper.ObjectTypeToSuffixLocaleMapActual[itemDbEntry.GameObjectType][itemDbEntry.Suffix].value,
                suffixLength);
        }
        else
        {
            PacketPart.UpdateValue(packetParts, "__hasSuffix", 1, 1);
            PacketPart.UpdateValue(packetParts, "suffix_length", 0, 2);
            PacketPart.UpdateValue(packetParts, "suffix", 2, 3);
        }
        
        if (itemDbEntry.ContentsData.TryGetValue("scroll_id", out var value))
        {
            PacketPart.UpdateValue(packetParts, "subtype_id", (int) value, 15);
        }
        
        foreach (var part in packetParts)
        {
            stream.WriteBits(part.Value);
        }
    }
}