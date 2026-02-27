using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PacketDotNet;
using PacketLogViewer.Extensions;
using PacketLogViewer.Models;
using SharpPcap;
using SphereHelpers.Extensions;

namespace PacketLogViewer;

public enum PacketSource
{
    CLIENT,
    SERVER
}

public class PacketCapture
{
    private const int sphereLiveServerPort = 25860;
    private readonly ILiveDevice captureDevice;
    private readonly List<byte> packetDataQueue = new ();
    private readonly List<CapturedPacketRawData> rawCapturedPackets = new ();

    private readonly HashSet<IPAddress> sphereLiveServers = new ()
    {
        IPAddress.Parse("77.223.107.68"),
        IPAddress.Parse("77.223.107.69")
    };

    internal short ClientId;

    public Action<List<StoredPacket>, bool> OnPacketProcessed;

    public PacketCapture (string macAddress)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        captureDevice = CaptureDeviceList.Instance.FirstOrDefault(x => x.MacAddress?.ToString() == macAddress);
        if (captureDevice is null)
        {
            var existingDevices =
                string.Join("\n", CaptureDeviceList.Instance.Select(x => x.Description + " ---- " + x.MacAddress));
            throw new ArgumentException(
                $"Unknown capture device with MAC address: {macAddress}.\n\nDevices found:\n{existingDevices}");
        }

        captureDevice.OnPacketArrival += CaptureDeviceOnPacketArrival;
        var time = DateTime.Now;
        // prewarm
        _ = SphObjectDb.GameObjectDataDb;
        BitStreamExtensions.RegisterBsonMapperForBit();
        var timeAfterLoad = DateTime.Now;
        ConsoleExtensions.WriteLineColored(
            $"Ready for packets. Load time: {(timeAfterLoad - time).TotalMilliseconds} msec", ConsoleColor.Yellow);

        Task.Run(PacketQueueProcessingLoop);
    }

    public void SetClientId (short clientId)
    {
        ClientId = clientId;
    }

    private void CaptureDeviceOnPacketArrival (object _, SharpPcap.PacketCapture capture)
    {
        try
        {
            var rawCapture = capture.GetPacket();
            var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
            var ipPacket = packet.Extract<IPPacket>();
            if (ipPacket is null)
            {
                return;
            }

            if (!sphereLiveServers.Contains(ipPacket.DestinationAddress) &&
                !sphereLiveServers.Contains(ipPacket.SourceAddress))
            {
                return;
            }

            var tcpPacket = packet.Extract<TcpPacket>();

            if (tcpPacket?.PayloadData is null
                || (tcpPacket.DestinationPort != sphereLiveServerPort && tcpPacket.SourcePort != sphereLiveServerPort))
            {
                return;
            }

            var source = tcpPacket.DestinationPort == sphereLiveServerPort ? PacketSource.CLIENT : PacketSource.SERVER;

            if (!tcpPacket.Push)
            {
                // ack
                packetDataQueue.AddRange(tcpPacket.PayloadData);
            }
            else
            {
                // psh + ack
                packetDataQueue.AddRange(tcpPacket.PayloadData);
                var combinedPacket = packetDataQueue.ToArray();
                packetDataQueue.Clear();
                SchedulePacketProcessing(combinedPacket, source, rawCapture.Timeval.Date);
            }
        }
        catch (Exception ex)
        {
            ConsoleExtensions.WriteException(ex);
        }
    }

    private void SchedulePacketProcessing (byte[] data, PacketSource source, DateTime arrivalTime,
        bool shouldDecode = true)
    {
        byte[] decodedData;
        if (shouldDecode && source == PacketSource.CLIENT)
        {
            decodedData = PacketDecoder.DecodeClientPacket(data);
        }
        else
        {
            decodedData = data;
        }

        rawCapturedPackets.Add(new CapturedPacketRawData
        {
            ArrivalTime = arrivalTime,
            Buffer = data,
            DecodedBuffer = decodedData,
            Source = source
        });
    }

    private void PacketQueueProcessingLoop ()
    {
        captureDevice.Open();
        captureDevice.StartCapture();
        while (true)
        {
            try
            {
                ProcessPacketQueue();
            }
            catch (Exception ex)
            {
                ConsoleExtensions.WriteException(ex);
            }

            Thread.Sleep(500);
        }

        captureDevice.Close();
    }

    private void ProcessPacketQueue ()
    {
        var packetsToProcess = new Dictionary<PacketSource, List<CapturedPacketRawData>>
        {
            [PacketSource.CLIENT] = new (),
            [PacketSource.SERVER] = new ()
        };
        var timeLimit = DateTime.UtcNow.AddSeconds(-1);

        for (var index = 0; index < rawCapturedPackets.Count; index++)
        {
            var rawCapturedPacket = rawCapturedPackets[index];
            if (rawCapturedPacket.WasProcessed || rawCapturedPacket.ArrivalTime > timeLimit)
            {
                continue;
            }

            rawCapturedPacket.WasProcessed = true;
            packetsToProcess[rawCapturedPacket.Source].Add(rawCapturedPacket);
        }

        packetsToProcess[PacketSource.CLIENT].Sort(CapturedPacketRawData.Compare);
        packetsToProcess[PacketSource.SERVER].Sort(CapturedPacketRawData.Compare);

        var combinedList = CapturedPacketRawData.CombinePacketsInSequence(packetsToProcess[PacketSource.SERVER]);

        combinedList.ForEach(ProcessPacketRawData);
        packetsToProcess[PacketSource.CLIENT].ForEach(ProcessPacketRawData);
    }

    public static List<byte[]> SplitContentIntoPackets (byte[] content)
    {
        var offset = 0;
        var result = new List<byte[]>();
        while (offset < content.Length)
        {
            if (content.HasEqualElementsAs(PacketAnalyzer.packet_04_00_4F_01, offset))
            {
                result.Add(PacketAnalyzer.packet_04_00_4F_01);

                offset += 4;
                continue;
            }

            if (!content.HasEqualElementsAs(PacketAnalyzer.ok_mark, 2))
            {
                // already without header or something is wrong
                result.Add(content[offset..]);
                break;
            }

            var subspanTotalLength = BitConverter.ToInt16(content, offset);
            var end = offset + subspanTotalLength;

            if (end > content.Length)
            {
                continue;
            }

            try
            {
                result.Add(content[offset..end]);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }

            offset = end;
        }

        return result;
    }

    internal void ProcessPacketRawData (CapturedPacketRawData packetRawData)
    {
        ProcessPacketRawDataForce(packetRawData);
    }

    internal void ProcessPacketRawDataForce (CapturedPacketRawData packetRawData, bool forceProcess = false)
    {
        var subpackets = SplitContentIntoPackets(packetRawData.DecodedBuffer);
        var storedPackets = new List<StoredPacket>();
        for (var index = 0; index < subpackets.Count; index++)
        {
            var subpacket = subpackets[index];
            var storedPacket = new StoredPacket
            {
                ContentBytes = subpacket,
                Source = packetRawData.Source,
                Timestamp = packetRawData.ArrivalTime,
                NumberInSequence = index
            };
            storedPacket.HiddenByDefault = PacketAnalyzer.ShouldBeHiddenByDefault(storedPacket);
            storedPackets.Add(storedPacket);
        }

        OnPacketProcessed(storedPackets, forceProcess);
    }
}