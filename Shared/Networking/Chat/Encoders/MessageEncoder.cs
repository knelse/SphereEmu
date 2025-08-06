using System;
using SphServer.Shared.BitStream;
using SphServer.System;

namespace SphServer.Shared.Networking.Chat.Encoders;

public static class MessageEncoder
{
    public static byte[] EncodeToSendFromServer (string messageContent, string name, int chatTypeVal)
    {
        if (Enum.IsDefined(typeof (PrivateChatType), chatTypeVal))
        {
            // todo
            return [];
        }

        if (chatTypeVal == (int) PublicChatType.GM_Outgoing)
        {
            chatTypeVal = (int) PublicChatType.GM;
        }

        var stream = SphBitStream.GetWriteBitStream();
        var length = messageContent.Length;
        var nameLength = name.Length;
        var firstPacketLength = (byte) (nameLength + 20);
        stream.WriteBytes([
                firstPacketLength, 0x00, 0x2C, 0x01, 0x00, 0x22, 0xE4, 0x45, 0xF0, 0x14, 0x80, 0x4F,
                (byte) (length % 0xFF), (byte) (length / 0xFF), 0x00, 0x00, 0x02, (byte) (nameLength + 1)
            ], 18,
            true);
        var nameBytes = SphEncoding.Win1251!.GetBytes(name);
        stream.WriteBytes(nameBytes, nameBytes.Length, true);
        stream.WriteByte(0x00);
        stream.WriteByte((byte) chatTypeVal);

        // client sometimes adds X bytes of 0x00 at the end, server does not send those back
        messageContent = messageContent.TrimEnd((char) 0);
        var messageBytes = SphEncoding.Win1251.GetBytes(messageContent);
        var end = 0;
        var previousEnd = 0;

        var packetCount = 0;

        while (end < messageBytes.Length)
        {
            end = Math.Min(end + 251, messageBytes.Length);
            var currentTextLength = end - previousEnd;
            var currentLength = currentTextLength + 13;
            stream.WriteBytes(
            [
                (byte) (currentLength % 256), (byte) (currentLength / 256), 0x2C, 0x01, 0x00, 0x22, 0xE4, 0x45,
                0xF0,
                0x14, 0xC0
            ], 11, true);
            stream.WriteByte(0, 5);
            stream.WriteByte((byte) currentTextLength);
            stream.WriteBytes(messageBytes[previousEnd..end], currentTextLength, true);
            stream.WriteByte(0, 3);
            previousEnd = end;
            packetCount++;
        }

        // Client technically supports chat messages without the <l="player:// tag and other BS,
        // but it expects 2 packets, otherwise it will glue messages on top of each other.
        // Sending an "empty" (with only string terminator) packet fixes that
        if (packetCount < 2)
        {
            stream.WriteBytes(
            [
                0x0E, 0x00, 0x2C, 0x01, 0x00, 0x22, 0xE4, 0x45, 0xF0, 0x14, 0xC0
            ], 11, true);
            stream.WriteByte(0, 5);
            stream.WriteByte(1);
            stream.WriteByte(0);
            stream.WriteByte(0, 3);
        }

        var data = stream.GetStreamData();

        return data;
    }

    // public static byte[] GetChatMessageBytesForServerSend(byte[] decodedClientMessageBytes, ChatType chatType)
    // {
    //     // process here
    //     return GetChatMessageBytesForServerSend("", "", chatType);
    // }
}