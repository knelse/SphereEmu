using System;
using System.IO;
using System.Linq;
using System.Text;
using PacketLogViewer.Models;

namespace PacketLogViewer;

internal static class PacketReclassifier
{
    private const int CommitEvery = 500;
    private const int LatestCount = 1000;

    public static int Run()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        PacketLogViewerMainWindow.Win1251 = Encoding.GetEncoding(1251);
        var dbPath = PacketLogViewerMainWindow.PacketDatabasePath;
        var logPath = Path.Combine(
            Path.GetDirectoryName(dbPath) ?? ".",
            Path.GetFileNameWithoutExtension(dbPath) + ".reclassify.log");

        void Log(string line)
        {
            Console.WriteLine(line);
            File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
        }

        File.WriteAllText(logPath, $"reclassify start {DateTime.Now:O}{Environment.NewLine}db {dbPath}{Environment.NewLine}",
            Encoding.UTF8);

        var collection = PacketLogViewerMainWindow.PacketCollection;
        var packets = collection.Query().OrderByDescending(x => x.Timestamp).Limit(LatestCount).ToList();
        packets.Reverse();
        Log($"loaded latest {packets.Count} packets");

        try
        {
            Log("loading MBC decoder");
            PacketAnalyzer.ClassifyNamesOnly = true;
            PacketAnalyzer.ResetMbcSession();
            Log("decoder ready");
            var db = PacketLogViewerMainWindow.PacketDatabase;
            var mbc = 0;
            var other = 0;
            var errors = 0;
            db.BeginTrans();
            try
            {
                for (var i = 0; i < packets.Count; i++)
                {
                    var packet = packets[i];
                    try
                    {
                        packet.AnalyzeResult ??= [];
                        packet.PacketParts ??= [];
                        packet.PacketType = null;
                        packet.AnalyzeResult.Clear();
                        packet.PacketParts.Clear();
                        packet.UpdatePacketPartsForContent();
                        packet.AnalyzeResult.Clear();
                        packet.PacketParts.Clear();
                        collection.Update(packet);
                        if (packet.EventName is not null &&
                            (packet.EventName.Contains('.') ||
                             packet.EventName.StartsWith("MBC.", StringComparison.Ordinal)))
                        {
                            mbc++;
                        }
                        else
                        {
                            other++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Log($"id={packet.Id} failed: {ex.Message}");
                    }

                    var n = i + 1;
                    if (n % CommitEvery == 0 || n == packets.Count)
                    {
                        db.Commit();
                        Log($"... {n}/{packets.Count} mbc={mbc} other={other} errors={errors}");
                        if (n != packets.Count)
                        {
                            db.BeginTrans();
                        }
                    }
                }
            }
            catch
            {
                db.Rollback();
                throw;
            }

            Log($"done mbc={mbc} other={other} errors={errors}");
            return errors == 0 ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log(ex.ToString());
            return 1;
        }
        finally
        {
            PacketAnalyzer.ClassifyNamesOnly = false;
        }
    }
}
