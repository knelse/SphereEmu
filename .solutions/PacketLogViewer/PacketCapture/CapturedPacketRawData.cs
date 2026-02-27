using System;
using System.Collections.Generic;

namespace PacketLogViewer;

internal class CapturedPacketRawData
{
    internal DateTime ArrivalTime;
    internal byte[] Buffer;
    internal byte[] DecodedBuffer;
    internal PacketSource Source;
    internal bool WasProcessed;

    internal static int Compare (CapturedPacketRawData self, CapturedPacketRawData other)
    {
        if (self.DecodedBuffer.Length < 7 || other.DecodedBuffer.Length < 7)
        {
            return 0;
        }

        return GetPacketNumberInSequence(self.DecodedBuffer).CompareTo(GetPacketNumberInSequence(other.DecodedBuffer));
    }

    internal int GetPacketNumberInSequence ()
    {
        return GetPacketNumberInSequence(DecodedBuffer);
    }

    internal static int GetPacketNumberInSequence (byte[] buffer)
    {
        if (buffer.Length < 8)
        {
            return 0;
        }

        return (buffer[7] << 8) + buffer[6];
    }

    internal static List<CapturedPacketRawData> CombinePacketsInSequence (List<CapturedPacketRawData> input)
    {
        var result = new List<CapturedPacketRawData>();
        input.Sort(Compare);

        for (var i = 0; i < input.Count; i++)
        {
            var currentDecoded = new List<byte>(input[i].DecodedBuffer);
            var current = new List<byte>(input[i].Buffer);
            if (i == input.Count - 1 ||
                input[i + 1].GetPacketNumberInSequence() != input[i].GetPacketNumberInSequence())
            {
                result.Add(new CapturedPacketRawData
                {
                    ArrivalTime = input[i].ArrivalTime,
                    DecodedBuffer = currentDecoded.ToArray(),
                    Buffer = current.ToArray(),
                    Source = input[i].Source,
                    WasProcessed = true
                });
                continue;
            }

            var j = 1;

            while (i + j < input.Count &&
                   input[i + j].GetPacketNumberInSequence() == input[i].GetPacketNumberInSequence())
            {
                currentDecoded.AddRange(input[i + j].DecodedBuffer);
                current.AddRange(input[i + j].Buffer);
                j++;
            }

            result.Add(new CapturedPacketRawData
            {
                ArrivalTime = input[i].ArrivalTime,
                DecodedBuffer = currentDecoded.ToArray(),
                Buffer = current.ToArray(),
                Source = input[i].Source,
                WasProcessed = true
            });

            i += j - 1;
        }

        return result;
    }

    internal void ProcessPacketRawData ()
    {
        switch (Source)
        {
            case PacketSource.CLIENT:
                ProcessPacketRawDataClient();
                return;
            case PacketSource.SERVER:
                ProcessPacketRawDataServer();
                return;
        }
    }

    private void ProcessPacketRawDataServer ()
    {
    }

    private void ProcessPacketRawDataClient ()
    {
    }
}