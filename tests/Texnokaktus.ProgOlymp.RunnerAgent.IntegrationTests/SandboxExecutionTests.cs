using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Texnokaktus.ProgOlymp.RunnerAgent.Execution;
using Texnokaktus.ProgOlymp.RunnerAgent.Workspace;

namespace Texnokaktus.ProgOlymp.RunnerAgent.IntegrationTests;

[Category("Sandbox")]
public sealed class SandboxExecutionTests : SetupBase
{
    private static readonly int[] TestIds = [8, 2, 5];
    private static readonly string[] ExpectedStdout = ["16", "4", "10"];
    private static readonly string[] ExpectedStderr = ["err8", "err2", "err5"];
    private static readonly string[] AllowedEnvironment = ["PATH", "LANG", "ONLINE_JUDGE", "LIBC_FATAL_STDERR_"];

    private static readonly ResourceLimits CompilationLimits = new(
        10000,
        20000,
        512 * 1024 * 1024,
        1024 * 1024,
        1024 * 1024,
        32
    );

    private static readonly ResourceLimits ExecutionLimits = new(1000, 2000, 64 * 1024 * 1024, 4096, 4096, 8);

    [OneTimeSetUp]
    public void RequireSandboxEnvironment()
    {
        Assert.That(OperatingSystem.IsLinux(), Is.True, "Run Sandbox tests using tests/prototype/run.ps1.");

        Assert.That(
            File.Exists("/sys/fs/cgroup/runner-agent-boxes/cgroup.controllers"),
            Is.True,
            "Initialize the private cgroup environment before running Sandbox tests."
        );
    }

    [Test]
    public async Task FailedCompilerStartHasNoFabricatedExecutionResult()
    {
        const string compiler = "/usr/bin/g++";
        const string saved = "/usr/bin/g++.runner-agent-test";
        File.Move(compiler, saved);

        try
        {
            using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(9);

            var result = await Runner.RunAsync(
                             CreateSubmission(workspace, "int main(){}", ExecutionLimits),
                             CancellationToken.None
                         );

            Assert.Multiple(() =>
                {
                    Assert.That(result.Status, Is.EqualTo(SubmissionStatus.InfrastructureError));
                    Assert.That(result.Compilation, Is.Null);
                    Assert.That(result.Tests, Is.Empty);
                    Assert.That(result.EnvironmentReusable, Is.True);
                    Assert.That(Directory.Exists("/var/lib/isolate/0"), Is.False);
                }
            );
        }
        finally
        {
            File.Move(saved, compiler);
        }
    }

    [Test]
    public async Task CompilesOnceAndPreservesOrderAndIndependentFiles()
    {
        const string source = """
                              #include <fstream>
                              #include <iostream>
                              int main() {
                                  if (std::ifstream("/box/marker").good()) return 90;
                                  std::ofstream("/box/marker") << "created";
                                  int n; std::cin >> n;
                                  std::cout << n * 2;
                                  std::cerr << "err" << n;
                                  return n == 2 ? 3 : 0;
                              }
                              """;

        using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(1);
        var submission = CreateSubmission(workspace, source, ExecutionLimits);
        var result = await Runner.RunAsync(submission, CancellationToken.None);

        Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SubmissionStatus.Completed));
                Assert.That(result.EnvironmentReusable, Is.True);
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(result.Compilation?.Reason, Is.EqualTo(TerminationReason.Success));
                Assert.That(result.Tests.Select(test => test.TestId), Is.EqualTo(TestIds));

                Assert.That(
                    result.Tests.Select(test => test.Execution?.Reason),
                    Is.EqualTo(
                        new[]
                        {
                            TerminationReason.Success,
                            TerminationReason.RuntimeError,
                            TerminationReason.Success
                        }
                    )
                );

                Assert.That(
                    result.Tests.Select(test => File.ReadAllText(test.Execution!.StdoutPath!)),
                    Is.EqualTo(ExpectedStdout)
                );

                Assert.That(
                    result.Tests.Select(test => File.ReadAllText(test.Execution!.StderrPath!)),
                    Is.EqualTo(ExpectedStderr)
                );

                Assert.That(result.Tests.All(test => test.Execution!.PeakMemoryBytes > 0), Is.True);
                Assert.That(result.Tests.All(test => File.Exists(test.Execution!.MetadataPath)), Is.True);
                Assert.That(Directory.Exists("/var/lib/isolate/0"), Is.False);
                Assert.That(Directory.Exists("/sys/fs/cgroup/runner-agent-boxes/box-0"), Is.False);
            }
        );
    }

    [Test]
    public async Task CompilationErrorDoesNotRunTests()
    {
        using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(2);

        var result = await Runner.RunAsync(
                         CreateSubmission(workspace, "this is not C++", ExecutionLimits),
                         CancellationToken.None
                     );

        Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SubmissionStatus.CompilationError));
                Assert.That(result.Compilation?.Reason, Is.EqualTo(TerminationReason.RuntimeError));
                Assert.That(result.Tests, Is.Empty);
                Assert.That(new FileInfo(result.Compilation!.StderrPath!).Length, Is.GreaterThan(0));
                Assert.That(result.Compilation.StdoutPath, Is.Not.EqualTo(result.Compilation.StderrPath));
                Assert.That(Directory.Exists("/var/lib/isolate/0"), Is.False);
            }
        );
    }

    [TestCase(
        "#include <unistd.h>\nint main(){sleep(10);}",
        TerminationReason.WallTimeLimitExceeded,
        1000,
        100,
        67108864,
        8
    )]
    [TestCase("int main(){for(;;){}}", TerminationReason.CpuTimeLimitExceeded, 50, 2000, 67108864, 8)]
    [TestCase(
        "#include <unistd.h>\nint main(){if(!fork())for(;;){}for(;;)pause();}",
        TerminationReason.CpuTimeLimitExceeded,
        50,
        2000,
        67108864,
        8
    )]
    [TestCase(
        "#include <cstdlib>\n#include <cstring>\nint main(){for(;;){void* p=malloc(1048576); if(p) memset(p,1,1048576);}}",
        TerminationReason.MemoryLimitExceeded,
        1000,
        2000,
        16777216,
        8
    )]
    [TestCase(
        "#include <unistd.h>\nint main(){for(;;){int p=fork();if(p<0)return 0;if(!p){pause();return 0;}}}",
        TerminationReason.ProcessLimitExceeded,
        1000,
        2000,
        67108864,
        2
    )]
    [TestCase(
        "#include <cstdlib>\n#include <unistd.h>\nint main(){if(fork())for(;;)pause();for(;;){volatile char* p=(char*)malloc(1048576);if(p)for(int i=0;i<1048576;i++)p[i]=1;}}",
        TerminationReason.MemoryLimitExceeded,
        1000,
        2000,
        16777216,
        8
    )]
    [TestCase(
        "#include <pthread.h>\n#include <unistd.h>\nvoid* wait(void*){sleep(30);return nullptr;}int main(){pthread_t t;while(!pthread_create(&t,nullptr,wait,nullptr)){}return 0;}",
        TerminationReason.ProcessLimitExceeded,
        1000,
        2000,
        67108864,
        2
    )]
    public async Task AppliesIndependentLimitsAndContinuesTests(
        string source,
        TerminationReason reason,
        long cpu,
        long wall,
        long memory,
        int processes
    )
    {
        using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(3);

        var limits = ExecutionLimits with
        {
            CpuTimeMs = cpu,
            WallTimeMs = wall,
            MemoryBytes = memory,
            Processes = processes
        };

        var result = await Runner.RunAsync(CreateSubmission(workspace, source, limits), CancellationToken.None);

        Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SubmissionStatus.Completed));
                Assert.That(result.EnvironmentReusable, Is.True);
                Assert.That(result.Tests, Has.Count.EqualTo(3));
                Assert.That(result.Tests.Select(test => test.Execution?.Reason), Is.All.EqualTo(reason));
                Assert.That(result.Tests.All(test => !test.InfrastructureError), Is.True);

                Assert.That(
                    result.Tests.All(test => test.Execution!.AppliedMemoryLimitBytes == limits.AppliedMemoryBytes),
                    Is.True
                );
            }
        );
    }

    [Test]
    public async Task BoundsBothStreamsAndPreservesTheirPrefixes()
    {
        const string source =
            "#include <unistd.h>\nint main(){write(2,\"err\",3);write(1,\"out\",3);for(;;)write(1,\"0123456789\",10);}";

        using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(4);

        var limits = ExecutionLimits with
        {
            StdoutBytes = 16,
            StderrBytes = 8
        };

        var result = await Runner.RunAsync(CreateSubmission(workspace, source, limits), CancellationToken.None);

        Assert.Multiple(() =>
            {
                Assert.That(result.Status, Is.EqualTo(SubmissionStatus.Completed));

                Assert.That(
                    result.Tests.Select(test => test.Execution?.Reason),
                    Is.All.EqualTo(TerminationReason.OutputLimitExceeded)
                );

                Assert.That(result.Tests.All(test => new FileInfo(test.Execution!.StdoutPath!).Length == 16), Is.True);
                Assert.That(result.Tests.All(test => File.ReadAllText(test.Execution!.StderrPath!) == "err"), Is.True);
            }
        );
    }

    [Test]
    public async Task DoesNotInheritSecretsOrExposeAgentFiles()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var reachable = new TcpClient();
        await reachable.ConnectAsync(IPAddress.Loopback, port);

        var source = $$"""
                       #include <cstdlib>
                       #include <cstring>
                       #include <fstream>
                       #include <unistd.h>
                       #include <sys/socket.h>
                       #include <arpa/inet.h>
                       #if !defined(ONLINE_JUDGE) || ONLINE_JUDGE != 1
                       #error ONLINE_JUDGE must be defined as 1
                       #endif
                       int main() {
                           const char* judge = getenv("ONLINE_JUDGE");
                           if (!judge || std::strcmp(judge, "1") != 0) return 16;
                           if (!geteuid()) return 10;
                           if (getenv("AWS_SECRET_ACCESS_KEY") || getenv("FOREIGN_SECRET")) return 11;
                           if (std::ifstream("/proc/1/environ").good()) return 12;
                           if (std::ifstream("/proc/1/root/app/appsettings.json").good()) return 13;
                           if (std::ifstream("/repo/ARCHITECTURE.md").good()) return 14;
                           int socket_fd = socket(AF_INET, SOCK_STREAM, 0);
                           sockaddr_in address{};
                           address.sin_family = AF_INET;
                           address.sin_port = htons({{port}});
                           inet_pton(AF_INET, "127.0.0.1", &address.sin_addr);
                           if (connect(socket_fd, (sockaddr*)&address, sizeof(address)) == 0) return 15;
                           return 0;
                       }
                       """;

        var previousAws = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var previousForeign = Environment.GetEnvironmentVariable("FOREIGN_SECRET");

        try
        {
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test-only-aws-secret");
            Environment.SetEnvironmentVariable("FOREIGN_SECRET", "test-only-foreign-secret");
            using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(5);

            var result = await Runner.RunAsync(
                             CreateSubmission(workspace, source, ExecutionLimits),
                             CancellationToken.None
                         );

            Assert.Multiple(() =>
                {
                    Assert.That(result.Status, Is.EqualTo(SubmissionStatus.Completed));

                    Assert.That(
                        result.Tests.Select(test => test.Execution?.Reason),
                        Is.All.EqualTo(TerminationReason.Success)
                    );

                    Assert.That(File.ReadAllText(result.Compilation!.StderrPath!), Does.Not.Contain("test-only"));
                }
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", previousAws);
            Environment.SetEnvironmentVariable("FOREIGN_SECRET", previousForeign);
        }
    }

    [Test]
    public async Task CancellationStopsCurrentStageAndCleansBox()
    {
        using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(6);
        var source = "#include <unistd.h>\nint main(){sleep(30);}";
        var limits = ExecutionLimits with { WallTimeMs = 40000 };
        using var cancellation = new CancellationTokenSource();
        var task = Runner.RunAsync(CreateSubmission(workspace, source, limits), cancellation.Token);

        try
        {
            var timeout = Stopwatch.StartNew();

            while (!File.Exists(Path.Combine(workspace.Path, "solution"))
                || !File.Exists("/sys/fs/cgroup/runner-agent-boxes/box-0/cgroup.events")
                || !File.ReadAllText("/sys/fs/cgroup/runner-agent-boxes/box-0/cgroup.events")
                        .Contains("populated 1", StringComparison.Ordinal))
            {
                if (task.IsCompleted || timeout.Elapsed > TimeSpan.FromSeconds(20))
                    Assert.Fail("Execution did not start before cancellation.");

                await Task.Delay(10);
            }

            await cancellation.CancelAsync();
            Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            Assert.That(Directory.Exists("/var/lib/isolate/0"), Is.False);
            Assert.That(Directory.Exists("/sys/fs/cgroup/runner-agent-boxes/box-0"), Is.False);
            var proxies = Process.GetProcessesByName("isolate");

            try
            {
                Assert.That(proxies, Is.Empty);
            }
            finally
            {
                foreach (var proxy in proxies) proxy.Dispose();
            }
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private static ISubmissionRunner Runner => Factory.Services.GetRequiredService<ISubmissionRunner>();

    [Test]
    public async Task CompilerAndSolutionHaveSeparateNamespacesAndCleanEnvironment()
    {
        using var workspace = Factory.Services.GetRequiredService<WorkspaceManager>().Create(7);
        using var cancellation = new CancellationTokenSource();
        const string source = "#include <iostream>\n#include <unistd.h>\nint main(){std::cout << 1;sleep(30);}";
        var limits = ExecutionLimits with { WallTimeMs = 40000 };
        var previous = Environment.GetEnvironmentVariable("FOREIGN_SECRET");
        Environment.SetEnvironmentVariable("FOREIGN_SECRET", "test-only-foreign-secret");
        var task = Runner.RunAsync(CreateSubmission(workspace, source, limits), cancellation.Token);

        try
        {
            var compilerNamespace = await ObserveProcessAsync("/usr/bin/g++", task);
            var solutionNamespace = await ObserveProcessAsync("/box/solution", task);
            Assert.That(compilerNamespace, Is.Not.EqualTo(solutionNamespace));
        }
        finally
        {
            await cancellation.CancelAsync();

            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
            }

            Environment.SetEnvironmentVariable("FOREIGN_SECRET", previous);
        }
    }

    private static async Task<string> ObserveProcessAsync(string executable, Task job)
    {
        var timeout = Stopwatch.StartNew();

        while (!job.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(20))
        {
            try
            {
                foreach (var pid in File.ReadAllLines("/sys/fs/cgroup/runner-agent-boxes/box-0/cgroup.procs"))
                {
                    var processRoot = $"/proc/{pid}";

                    if (File.ReadAllText(Path.Combine(processRoot, "cmdline")).Split('\0')[0] != executable)
                        continue;

                    var uid = File.ReadLines(Path.Combine(processRoot, "status"))
                                  .First(line => line.StartsWith("Uid:", StringComparison.Ordinal))
                                  .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                    Assert.That(uid[2], Is.EqualTo("60000"));

                    var environment = File.ReadAllText(Path.Combine(processRoot, "environ"))
                                          .Split('\0', StringSplitOptions.RemoveEmptyEntries);

                    var environmentKeys = environment.Select(entry => entry.Split('=', 2)[0]);

                    Assert.That(environmentKeys, Is.SubsetOf(AllowedEnvironment));
                    Assert.That(environment, Does.Contain("ONLINE_JUDGE=1"));
                    var networkNamespace = new FileInfo(Path.Combine(processRoot, "ns/net")).LinkTarget;

                    Assert.That(
                        networkNamespace,
                        Is.Not.Null.And.Not.EqualTo(new FileInfo("/proc/self/ns/net").LinkTarget)
                    );

                    return networkNamespace!;
                }
            }
            catch (IOException)
            {
            }

            await Task.Delay(1);
        }

        throw new AssertionException($"The sandbox process {executable} was not observed.");
    }

    private static LocalSubmission CreateSubmission(JobWorkspace workspace, string source, ResourceLimits execution)
    {
        var sourcePath = Path.Combine(workspace.Path, "source.cpp");
        File.WriteAllText(sourcePath, source);
        var tests = new List<LocalTest>();

        foreach (var id in TestIds)
        {
            var input = Path.Combine(workspace.Path, $"input-{id}.txt");
            File.WriteAllText(input, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            tests.Add(new(id, input));
        }

        return new(1, workspace.Path, sourcePath, tests, CompilationLimits, execution);
    }
}
