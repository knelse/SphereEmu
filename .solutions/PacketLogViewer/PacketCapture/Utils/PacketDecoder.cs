using System;

namespace PacketLogViewer;

internal static class PacketDecoder
{
    internal static byte[] DecodeClientPacket (byte[] input, int start = 9)
    {
        if (input.Length <= 9)
        {
            return input;
        }

        var encoded = input[start..];
        var result = new byte[encoded.Length + start];
        byte mask3 = 0x0;
        var encodingMask = new byte[] { 0x4B, 0x0D, 0xEF, 0x60, 0xC9, 0x9A, 0x70, 0x0E, 0x03 };
        for (var i = 0; i < encoded.Length; i++)
        {
            var current = (byte) (encoded[i] ^ encodingMask[i % 9] ^ mask3);
            result[i + start] = current;
            mask3 = (byte) (current * i + 2 * mask3);
        }

        Array.Copy(input, result, start);
        return result;
    }
}