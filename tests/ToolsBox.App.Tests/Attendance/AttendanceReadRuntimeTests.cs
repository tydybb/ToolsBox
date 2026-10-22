using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ToolsBox.App.Attendance;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Tests.Attendance;

public sealed class AttendanceReadRuntimeTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 8, 0, 0);

    [Fact]
    public async Task FreshProcessesRecoverFromInitializationFailureAndReadAgainNextDay()
    {
        using var fixture = new ReaderFixture("bootstrap", "success", "success");
        using var client = CreateClient(fixture.Start);

        AttendanceStatus failed = await Check(client, null, Now);
        AttendanceStatus recovered = await Check(client, null, Now.AddMinutes(5));
        AttendanceStatus nextDay = await Check(client, null, Now.AddDays(1));

        Assert.False(failed.Obtained);
        Assert.True(recovered.ClockedIn);
        Assert.True(nextDay.ClockedIn);
        Assert.Equal("07:50", nextDay.ClockInTime);
        Assert.Null(recovered.LatestRecord);
        Assert.Equal(3, fixture.Started.Count);
        Assert.Equal(3, fixture.Started.Select(p => p.Id).Distinct().Count());
        Assert.All(fixture.Started, p => Assert.True(p.HasExited));
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("invalidjson")]
    [InlineData("oversized")]
    [InlineData("mismatch")]
    [InlineData("wrongnow")]
    [InlineData("future")]
    [InlineData("count")]
    [InlineData("inconsistent")]
    [InlineData("unknownfield")]
    [InlineData("unknownerror")]
    [InlineData("trailing")]
    [InlineData("nonzero")]
    public async Task InvalidWorkerOutputFailsSafelyAndNextFreshReadSucceeds(string mode)
    {
        using var fixture = new ReaderFixture(mode, "success");
        using var client = CreateClient(fixture.Start);

        AttendanceStatus failed = await Check(client, null, Now);
        AttendanceStatus recovered = await Check(client, null, Now);

        Assert.False(failed.Obtained);
        Assert.False(failed.ClockedIn);
        Assert.Null(failed.ClockInTime);
        Assert.Null(failed.LatestRecord);
        Assert.Equal(0, failed.TodayMessages);
        Assert.False(string.IsNullOrWhiteSpace(failed.Error));
        Assert.DoesNotContain("PRIVATE", failed.Error!);
        Assert.True(recovered.ClockedIn);
    }

    [Fact]
    public async Task LaunchUsesNoWindowAndPipesAndOnlyWorkerFlagOnCommandLine()
    {
        ProcessStartInfo? actual = null;
        using var fixture = new ReaderFixture("success");
        using var client = CreateClient(info => { actual = info; return fixture.Start(info); });
        string directory = Path.Combine(Path.GetTempPath(), "PRIVATE-account-directory");

        Assert.True((await Check(client, directory, Now)).ClockedIn);
        Assert.NotNull(actual);
        Assert.False(actual!.UseShellExecute);
        Assert.True(actual.CreateNoWindow);
        Assert.True(actual.RedirectStandardInput);
        Assert.True(actual.RedirectStandardOutput);
        Assert.True(actual.RedirectStandardError);
        Assert.Equal(new[] { "--attendance-read-worker" }, actual.ArgumentList.ToArray());
        Assert.DoesNotContain(directory, actual.Arguments);
        Assert.DoesNotContain(directory, string.Join(" ", actual.ArgumentList));
        Assert.Equal("", actual.Verb);
    }

    [Fact]
    public async Task ManagedDebugLaunchAddsEntryAssemblyBeforeWorkerFlag()
    {
        ProcessStartInfo? actual = null;
        using var fixture = new ReaderFixture("success");
        string executable = Path.Combine(AppContext.BaseDirectory, "dotnet.exe");
        string entry = Path.Combine(AppContext.BaseDirectory, "test-entry.dll");
        using var client = CreateClient(info => { actual = info; return fixture.Start(info); }, executable: executable, entry: entry);

        Assert.True((await Check(client, null, Now)).ClockedIn);
        Assert.Equal(new[] { entry, "--attendance-read-worker" }, actual!.ArgumentList.ToArray());
    }

    [Fact]
    public async Task DefaultDotnetLaunchResolvesFullyQualifiedManagedCommandLineEntry()
    {
        ProcessStartInfo? actual = null;
        using var fixture = new ReaderFixture("success");
        string executable = Path.Combine(AppContext.BaseDirectory, "dotnet.exe");
        string entry = Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
        Assert.True(Path.IsPathFullyQualified(entry));
        Assert.Equal(".dll", Path.GetExtension(entry), ignoreCase: true);
        using var client = CreateClient(info => { actual = info; return fixture.Start(info); }, executable: executable);

        Assert.True((await Check(client, null, Now)).ClockedIn);
        Assert.Equal(new[] { entry, "--attendance-read-worker" }, actual!.ArgumentList.ToArray());
    }

    [Fact]
    public void ReaderEntryResolutionDoesNotDependOnAssemblyLocationForSingleFilePublish()
    {
        string source = ReadSource("src/ToolsBox.App/Attendance/AttendanceReadClient.cs");
        Assert.DoesNotContain("Assembly.GetEntryAssembly", source);
        Assert.Contains("Environment.GetCommandLineArgs()", source);
        Assert.Contains("Path.GetFullPath", source);
    }

    [Theory]
    [InlineData("entry.exe")]
    [InlineData("entry.txt")]
    public async Task DotnetLaunchRejectsNonManagedEntryBeforeStartingProcess(string filename)
    {
        using var fixture = new ReaderFixture("success");
        using var client = CreateClient(fixture.Start, executable: Path.Combine(AppContext.BaseDirectory, "dotnet.exe"),
            entry: Path.Combine(AppContext.BaseDirectory, filename));

        Assert.False((await Check(client, null, Now)).Obtained);
        Assert.Empty(fixture.Started);
    }

    [Theory]
    [InlineData("relative-account")]
    [InlineData("\\\\server\\share\\account")]
    [InlineData("\\\\?\\C:\\account")]
    [InlineData("C:\\account\u0000PRIVATE")]
    public async Task InvalidDirectoryIsRejectedWithoutStartingProcess(string directory)
    {
        int starts = 0;
        using var client = CreateClient(_ => { starts++; return null; });

        AttendanceStatus status = await Check(client, directory, Now);

        Assert.False(status.Obtained);
        Assert.Equal(0, starts);
        Assert.DoesNotContain("PRIVATE", status.Error!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DefaultBlankAccountUsesAutomaticDiscovery(string directory)
    {
        using var fixture = new ReaderFixture("autoblank");
        using var client = CreateClient(fixture.Start);

        Assert.True((await Check(client, directory, Now)).ClockedIn);
    }

    [Fact]
    public async Task PreviewAndMainCallsShareOneSerializedProcessSlot()
    {
        using var fixture = new ReaderFixture("slow", "success");
        bool overlapped = false;
        using var client = CreateClient(info =>
        {
            if (fixture.Started.Count > 0 && !fixture.Started[^1].HasExited) overlapped = true;
            return fixture.Start(info);
        });

        Task<AttendanceStatus> preview = Check(client, null, Now);
        await fixture.WaitForStarts(1);
        Task<AttendanceStatus> main = Check(client, null, Now);
        AttendanceStatus[] statuses = await Task.WhenAll(preview, main);

        Assert.All(statuses, status => Assert.True(status.ClockedIn));
        Assert.False(overlapped);
        Assert.Equal(2, fixture.Started.Count);
    }

    [Fact]
    public async Task TimeoutIncludesQueueTimeAndKillsOnlyOwnedProcess()
    {
        using var outside = ReaderFixture.StartIndependentSleeper();
        try
        {
            using var fixture = new ReaderFixture("hang", "hang");
            using var client = CreateClient(fixture.Start, TimeSpan.FromSeconds(2));
            Task<AttendanceStatus> first = Check(client, null, Now);
            await fixture.WaitForStarts(1);
            await Task.Delay(800);
            var watch = Stopwatch.StartNew();

            AttendanceStatus queued = await Check(client, null, Now);
            await first;

            Assert.False(queued.Obtained);
            Assert.InRange(watch.Elapsed.TotalSeconds, 1.4, 2.8);
            Assert.All(fixture.Started, p => Assert.True(p.HasExited));
            Assert.False(outside.HasExited);
        }
        finally { if (!outside.HasExited) outside.Kill(); }
    }

    [Fact]
    public async Task CancellationWhileQueuedDoesNotStartASecondProcess()
    {
        using var fixture = new ReaderFixture("hang");
        using var client = CreateClient(fixture.Start);
        using var firstCancellation = new CancellationTokenSource();
        Task<AttendanceStatus> first = Check(client, null, Now, firstCancellation.Token);
        await fixture.WaitForStarts(1);
        using var queuedCancellation = new CancellationTokenSource();
        Task<AttendanceStatus> queued = Check(client, null, Now, queuedCancellation.Token);

        queuedCancellation.Cancel();
        Assert.False((await queued.WaitAsync(TimeSpan.FromSeconds(3))).Obtained);
        Assert.Single(fixture.Started);
        Assert.False(fixture.Started[0].HasExited);
        firstCancellation.Cancel();
        Assert.False((await first.WaitAsync(TimeSpan.FromSeconds(3))).Obtained);
        Assert.True(fixture.Started[0].HasExited);
    }

    [Fact]
    public async Task CancellationOfStartedReadAllowsNextReadToRecover()
    {
        using var fixture = new ReaderFixture("hang", "success");
        using var client = CreateClient(fixture.Start);
        using var cancellation = new CancellationTokenSource();
        Task<AttendanceStatus> pending = Check(client, null, Now, cancellation.Token);
        await fixture.WaitForStarts(1);

        cancellation.Cancel();
        Assert.False((await pending.WaitAsync(TimeSpan.FromSeconds(3))).Obtained);
        Assert.True(fixture.Started[0].HasExited);
        Assert.True((await Check(client, null, Now)).ClockedIn);
    }

    [Fact]
    public async Task DisposeCancelsActiveAndQueuedReadsAndPreventsNewLaunch()
    {
        using var fixture = new ReaderFixture("hang");
        using var client = CreateClient(fixture.Start);
        Task<AttendanceStatus> active = Check(client, null, Now);
        await fixture.WaitForStarts(1);
        Task<AttendanceStatus> queued = Check(client, null, Now);

        client.Dispose();
        Assert.False((await active.WaitAsync(TimeSpan.FromSeconds(3))).Obtained);
        Assert.False((await queued.WaitAsync(TimeSpan.FromSeconds(3))).Obtained);
        Assert.False((await Check(client, null, Now)).Obtained);
        Assert.Single(fixture.Started);
        Assert.True(fixture.Started[0].HasExited);
    }

    [Fact]
    public async Task ValidFrameStillRequiresWorkerExitBeforeDeadline()
    {
        using var fixture = new ReaderFixture("validhang");
        using var client = CreateClient(fixture.Start, TimeSpan.FromSeconds(2));

        AttendanceStatus status = await Check(client, null, Now);

        Assert.False(status.Obtained);
        Assert.True(fixture.Started[0].HasExited);
    }

    [Fact]
    public async Task LargeStderrIsDrainedWithoutPublishingRawText()
    {
        using var fixture = new ReaderFixture("stderr");
        using var client = CreateClient(fixture.Start);

        AttendanceStatus status = await Check(client, null, Now);

        Assert.True(status.ClockedIn);
        Assert.Null(status.Error);
        Assert.True(fixture.Started[0].HasExited);
    }

    [Fact]
    public async Task DisposeBetweenLaunchAndRegistrationKillsReturnedOwnedProcess()
    {
        using var fixture = new ReaderFixture("hang");
        IDisposable? client = null;
        client = CreateClient(info =>
        {
            Process process = fixture.Start(info);
            client!.Dispose();
            return process;
        });
        using (client)
        {
            Assert.False((await Check(client, null, Now).WaitAsync(TimeSpan.FromSeconds(3))).Obtained);
            Assert.True(fixture.Started[0].HasExited);
        }
    }

    [Fact]
    public async Task RequestFrameIsLittleEndianBoundedAndRoundTripsNullDirectory()
    {
        object request = Protocol("CreateRequest", null, Now)!;
        using var stream = new MemoryStream();
        await ProtocolTask("WriteRequestAsync", stream, request, CancellationToken.None);
        byte[] frame = stream.ToArray();
        Assert.Equal(frame.Length - 4, BinaryPrimitives.ReadInt32LittleEndian(frame));
        using var json = JsonDocument.Parse(frame.AsMemory(4));
        Assert.Equal(1, json.RootElement.GetProperty("Version").GetInt32());
        Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("Id").GetGuid());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("Directory").ValueKind);
        Assert.Equal(Now, json.RootElement.GetProperty("Now").GetDateTime());
        stream.Position = 0;
        object read = (await ProtocolTask("ReadRequestAsync", stream, CancellationToken.None))!;
        Assert.Equal(request, read);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(32769)]
    public async Task RequestRejectsInvalidDeclaredFrameSizeBeforeBodyRead(int size)
    {
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, size);
        using var stream = new MemoryStream(prefix);

        await Assert.ThrowsAsync<InvalidDataException>(() => ProtocolTask("ReadRequestAsync", stream, CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"Version\":1,\"Id\":\"00000000-0000-0000-0000-000000000000\",\"Directory\":null,\"Now\":\"2026-10-13T08:00:00\"}")]
    [InlineData("{\"Version\":2,\"Id\":\"7058ae5f-1d46-46e5-9fb9-f97c24f665b0\",\"Directory\":null,\"Now\":\"2026-10-13T08:00:00\"}")]
    [InlineData("{\"Version\":1,\"Id\":\"7058ae5f-1d46-46e5-9fb9-f97c24f665b0\",\"Directory\":\"relative\",\"Now\":\"2026-10-13T08:00:00\"}")]
    [InlineData("{\"Version\":1,\"Id\":\"7058ae5f-1d46-46e5-9fb9-f97c24f665b0\",\"Directory\":null,\"Now\":\"2026-10-13T08:00:00\",\"Extra\":{\"a\":{\"b\":{\"c\":{\"d\":1}}}}}")]
    public async Task RequestRejectsInvalidVersionIdentityDirectoryOrDepth(string json)
    {
        using var stream = Frame(json);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProtocolTask("ReadRequestAsync", stream, CancellationToken.None));
    }

    [Fact]
    public async Task ResponseContainsOnlyAllowlistedFieldsAndMapsRawCheckerErrors()
    {
        object request = Protocol("CreateRequest", Path.Combine(Path.GetTempPath(), "PRIVATE-directory"), Now)!;
        var privateResult = new AttendanceStatus(false, false, null, "PRIVATE-card-uid-key", 0, "PRIVATE-exception-directory-key");
        object response = Protocol("CreateResponse", request, privateResult)!;
        using var stream = new MemoryStream();
        await ProtocolTask("WriteResponseAsync", stream, response, CancellationToken.None);
        string json = Encoding.UTF8.GetString(stream.ToArray(), 4, checked((int)stream.Length - 4));
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal(new[] { "ClockInTime", "ClockedIn", "Error", "Id", "Now", "Obtained", "TodayMessages", "Version" },
            parsed.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain("PRIVATE", json);
        stream.Position = 0;
        var status = (AttendanceStatus)(await ProtocolTask("ReadResponseAsync", stream, request, CancellationToken.None))!;
        Assert.False(status.Obtained);
        Assert.Null(status.LatestRecord);
        Assert.DoesNotContain("PRIVATE", status.Error!);
    }

    [Fact]
    public void WorkerRejectsElevationAndInitializesNativeSqliteBeforeChecker()
    {
        string source = ReadSource("src/ToolsBox.App/Attendance/AttendanceReadWorker.cs");
        Assert.Contains("WindowsBuiltInRole.Administrator", source);
        Assert.Contains("AttendanceSqliteRuntime.EnsureInitialized", source);
        Assert.Contains("new DingTalkAttendanceChecker", source);
        Assert.True(source.IndexOf("WindowsBuiltInRole.Administrator", StringComparison.Ordinal) <
            source.IndexOf("AttendanceSqliteRuntime.EnsureInitialized", StringComparison.Ordinal));
        Assert.True(source.IndexOf("AttendanceSqliteRuntime.EnsureInitialized", StringComparison.Ordinal) <
            source.IndexOf("new DingTalkAttendanceChecker", StringComparison.Ordinal));
        Assert.Contains("Task.Run", source);
        Assert.DoesNotContain("AttendanceStore", source);
        Assert.DoesNotContain("AttendanceHost", source);
        Assert.DoesNotContain("AttendanceReminderWindow", source);
        Assert.DoesNotContain("Console.Error", source);
    }

    private static IDisposable CreateClient(Func<ProcessStartInfo, Process?> start, TimeSpan? timeout = null,
        string? executable = null, string? entry = null)
    {
        Type type = FindType("AttendanceReadClient");
        return (IDisposable)Activator.CreateInstance(type,
            executable ?? Path.Combine(AppContext.BaseDirectory, "reader.exe"), entry, start, timeout ?? TimeSpan.FromSeconds(12))!;
    }

    private static Task<AttendanceStatus> Check(IDisposable client, string? directory, DateTime now, CancellationToken token = default)
        => (Task<AttendanceStatus>)client.GetType().GetMethod("CheckAsync")!.Invoke(client, [directory, now, token])!;

    private static Type FindType(string name)
    {
        Type? type = typeof(AttendanceCoordinator).Assembly.GetType("ToolsBox.App.Attendance." + name);
        Assert.True(type is not null, "Missing isolated reader implementation: " + name);
        return type!;
    }

    private static object? Protocol(string method, params object?[] values)
        => FindType("AttendanceReadProtocol").GetMethod(method)!.Invoke(null, values);

    private static async Task<object?> ProtocolTask(string method, params object?[] values)
    {
        Task task;
        try { task = (Task)Protocol(method, values)!; }
        catch (TargetInvocationException error) when (error.InnerException is InvalidDataException)
        { throw error.InnerException; }
        await task;
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private static MemoryStream Frame(string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        var stream = new MemoryStream();
        Span<byte> prefix = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        stream.Write(prefix);
        stream.Write(body);
        stream.Position = 0;
        return stream;
    }

    private static string ReadSource(string relative)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string path = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }

    private sealed class ReaderFixture(params string[] modes) : IDisposable
    {
        public List<Process> Started { get; } = [];

        public Process Start(ProcessStartInfo ignored)
        {
            string mode = modes[Math.Min(Started.Count, modes.Length - 1)];
            var process = Process.Start(PowerShell(Script.Replace("FIXTURE_MODE", mode)))!;
            Started.Add(Process.GetProcessById(process.Id));
            return process;
        }

        public async Task WaitForStarts(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            while (Started.Count < count) await Task.Delay(10, timeout.Token);
        }

        public static Process StartIndependentSleeper() => Process.Start(PowerShell("Start-Sleep -Seconds 30"))!;

        public void Dispose()
        {
            foreach (Process process in Started)
            {
                if (!process.HasExited) process.Kill();
                process.Dispose();
            }
        }

        private static ProcessStartInfo PowerShell(string script)
        {
            var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-EncodedCommand");
            info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            return info;
        }

        private const string Script = """
            $mode = 'FIXTURE_MODE'
            $inputPipe = [Console]::OpenStandardInput()
            $outputPipe = [Console]::OpenStandardOutput()
            $header = New-Object byte[] 4
            $offset = 0
            while ($offset -lt 4) { $n = $inputPipe.Read($header, $offset, 4-$offset); if ($n -eq 0) { exit 2 }; $offset += $n }
            $size = [BitConverter]::ToInt32($header, 0)
            if ($size -le 0 -or $size -gt 32768) { exit 2 }
            $body = New-Object byte[] $size
            $offset = 0
            while ($offset -lt $size) { $n = $inputPipe.Read($body, $offset, $size-$offset); if ($n -eq 0) { exit 2 }; $offset += $n }
            $request = [Text.Encoding]::UTF8.GetString($body) | ConvertFrom-Json
            if ($mode -eq 'autoblank' -and $null -ne $request.Directory) { exit 3 }
            if ($mode -eq 'hang') { Start-Sleep -Seconds 30; exit 0 }
            if ($mode -eq 'slow') { Start-Sleep -Milliseconds 600 }
            if ($mode -eq 'eof') { exit 0 }
            $response = [ordered]@{ Version=1; Id=$request.Id; Now=$request.Now; Obtained=$true; ClockedIn=$true; ClockInTime='07:50'; TodayMessages=1; Error='None' }
            if ($mode -eq 'bootstrap') { $response.Obtained=$false; $response.ClockedIn=$false; $response.ClockInTime=$null; $response.TodayMessages=0; $response.Error='BootstrapFailed' }
            if ($mode -eq 'mismatch') { $response.Id='7058ae5f-1d46-46e5-9fb9-f97c24f665b0' }
            if ($mode -eq 'wrongnow') { $response.Now='2026-10-14T08:00:00' }
            if ($mode -eq 'future') { $response.ClockInTime='09:00' }
            if ($mode -eq 'count') { $response.TodayMessages=10001 }
            if ($mode -eq 'inconsistent') { $response.Obtained=$false }
            if ($mode -eq 'unknownfield') { $response.LatestRecord='PRIVATE-card-uid-key' }
            if ($mode -eq 'unknownerror') { $response.Error='PRIVATE-exception-directory-key' }
            $json = $response | ConvertTo-Json -Compress
            if ($mode -eq 'invalidjson') { $json='PRIVATE-malformed-json' }
            $bytes = [Text.Encoding]::UTF8.GetBytes($json)
            $length = $bytes.Length
            if ($mode -eq 'oversized') { $length=4097 }
            if ($mode -eq 'stderr') { [Console]::Error.Write(('PRIVATE-stderr-directory-key' * 10000)) }
            [Console]::Error.Write('PRIVATE-stderr-directory-key')
            $outputPipe.Write([BitConverter]::GetBytes([int]$length), 0, 4)
            $outputPipe.Write($bytes, 0, $bytes.Length)
            if ($mode -eq 'trailing') { $outputPipe.WriteByte(1) }
            $outputPipe.Flush()
            if ($mode -eq 'validhang') { Start-Sleep -Seconds 30 }
            if ($mode -eq 'nonzero') { exit 9 }
            exit 0
            """;
    }
}
