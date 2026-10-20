using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Packets;
using SphServer.Shared.BitStream;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.Networking;
using SphServer.Shared.WorldState;
using SphServer.Sphere.Game.WorldObject;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// Selling to an NPC goes through the player's own sell window (_pcontrol.WinTrade), the same one
/// players trade with. The client puts an item into a sell slot (region 12 cmd 6), the vendor answers
/// with an offer (region 12 cmd 7), and accepting it sends the offered price back (cmd 6, length 9)
/// followed by an offer reset (region 9 cmd 2). Layout from the SphereTools vendorTrade.txt capture
/// </summary>
public class SellItemToVendorHandler (ushort localId, SphereClient client)
    : ISphereClientNetworkingHandler
{
    /// <summary>
    /// The first record's 7-bit tag: 13 for region 12, 10 for region 9
    /// </summary>
    private const int TagBit = ClientFrame.BodyOffset * 8 + 46;

    private const int OfferResetCommand = 2;
    private const byte SetTradeSlotCommand = 6;
    private const byte TradePartnerCommand = 21;
    private const byte OfferCommand = 7;
    private const byte BoughtCommand = 13;

    /// <summary>
    /// Only data point: a firework the vendor sells for 100 was bought back for 10
    /// </summary>
    private const int SellPricePercent = 10;

    /// <summary>
    /// Partner named by region 12 cmd 21, used when no vendor window was opened
    /// </summary>
    private ushort partnerId;

    private readonly Dictionary<byte, SellSlot> slots = new ();

    private sealed class SellSlot
    {
        public int ItemId;
        public int Count;
        public int Offer;
        public ushort Vendor;
        public bool Accepted;
    }

    public Task Handle (byte[] frame, double delta)
    {
        var identity = GameplayRecord.ReadIdentity (frame);
        var shift = identity.PositionFlag ? GameplayRecord.PositionBlockWidth : 0;

        // Accepting an offer sends the new price and the offer reset as two records in one frame,
        // each after its own 7-bit tag
        var tagBit = TagBit + shift;
        while (tagBit + 7 <= frame.Length * 8)
        {
            var tag = (int) ReadBits (frame, tagBit, 7);
            var nextBit = tag switch
            {
                GameplayRecord.TagPlayerAction => HandleManagerRecord (frame, tagBit + 7),
                GameplayRecord.TagTradeMan => HandleTradeManRecord (frame, tagBit + 7),
                _ => -1
            };
            if (nextBit < 0)
            {
                break;
            }

            tagBit = nextBit;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Region 12: u8 command, u8 length; returns the bit after the record, or -1
    /// </summary>
    private int HandleManagerRecord (byte[] frame, int commandBit)
    {
        var command = (byte) ReadBits (frame, commandBit, 8);
        var length = (int) ReadBits (frame, commandBit + 8, 8);
        var payload = ReadPayload (frame, commandBit + 16, length);
        if (payload is null)
        {
            return -1;
        }

        SphLogger.Debug ($"Sell window: cmd {command} data {Convert.ToHexString (payload)}. Client ID: {localId:X4}");
        switch (command)
        {
            case TradePartnerCommand when length == 3:
                SetPartner ((ushort) (payload[0] | (payload[1] << 8)));
                break;
            case SetTradeSlotCommand:
                SetSlot (payload);
                break;
        }

        return commandBit + 16 + length * 8;
    }

    /// <summary>
    /// Region 9: u4 command, u4 length; cmd 2 resets an offer
    /// </summary>
    private int HandleTradeManRecord (byte[] frame, int commandBit)
    {
        var command = (int) ReadBits (frame, commandBit, 4);
        var length = (int) ReadBits (frame, commandBit + 4, 4);
        var payload = ReadPayload (frame, commandBit + 8, length);
        if (payload is null)
        {
            return -1;
        }

        SphLogger.Debug ($"Sell window: trade cmd {command} data {Convert.ToHexString (payload)}. Client ID: {localId:X4}");
        if (command == OfferResetCommand && length == 5)
        {
            ResolveOffer (payload[0]);
        }

        return commandBit + 8 + length * 8;
    }

    private void SetPartner (ushort partner)
    {
        partnerId = partner;
        slots.Clear ();
        SphLogger.Info ($"Sell: trading with {partner:X4}. Client ID: {localId:X4}");
    }

    /// <summary>
    /// The open vendor window wins; the client sends cmd 21 only in some flows
    /// </summary>
    private ushort CurrentVendor ()
    {
        var candidate = client.TradeVendorId != 0 ? client.TradeVendorId : partnerId;
        return ActiveWorldObjects.Get (candidate) is NpcInteractable ? candidate : (ushort) 0;
    }

    /// <summary>
    /// Length 12 puts an item in, 9 changes count and price, 1 empties the slot
    /// </summary>
    private void SetSlot (byte[] payload)
    {
        var slot = payload[0];
        switch (payload.Length)
        {
            case 12:
            {
                var itemId = payload[1] | (payload[2] << 8) | (payload[3] << 16);
                var count = BinaryPrimitives.ReadInt32LittleEndian (payload.AsSpan (4));
                slots[slot] = new SellSlot { ItemId = itemId, Count = count };
                OfferPrice (slot);
                break;
            }
            case 9 when slots.TryGetValue (slot, out var sellSlot):
            {
                // Accepting copies the offer into the player's own price before the reset
                sellSlot.Count = BinaryPrimitives.ReadInt32LittleEndian (payload.AsSpan (1));
                var price = BinaryPrimitives.ReadInt32LittleEndian (payload.AsSpan (5));
                sellSlot.Accepted = sellSlot.Offer > 0 && price == sellSlot.Offer;
                break;
            }
            case 1:
                slots.Remove (slot);
                break;
        }
    }

    private void OfferPrice (byte slot)
    {
        var sellSlot = slots[slot];
        var character = client.CurrentCharacter;
        var vendorId = CurrentVendor ();
        if (character is null || vendorId == 0)
        {
            SphLogger.Info ($"Sell: slot {slot} filled with no vendor open. Client ID: {localId:X4}");
            return;
        }

        if (FindCarriedItem (character, sellSlot.ItemId) is not { } item)
        {
            SphLogger.Warning ($"Sell: item {sellSlot.ItemId:X4} is not in the bag. Client ID: {localId:X4}");
            return;
        }

        sellSlot.Vendor = vendorId;
        sellSlot.Offer = SellPrice (item, SoldCount (item, sellSlot.Count));
        if (sellSlot.Offer <= 0)
        {
            SphLogger.Info ($"Sell: vendor {vendorId:X4} does not pay for {NameOf (item)}. Client ID: {localId:X4}");
            return;
        }

        byte[] offer = [slot, 0, 0, 0, 0, (byte) vendorId, (byte) (vendorId >> 8), 0];
        BinaryPrimitives.WriteInt32LittleEndian (offer.AsSpan (1), sellSlot.Offer);
        client.MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerManagerCommand (SelfId (character), OfferCommand, offer));
        SphLogger.Info ($"Sell: vendor {vendorId:X4} offers {sellSlot.Offer} for {NameOf (item)} " +
                        $"in slot {slot}. Client ID: {localId:X4}");
    }

    /// <summary>
    /// Accept and decline both end with this reset; only an accept set the price to the offer first
    /// </summary>
    private void ResolveOffer (byte slot)
    {
        if (!slots.TryGetValue (slot, out var sellSlot) || !sellSlot.Accepted)
        {
            return;
        }

        slots.Remove (slot);
        var vendorId = sellSlot.Vendor;
        if (CurrentVendor () != vendorId)
        {
            SphLogger.Info ($"Sell: vendor {vendorId:X4} window closed before the sale. Client ID: {localId:X4}");
            return;
        }

        var character = client.CurrentCharacter;
        if (character is null || FindCarriedItem (character, sellSlot.ItemId) is not { } item)
        {
            return;
        }

        var count = SoldCount (item, sellSlot.Count);
        var price = SellPrice (item, count);
        if (price != sellSlot.Offer || (long) character.Money + price > int.MaxValue)
        {
            SphLogger.Warning ($"Sell: {NameOf (item)} changed since the offer. Client ID: {localId:X4}");
            return;
        }

        var name = NameOf (item);
        var wireType = (ushort) item.ObjectType;
        RemoveFromBag (character, item, count);

        character.Money += price;
        client.SendMoney ();
        client.SaveCharacter ();

        // mgrSBought: item, money, buyer, count, then a u24 and the item's object type
        byte[] bought = new byte[18];
        bought[0] = (byte) sellSlot.ItemId;
        bought[1] = (byte) (sellSlot.ItemId >> 8);
        bought[2] = (byte) (sellSlot.ItemId >> 16);
        bought[3] = (byte) price;
        bought[4] = (byte) (price >> 8);
        bought[5] = (byte) (price >> 16);
        bought[6] = (byte) vendorId;
        bought[7] = (byte) (vendorId >> 8);
        BinaryPrimitives.WriteInt32LittleEndian (bought.AsSpan (9), sellSlot.Count);
        BinaryPrimitives.WriteUInt16LittleEndian (bought.AsSpan (16), wireType);
        client.MaybeQueueNetworkPacketSend (CommonPackets.BuildPlayerManagerCommand (SelfId (character), BoughtCommand, bought));

        SphLogger.Info ($"Sell: {name} x{count} to vendor {vendorId:X4} for {price}, money {character.Money}. " +
                        $"Client ID: {localId:X4}");
    }

    /// <summary>
    /// Only bag cells sell, as the client allows; worn items and the hand are refused
    /// </summary>
    private static ItemDbEntry? FindCarriedItem (CharacterDbEntry character, int itemId)
    {
        var slots = character.Items.Where (x => x.Value == itemId).Select (x => x.Key).ToList ();
        if (slots.Count == 0 || !slots.All (ItemDbEntry.IsInventorySlot))
        {
            return null;
        }

        return DbConnection.Items.FindById (itemId);
    }

    /// <summary>
    /// A single item comes with count 0; a stack asks how many first
    /// </summary>
    private static int SoldCount (ItemDbEntry item, int count)
    {
        var carried = Math.Max (item.ItemCount, 1);
        return count <= 0 ? carried : Math.Min (count, carried);
    }

    /// <summary>
    /// The item scripts' own GetPrice scales the base price by the durability left
    /// </summary>
    private static int SellPrice (ItemDbEntry item, int count)
    {
        long perOne = (long) Math.Max (item.VendorCost, 0) * SellPricePercent / 100;
        // Wear is not tracked yet, so 0 means a full item, as in the admin item view
        if (item.Durability > 0 && item.CurrentDurability > 0)
        {
            perOne = perOne * Math.Min (item.CurrentDurability, item.Durability) / item.Durability;
        }

        return (int) Math.Min (perOne * count, int.MaxValue);
    }

    private void RemoveFromBag (CharacterDbEntry character, ItemDbEntry item, int count)
    {
        var cell = character.Items.First (x => x.Value == item.Id).Key;
        if (count < item.ItemCount)
        {
            item.ItemCount -= count;
            DbConnection.Items.Update (item);
            client.MaybeQueueNetworkPacketSend (ItemRecordEncoder.EncodeCount (item));
            if (ItemSlotReserve.Build (character.ClientIndex, cell, item.Id, item.ItemCount) is { } reserve)
            {
                client.MaybeQueueNetworkPacketSend (reserve);
            }

            return;
        }

        character.Items.Remove (cell);
        if (ItemSlotReserve.Build (character.ClientIndex, cell, ItemSlotReserve.NoItem) is { } clear)
        {
            client.MaybeQueueNetworkPacketSend (clear);
        }

        client.MaybeQueueNetworkPacketSend (CommonPackets.DespawnEntity (client.GetLocalObjectId ((ushort) item.Id)));
        DbConnection.Items.Delete (item.Id);
        WorldObjectIndex.Release ((ushort) item.Id);
    }

    private static byte[]? ReadPayload (byte[] frame, int bit, int length)
    {
        if (length <= 0 || bit + length * 8 > frame.Length * 8)
        {
            return null;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (bit);
        var payload = new byte[length];
        for (var i = 0; i < length; i++)
        {
            payload[i] = stream.ReadByte (8);
        }

        return payload;
    }

    private static uint ReadBits (byte[] frame, int bitOffset, int count)
    {
        var stream = new BitStream (frame);
        stream.ReadBits (bitOffset);
        return stream.ReadUInt32 (count);
    }

    private static ushort SelfId (CharacterDbEntry character) => SphBitStream.ByteSwap (character.ClientIndex);

    private static string NameOf (ItemDbEntry item) =>
        item.Localization.GetValueOrDefault (Locale.Russian, item.GameObjectType.ToString ());
}
