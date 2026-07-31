using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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

    private static readonly HashSet<int> KnownSphereServerPorts = new() { 25860, 25861 };

    private readonly Timer _scanTimer;
    private readonly object _stateLock = new();
    private int _clientLocalPort;
    private int _serverRemotePort;
    private bool _clientRunning;
    private bool _preferLocalConnections;

    public SphereClientConnectionDiscovery(TimeSpan scanInterval)
    {
        _scanTimer = new Timer(_ => Scan(), null, TimeSpan.Zero, scanInterval);
    }

    /// <summary>
    /// When true, prefer loopback/private LAN connections (local emu / MITM).
    /// When false, ignore those and prefer live Sphere servers.
    /// </summary>
    public bool PreferLocalConnections
    {
        get
        {
            lock (_stateLock)
            {
                return _preferLocalConnections;
            }
        }
        set
        {
            lock (_stateLock)
            {
                if (_preferLocalConnections == value)
                {
                    return;
                }

                _preferLocalConnections = value;
            }

            // Re-evaluate immediately so the UI/capture picks up the new mode.
            Scan();
        }
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
            var localMode = _preferLocalConnections ? "Local capture: ON" : "Local capture: OFF";

            if (!_clientRunning)
            {
                return
                    $"Capturing on {captureDeviceCount} network adapter(s).\n{localMode}\n\n" +
                    "sphereclient.exe is not running. Ports will be detected automatically when the client starts.";
            }

            if (_clientLocalPort == 0 || _serverRemotePort == 0)
            {
                var hint = _preferLocalConnections
                    ? "No local/private established TCP connection was found yet."
                    : "No live-server established TCP connection was found yet. Enable Local to capture localhost.";

                return
                    $"Capturing on {captureDeviceCount} network adapter(s).\n{localMode}\n\n" +
                    $"sphereclient.exe is running but {hint}";
            }

            return
                $"Capturing on {captureDeviceCount} network adapter(s).\n{localMode}\n\n" +
                $"sphereclient.exe is running.\n" +
                $"Incoming (local) port: {_clientLocalPort}\n" +
                $"Outgoing (server) port: {_serverRemotePort}";
        }
    }

    private void Scan()
    {
        var processIds = FindSphereClientProcessIds();
        TcpConnection? selectedConnection = null;
        bool preferLocal;

        lock (_stateLock)
        {
            preferLocal = _preferLocalConnections;
        }

        foreach (var processId in processIds)
        {
            foreach (var connection in WindowsProcessTcpConnections.GetEstablishedConnectionsForProcess(processId))
            {
                if (!IsEligibleConnection(connection, preferLocal))
                {
                    continue;
                }

                if (selectedConnection is null)
                {
                    selectedConnection = connection;
                    continue;
                }

                if (ScoreConnection(connection, preferLocal) > ScoreConnection(selectedConnection.Value, preferLocal))
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

    private static bool IsEligibleConnection(TcpConnection connection, bool preferLocal)
    {
        var remoteIsLocal = IsLocalCaptureAddress(connection.RemoteAddress);
        if (preferLocal)
        {
            // Local mode: accept loopback/private, or known sphere ports on any host.
            return remoteIsLocal || KnownSphereServerPorts.Contains(connection.RemotePort);
        }

        // Live mode: never track localhost/private targets.
        if (remoteIsLocal)
        {
            return false;
        }

        return PreferredServerAddresses.Contains(connection.RemoteAddress) ||
               KnownSphereServerPorts.Contains(connection.RemotePort);
    }

    private static int ScoreConnection(TcpConnection connection, bool preferLocal)
    {
        var score = 0;

        if (PreferredServerAddresses.Contains(connection.RemoteAddress))
        {
            score += preferLocal ? 10 : 100;
        }

        if (KnownSphereServerPorts.Contains(connection.RemotePort))
        {
            score += 50;
        }

        if (IsLocalCaptureAddress(connection.RemoteAddress))
        {
            score += preferLocal ? 100 : -1000;
        }

        return score;
    }

    internal static bool IsLocalCaptureAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        // 10.0.0.0/8
        if (bytes[0] == 10)
        {
            return true;
        }

        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
        {
            return true;
        }

        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            return true;
        }

        // 169.254.0.0/16 link-local
        if (bytes[0] == 169 && bytes[1] == 254)
        {
            return true;
        }

        return false;
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
