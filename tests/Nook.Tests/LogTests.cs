using Nook;

namespace Nook.Tests;

/// <summary>
/// <see cref="Log"/> is static, so each test points it at its own temporary directory and the
/// collection keeps these tests from running in parallel with one another.
/// </summary>
[Collection("Log")]
public class LogTests : IDisposable
{
    private readonly string _directory;
    private readonly string _previousPath;

    public LogTests()
    {
        _previousPath = Log.FilePath;
        _directory = Path.Combine(Path.GetTempPath(), "nook-log-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        Log.Configure(Path.Combine(_directory, "nook.log"));
    }

    public void Dispose()
    {
        Log.Configure(_previousPath);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless; failing the test run over it is not.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string ActiveFile => Path.Combine(_directory, "nook.log");

    private string RolledFile => Path.Combine(_directory, "nook.1.log");

    [Fact]
    public void Info_writes_a_timestamped_line_with_the_level_and_message()
    {
        Log.Info("hello");

        Assert.Matches(
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2} INFO hello\r?\n$",
            File.ReadAllText(ActiveFile));
    }

    [Fact]
    public void Warn_writes_the_warn_level()
    {
        Log.Warn("careful");

        Assert.Matches(@" WARN careful\r?\n$", File.ReadAllText(ActiveFile));
    }

    [Fact]
    public void Error_appends_the_exception_text_after_the_entry()
    {
        var exception = new InvalidOperationException("kaboom");

        Log.Error("refresh failed", exception);

        var text = File.ReadAllText(ActiveFile);
        Assert.Matches(@" ERROR refresh failed\r?\n", text);
        Assert.Contains(exception.ToString(), text);
    }

    [Fact]
    public void Error_without_an_exception_writes_only_the_entry()
    {
        Log.Error("no exception here");

        var lines = File.ReadAllLines(ActiveFile);
        Assert.Single(lines);
        Assert.EndsWith(" ERROR no exception here", lines[0]);
    }

    [Fact]
    public void Exceeding_the_limit_rolls_the_active_file_over_to_nook_1_log()
    {
        Log.Configure(ActiveFile, maxBytes: 200);
        var longMessage = "first-" + new string('x', 150);

        Log.Info(longMessage);
        Log.Info("second");

        Assert.Contains("first-", File.ReadAllText(RolledFile));
        Assert.DoesNotContain("first-", File.ReadAllText(ActiveFile));
        Assert.Contains("second", File.ReadAllText(ActiveFile));
    }

    [Fact]
    public void Rollover_keeps_at_most_two_files()
    {
        Log.Configure(ActiveFile, maxBytes: 200);

        for (var i = 0; i < 10; i++)
        {
            Log.Info("entry " + i + new string('x', 120));
        }

        Assert.Equal(2, Directory.GetFiles(_directory).Length);
    }

    [Fact]
    public void An_unwritable_directory_is_skipped_without_throwing()
    {
        // A file where a directory is needed makes the directory unwritable on every platform.
        var blocker = Path.Combine(_directory, "blocker");
        File.WriteAllText(blocker, string.Empty);
        Log.Configure(Path.Combine(blocker, "nook.log"));

        Assert.Null(Record.Exception(() => Log.Info("dropped")));
        Assert.Null(Record.Exception(() => Log.Error("dropped", new Exception("x"))));
    }

    [Fact]
    public void An_invalid_path_is_skipped_without_throwing()
    {
        Log.Configure(Path.Combine(_directory, "bad\0name.log"));

        Assert.Null(Record.Exception(() => Log.Info("dropped")));
        Assert.Null(Record.Exception(() => Log.Warn("dropped")));
    }
}
