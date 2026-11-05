using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Texnokaktus.ProgOlymp.RunnerAgent.Languages;

namespace Texnokaktus.ProgOlymp.RunnerAgent.Execution;

internal sealed class IsolateSandbox
{
    private bool _reusable = true;
    private const string Box = "/var/lib/isolate/0/box";
    private const string Cgroup = "/sys/fs/cgroup/runner-agent-boxes/box-0";

    public async Task<SandboxOutcome> RunAsync(
        string stage,
        SandboxCommand command,
        string file,
        string name,
        string? input,
        string artifacts,
        ResourceLimits limits,
        string? executable,
        CancellationToken cancellationToken
    )
    {
        if (!Volatile.Read(ref _reusable))
            throw new SandboxCleanupException();

        var diagnostics = new List<InfrastructureDiagnostic>();
        var violations = new List<TerminationReason>();
        var stdout = Path.Combine(artifacts, "stdout.txt");
        var stderr = Path.Combine(artifacts, "stderr.txt");
        var metadata = Path.Combine(artifacts, "isolate.meta");
        var started = false;
        var executionStarted = false;
        var reusable = true;
        var outputExceeded = 0;
        long? cpu = null, wall = null, peak = null, applied = null;
        int? exitCode = null, signal = null;
        TerminationReason? reason = null;
        Process? process = null;
        Task? stdoutCopy = null, stderrCopy = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(artifacts);

            if (await ControlAsync("--init", cancellationToken) != 0)
                throw new InvalidOperationException("isolate initialization failed.");

            await CopyAsync(file, Path.Combine(Box, name), cancellationToken);

            if (input is null)
                await File.WriteAllBytesAsync(Path.Combine(Box, "input.txt"), [], cancellationToken);
            else
                await CopyAsync(input, Path.Combine(Box, "input.txt"), cancellationToken);

            var executablePath = command.Executable.StartsWith("/box/", StringComparison.Ordinal)
                                     ? Path.Combine(Box, command.Executable[5..])
                                     : command.Executable;

            if (!File.Exists(executablePath))
                throw new IOException("The sandbox executable is missing.");

            await File.WriteAllTextAsync(Path.Combine(Cgroup, "memory.oom.group"), "1", cancellationToken);

            await File.WriteAllTextAsync(
                Path.Combine(Cgroup, "pids.max"),
                limits.Processes.ToString(CultureInfo.InvariantCulture),
                cancellationToken
            );

            applied = limits.AppliedMemoryBytes;
            process = new Process { StartInfo = StartInfo() };

            var arguments = new[]
            {
                "--cg",
                "--box-id=0",
                "--silent",
                "--processes",
                $"--cg-mem={applied / 1024}",
                $"--time={Seconds(limits.CpuTimeMs)}",
                $"--wall-time={Seconds(limits.WallTimeMs)}",
                $"--meta={metadata}",
                "--stdin=/box/input.txt",
                "--env=PATH=/usr/bin:/bin",
                "--env=LANG=C",
                "--env=ONLINE_JUDGE=1",
                "--no-default-dirs",
                $"--dir=box={Box}:rw",
                "--dir=usr",
                "--dir=bin",
                "--dir=lib",
                "--dir=lib64:maybe",
                "--dir=proc=proc:fs",
                "--dir=tmp:tmp",
                "--dir=dev=/var/lib/runner-agent-dev:dev:norec",
                "--run",
                "--",
                command.Executable
            };

            foreach (var argument in arguments.Concat(command.Arguments))
                process.StartInfo.ArgumentList.Add(argument);

            process.Start();
            started = true;
            process.StandardInput.Close();

            void OnOutputExceeded()
            {
                if (Interlocked.Exchange(ref outputExceeded, 1) == 0)
                    KillGroup();
            }

            stdoutCopy = CaptureAsync(process.StandardOutput.BaseStream, stdout, limits.StdoutBytes, OnOutputExceeded);
            stderrCopy = CaptureAsync(process.StandardError.BaseStream, stderr, limits.StderrBytes, OnOutputExceeded);
            var exited = process.WaitForExitAsync(CancellationToken.None);

            while (!exited.IsCompleted)
            {
                executionStarted |= HasExecutionStarted(command.Executable);

                if (cancellationToken.IsCancellationRequested)
                {
                    KillGroup();

                    if (!process.HasExited)
                        Interrupt(process);

                    break;
                }

                if (ReadPairs(Path.Combine(Cgroup, "pids.events")).GetValueOrDefault("max") > 0)
                    KillGroup();

                await Task.WhenAny(exited, Task.Delay(10, CancellationToken.None));
            }

            await exited;

            if (process.ExitCode == 0)
            {
                executionStarted = true;
                exitCode = 0;
                reason = TerminationReason.Success;
            }

            await Task.WhenAll(stdoutCopy, stderrCopy);
            cancellationToken.ThrowIfCancellationRequested();

            cpu = ReadPairs(Path.Combine(Cgroup, "cpu.stat"))["usage_usec"] / 1000;

            peak = long.Parse(
                await File.ReadAllTextAsync(Path.Combine(Cgroup, "memory.peak"), cancellationToken),
                CultureInfo.InvariantCulture
            );

            applied = long.Parse(
                await File.ReadAllTextAsync(Path.Combine(Cgroup, "memory.max"), cancellationToken),
                CultureInfo.InvariantCulture
            );

            if (applied != limits.AppliedMemoryBytes)
                diagnostics.Add(new(stage, "memory_limit_not_applied"));

            var values = (await File.ReadAllLinesAsync(metadata, cancellationToken))
                        .Select(line => line.Split(':', 2))
                        .Where(parts => parts.Length == 2)
                        .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);

            if (values.TryGetValue("time-wall", out var wallValue))
                wall = checked((long)(decimal.Parse(wallValue, CultureInfo.InvariantCulture) * 1000));

            if (values.TryGetValue("exitcode", out var exitValue))
                exitCode = int.Parse(exitValue, CultureInfo.InvariantCulture);

            if (values.TryGetValue("exitsig", out var signalValue))
                signal = int.Parse(signalValue, CultureInfo.InvariantCulture);

            executionStarted |=
                exitCode is not null || signal is not null || values.GetValueOrDefault("status") == "TO";

            if (ReadPairs(Path.Combine(Cgroup, "memory.events")).GetValueOrDefault("oom_kill") > 0)
                violations.Add(TerminationReason.MemoryLimitExceeded);

            if (outputExceeded != 0)
                violations.Add(TerminationReason.OutputLimitExceeded);

            if (cpu > limits.CpuTimeMs)
                violations.Add(TerminationReason.CpuTimeLimitExceeded);

            if (wall > limits.WallTimeMs)
                violations.Add(TerminationReason.WallTimeLimitExceeded);

            if (ReadPairs(Path.Combine(Cgroup, "pids.events")).GetValueOrDefault("max") > 0)
                violations.Add(TerminationReason.ProcessLimitExceeded);

            if (signal is not null || exitCode is not (null or 0))
                violations.Add(TerminationReason.RuntimeError);

            reason = violations.Count != 0 ? violations[0] : exitCode == 0 ? TerminationReason.Success : null;

            if (process.ExitCode is not (0 or 1)
             || values.GetValueOrDefault("status") == "XX"
             || wall is null
             || reason is null)
                diagnostics.Add(new(stage, "sandbox_result_incomplete"));

            if (reason == TerminationReason.Success && executable is not null && diagnostics.Count == 0)
            {
                var binary = Path.Combine(Box, "solution");

                if ((File.GetAttributes(binary) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("The compiler output must be a regular file.");

                await CopyAsync(binary, executable, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cleanup below uses an independent lifetime.
        }
        catch (Exception exception) when (exception is IOException
                                                    or UnauthorizedAccessException
                                                    or InvalidOperationException
                                                    or Win32Exception
                                                    or FormatException
                                                    or OverflowException
                                                    or ArgumentException
                                                    or KeyNotFoundException)
        {
            diagnostics.Add(new(stage, started ? "sandbox_execution_failed" : "sandbox_prepare_failed"));
        }
        finally
        {
            try
            {
                KillGroup();

                if (process is not null && started && !process.HasExited)
                {
                    Interrupt(process);
                    await process.WaitForExitAsync(CancellationToken.None);
                }
            }
            catch (Exception exception) when (exception is IOException
                                                        or UnauthorizedAccessException
                                                        or InvalidOperationException
                                                        or Win32Exception)
            {
                reusable = false;
                diagnostics.Add(new(stage, "sandbox_cleanup_failed"));
            }

            try
            {
                if (stdoutCopy is not null && stderrCopy is not null)
                    await Task.WhenAll(stdoutCopy, stderrCopy);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(new(stage, "output_capture_failed"));
            }

            try
            {
                if (await ControlAsync("--cleanup", CancellationToken.None) != 0 || Directory.Exists(Cgroup))
                {
                    reusable = false;
                    diagnostics.Add(new(stage, "sandbox_cleanup_failed"));
                }
            }
            catch (Exception exception) when (exception is IOException
                                                        or UnauthorizedAccessException
                                                        or InvalidOperationException
                                                        or Win32Exception)
            {
                reusable = false;
                diagnostics.Add(new(stage, "sandbox_cleanup_failed"));
            }

            process?.Dispose();
        }

        if (!reusable)
            Volatile.Write(ref _reusable, false);

        if (cancellationToken.IsCancellationRequested)
        {
            if (!reusable)
                throw new SandboxCleanupException();

            cancellationToken.ThrowIfCancellationRequested();
        }

        var result = executionStarted
                         ? new LocalStageResult(
                             reason,
                             cpu,
                             wall,
                             peak,
                             applied,
                             exitCode,
                             signal,
                             File.Exists(stdout) ? stdout : null,
                             File.Exists(stderr) ? stderr : null,
                             File.Exists(metadata) ? metadata : null,
                             diagnostics,
                             violations
                         )
                         : null;

        return new(result, diagnostics, reusable);
    }

    private static string Seconds(long milliseconds) =>
        ((milliseconds + 0.25m) / 1000).ToString(CultureInfo.InvariantCulture);

    private static bool HasExecutionStarted(string executable)
    {
        foreach (var pid in File.ReadLines(Path.Combine(Cgroup, "cgroup.procs")))
        {
            try
            {
                if (File.ReadAllText($"/proc/{pid}/cmdline").Split('\0')[0] == executable)
                    return true;
            }
            catch (IOException)
            {
            }
        }

        return false;
    }

    private static void Interrupt(Process process)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("isolate requires Linux.");

        // Let isolate reap its proxy. Killing the keeper directly can orphan a zombie.
        if (PosixSignals.Send(process.Id, 15) != 0 && !process.HasExited)
            throw new InvalidOperationException("Cannot interrupt isolate.");
    }

    private static ProcessStartInfo StartInfo()
    {
        var info = new ProcessStartInfo("/usr/bin/isolate")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        info.Environment.Clear();
        info.Environment["PATH"] = "/usr/bin:/bin";
        info.Environment["LANG"] = "C";

        return info;
    }

    private static async Task<int> ControlAsync(string action, CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = StartInfo() };
        process.StartInfo.ArgumentList.Add("--cg");
        process.StartInfo.ArgumentList.Add("--box-id=0");
        process.StartInfo.ArgumentList.Add(action);
        process.Start();
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }

            await Task.WhenAll(stdout, stderr);
        }

        return process.ExitCode;
    }

    private static void KillGroup()
    {
        if (Directory.Exists(Cgroup))
            File.WriteAllText(Path.Combine(Cgroup, "cgroup.kill"), "1");
    }

    private static Dictionary<string, long> ReadPairs(string path) => File.ReadLines(path)
                                                                          .Select(line => line.Split(
                                                                                      ' ',
                                                                                      StringSplitOptions
                                                                                         .RemoveEmptyEntries
                                                                                  )
                                                                           )
                                                                          .ToDictionary(
                                                                               parts => parts[0],
                                                                               parts => long.Parse(
                                                                                   parts[1],
                                                                                   CultureInfo.InvariantCulture
                                                                               ),
                                                                               StringComparer.Ordinal
                                                                           );

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);

        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(destination, File.GetUnixFileMode(source));
    }

    private static async Task CaptureAsync(Stream input, string path, long limit, Action exceeded)
    {
        await using var output = File.Create(path);
        var buffer = new byte[16384];
        long written = 0;
        int read;

        while ((read = await input.ReadAsync(buffer, CancellationToken.None)) != 0)
        {
            var allowed = (int)Math.Min(read, limit - written);
            await output.WriteAsync(buffer.AsMemory(0, allowed), CancellationToken.None);
            written += allowed;

            if (allowed != read)
                exceeded();
        }
    }
}
