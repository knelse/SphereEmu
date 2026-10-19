using System.Buffers.Binary;
using System.Security.Cryptography;
using CryptoRandom = global::System.Security.Cryptography.RandomNumberGenerator;
using Godot;
using SphServer.Client;
using SphServer.Server.Handlers;
using SphServer.Server.Login.Auth;
using SphServer.Shared.Db.DataModels;
using SphServer.Shared.Logger;
using SphServer.Shared.WorldState;

namespace SphServer.Server.Login.Secure;

/// <summary>
/// UDP welcome, then the same frames on that socket
/// </summary>
sealed class SecureLoginServer (ConnectionHandler connectionHandler)
{
    public static SecureLoginServer? Active { get; private set; }

    private readonly UdpServer udpServer = new ();
    private readonly List<PacketPeerUdp> peers = [];
    private readonly List<PendingCookie> cookies = [];
    private readonly Dictionary<uint, ResumeSession> sessions = [];

    public void Start (ushort udpPort)
    {
        var udpError = udpServer.Listen (udpPort, "*");
        if (udpError != Error.Ok)
        {
            SphLogger.Error ($"Secure UDP listen failed on {udpPort}: {udpError}");
            return;
        }

        Active = this;
        SphLogger.Info ($"Secure login UDP {udpPort}");
    }

    public void Poll ()
    {
        Expire ();
        if (udpServer.IsListening ())
        {
            udpServer.Poll ();
            while (udpServer.IsConnectionAvailable ())
            {
                peers.Add (udpServer.TakeConnection ());
            }

            foreach (var peer in peers)
            {
                Drain (peer);
            }
        }
    }

    private void Expire ()
    {
        var now = DateTime.UtcNow;
        cookies.RemoveAll (cookie => now - cookie.Created > SecureLoginWire.CookieLifetime);
        // UDP has no close. A session that stops sending is the disconnect
        var stale = sessions.Where (pair => now - pair.Value.LastActivity > SecureLoginWire.ResumeLifetime)
            .Select (pair => pair.Key).ToArray ();
        foreach (var sessionId in stale)
        {
            if (!sessions.Remove (sessionId, out var session))
            {
                continue;
            }

            session.Client?.RemoveClient ();
        }
    }

    private void Drain (PacketPeerUdp peer)
    {
        while (peer.GetAvailablePacketCount () > 0)
        {
            var packet = peer.GetPacket ();
            var peerKey = peer.GetPacketIP () + ":" + peer.GetPacketPort ();
            if (packet.Length == 0)
            {
                continue;
            }

            if (packet[0] == SecureLoginWire.CookieAsk)
            {
                AcceptAsk (peer, peerKey, packet);
            }
            else if (packet[0] == SecureLoginWire.ClientFinish)
            {
                AcceptFinish (peer, peerKey, packet);
            }
            else if (packet[0] == SecureLoginWire.BeforeGame)
            {
                AcceptBeforeGame (peer, peerKey, packet);
            }
        }
    }

    private void AcceptAsk (PacketPeerUdp peer, string peerKey, byte[] packet)
    {
        if (!SecureLoginWire.TryReadAsk (packet, out var clientNonce, out var token, out var loginBytes,
                out var passwordBytes))
        {
            SphLogger.Info ($"Secure login ignored a cookie ask from {peerKey}");
            return;
        }

        var login = SecureLoginWire.DecodeName (loginBytes);
        var password = SecureLoginWire.DecodeName (passwordBytes);
        if (ActiveWorldObjects.LoggedInClients.ContainsKey (login))
        {
            SphLogger.Info ($"Secure login rejected [{login}], already in");
            peer.PutPacket (SecureLoginWire.WriteReject (clientNonce, SecureLoginWire.RejectAccessDenied));
            return;
        }

        var player = LoginManager.CheckLoginAndGetPlayer (login, password, Guid.Empty);
        if (player is null)
        {
            SphLogger.Info ($"Secure login rejected [{login}]");
            peer.PutPacket (SecureLoginWire.WriteReject (clientNonce, SecureLoginWire.RejectAccessDenied));
            return;
        }

        var cookie = CryptoRandom.GetBytes (SecureLoginWire.CookieLength);
        var serverNonce = CryptoRandom.GetBytes (SecureLoginWire.NonceLength);
        var sessionId = NewSessionId ();
        cookies.Add (new PendingCookie (clientNonce, token, cookie, serverNonce, sessionId, player, peerKey,
            DateTime.UtcNow));
        peer.PutPacket (SecureLoginWire.WriteOffer (clientNonce, cookie, serverNonce, sessionId));
        SphLogger.Info ($"Secure login offered session {sessionId:X8} to [{login}]");
    }

    private void AcceptFinish (PacketPeerUdp peer, string peerKey, byte[] packet)
    {
        if (!SecureLoginWire.TryReadFinish (packet, out var clientNonce, out var token, out var cookie))
        {
            return;
        }

        var index = cookies.FindIndex (pending =>
            pending.Peer == peerKey
            && CryptographicOperations.FixedTimeEquals (pending.Cookie, cookie)
            && CryptographicOperations.FixedTimeEquals (pending.ClientNonce, clientNonce)
            && CryptographicOperations.FixedTimeEquals (pending.Token, token));
        if (index < 0)
        {
            SphLogger.Info ($"Secure login ignored a finish from {peerKey}");
            return;
        }

        var pending = cookies[index];
        cookies.RemoveAt (index);
        var client = connectionHandler.HandleUdp (pending.Player, peerKey);
        if (client is null)
        {
            SphLogger.Info ($"Secure login dropped session {pending.SessionId:X8}");
            return;
        }

        var sendKey = SecureLoginWire.DeriveKey (token, clientNonce, pending.ServerNonce, "sphere-s2c");
        var receiveKey = SecureLoginWire.DeriveKey (token, clientNonce, pending.ServerNonce, "sphere-c2s");
        var session = new ResumeSession (pending.SessionId, pending.Player, sendKey, receiveKey, peer, peerKey,
            DateTime.UtcNow);
        sessions[pending.SessionId] = session;
        session.Client = client;
        client.BindBeforeGameUdp (packet => SendSealed (session, SecureLoginWire.BeforeGame, packet));
        foreach (var frame in session.EarlyFrames)
        {
            if (!client.TryAcceptChannelDatagram (frame))
            {
                client.FeedBeforeGameUdp (frame);
            }
        }

        session.EarlyFrames.Clear ();
        connectionHandler.Attach (client);
        peer.PutPacket (SecureLoginWire.WriteWelcome (pending.SessionId, sendKey));
        SphLogger.Info ($"Secure welcome for session {pending.SessionId:X8}");
    }

    public void DropClient (SphereClient client)
    {
        var match = sessions.FirstOrDefault (pair => pair.Value.Client == client);
        if (match.Key != 0)
        {
            sessions.Remove (match.Key);
        }
    }

    private void AcceptBeforeGame (PacketPeerUdp peer, string peerKey, byte[] packet)
    {
        if (packet.Length < 1 + 4 + 8 + SecureLoginWire.TagLength)
        {
            return;
        }

        var sessionId = BinaryPrimitives.ReadUInt32LittleEndian (packet.AsSpan (1, 4));
        var counter = BinaryPrimitives.ReadUInt64LittleEndian (packet.AsSpan (5, 8));
        if (!sessions.TryGetValue (sessionId, out var session) || session.PeerKey != peerKey ||
            counter != session.ReceiveCounter)
        {
            return;
        }

        if (!SecureLoginWire.TryOpenSealed (packet, session.ReceiveKey, out var plain) || plain.Length == 0)
        {
            return;
        }

        session.ReceiveCounter++;
        session.LastActivity = DateTime.UtcNow;
        if (session.Client is null)
        {
            session.EarlyFrames.Add (plain);
            return;
        }

        if (!session.Client.TryAcceptChannelDatagram (plain))
        {
            session.Client.FeedBeforeGameUdp (plain);
        }
    }

    private void SendSealed (ResumeSession session, byte type, byte[] plain)
    {
        var counter = session.SendCounter++;
        session.PeerUdp.PutPacket (SecureLoginWire.WriteSealed (type, session.SessionId, counter, session.SendKey,
            plain));
    }

    private uint NewSessionId ()
    {
        Span<byte> bytes = stackalloc byte[4];
        uint sessionId;
        do
        {
            CryptoRandom.Fill (bytes);
            sessionId = BinaryPrimitives.ReadUInt32LittleEndian (bytes);
        } while (sessionId == 0 || sessions.ContainsKey (sessionId) || cookies.Any (cookie => cookie.SessionId == sessionId));

        return sessionId;
    }

    private sealed class PendingCookie (
        byte[] clientNonce,
        byte[] token,
        byte[] cookie,
        byte[] serverNonce,
        uint sessionId,
        PlayerDbEntry player,
        string peer,
        DateTime created)
    {
        public byte[] ClientNonce { get; } = clientNonce;
        public byte[] Token { get; } = token;
        public byte[] Cookie { get; } = cookie;
        public byte[] ServerNonce { get; } = serverNonce;
        public uint SessionId { get; } = sessionId;
        public PlayerDbEntry Player { get; } = player;
        public string Peer { get; } = peer;
        public DateTime Created { get; } = created;
    }

    private sealed class ResumeSession (
        uint sessionId,
        PlayerDbEntry player,
        byte[] sendKey,
        byte[] receiveKey,
        PacketPeerUdp peerUdp,
        string peerKey,
        DateTime created)
    {
        public uint SessionId { get; } = sessionId;
        public PlayerDbEntry Player { get; } = player;
        public byte[] SendKey { get; } = sendKey;
        public byte[] ReceiveKey { get; } = receiveKey;
        public PacketPeerUdp PeerUdp { get; } = peerUdp;
        public string PeerKey { get; } = peerKey;
        public DateTime Created { get; } = created;
        public DateTime LastActivity { get; set; } = created;
        public ulong SendCounter { get; set; } = 2;
        public ulong ReceiveCounter { get; set; } = 1;
        public SphereClient? Client { get; set; }
        public List<byte[]> EarlyFrames { get; } = [];
    }
}
