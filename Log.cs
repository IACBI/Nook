using System.Globalization;
using System.Text;

namespace Nook;

/// <summary>
/// Appends diagnostics to %LocalAppData%\Nook\nook.log. Logging is best effort: a full disk or a
/// read-only profile must never affect monitoring, so write failures are dropped silently.
/// </summary>
internal static class Log
{
    private const long DefaultMaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static long rollAtBytes = DefaultMaxBytes;

    /// <summary>Full path of the active log file. The previous generation is kept beside it as nook.1.log.</summary>
    public static string FilePath { get; private set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Nook", "nook.log");

    /// <summary>Records an informational entry.</summary>
    public static void Info(string message) => Write("INFO", message, null);

    /// <summary>Records a warning.</summary>
    public static void Warn(string message) => Write("WARN", message, null);

    /// <summary>Records an error. The exception's full <c>ToString()</c> follows on the lines after the entry.</summary>
    public static void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    /// <summary>Points logging at another file. Used by tests; production always writes to <see cref="FilePath"/>.</summary>
    internal static void Configure(string filePath, long maxBytes = DefaultMaxBytes)
    {
        lock (Gate)
        {
            FilePath = filePath;
            rollAtBytes = maxBytes;
        }
    }

    private static void Write(string level, string message, Exception? exception)
    {
        var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffK", CultureInfo.InvariantCulture);
        var entry = $"{timestamp} {level} {message}{Environment.NewLine}";
        if (exception is not null)
        {
            entry += exception.ToString() + Environment.NewLine;
        }

        try
        {
            lock (Gate)
            {
                var path = Path.GetFullPath(FilePath);
                var directory = Path.GetDirectoryName(path);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                var info = new FileInfo(path);
                // Rolling before the write keeps the active file at or under the limit, so at most two generations exist.
                if (info.Exists && info.Length + Utf8.GetByteCount(entry) > rollAtBytes)
                {
                    File.Move(path, Path.ChangeExtension(path, ".1.log"), overwrite: true);
                }

                File.AppendAllText(path, entry, Utf8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Logging must never throw into the caller. An invalid path surfaces as ArgumentException or NotSupportedException.
        }
    }
}
