using System.Text;

namespace VirtualPianoBot.Core;

/// <summary>A small dependency-free file logger for early startup diagnostics.</summary>
public sealed class AppLogger
{
    private readonly object _gate = new();
    private readonly string _logFile;

    public AppLogger(AppPaths paths)
    {
        Directory.CreateDirectory(paths.LogsDirectory);
        _logFile = Path.Combine(paths.LogsDirectory, $"vpb6-{DateTime.Now:yyyy-MM-dd}.log");
        Info("Logger initialized.");
    }

    public string LogFilePath => _logFile;

    public void Info(string message) => Write("INFO", message, null);
    public void Warning(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    public void Flush()
    {
        // Writes are synchronous and append-only, so nothing is buffered here.
        lock (_gate) { }
    }

    private void Write(string level, string message, Exception? exception)
    {
        var builder = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("O"))
            .Append(" [").Append(level).Append("] ")
            .AppendLine(message);

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        var line = builder.ToString();
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_logFile, line, Encoding.UTF8);
            }
            catch
            {
                try { Console.Error.Write(line); }
                catch { /* Logging must never crash the application. */ }
            }
        }
    }
}
