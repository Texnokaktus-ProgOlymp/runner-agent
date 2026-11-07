using System.Runtime.InteropServices;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Jobs;

internal sealed class RunnerLifetime(
    RunnerState state,
    IHostApplicationLifetime lifetime,
    ILogger<RunnerLifetime> logger
) : IHostLifetime, IDisposable
{
    private PosixSignalRegistration? _term;
    private PosixSignalRegistration? _interrupt;
    private CancellationTokenRegistration _started;

    public Task WaitForStartAsync(CancellationToken cancellationToken)
    {
        _started = lifetime.ApplicationStarted.Register(() => RunnerLog.HostStarted(logger));

        if (OperatingSystem.IsLinux())
        {
            _term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Signal);
            _interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Signal);
        }

        Console.CancelKeyPress += Cancel;

        return Task.CompletedTask;
    }

    private void Signal(PosixSignalContext context)
    {
        context.Cancel = true;
        state.BeginStop();
    }

    private void Cancel(object? sender, ConsoleCancelEventArgs args)
    {
        args.Cancel = true;
        state.BeginStop();
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose()
    {
        _term?.Dispose();
        _interrupt?.Dispose();
        _started.Dispose();
        Console.CancelKeyPress -= Cancel;
    }
}
