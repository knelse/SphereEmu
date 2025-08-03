using System.Collections.Generic;
using System.IO;
using System.Linq;
using BitStreams;
using LiteDB;
using SphServer;
using SphServer.DataModels;
using SphServer.Helpers;
using SphServer.Packets;
using SphServer.Providers;

public class Vendor
{
    [BsonId] public int Id { get; set; }

    // TODO: will be used when we can place npcs
    // public double X { get; set; }
    // public double Y { get; set; }
    // public double Z { get; set; }
    // public double Angle { get; set; }
    // public int TitleMinusOne { get; set; }
    // public int DegreeMinusOne { get; set; }
    public string Name { get; set; }
    public string FamilyName { get; set; }

    public List<int> ItemIdsOnSale { get; set; } = new ();

    [BsonIgnore]
    public List<Item> ItemsOnSale =>
        ItemIdsOnSale.Select(x => DbConnectionProvider.ItemCollection.FindById(x)).ToList();
    //public ulong? ParentNodeId { get; set; }

    public byte[] GetItemSlotListForClient (ushort clientId)
    {
        var localId = Client.GetLocalObjectId(clientId, Id);

        var memoryStream = new MemoryStream();
        var stream = new BitStream(memoryStream)
        {
            AutoIncreaseStream = true
        };

        stream.WriteBytes(
            new byte[] { BitHelper.MinorByte(localId), BitHelper.MajorByte(localId), 0x54, 0x43, 0x61, 0x02, 0x00 }, 7,
            true);
        stream.WriteBit(0);

        var itemSeparator = (ushort) 0b110000000001010;

        for (var i = 0; i < ItemsOnSale.Count; i++)
        {
            var item = ItemsOnSale[i];
            stream.WriteUInt16(itemSeparator, 15);
            stream.WriteByte((byte) i, 8);
            var itemLocalId = Client.GetLocalObjectId(clientId, item.Id);
            stream.WriteUInt16(itemLocalId);
            stream.WriteBytes(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 }, 5, true);
            stream.WriteUInt32((uint) item.VendorCost);
        }

        stream.WriteByte(0x3F, 7);
        stream.WriteUInt16(clientId);
        stream.WriteBytes(new byte[] { 0b00001000, 0b01000000, 0b10100011, 0b01100010 }, 4, true);
        stream.WriteByte(0x0, 5);
        stream.WriteUInt16(localId);
        stream.WriteByte(0x0, 8);

        var streamData = stream.GetStreamData();
        // I have no idea why but it becomes 0xFC in some cases, because value pops at stream.WriteByte(0x0, 5)
        // and keeps getting shifted back to end of stream
        if (streamData[^1] != 0)
        {
            streamData = streamData[..^1];
        }

        return Packet.ToByteArray(streamData, 3);
    }
}