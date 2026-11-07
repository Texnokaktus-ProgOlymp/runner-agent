using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Diagnostics;

internal static class RunnerTelemetry
{
    public const string Name = "Texnokaktus.ProgOlymp.RunnerAgent";
    public static readonly ActivitySource Activities = new(Name);
    private static readonly Meter Meter = new(Name);
    public static readonly Counter<long> Jobs = Meter.CreateCounter<long>("runner.jobs", "{job}");
    public static readonly Counter<long> Rejections = Meter.CreateCounter<long>("runner.rejections", "{call}");

    public static readonly UpDownCounter<long> ActiveCalls =
        Meter.CreateUpDownCounter<long>("runner.active_calls", "{call}");

    public static readonly Histogram<double>
        StageDuration = Meter.CreateHistogram<double>("runner.stage.duration", "s");

    public static readonly Counter<long> StorageErrors = Meter.CreateCounter<long>("runner.storage.errors", "{error}");

    public static readonly Counter<long>
        StorageRetries = Meter.CreateCounter<long>("runner.storage.retries", "{retry}");
}
