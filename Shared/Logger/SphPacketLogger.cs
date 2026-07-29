namespace SphServer.Shared.Logger;

public static class SphPacketLogger
{
    private static readonly Lock lockObject = new();
    private static string logFilePath = "logs/packets.log";
    private static bool initialized;

    public static void Initialize(string? filePath = null)
    {
        lock (lockObject)
        {
            logFilePath = GenerateTimestampedLogPath(
                !string.IsNullOrEmpty(filePath) ? filePath : "logs/packets.log");

            try
            {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.AppendAllText(logFilePath,
                    $"=== Packet Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                initialized = true;
                Console.WriteLine($"Packet log file created: {logFilePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize packet log file: {ex.Message}");
                initialized = false;
            }
        }
    }

    public static void LogIncoming(ushort clientId, byte[] packet)
    {
        Log("IN", clientId, packet);
    }

    public static void LogOutgoing(ushort clientId, byte[] packet)
    {
        Log("OUT", clientId, packet);
    }

    private static void Log(string direction, ushort clientId, byte[] packet)
    {
        if (!initialized || packet.Length == 0)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var hex = Convert.ToHexString(packet);
        var logMessage = $"[{timestamp}] [{direction}] CLI {clientId:X4} {hex}";

        lock (lockObject)
        {
            try
            {
                File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write to packet log file: {ex.Message}");
            }
        }
    }

    private static string GenerateTimestampedLogPath(string originalPath)
    {
        var directory = Path.GetDirectoryName(originalPath) ?? "";
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalPath);
        var extension = Path.GetExtension(originalPath);
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        var timestampedFileName = $"{fileNameWithoutExtension}_{timestamp}{extension}";
        return Path.Combine(directory, timestampedFileName);
    }
}
