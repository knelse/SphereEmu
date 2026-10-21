using System;
using System.Linq;
using System.Threading.Tasks;
using SphServer.Helpers.Networking;
using SphServer.Shared.Db;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;

namespace SphServer.Client.Networking.Handlers.InGame.Items;

/// <summary>
/// st_map SndUser: the map item's own ContMan records, cmd 15 with [cell, pic, x u16, y u16] or
/// [cell + 50, label]; a frame may hold several records. The client never waits for a reply
/// </summary>
public class MapMarkHandler (ushort localId, ClientConnection clientConnection) : ISphereClientNetworkingHandler
{
    private const int PicRecordLength = 6;

    /// <summary>
    /// Map picture is 384 pixels; anything past this is not a click on it
    /// </summary>
    private const int MaxCoordinate = 1023;

    public Task Handle (byte[] frame, double delta)
    {
        var identity = GameplayRecord.ReadIdentity (frame);
        var mapId = (int) (identity.EntityId & 0xFFFF);
        var character = clientConnection.GetSelectedCharacter ();
        var map = DbConnection.Items.FindById (mapId);
        if (character is null || map is null || map.WireObjectType is not ObjectType.Map
            || !character.Items.Any (x => x.Value == map.Id))
        {
            Log (mapId, "not a carried map");
            return Task.CompletedTask;
        }

        // _pcontrol IsChern: the big map, the only place marks are edited, needs an inkpot in slot 20
        var inkpot = character.Items.TryGetValue (BelongingSlot.Inkpot, out var inkpotId)
            ? DbConnection.Items.FindById (inkpotId)
            : null;
        if (inkpot?.WireObjectType is not ObjectType.Inkpot)
        {
            Log (mapId, "no inkpot");
            return Task.CompletedTask;
        }

        var marks = MapMarks.Copy (map);
        var changed = false;
        var bit = ClientFrame.BodyOffset * 8 + 53 + (identity.PositionFlag ? GameplayRecord.PositionBlockWidth : 0);
        var end = frame.Length * 8;
        while (bit + 16 <= end)
        {
            var command = ReadBits (frame, bit, 8);
            var length = ReadBits (frame, bit + 8, 8);
            bit += 16;
            if (bit + length * 8 > end)
            {
                break;
            }

            var data = new byte[length];
            for (var i = 0; i < length; i++)
            {
                data[i] = (byte) ReadBits (frame, bit + i * 8, 8);
            }

            bit += length * 8;
            if (command == ClientPacketClassifier.MapMarkCommand)
            {
                changed |= Apply (marks, data, mapId);
            }

            // Another record of the same object follows under the same region tag; 0 ends the frame
            if (bit + 7 > end || ReadBits (frame, bit, 7) != GameplayRecord.TagObjectInteract)
            {
                break;
            }

            bit += 7;
        }

        if (changed)
        {
            MapMarks.Store (map, marks);
            DbConnection.Items.Update (map);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// The client's eraser can reach cell 24, so only 0-23 and 50-73 are taken
    /// </summary>
    private bool Apply (byte[] marks, byte[] data, int mapId)
    {
        if (data.Length == 0)
        {
            return false;
        }

        var cell = (int) data[0];
        if (cell < MapMarks.CellCount && data.Length == PicRecordLength)
        {
            var pic = data[1];
            var x = (ushort) (data[2] | data[3] << 8);
            var y = (ushort) (data[4] | data[5] << 8);
            if (pic > MapMarks.MaxPic || x > MaxCoordinate || y > MaxCoordinate)
            {
                Log (mapId, $"cell {cell} pic {pic} at {x},{y} rejected");
                return false;
            }

            MapMarks.SetPic (marks, cell, pic, x, y);
            Log (mapId, pic == 0 ? $"cell {cell} removed" : $"cell {cell} pic {pic} at {x},{y}");
            return true;
        }

        cell -= MapMarks.LabelCellOffset;
        if (cell is >= 0 and < MapMarks.CellCount && data.Length is > 1 and <= MapMarks.MaxLabelLength + 1)
        {
            // A label without its mark would be wiped by the next pic record anyway
            if (MapMarks.Pic (marks, cell) == 0)
            {
                Log (mapId, $"label for empty cell {cell} ignored");
                return false;
            }

            MapMarks.SetLabel (marks, cell, data.AsSpan (1));
            Log (mapId, $"cell {cell} label of {data.Length - 1} bytes");
            return true;
        }

        Log (mapId, $"record [{Convert.ToHexString (data)}] rejected");
        return false;
    }

    /// <summary>
    /// Bit 0 is the least significant bit of byte 0
    /// </summary>
    private static int ReadBits (byte[] frame, int startBit, int width)
    {
        var value = 0;
        for (var i = 0; i < width; i++)
        {
            var bit = startBit + i;
            if ((frame[bit >> 3] & (1 << (bit & 7))) != 0)
            {
                value |= 1 << i;
            }
        }

        return value;
    }

    private void Log (int mapId, string outcome)
    {
        SphLogger.Info ($"MapMark: Source [{localId:X4}] - Map [{mapId:X4}] - [{outcome}]");
    }
}
