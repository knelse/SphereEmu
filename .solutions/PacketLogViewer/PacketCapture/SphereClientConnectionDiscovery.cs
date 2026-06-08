using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace PacketLogViewer;

/// <summary>
/// Periodically scans for sphereclient.exe and reads its established TCP connections
/// to derive the local (incoming) and remote server (outgoing) ports used for capture filtering.
/// </summary>
public sealed class SphereClientConnectionDiscovery : IDisposable
{
    private static readonly HashSet<IPAddress> PreferredServerAddresses = new()
    {
        IPAddress.Parse("77.223.107.68"),
        IPAddress.Parse("77.223.107.69")
    };

    private readonly Timer _scanTimer;
    private readonly object _stateLock = new();
    private int _clientLocalPort;
    private int _serverRemotePort;
    private bool _clientRunning;

    public SphereClientConnectionDiscovery(TimeSpan scanInterval)
    {
        _scanTimer = new Timer(_ => Scan(), null, TimeSpan.Zero, scanInterval);
    }

    /// <summary>Local ephemeral TCP port on the game client (server sends packets here).</summary>
    public int ClientLocalPort
    {
        get
        {
            lock (_stateLock)
            {
                return _clientLocalPort;
            }
        }
    }

    /// <summary>Remote TCP port on the game server (client sends packets here).</summary>
    public int ServerRemotePort
    {
        get
        {
            lock (_stateLock)
            {
                return _serverRemotePort;
            }
        }
    }

    public bool IsClientRunning
    {
        get
        {
            lock (_stateLock)
            {
                return _clientRunning;
            }
        }
    }

    public bool HasActiveConnection => ClientLocalPort > 0 && ServerRemotePort > 0;

    public string GetStatusSummary(int captureDeviceCount)
    {
        lock (_stateLock)
        {
            if (!_clientRunning)
            {
                return
                    $"Capturing on {captureDeviceCount} network adapter(s).\n\n" +
                    "sphereclient.exe is not running. Ports will be detected automatically when the client starts.";
            }

            if (_clientLocalPort == 0 || _serverRemotePort == 0)
            {
                return
                    $"Capturing on {captureDeviceCount} network adapter(s).\n\n" +
                    "sphereclient.exe is running but no established TCP connection was found yet.";
            }

            return
                $"Capturing on {captureDeviceCount} network adapter(s).\n\n" +
                $"sphereclient.exe is running.\n" +
                $"Incoming (local) port: {_clientLocalPort}\n" +
                $"Outgoing (server) port: {_serverRemotePort}";
        }
    }

    private void Scan()
    {
        var processIds = FindSphereClientProcessIds();
        TcpConnection? selectedConnection = null;

        foreach (var processId in processIds)
        {
            foreach (var connection in WindowsProcessTcpConnections.GetEstablishedConnectionsForProcess(processId))
            {
                if (selectedConnection is null)
                {
                    selectedConnection = connection;
                    continue;
                }

                if (ScoreConnection(connection) > ScoreConnection(selectedConnection.Value))
                {
                    selectedConnection = connection;
                }
            }
        }

        lock (_stateLock)
        {
            _clientRunning = processIds.Count > 0;
            if (selectedConnection is null)
            {
                _clientLocalPort = 0;
                _serverRemotePort = 0;
                return;
            }

            _clientLocalPort = selectedConnection.Value.LocalPort;
            _serverRemotePort = selectedConnection.Value.RemotePort;
        }
    }

    private static int ScoreConnection(TcpConnection connection)
    {
        var score = 0;
        if (PreferredServerAddresses.Contains(connection.RemoteAddress))
        {
            score += 100;
        }

        if (!IPAddress.IsLoopback(connection.RemoteAddress))
        {
            score += 10;
        }

        return score;
    }

    private static List<int> FindSphereClientProcessIds()
    {
        var processIds = new List<int>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (!IsSphereClientProcess(process))
                {
                    continue;
                }

                processIds.Add(process.Id);
            }
            catch
            {
                // Access denied or process exited between enumeration and inspection.
            }
            finally
            {
                process.Dispose();
            }
        }

        return processIds;
    }

    private static bool IsSphereClientProcess(Process process)
    {
        var name = process.ProcessName;
        return name.Equals("sphereclient", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("sphereclient", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        _scanTimer.Dispose();
    }

    private readonly record struct TcpConnection(
        IPAddress LocalAddress,
        int LocalPort,
        IPAddress RemoteAddress,
        int RemotePort);

    private static class WindowsProcessTcpConnections
    {
        private const int AfInet = 2;
        private const uint MibTcpStateEstablished = 5;

        private enum TcpTableClass
        {
            TcpTableOwnerPidAll = 5
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(
            IntPtr pTcpTable,
            ref int dwOutBufLen,
            bool sort,
            int ipVersion,
            TcpTableClass tblClass,
            uint reserved);

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint State;
            public uint LocalAddr;
            public uint LocalPort;
            public uint RemoteAddr;
            public uint RemotePort;
            public uint OwningProcess;
        }

        public static IEnumerable<TcpConnection> GetEstablishedConnectionsForProcess(int processId)
        {
            var bufferSize = 0;
            _ = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AfInet, TcpTableClass.TcpTableOwnerPidAll, 0);

            if (bufferSize <= 0)
            {
                yield break;
            }

            var buffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                var result = GetExtendedTcpTable(buffer, ref bufferSize, true, AfInet, TcpTableClass.TcpTableOwnerPidAll, 0);
                if (result != 0)
                {
                    yield break;
                }

                var rowCount = Marshal.ReadInt32(buffer);
                var rowPtr = buffer + 4;
                var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();

                for (var i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                    rowPtr += rowSize;

                    if (row.OwningProcess != (uint)processId || row.State != MibTcpStateEstablished)
                    {
                        continue;
                    }

                    yield return new TcpConnection(
                        ConvertAddress(row.LocalAddr),
                        ConvertPort(row.LocalPort),
                        ConvertAddress(row.RemoteAddr),
                        ConvertPort(row.RemotePort));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static IPAddress ConvertAddress(uint address)
        {
            return new IPAddress(BitConverter.GetBytes(address));
        }

        private static int ConvertPort(uint port)
        {
            var networkOrderPort = (ushort)(port & 0xFFFF);
            return (networkOrderPort >> 8) | ((networkOrderPort & 0xFF) << 8);
        }
    }
}
