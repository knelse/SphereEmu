using System;
using System.IO;
using System.Threading;

namespace SphServer.Shared.Logger;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public static class SphLogger
{
    private static readonly Lock lockObject = new();
    private static string logFilePath = string.Empty;
    private static bool logToConsole = true;
    private static bool logToFile = true;
    private static LogLevel minimumLogLevel = LogLevel.Info;

    public static void Initialize(string? filePath = null, bool enableConsole = true, bool enableFile = true,
        LogLevel minLevel = LogLevel.Info)
    {
        lock (lockObject)
        {
            logFilePath = GenerateTimestampedLogPath(!string.IsNullOrEmpty(filePath) ? filePath : "logs/server.log");

            logToConsole = enableConsole;
            logToFile = enableFile;
            minimumLogLevel = minLevel;

            if (logToFile)
            {
                try
                {
                    var directory = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllText(logFilePath,
                        $"=== SphServer Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
                    Console.WriteLine($"Log file created: {logFilePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize log file: {ex.Message}");
                    logToFile = false;
                }
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

    public static void Debug(string message)
    {
        Log(LogLevel.Debug, message);
    }

    public static void Info(string message)
    {
        Log(LogLevel.Info, message);
    }

    public static void Warning(string message)
    {
        Log(LogLevel.Warning, message);
    }

    public static void Error(string message)
    {
        Log(LogLevel.Error, message);
    }

    public static void Error(string message, Exception exception)
    {
        Log(LogLevel.Error, $"{message}: {exception}");
    }

    private static void Log(LogLevel level, string message)
    {
        if (level < minimumLogLevel)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var levelStr = level.ToString().ToUpper();
        var logMessage = $"[{timestamp}] [{levelStr}] {message}";

        lock (lockObject)
        {
            if (logToConsole)
            {
                var originalColor = Console.ForegroundColor;
                Console.ForegroundColor = GetConsoleColor(level);
                Console.WriteLine(logMessage);
                Console.ForegroundColor = originalColor;
            }

            if (logToFile)
            {
                try
                {
                    File.AppendAllText(logFilePath, logMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    if (logToConsole)
                    {
                        Console.WriteLine($"Failed to write to log file: {ex.Message}");
                    }
                }
            }
        }
    }

    private static ConsoleColor GetConsoleColor(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => ConsoleColor.Gray,
            LogLevel.Info => ConsoleColor.White,
            LogLevel.Warning => ConsoleColor.Yellow,
            LogLevel.Error => ConsoleColor.Red,
            _ => ConsoleColor.White
        };
    }
}