using System;
using System.IO;
using System.Windows;

namespace PacketLogViewer;

public partial class App : Application
{
    public static string? PacketDatabaseOverride { get; internal set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        PacketDatabaseOverride = ParseDatabasePath(e.Args);
        if (PacketDatabaseOverride is not null && !File.Exists(PacketDatabaseOverride))
        {
            MessageBox.Show($"Packet database not found:\n{PacketDatabaseOverride}", "PacketLogViewer");
            Shutdown(1);
            return;
        }

        base.OnStartup(e);
    }

    internal static string? ParseDatabasePath(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--db" or "-db" or "/db")
            {
                return i + 1 < args.Length ? args[i + 1] : null;
            }

            if (arg.StartsWith("--db=", StringComparison.OrdinalIgnoreCase))
            {
                return arg[5..];
            }

            if (!arg.StartsWith('-') && arg.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
            {
                return arg;
            }
        }

        return null;
    }
}