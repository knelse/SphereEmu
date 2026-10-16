using BitStreams;
using SphereHelpers.Extensions;

namespace SphServer.Helpers.Networking;

/// Header is 238 bits; item guid then state at 251/11 (0x2F), 392/12 (0x41), 408/11 (0x43), 439/12
/// (0x47), 470/12 (0x4B)
public readonly record struct MainhandFrame (Guid ItemId, MainhandSlotState State, int FrameLength)
{
    /// 0x41 is also a fist swing
    public static bool IsExclusiveTakeLength (int frameLength) =>
        frameLength is 0x2F or 0x43 or 0x47 or 0x4B;

    public bool IsUnequip =>
        State is MainhandSlotState.Empty or MainhandSlotState.Fists || ItemId == Guid.Empty;

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

        if (frame.Length * 8 < itemIdBit + 128 + stateBits)
        {
            return false;
        }

        var stream = new BitStream (frame);
        stream.ReadBits (itemIdBit);
        var itemId = stream.ReadGuid ();
        var state = (MainhandSlotState) stream.ReadUInt16 (stateBits);
        reading = new MainhandFrame (itemId, state, frameLength);
        return true;
    }

    private static bool TryGetGrammar (int frameLength, out int itemIdBit, out int stateBits)
    {
        switch (frameLength)
        {
            case 0x2F:
                itemIdBit = 251;
                stateBits = 11;
                return true;
            case 0x41:
                itemIdBit = 392;
                stateBits = 12;
                return true;
            case 0x43:
                itemIdBit = 408;
                stateBits = 11;
                return true;
            case 0x47:
                itemIdBit = 439;
                stateBits = 12;
                return true;
            case 0x4B:
                itemIdBit = 470;
                stateBits = 12;
                return true;
            default:
                itemIdBit = 0;
                stateBits = 0;
                return false;
        }
    }
}
