using BitStreams;
using SphereHelpers.Extensions;

namespace SphServer.Helpers.Networking;

/// <summary>
///     Client take / put-down of the hotkeyed main-hand item. Lengths and bit offsets come from
///     the labelled kaitai grammars (header is always 128 bits):
///     <list type="bullet">
///         <item>0x15 empty → powder, item at 141 (skip 13), state 11 bits</item>
///         <item>0x19 empty → sword, item at 172 (skip 32+12), state 12 bits</item>
///         <item>0x1B powder → powder, item at 188 (no usable ksy; old length map)</item>
///         <item>0x1F powder ↔ sword, item at 219 (skip 32+32+27), state 12 bits</item>
///         <item>0x23 sword → sword, item at 250 (skip 32+32+32+26), state 12 bits</item>
///     </list>
///     Fists and empty are <see cref="MainhandSlotState" /> on these same frames, not extra lengths.
///     0x19 + 08 40 A3 is also a fist swing; only an owned item id (or 0xFFFF) makes it a take.
/// </summary>
public readonly record struct MainhandFrame(ushort ItemId, MainhandSlotState State, int FrameLength)
{
    public const ushort NoItem = 0xFFFF;

    /// <summary>
    ///     Lengths that are only ever take / put-down. 0x19 is omitted: it is also a fist swing.
    /// </summary>
    public static bool IsExclusiveTakeLength(int frameLength) =>
        frameLength is 0x15 or 0x1B or 0x1F or 0x23;

    public bool IsUnequip =>
        State is MainhandSlotState.Empty or MainhandSlotState.Fists || ItemId == NoItem;

    public static bool TryRead(byte[] frame, out MainhandFrame reading)
    {
        reading = default;
        if (frame.Length < 2)
        {
            return false;
        }

        var frameLength = frame[0] | (frame[1] << 8);
        if (!TryGetGrammar(frameLength, out var itemIdBit, out var stateBits))
        {
            return false;
        }

        if (frame.Length * 8 < itemIdBit + 16 + stateBits)
        {
            return false;
        }

        var stream = new BitStream(frame);
        stream.ReadBits(itemIdBit);
        var itemId = stream.ReadUInt16(16);
        var state = (MainhandSlotState)stream.ReadUInt16(stateBits);
        reading = new MainhandFrame(itemId, state, frameLength);
        return true;
    }

    private static bool TryGetGrammar(int frameLength, out int itemIdBit, out int stateBits)
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
