using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PacketLogViewer;

/// <summary>
/// Listens for the game client and forwards TCP to the real Sphere server, allowing injection of extra server→client payloads (e.g. teleport).
/// Point the game at <see cref="ListenEndPoint"/> instead of the upstream host.
/// </summary>
public sealed class SphereMitmProxy : IDisposable
{
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private readonly List<ProxySession> _sessions = new();
    private readonly object _sessionsLock = new();

    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private bool _disposed;

    public SphereMitmProxy(string listenAddress, int listenPort, string upstreamHost, int upstreamPort)
    {
        ListenAddress = listenAddress;
        ListenPort = listenPort;
        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        ListenEndPoint = new IPEndPoint(ResolveListenAddress(listenAddress), listenPort);
    }

    public string ListenAddress { get; }

    public int ListenPort { get; }

    public IPEndPoint ListenEndPoint { get; }

    public bool IsRunning => _listener is not null && !_disposed;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(ListenEndPoint.Address, ListenEndPoint.Port);
        _listener.Server.NoDelay = true;
        _listener.Start();

        var token = _cts.Token;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(token), token);
        Debug.WriteLine($"[SphereMitmProxy] Listening on {ListenEndPoint}, upstream {_upstreamHost}:{_upstreamPort}");
    }

    /// <summary>Injects raw bytes on the server→game direction for every active proxied connection.</summary>
    public bool TryInjectTowardClient(byte[] payload)
    {
        lock (_sessionsLock)
        {
            if (_sessions.Count == 0)
            {
                return false;
            }

            foreach (var session in _sessions.ToArray())
            {
                session.InjectTowardGame(payload);
            }

            return true;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignored
        }

        try
        {
            _acceptLoop?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // ignored
        }

        _cts?.Dispose();
        _cts = null;
        _listener = null;
        _acceptLoop = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient? gameClient = null;
            try
            {
                gameClient = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                await Task.Delay(50, ct).ConfigureAwait(false);
                continue;
            }

            if (gameClient is null)
            {
                continue;
            }

            gameClient.NoDelay = true;
            var clientCopy = gameClient;
            _ = Task.Run(() => HandleIncomingConnection(clientCopy, ct), CancellationToken.None);
        }
    }

    private void HandleIncomingConnection(TcpClient gameClient, CancellationToken proxyCt)
    {
        ProxySession? session = null;
        try
        {
            using var upstream = new TcpClient();
            upstream.NoDelay = true;

            if (!TryConnectUpstream(upstream, _upstreamHost, _upstreamPort, proxyCt))
            {
                Debug.WriteLine($"[SphereMitmProxy] Upstream connect failed for {_upstreamHost}:{_upstreamPort}");
                return;
            }

            session = new ProxySession(gameClient, upstream);
            lock (_sessionsLock)
            {
                _sessions.Add(session);
            }

            session.Run(proxyCt);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SphereMitmProxy] Session error: {ex.Message}");
        }
        finally
        {
            if (session is not null)
            {
                lock (_sessionsLock)
                {
                    _sessions.Remove(session);
                }
            }

            try
            {
                gameClient.Dispose();
            }
            catch
            {
                // ignored
            }
        }
    }

    private static bool TryConnectUpstream(TcpClient client, string host, int port, CancellationToken ct)
    {
        try
        {
            if (IPAddress.TryParse(host, out var ip))
            {
                client.Connect(ip, port);
                return true;
            }

            foreach (var addr in Dns.GetHostAddresses(host))
            {
                if (addr.AddressFamily is not (AddressFamily.InterNetwork or AddressFamily.InterNetworkV6))
                {
                    continue;
                }

                ct.ThrowIfCancellationRequested();
                try
                {
                    client.Connect(addr, port);
                    return true;
                }
                catch (SocketException)
                {
                    // try next address
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SphereMitmProxy] Upstream resolution/connect: {ex.Message}");
        }

        return false;
    }

    private static IPAddress ResolveListenAddress(string listenAddress)
    {
        if (IPAddress.TryParse(listenAddress, out var ip))
        {
            return ip;
        }

        var first = Dns.GetHostAddresses(listenAddress)
            .FirstOrDefault(a => a.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6);
        return first ?? throw new InvalidOperationException($"Could not resolve listen address: {listenAddress}");
    }

    private sealed class ProxySession
    {
        private readonly TcpClient _game;
        private readonly TcpClient _upstream;
        private readonly object _clientWriteLock = new();

        public ProxySession(TcpClient game, TcpClient upstream)
        {
            _game = game;
            _upstream = upstream;
        }

        public void Run(CancellationToken ct)
        {
            using var registration = ct.Register(static state =>
            {
                try
                {
                    ((ProxySession)state!).ShutdownSockets();
                }
                catch
                {
                    // ignored
                }
            }, this);

            var gameStream = _game.GetStream();
            var upstreamStream = _upstream.GetStream();

            var gameToServer = Task.Run(() => Pump(gameStream, upstreamStream, ct), ct);
            var serverToGame = Task.Run(() => PumpFromServer(upstreamStream, gameStream, _clientWriteLock, ct), ct);

            Task.WaitAll(gameToServer, serverToGame);
        }

        public void InjectTowardGame(byte[] payload)
        {
            try
            {
                lock (_clientWriteLock)
                {
                    var stream = _game.GetStream();
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SphereMitmProxy] Inject failed: {ex.Message}");
            }
        }

        private void ShutdownSockets()
        {
            try
            {
                _game.Close();
            }
            catch
            {
                // ignored
            }

            try
            {
                _upstream.Close();
            }
            catch
            {
                // ignored
            }
        }

        private static void Pump(Stream read, Stream write, CancellationToken ct)
        {
            var buffer = new byte[65536];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var n = read.Read(buffer, 0, buffer.Length);
                    if (n <= 0)
                    {
                        break;
                    }

                    write.Write(buffer, 0, n);
                    write.Flush();
                }
            }
            catch (ObjectDisposedException)
            {
                // normal on shutdown
            }
            catch (IOException)
            {
                // peer reset
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SphereMitmProxy] Pump: {ex.Message}");
            }
        }

        private static void PumpFromServer(Stream read, Stream write, object writeLock, CancellationToken ct)
        {
            var buffer = new byte[65536];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var n = read.Read(buffer, 0, buffer.Length);
                    if (n <= 0)
                    {
                        break;
                    }

                    lock (writeLock)
                    {
                        write.Write(buffer, 0, n);
                        write.Flush();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                // normal on shutdown
            }
            catch (IOException)
            {
                // peer reset
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SphereMitmProxy] PumpFromServer: {ex.Message}");
            }
        }
    }
}
