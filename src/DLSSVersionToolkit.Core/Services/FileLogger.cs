namespace DLSSVersionToolkit.Core.Services;

using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

public class FileLogger : ILogger
{
    private readonly string _name;
    private readonly string _logFile;
    private readonly string _categoryName;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private static readonly ConcurrentQueue<string> _logFiles = new();

    public FileLogger(string name, string logDirectory)
    {
        _name = name;
        _categoryName = name;
        _logFile = Path.Combine(logDirectory, $"dlss-version-toolkit-{DateTime.Now:yyyy-MM-dd}.log");

        if (_logFiles.IsEmpty)
        {
            CleanupOldLogs(logDirectory);
        }
        _logFiles.Enqueue(_logFile);

        var dir = Path.GetDirectoryName(_logFile);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var message = new StringBuilder();
        message.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        message.Append(" [");
        message.Append(logLevel.ToString().ToUpperInvariant());
        message.Append("] ");
        message.Append(_categoryName);
        message.Append(": ");
        message.Append(formatter(state, exception));

        if (exception != null)
        {
            message.AppendLine();
            message.Append("Exception: ");
            message.Append(exception);
        }

        message.AppendLine();

        WriteToFile(message.ToString());
    }

    private void WriteToFile(string message)
    {
        try
        {
            _semaphore.Wait();
            try
            {
                File.AppendAllText(_logFile, message, Encoding.UTF8);
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch
        {
            // Logging should never throw
        }
    }

    private static void CleanupOldLogs(string logDirectory)
    {
        if (!Directory.Exists(logDirectory)) return;
        var cutoff = DateTime.Now.AddDays(-7);
        foreach (var file in Directory.GetFiles(logDirectory, "dlss-version-toolkit-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch { }
        }
    }
}

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _logDirectory);
    }

    public void Dispose() { }
}

public static class LoggingExtensions
{
    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder, string logDirectory)
    {
        builder.AddProvider(new FileLoggerProvider(logDirectory));
        return builder;
    }

    public static string GetDefaultLogDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DLSSVersionToolkit",
            "logs"
        );
    }
}