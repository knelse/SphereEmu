namespace SphServer.Helpers.Networking;

/// _player Manager command 4 (SendHand): 16-byte item guid, or Guid.Empty when emptying hand
public readonly record struct MainhandFrame (Guid ItemId, MainhandSlotState State, int FrameLength)
{
    public static bool IsExclusiveTakeLength (int frameLength) =>
        frameLength is 0x2F or 0x30 or 0x43 or 0x47 or 0x4B;

    public bool IsUnequip =>
        State is MainhandSlotState.Empty or MainhandSlotState.Fists || ItemId == Guid.Empty;

    public static bool TryRead (ReadOnlySpan<byte> frame, out MainhandFrame reading)
    {
        reading = default;
        if (frame.Length < 48)
        {
            return false;
        }

        var frameLength = frame[0] | (frame[1] << 8);
        var bodyBit = ClientFrame.BodyOffset * 8;
        if (GameplayRecord.ReadBits (frame, bodyBit, 1) != 0)
        {
            return false;
        }

        var bit = bodyBit + 1 + 15 + 128;
        var subjectType = (int) GameplayRecord.ReadBits (frame, bit, 12);
        bit += 12;
        if (subjectType != GameplayRecord.SubjectTypePlayer)
        {
            return false;
        }

        while (bit + 7 <= frame.Length * 8)
        {
            var wire = (int) GameplayRecord.ReadBits (frame, bit, 7);
            bit += 7;
            if (wire == 0)
            {
                break;
            }

            if (wire == 4)
            {
                bit += 7;
                continue;
            }

            if (wire == 13)
            {
                if (bit + 16 > frame.Length * 8)
                {
                    break;
                }

                var op = (int) GameplayRecord.ReadBits (frame, bit, 8);
                var count = (int) GameplayRecord.ReadBits (frame, bit + 8, 8);
                bit += 16;
                if (op == 4 && count == 16 && bit + 128 <= frame.Length * 8)
                {
                    var itemId = GameplayRecord.ReadGuidBits (frame, bit);
                    var state = itemId == Guid.Empty ? MainhandSlotState.Empty : MainhandSlotState.Full;
                    reading = new MainhandFrame (itemId, state, frameLength);
                    return true;
                }

                bit += count * 8;
                continue;
            }

            break;
        }

        return false;
    }
}
