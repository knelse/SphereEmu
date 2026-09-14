using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace PacketLogViewer;

public partial class App : Application
{
    public static string? PacketDatabaseOverride { get; internal set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        PacketDatabaseOverride = ParseDatabasePath(e.Args);
        if (HasFlag(e.Args, "--reclassify"))
        {
            if (PacketDatabaseOverride is not null && !File.Exists(PacketDatabaseOverride))
            {
                Console.WriteLine($"Packet database not found: {PacketDatabaseOverride}");
                Shutdown(1);
                return;
            }

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            try
            {
                Shutdown(PacketReclassifier.Run());
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                Shutdown(1);
            }

            return;
        }

        if (PacketDatabaseOverride is not null && !File.Exists(PacketDatabaseOverride))
        {
            MessageBox.Show($"Packet database not found:\n{PacketDatabaseOverride}", "PacketLogViewer");
            Shutdown(1);
            return;
        }

        StartupUri = new Uri("PacketLogViewerMainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

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