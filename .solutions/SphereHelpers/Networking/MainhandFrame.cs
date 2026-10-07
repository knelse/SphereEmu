using BitStreams;
using SphereHelpers.Extensions;

namespace SphServer.Helpers.Networking;

/// Header is 128 bits; item id then state at 141/11 (0x15), 172/12 (0x19), 188/11 (0x1B), 219/12
/// (0x1F), 250/12 (0x23); 0x19 with 08 40 A3 is a fist swing unless the item id is owned or 0xFFFF
public readonly record struct MainhandFrame (ushort ItemId, MainhandSlotState State, int FrameLength)
{
    public const ushort NoItem = 0xFFFF;

    /// 0x19 is also a fist swing
    public static bool IsExclusiveTakeLength (int frameLength) =>
        frameLength is 0x15 or 0x1B or 0x1F or 0x23;

    public bool IsUnequip =>
        State is MainhandSlotState.Empty or MainhandSlotState.Fists || ItemId == NoItem;

    public static bool TryRead (byte[] frame, out MainhandFrame reading)
    {
        reading = default;
        if (frame.Length < 2)
        {
            return false;
        }

        var frameLength = frame[0] | (frame[1] << 8);
        if (!TryGetGrammar (frameLength, out var itemIdBit, out var stateBits))
        {
            return false;
        }

        if (frame.Length * 8 < itemIdBit + 16 + stateBits)
        {
            return false;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (itemIdBit);
        var itemId = stream.ReadUInt16 (16);
        var state = (MainhandSlotState) stream.ReadUInt16 (stateBits);
        reading = new MainhandFrame (itemId, state, frameLength);
        return true;
    }

    private static bool TryGetGrammar (int frameLength, out int itemIdBit, out int stateBits)
    {
        switch (frameLength)
        {
            case 0x15:
                itemIdBit = 141;
                stateBits = 11;
                return true;
            case 0x19:
                itemIdBit = 172;
                stateBits = 12;
                return true;
            case 0x1B:
                itemIdBit = 188;
                stateBits = 11;
                return true;
            case 0x1F:
                itemIdBit = 219;
                stateBits = 12;
                return true;
            case 0x23:
                itemIdBit = 250;
                stateBits = 12;
                return true;
            default:
                itemIdBit = 0;
                stateBits = 0;
                return false;
        }
    }
}
