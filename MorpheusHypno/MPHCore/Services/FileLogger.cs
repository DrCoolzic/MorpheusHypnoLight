using Microsoft.Extensions.Logging;
using System.Threading;

namespace MPHCore.Services;

/// <summary>
/// A file-based logger implementation that writes log messages to a specified file path.
/// </summary>
/// <param name="path">The file path where log messages will be written.</param>
/// <param name="categoryName">The category name for the logger, typically the class name.</param>
public class FileLogger(string path, string categoryName) : ILogger
{
    private readonly string _path = path;
    private readonly string _categoryName = categoryName;
    private static readonly object _lockObject = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var now = DateTime.Now;
        var message = $"[{now:yyyy-MM-dd HH:mm:ss}] [{logLevel}] {_categoryName}: {formatter(state, exception)}";
        if (exception != null)
        {
            message += Environment.NewLine + exception;
        }

        var lockTaken = false;
        try
        {
            Monitor.Enter(_lockObject, ref lockTaken);
            
            // Ensure directory exists before writing
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            
            File.AppendAllText(_path, message + Environment.NewLine);
        }
        catch
        {
            // Ignore file write errors in logger
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_lockObject);
            }
        }
    }
}

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _path;
    private readonly Dictionary<string, FileLogger> _loggers = new();
    private static readonly object _lockObject = new();

    public FileLoggerProvider(string path)
    {
        _path = path;
        
        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Clear existing log file
        try
        {
            if (File.Exists(path))
            {
                File.WriteAllText(path, string.Empty);
            }
        }
        catch
        {
            // Ignore errors when clearing log file
        }
    }

    public ILogger CreateLogger(string categoryName)
    {
        var lockTaken = false;
        try
        {
            Monitor.Enter(_lockObject, ref lockTaken);
            if (!_loggers.TryGetValue(categoryName, out var logger))
            {
                logger = new FileLogger(_path, categoryName);
                _loggers.Add(categoryName, logger);
            }
            return logger;
        }
        finally
        {
            if (lockTaken)
            {
                Monitor.Exit(_lockObject);
            }
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _loggers.Clear();
        }
    }
}
