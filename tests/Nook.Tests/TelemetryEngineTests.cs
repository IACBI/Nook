using Nook;
using Xunit.Abstractions;

namespace Nook.Tests;

/// <summary>
/// Recovery after a driver reset disposes the engine and opens a new one in the same process.
/// This runs that cycle against the real vendor libraries: on a machine with an NVIDIA or AMD GPU
/// it proves readings come back after a reopen, and on a CI runner without them it proves the
/// cycle is harmless.
/// </summary>
public class TelemetryEngineTests(ITestOutputHelper output)
{
    [Fact]
    public void Reopening_the_engine_brings_back_the_same_readings()
    {
        var adapters = GpuAdapterCatalog.GetAdapters();
        var answeredFirst = ReadAnsweringAdapters(adapters);
        output.WriteLine($"{answeredFirst.Count} of {adapters.Count} adapters have driver readings.");

        for (var reopen = 0; reopen < 3; reopen++)
        {
            Assert.Equal(answeredFirst, ReadAnsweringAdapters(adapters));
        }
    }

    private static HashSet<GpuLuid> ReadAnsweringAdapters(IReadOnlyList<GpuAdapterItem> adapters)
    {
        using var engine = new GpuTelemetryEngine();
        return adapters
            .Where(adapter => TelemetryRecovery.HasReading(engine.GetTelemetry(adapter.Luid, adapter.Name)))
            .Select(adapter => adapter.Luid)
            .ToHashSet();
    }
}
