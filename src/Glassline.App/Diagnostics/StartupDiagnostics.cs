using System.Diagnostics;

namespace Glassline.App.Diagnostics;

internal sealed class StartupDiagnostics
{
    private static readonly TimeSpan IdleSampleDuration = TimeSpan.FromSeconds(5);

    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly Process _process = Process.GetCurrentProcess();

    internal void Record(string milestone)
    {
        _process.Refresh();
        Debug.WriteLine(
            $"[Glassline startup] milestone={milestone}; " +
            $"elapsed_ms={_elapsed.Elapsed.TotalMilliseconds:F0}; " +
            $"working_set_mb={ToMegabytes(_process.WorkingSet64):F1}; " +
            $"private_memory_mb={ToMegabytes(_process.PrivateMemorySize64):F1}; " +
            $"cpu_ms={_process.TotalProcessorTime.TotalMilliseconds:F0}");
    }

    internal async Task RecordIdleSampleAsync(CancellationToken cancellationToken = default)
    {
        _process.Refresh();
        var cpuBefore = _process.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();

        await Task.Delay(IdleSampleDuration, cancellationToken);

        elapsed.Stop();
        _process.Refresh();
        var cpuUsed = _process.TotalProcessorTime - cpuBefore;
        var normalizedCpuPercent = cpuUsed.TotalMilliseconds
            / elapsed.Elapsed.TotalMilliseconds
            / Environment.ProcessorCount
            * 100;

        Debug.WriteLine(
            $"[Glassline idle] sample_ms={elapsed.Elapsed.TotalMilliseconds:F0}; " +
            $"cpu_percent={normalizedCpuPercent:F2}; " +
            $"working_set_mb={ToMegabytes(_process.WorkingSet64):F1}; " +
            $"private_memory_mb={ToMegabytes(_process.PrivateMemorySize64):F1}");
    }

    private static double ToMegabytes(long bytes) => bytes / 1024d / 1024d;
}
