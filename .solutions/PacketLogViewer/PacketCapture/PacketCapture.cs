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

public class PacketCapture : IDisposable
{
    private readonly List<ILiveDevice> captureDevices = new();
    private readonly List<byte> packetDataQueue = new();
    private readonly List<CapturedPacketRawData> rawCapturedPackets = new();
    private readonly object captureStateLock = new();
    private readonly ManualResetEventSlim _packetsPending = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _processingLoopTask;
    private readonly SphereClientConnectionDiscovery _connectionDiscovery;

    private readonly HashSet<IPAddress> sphereLiveServers = new()
    {
        IPAddress.Parse("77.223.107.68"),
        IPAddress.Parse("77.223.107.69")
    };

    internal short ClientId;

    /// <summary>0 until sphereclient connection ports are discovered.</summary>
    internal int ObservedLocalClientTcpPort => _connectionDiscovery.ClientLocalPort;

    public Action<List<StoredPacket>, bool> OnPacketProcessed;

    public PacketCapture()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _connectionDiscovery = new SphereClientConnectionDiscovery(TimeSpan.FromSeconds(5));

        foreach (var device in CaptureDeviceList.Instance)
        {
            device.OnPacketArrival += CaptureDeviceOnPacketArrival;
            captureDevices.Add(device);
        }

        if (captureDevices.Count == 0)
        {
            throw new InvalidOperationException("No network capture adapters were found.");
        }

        var time = DateTime.Now;
        _ = SphObjectDb.GameObjectDataDb;
        BitStreamExtensions.RegisterBsonMapperForBit();
        var timeAfterLoad = DateTime.Now;
        ConsoleExtensions.WriteLineColored(
            $"Ready for packets on {captureDevices.Count} adapter(s). Load time: {(timeAfterLoad - time).TotalMilliseconds} msec",
            ConsoleColor.Yellow);

        _processingLoopTask = Task.Run(PacketQueueProcessingLoop, _cts.Token);
    }

    internal int CaptureDeviceCount => captureDevices.Count;

    internal string GetCaptureStatusSummary() => _connectionDiscovery.GetStatusSummary(captureDevices.Count);

    public void SetClientId(short clientId)
    {
        ClientId = clientId;
    }

    private void CaptureDeviceOnPacketArrival(object _, SharpPcap.PacketCapture capture)
    {
        try
        {
            var clientLocalPort = _connectionDiscovery.ClientLocalPort;
            var serverRemotePort = _connectionDiscovery.ServerRemotePort;
            if (clientLocalPort == 0 || serverRemotePort == 0)
            {
                return;
            }

            var rawCapture = capture.GetPacket();
            var packet = Packet.ParsePacket(rawCapture.LinkLayerType, rawCapture.Data);
            var ipPacket = packet.Extract<IPPacket>();
            if (ipPacket is null)
            {
                return;
            }

            if (!IsSphereCaptureScopeIp(ipPacket))
            {
                return;
            }

            var tcpPacket = packet.Extract<TcpPacket>();
            if (tcpPacket is null)
            {
                return;
            }

            if (!IsTrackedSphereClientConnection(tcpPacket, clientLocalPort, serverRemotePort))
            {
                return;
            }

            var payload = tcpPacket.PayloadData;
            if (payload is null || payload.Length == 0)
            {
                return;
            }

            var source = tcpPacket.DestinationPort == serverRemotePort ? PacketSource.CLIENT : PacketSource.SERVER;

            lock (captureStateLock)
            {
                if (!tcpPacket.Push)
                {
                    packetDataQueue.AddRange(payload);
                }
                else
                {
                    packetDataQueue.AddRange(payload);
                    var combinedPacket = packetDataQueue.ToArray();
                    packetDataQueue.Clear();
                    SchedulePacketProcessing(combinedPacket, source, rawCapture.Timeval.Date);
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleExtensions.WriteException(ex);
        }
    }

    private static bool IsTrackedSphereClientConnection(TcpPacket tcpPacket, int clientLocalPort, int serverRemotePort)
    {
        return (tcpPacket.SourcePort == clientLocalPort && tcpPacket.DestinationPort == serverRemotePort) ||
               (tcpPacket.SourcePort == serverRemotePort && tcpPacket.DestinationPort == clientLocalPort);
    }

    private bool IsSphereCaptureScopeIp(IPPacket ipPacket)
    {
        if (sphereLiveServers.Contains(ipPacket.DestinationAddress) ||
            sphereLiveServers.Contains(ipPacket.SourceAddress))
        {
            return true;
        }

        return IPAddress.IsLoopback(ipPacket.SourceAddress) ||
               IPAddress.IsLoopback(ipPacket.DestinationAddress);
    }

    private void SchedulePacketProcessing(byte[] data, PacketSource source, DateTime arrivalTime,
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

        lock (captureStateLock)
        {
            rawCapturedPackets.Add(new CapturedPacketRawData
            {
                ArrivalTime = arrivalTime,
                Buffer = data,
                DecodedBuffer = decodedData,
                Source = source
            });
        }

        _packetsPending.Set();
    }

    private void PacketQueueProcessingLoop()
    {
        foreach (var device in captureDevices)
        {
            device.Open();
            device.StartCapture();
        }

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                var hasDeferredServerPackets = ProcessPacketQueue();
                if (hasDeferredServerPackets)
                {
                    _packetsPending.Wait(TimeSpan.FromMilliseconds(50), _cts.Token);
                    continue;
                }
            }
            catch (Exception ex)
            {
                ConsoleExtensions.WriteException(ex);
            }

            _packetsPending.Reset();
            _packetsPending.Wait(TimeSpan.FromMilliseconds(100), _cts.Token);
        }

        foreach (var device in captureDevices)
        {
            try
            {
                device.StopCapture();
            }
            catch
            {
                // best-effort stop
            }

            try
            {
                device.Close();
            }
            catch
            {
                // best-effort close
            }
        }
    }

    private bool ProcessPacketQueue()
    {
        List<CapturedPacketRawData> snapshot;
        lock (captureStateLock)
        {
            snapshot = rawCapturedPackets.ToList();
        }

        var packetsToProcess = new Dictionary<PacketSource, List<CapturedPacketRawData>>
        {
            [PacketSource.CLIENT] = new(),
            [PacketSource.SERVER] = new()
        };
        // Brief hold for server packets so fragments sharing a sequence number can be combined.
        var serverReorderCutoff = DateTime.Now.AddMilliseconds(-100);
        var hasDeferredServerPackets = false;

        for (var index = 0; index < snapshot.Count; index++)
        {
            var rawCapturedPacket = snapshot[index];
            if (rawCapturedPacket.WasProcessed)
            {
                continue;
            }

            if (rawCapturedPacket.Source == PacketSource.SERVER &&
                rawCapturedPacket.ArrivalTime > serverReorderCutoff)
            {
                hasDeferredServerPackets = true;
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

        return hasDeferredServerPackets;
    }

    public static List<byte[]> SplitContentIntoPackets(byte[] content)
    {
        var offset = 0;
        var result = new List<byte[]>();
        var tries = 0;
        while (offset < content.Length && tries < 1000)
        {
            tries++;
            if (content.HasEqualElementsAs(PacketAnalyzer.packet_04_00_4F_01, offset))
            {
                result.Add(PacketAnalyzer.packet_04_00_4F_01);

                offset += 4;
                continue;
            }

            if (!content.HasEqualElementsAs(PacketAnalyzer.ok_mark, 2))
            {
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

    internal void ProcessPacketRawData(CapturedPacketRawData packetRawData)
    {
        ProcessPacketRawDataForce(packetRawData);
    }

    internal void ProcessPacketRawDataForce(CapturedPacketRawData packetRawData, bool forceProcess = false)
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
            PacketAnalyzer.RefreshHiddenByDefaultFlags(storedPacket);
            storedPackets.Add(storedPacket);
        }

        OnPacketProcessed(storedPackets, forceProcess);
    }

    public void Stop()
    {
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _processingLoopTask.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // best-effort shutdown
        }
    }

    public void Dispose()
    {
        Stop();
        foreach (var device in captureDevices)
        {
            device.OnPacketArrival -= CaptureDeviceOnPacketArrival;
        }

        _connectionDiscovery.Dispose();
        _packetsPending.Dispose();
        _cts.Dispose();
    }
}
