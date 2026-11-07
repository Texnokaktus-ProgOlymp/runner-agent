using Texnokaktus.ProgOlymp.RunnerAgent.Diagnostics;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Jobs;

internal enum Admission
{
    Accepted,
    Busy,
    Stopping
}

internal sealed class RunnerState(IHostApplicationLifetime lifetime)
{
    private readonly object _gate = new();
    private bool _busy;
    private bool _stopping;
    private TaskCompletionSource _idle = Completed();
    public int ExitCode { get; private set; }

    public Admission TryEnter()
    {
        lock (_gate)
        {
            if (_stopping) return Admission.Stopping;
            if (_busy) return Admission.Busy;

            _busy = true;
            RunnerTelemetry.ActiveCalls.Add(1);
            _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

            return Admission.Accepted;
        }
    }

    public void Leave()
    {
        lock (_gate)
        {
            _busy = false;
            RunnerTelemetry.ActiveCalls.Add(-1);
            _idle.TrySetResult();
        }
    }

    public void BeginStop(bool fatal = false)
    {
        Task idle;

        lock (_gate)
        {
            _stopping = true;
            if (fatal) ExitCode = 1;
            idle = _idle.Task;
        }

        _ = StopWhenIdleAsync(idle);
    }

    private async Task StopWhenIdleAsync(Task idle)
    {
        await idle;
        lifetime.StopApplication();
    }

    private static TaskCompletionSource Completed()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();

        return completion;
    }
}
