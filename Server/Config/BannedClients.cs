using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using SphServer.Shared.Logger;

namespace SphServer.Server.Config;

public class BannedClientEntry
{
    public string Login { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime BannedAt { get; set; }
}

public class BannedClientsData
{
    public List<BannedClientEntry> BannedClients { get; set; } = [];
}

public static class BannedClients
{
    private const string BANNED_CLIENTS_FILE = "bannedclients.json";
    private static BannedClientsData bannedClientsData = new ();
    private static readonly Lock lockObject = new ();
    private static readonly JsonSerializerOptions serializerOptions = new () { WriteIndented = true };

    static BannedClients ()
    {
        LoadBannedClients();
    }

    private static void LoadBannedClients ()
    {
        try
        {
            if (!File.Exists(BANNED_CLIENTS_FILE))
            {
                SphLogger.Info("No banned clients file found. Creating new one.");
                SaveBannedClients();
                return;
            }

            var json = File.ReadAllText(BANNED_CLIENTS_FILE);
            lock (lockObject)
            {
                bannedClientsData = JsonSerializer.Deserialize<BannedClientsData>(json) ?? new BannedClientsData();
            }

            SphLogger.Info($"Loaded {bannedClientsData.BannedClients.Count} banned clients.");
        }
        catch (Exception ex)
        {
            SphLogger.Error("Failed to load banned clients file.", ex);
            bannedClientsData = new BannedClientsData();
        }
    }

    private static void SaveBannedClients ()
    {
        try
        {
            var json = JsonSerializer.Serialize(bannedClientsData, serializerOptions);
            File.WriteAllText(BANNED_CLIENTS_FILE, json);
            SphLogger.Info($"Saved {bannedClientsData.BannedClients.Count} banned clients.");
        }
        catch (Exception ex)
        {
            SphLogger.Error("Failed to save banned clients file.", ex);
        }
    }

    public static bool IsIpBanned (string ipAddress)
    {
        lock (lockObject)
        {
            return bannedClientsData.BannedClients.Any(b => b.IpAddress == ipAddress);
        }
    }

    public static bool IsLoginBanned (string login)
    {
        lock (lockObject)
        {
            return bannedClientsData.BannedClients.Any(b => b.Login == login);
        }
    }

    public static void BanClient (string login, string ipAddress)
    {
        lock (lockObject)
        {
            if (bannedClientsData.BannedClients.Any(b => b.Login == login && b.IpAddress == ipAddress))
            {
                SphLogger.Info($"Client already banned. Login: {login}, IP: {ipAddress}");
                return;
            }

            var entry = new BannedClientEntry
            {
                Login = login,
                IpAddress = ipAddress,
                BannedAt = DateTime.UtcNow
            };

            bannedClientsData.BannedClients.Add(entry);
            SaveBannedClients();
            SphLogger.Info($"Banned client. Login: {login}, IP: {ipAddress}");
        }
    }

    public static void UnbanClient (string login)
    {
        lock (lockObject)
        {
            var removed = bannedClientsData.BannedClients.RemoveAll(b => b.Login == login);
            if (removed <= 0)
            {
                return;
            }

            SaveBannedClients();
            SphLogger.Info($"Unbanned client. Login: {login}");
        }
    }
}