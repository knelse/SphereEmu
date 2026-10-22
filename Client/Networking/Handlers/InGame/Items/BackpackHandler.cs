using System;
using System.Threading.Tasks;
using BitStreams;
using SphereHelpers.Extensions;
using SphServer.Helpers.Networking;
using SphServer.Server;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using static SphServer.Shared.BitStream.SphBitStream;
using static SphServer.Shared.Networking.DataModel.Serializers.SphereDbEntrySerializerBase;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// ContMan sent by the worn backpack's own window (_inv runs in the bag's process)
/// </summary>
public class BackpackHandler (ushort localId, ClientConnection clientConnection)
    : ISphereClientNetworkingHandler
{
    /// <summary>
    /// Drop into a cell: player id u32, item u24, cell u8, then a u24 amount on a stack split
    /// </summary>
    private const int PutInCommand = 6;

    /// <summary>
    /// Cell to cell: player id u32, then the drop cell before the source cell (_inv: [4] g_0460,
    /// [5] g_0654)
    /// </summary>
    private const int MoveCommand = 4;

    /// <summary>
    /// The window emptied a cell after a drag that ended elsewhere
    /// </summary>
    private const int LeftCellCommand = 5;

    private const int PlayerIdWidth = 32;

    /// <summary>
    /// _inv appends the amount only when the drag splits a stack: cmd 6 grows from 8 to 11 bytes,
    /// cmd 4 from 6 to 9
    /// </summary>
    private const int PutInSplitLength = 11;

    private const int MoveSplitLength = 9;

    /// <summary>
    /// Long enough for the request that took the item away to land first
    /// </summary>
    private const double RestoreCellAfterSeconds = 0.5;

    public Task Handle (byte[] frame, double delta)
    {
        var bagId = frame[11] + frame[12] * 0x100;
        var command = GameplayRecord.ReadBackpackCommand (frame);
        var payloadBit = GameplayRecord.BackpackPayloadBit (frame);
        SphLogger.Info ($"Backpack {bagId:X4}: cmd {command} frame {Convert.ToHexString (frame)}. Client ID: {localId:X4}");

        var character = clientConnection.GetSelectedCharacter ();
        var bag = DbConnection.Items.FindById (bagId);
        if (character is null || bag is null || !Backpack.IsBackpack (bag) ||
            !character.Items.TryGetValue (BelongingSlot.Backpack, out var wornBagId) || wornBagId != bag.Id)
        {
            SphLogger.Warning ($"Backpack {bagId:X4} is not the worn backpack. Client ID: {localId:X4}");
            return Task.CompletedTask;
        }

        var stream = new BitStream (frame);
        stream.SeekBitOffset (payloadBit - 8);
        var length = stream.ReadByte (8);
        stream.SeekBitOffset (payloadBit + PlayerIdWidth);
        switch (command)
        {
            case PutInCommand:
            {
                var itemId = (int) stream.ReadUInt32 (24);
                var cell = stream.ReadByte (8);
                var count = length >= PutInSplitLength ? (int) stream.ReadUInt32 (24) : 0;
                if (DbConnection.Items.FindById (itemId) is not { } item)
                {
                    SphLogger.Warning ($"Backpack {bagId:X4}: no item {itemId:X4} to put in. Client ID: {localId:X4}");
                    Backpack.Redraw (clientConnection, bag, null);
                    break;
                }

                Backpack.PutIn (clientConnection, bag, item, cell, count);
                break;
            }
            case MoveCommand:
            {
                var to = stream.ReadByte (8);
                var from = stream.ReadByte (8);
                var count = length >= MoveSplitLength ? (int) stream.ReadUInt32 (24) : 0;
                Backpack.MoveCell (clientConnection, bag, from, to, count);
                break;
            }
            case LeftCellCommand:
            {
                stream.SeekBitOffset (payloadBit);
                var cell = stream.ReadByte (8);
                if (!bag.BackpackContents.TryGetValue (cell, out var itemId))
                {
                    break;
                }

                SphereServer.ServerNode.GetTree ().CreateTimer (RestoreCellAfterSeconds).Timeout += () =>
                {
                    var current = DbConnection.Items.FindById (bag.Id);
                    if (current is null || !current.BackpackContents.TryGetValue (cell, out var stillThere) ||
                        stillThere != itemId)
                    {
                        return;
                    }

                    Backpack.Redraw (clientConnection, current, null);
                    SphLogger.Info ($"Backpack {bag.Id:X4}: cell {cell} restored with {itemId:X4} after the " +
                                    $"window let go of it. Client ID: {localId:X4}");
                };
                break;
            }
        }

        return Task.CompletedTask;
    }
}
