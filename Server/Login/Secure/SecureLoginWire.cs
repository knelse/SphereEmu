using System.Security.Cryptography;
using System.Text;
using BinaryPrimitives = global::System.Buffers.Binary.BinaryPrimitives;
using SphServer.System;

namespace SphServer.Server.Login.Secure;

/// <summary>
/// Cookie ask through the sealed welcome, then before-game frames on the same keys
/// </summary>
static class SecureLoginWire
{
    public const byte CookieAsk = 1;
    public const byte CookieOffer = 2;
    public const byte ClientFinish = 3;
    public const byte LoginReject = 4;
    public const byte Welcome = 5;
    public const byte RejectAccessDenied = 6;
    public const byte BeforeGame = 0x10;
    public const byte EnteredGame = 0x11;
    public const byte WelcomeOpcode = 1;

    public const int NonceLength = 16;
    public const int TokenLength = 32;
    public const int CookieLength = 16;
    public const int TagLength = 16;
    public const int MaxNameLength = 255;

    public static readonly TimeSpan CookieLifetime = TimeSpan.FromSeconds (5);
    public static readonly TimeSpan ResumeLifetime = TimeSpan.FromSeconds (30);

    public static byte[] Token (ReadOnlySpan<byte> login, ReadOnlySpan<byte> password)
    {
        var material = new byte[login.Length + 1 + password.Length];
        login.CopyTo (material);
        material[login.Length] = 0;
        password.CopyTo (material.AsSpan (login.Length + 1));
        return SHA256.HashData (material);
    }

    public static bool TryReadAsk (ReadOnlySpan<byte> packet, out byte[] clientNonce, out byte[] token,
        out byte[] login, out byte[] password)
    {
        clientNonce = [];
        token = [];
        login = [];
        password = [];
        if (packet.Length < 1 + NonceLength + TokenLength + 2 || packet[0] != CookieAsk)
        {
            return false;
        }

        var offset = 1;
        clientNonce = packet.Slice (offset, NonceLength).ToArray ();
        offset += NonceLength;
        token = packet.Slice (offset, TokenLength).ToArray ();
        offset += TokenLength;
        if (offset >= packet.Length)
        {
            return false;
        }

        var loginLength = packet[offset++];
        if (loginLength == 0 || loginLength > MaxNameLength || offset + loginLength >= packet.Length)
        {
            return false;
        }

        login = packet.Slice (offset, loginLength).ToArray ();
        offset += loginLength;
        var passwordLength = packet[offset++];
        if (passwordLength == 0 || passwordLength > MaxNameLength || offset + passwordLength != packet.Length)
        {
            return false;
        }

        password = packet.Slice (offset, passwordLength).ToArray ();
        return CryptographicOperations.FixedTimeEquals (token, Token (login, password));
    }

    public static byte[] WriteOffer (ReadOnlySpan<byte> clientNonce, ReadOnlySpan<byte> cookie,
        ReadOnlySpan<byte> serverNonce, uint sessionId)
    {
        var packet = new byte[1 + NonceLength + CookieLength + NonceLength + 4];
        packet[0] = CookieOffer;
        clientNonce.CopyTo (packet.AsSpan (1));
        cookie.CopyTo (packet.AsSpan (1 + NonceLength));
        serverNonce.CopyTo (packet.AsSpan (1 + NonceLength + CookieLength));
        BinaryPrimitives.WriteUInt32LittleEndian (
            packet.AsSpan (1 + NonceLength + CookieLength + NonceLength), sessionId);
        return packet;
    }

    public static byte[] WriteReject (ReadOnlySpan<byte> clientNonce, byte reason)
    {
        var packet = new byte[1 + NonceLength + 1];
        packet[0] = LoginReject;
        clientNonce.CopyTo (packet.AsSpan (1));
        packet[1 + NonceLength] = reason;
        return packet;
    }

    public static bool TryReadFinish (ReadOnlySpan<byte> packet, out byte[] clientNonce, out byte[] token,
        out byte[] cookie)
    {
        clientNonce = [];
        token = [];
        cookie = [];
        if (packet.Length != 1 + NonceLength + TokenLength + CookieLength || packet[0] != ClientFinish)
        {
            return false;
        }

        clientNonce = packet.Slice (1, NonceLength).ToArray ();
        token = packet.Slice (1 + NonceLength, TokenLength).ToArray ();
        cookie = packet.Slice (1 + NonceLength + TokenLength, CookieLength).ToArray ();
        return true;
    }

    public static string DecodeName (byte[] bytes)
    {
        return SphEncoding.Win1251.GetString (bytes);
    }

    public static byte[] DeriveKey (ReadOnlySpan<byte> token, ReadOnlySpan<byte> clientNonce,
        ReadOnlySpan<byte> serverNonce, string info)
    {
        var salt = new byte[NonceLength * 2];
        clientNonce.CopyTo (salt);
        serverNonce.CopyTo (salt.AsSpan (NonceLength));
        return HKDF.DeriveKey (HashAlgorithmName.SHA256, token.ToArray (), TokenLength, salt,
            Encoding.ASCII.GetBytes (info));
    }

    public static byte[] Nonce (uint sessionId, ulong counter)
    {
        var nonce = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian (nonce, sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian (nonce.AsSpan (4), counter);
        return nonce;
    }

    public static byte[] WriteWelcome (uint sessionId, ReadOnlySpan<byte> serverToClientKey)
    {
        const ulong counter = 1;
        var plain = new byte[] { WelcomeOpcode };
        var cipher = new byte[1];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm (serverToClientKey.ToArray (), TagLength))
        {
            aes.Encrypt (Nonce (sessionId, counter), plain, cipher, tag);
        }

        var packet = new byte[1 + 4 + 8 + 1 + TagLength];
        packet[0] = Welcome;
        BinaryPrimitives.WriteUInt32LittleEndian (packet.AsSpan (1), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian (packet.AsSpan (5), counter);
        packet[13] = cipher[0];
        tag.CopyTo (packet.AsSpan (14));
        return packet;
    }

    public static byte[] WriteSealed (byte type, uint sessionId, ulong counter, ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> plain)
    {
        var cipher = new byte[plain.Length];
        var tag = new byte[TagLength];
        using (var aes = new AesGcm (key.ToArray (), TagLength))
        {
            aes.Encrypt (Nonce (sessionId, counter), plain, cipher, tag);
        }

        var packet = new byte[1 + 4 + 8 + cipher.Length + TagLength];
        packet[0] = type;
        BinaryPrimitives.WriteUInt32LittleEndian (packet.AsSpan (1), sessionId);
        BinaryPrimitives.WriteUInt64LittleEndian (packet.AsSpan (5), counter);
        cipher.CopyTo (packet.AsSpan (13));
        tag.CopyTo (packet.AsSpan (13 + cipher.Length));
        return packet;
    }

    public static bool TryOpenSealed (ReadOnlySpan<byte> packet, ReadOnlySpan<byte> key, out byte[] plain)
    {
        plain = [];
        if (packet.Length < 1 + 4 + 8 + TagLength)
        {
            return false;
        }

        var cipherLength = packet.Length - (1 + 4 + 8 + TagLength);
        var sessionId = BinaryPrimitives.ReadUInt32LittleEndian (packet.Slice (1, 4));
        var counter = BinaryPrimitives.ReadUInt64LittleEndian (packet.Slice (5, 8));
        var cipher = packet.Slice (13, cipherLength);
        var tag = packet.Slice (13 + cipherLength, TagLength);
        plain = new byte[cipherLength];
        try
        {
            using var aes = new AesGcm (key.ToArray (), TagLength);
            aes.Decrypt (Nonce (sessionId, counter), cipher, tag, plain);
            return true;
        }
        catch (CryptographicException)
        {
            plain = [];
            return false;
        }
    }
}
