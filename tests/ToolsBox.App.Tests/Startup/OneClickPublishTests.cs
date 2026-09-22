using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace ToolsBox.App.Tests.Startup;

public sealed class OneClickPublishTests
{
    [Theory]
    [InlineData("Publish-SelfContained.cmd", "self-contained-win-x64", "true")]
    [InlineData("Publish-FrameworkDependent.cmd", "framework-dependent-win-x64", "false")]
    public async Task EntryPoint_PublishesExpectedModeFromAnotherDirectory(string entryPoint, string modeDirectory, string selfContained)
    {
        using var fixture = new PublishFixture();
        RunResult result = await fixture.RunAsync(entryPoint);

        Assert.True(result.ExitCode == 0, result.Output);
        Invocation[] calls = fixture.ReadInvocations();
        Assert.Equal(2, calls.Length);
        Assert.Equal(["--list-sdks"], calls[0].Arguments);
        Assert.All(calls, call => Assert.Equal(fixture.Repository, call.WorkingDirectory));
        string[] args = calls[1].Arguments;
        Assert.Equal("publish", args[0]);
        Assert.Contains(Path.Combine(fixture.Repository, "src", "ToolsBox.App", "ToolsBox.App.csproj"), args);
        AssertOption(args, "--configuration", "Release");
        AssertOption(args, "--framework", "net8.0-windows");
        AssertOption(args, "--runtime", "win-x64");
        AssertOption(args, "--self-contained", selfContained);
        Assert.True(args.Contains("-p:PublishProfile=PortableWinX64"), string.Join(" | ", args));
        Assert.Contains($"-p:EnableCompressionInSingleFile={selfContained}", args);
        Assert.DoesNotContain("--no-restore", args);
        string outputDirectory = GetOption(args, "--output");
        Assert.Equal(Path.Combine(fixture.Repository, "bin", "publish", modeDirectory), Path.GetDirectoryName(outputDirectory));
        Assert.True(new FileInfo(Path.Combine(outputDirectory, "宝哥工具箱.exe")).Length > 0);
        Assert.Contains(outputDirectory, result.Output);
        Assert.Contains("MiB", result.Output);
        Assert.Contains("STUB PUBLISH OUTPUT", result.Output);
        Assert.DoesNotContain("按 Enter", result.Output);
    }

    [Theory]
    [InlineData("no-dotnet")]
    [InlineData("runtime-only")]
    [InlineData("sdk-list-fails")]
    public async Task MissingSdk_ReturnsFailureWithOfficialDownloadLink(string behavior)
    {
        using var fixture = new PublishFixture(behavior);
        RunResult result = await fixture.RunAsync("Publish-SelfContained.cmd");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("SDK", result.Output);
        Assert.Contains("https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0", result.Output);
        Assert.DoesNotContain("MiB", result.Output);
        Assert.DoesNotContain(fixture.ReadInvocations(), call => call.Arguments[0] == "publish");
    }

    [Fact]
    public async Task FailedPublish_PreservesNativeExitCodeAndDoesNotReportSuccess()
    {
        using var fixture = new PublishFixture("publish-fails");
        RunResult result = await fixture.RunAsync("Publish-FrameworkDependent.cmd");

        Assert.Equal(37, result.ExitCode);
        Assert.Contains("STUB PUBLISH FAILURE", result.Output);
        Assert.DoesNotContain("MiB", result.Output);
        Assert.DoesNotContain("发布成功", result.Output);
    }

    [Theory]
    [InlineData("missing-exe")]
    [InlineData("empty-exe")]
    public async Task SuccessfulProcessWithoutUsableExecutable_IsFailure(string behavior)
    {
        using var fixture = new PublishFixture(behavior);
        RunResult result = await fixture.RunAsync("Publish-SelfContained.cmd");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("宝哥工具箱.exe", result.Output);
        Assert.DoesNotContain("MiB", result.Output);
        Assert.DoesNotContain("发布成功", result.Output);
    }

    [Fact]
    public async Task RepeatedPublish_KeepsExistingArtifactsAndChoosesUniqueDirectories()
    {
        using var fixture = new PublishFixture();
        Assert.Equal(0, (await fixture.RunAsync("Publish-SelfContained.cmd")).ExitCode);
        string firstOutput = GetOption(fixture.ReadInvocations().Last().Arguments, "--output");
        string firstExecutable = Path.Combine(firstOutput, "宝哥工具箱.exe");
        File.WriteAllText(firstExecutable, "retained artifact");

        Assert.Equal(0, (await fixture.RunAsync("Publish-SelfContained.cmd")).ExitCode);
        string secondOutput = GetOption(fixture.ReadInvocations().Last().Arguments, "--output");

        Assert.NotEqual(firstOutput, secondOutput);
        Assert.Equal("retained artifact", File.ReadAllText(firstExecutable));
        Assert.True(File.Exists(Path.Combine(secondOutput, "宝哥工具箱.exe")));
    }

    [Fact]
    public async Task ConcurrentPublish_IsRejectedAndLockIsReleasedAfterCompletion()
    {
        using var fixture = new PublishFixture("hold-publish");
        using Process first = fixture.Start("Publish-SelfContained.cmd");
        Task<string> firstOutput = first.StandardOutput.ReadToEndAsync();
        Task<string> firstError = first.StandardError.ReadToEndAsync();
        try
        {
            await fixture.WaitForPublishStartedAsync(first);
            RunResult blocked = await fixture.RunAsync("Publish-FrameworkDependent.cmd");
            Assert.NotEqual(0, blocked.ExitCode);
            Assert.Contains("发布任务", blocked.Output);
            Assert.DoesNotContain("MiB", blocked.Output);
            Assert.Single(fixture.ReadInvocations().Where(call => call.Arguments[0] == "publish"));
        }
        finally
        {
            File.WriteAllText(fixture.ReleasePath, "release");
            await PublishFixture.WaitForExitAsync(first);
        }

        Assert.True(first.ExitCode == 0, await firstOutput + await firstError);
        Assert.Equal(0, (await fixture.RunAsync("Publish-FrameworkDependent.cmd")).ExitCode);
    }

    [Fact]
    public async Task DefaultInvocation_PausesAfterFailureAndPreservesExitCode()
    {
        using var fixture = new PublishFixture("publish-fails");
        using Process process = fixture.Start("Publish-SelfContained.cmd", noPause: false);
        var output = new StringBuilder();
        Task outputReader = Task.Run(async () =>
        {
            var buffer = new char[1];
            int count;
            while ((count = await process.StandardOutput.ReadAsync(buffer)) > 0)
            {
                lock (output) output.Append(buffer, 0, count);
            }
        });
        Task<string> errorReader = process.StandardError.ReadToEndAsync();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                lock (output)
                {
                    if (output.ToString().Contains("按 Enter", StringComparison.Ordinal)) break;
                }
                Assert.False(process.HasExited, "The default entry point exited without waiting for input.");
                await Task.Delay(50, timeout.Token);
            }
            Assert.False(process.HasExited);
            // The first window is still open, so the OS has not released its handles.
            RunResult next = await fixture.RunAsync("Publish-FrameworkDependent.cmd");
            Assert.Equal(37, next.ExitCode);
            Assert.Equal(2, fixture.ReadInvocations().Count(call => call.Arguments[0] == "publish"));
        }
        catch (OperationCanceledException)
        {
            lock (output) Assert.Fail("Timed out waiting for pause prompt. Captured output: " + output);
        }
        finally
        {
            await process.StandardInput.WriteLineAsync();
            process.StandardInput.Close();
            await PublishFixture.WaitForExitAsync(process);
            await outputReader;
            await errorReader;
        }
        Assert.Equal(37, process.ExitCode);
    }

    [Theory]
    [InlineData("Publish-SelfContained.cmd")]
    [InlineData("Publish-FrameworkDependent.cmd")]
    public async Task EntryPoint_RejectsModeOverrides(string entryPoint)
    {
        using var fixture = new PublishFixture();
        RunResult result = await fixture.RunAsync(entryPoint, extraArguments: "-Mode FrameworkDependent");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(fixture.ReadInvocations());
    }

    private static void AssertOption(string[] arguments, string option, string expected) =>
        Assert.Equal(expected, GetOption(arguments, option));

    private static string GetOption(string[] arguments, string option)
    {
        int index = Array.IndexOf(arguments, option);
        Assert.InRange(index, 0, arguments.Length - 2);
        return arguments[index + 1];
    }

    private sealed record RunResult(int ExitCode, string Output);
    private sealed record Invocation(string WorkingDirectory, string[] Arguments);

    private sealed class PublishFixture : IDisposable
    {
        private readonly string fixtureRoot;
        private readonly string stubDirectory;
        private readonly string invocationLog;
        private readonly string behavior;
        public string Repository { get; }
        public string ReleasePath => Path.Combine(fixtureRoot, "release");

        public PublishFixture(string behavior = "success")
        {
            this.behavior = behavior;
            string sourceRoot = FindRepositoryRoot();
            string[] scripts = ["Publish-SelfContained.cmd", "Publish-FrameworkDependent.cmd", Path.Combine("scripts", "Publish-Portable.ps1")];
            foreach (string script in scripts)
                Assert.True(File.Exists(Path.Combine(sourceRoot, script)), $"Missing one-click publish entry point: {script}");

            fixtureRoot = Path.Combine(sourceRoot, "bin", "publish-script-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(fixtureRoot, "中文 空格仓库");
            stubDirectory = Path.Combine(fixtureRoot, "stub tools");
            invocationLog = Path.Combine(fixtureRoot, "invocations.jsonl");
            Directory.CreateDirectory(stubDirectory);
            Directory.CreateDirectory(Path.Combine(fixtureRoot, "other cwd"));
            foreach (string script in scripts)
            {
                string destination = Path.Combine(Repository, script);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(Path.Combine(sourceRoot, script), destination);
            }
            string projectDirectory = Path.Combine(Repository, "src", "ToolsBox.App");
            Directory.CreateDirectory(projectDirectory);
            File.WriteAllText(Path.Combine(projectDirectory, "ToolsBox.App.csproj"), "<Project />");
            if (behavior != "no-dotnet")
            {
                File.WriteAllText(Path.Combine(stubDirectory, "dotnet.cmd"), "@echo off\r\n\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoLogo -NoProfile -ExecutionPolicy Bypass -File \"%~dp0dotnet-stub.ps1\" %*\r\nexit /b %ERRORLEVEL%\r\n", Encoding.ASCII);
                File.WriteAllText(Path.Combine(stubDirectory, "dotnet-stub.ps1"), StubScript, new UTF8Encoding(true));
            }
        }

        public Process Start(string entryPoint, bool noPause = true, string extraArguments = "")
        {
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var info = new ProcessStartInfo(Path.Combine(systemDirectory, "cmd.exe"))
            {
                Arguments = $"/d /s /c \"\"{Path.Combine(Repository, entryPoint)}\" {(noPause ? "-NoPause" : "")} {extraArguments}\"",
                WorkingDirectory = Path.Combine(fixtureRoot, "other cwd"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            info.Environment["PATH"] = stubDirectory + ";" + systemDirectory;
            info.Environment["TOOLSBOX_STUB_BEHAVIOR"] = behavior;
            info.Environment["TOOLSBOX_STUB_LOG"] = invocationLog;
            info.Environment["TOOLSBOX_STUB_STARTED"] = Path.Combine(fixtureRoot, "started");
            info.Environment["TOOLSBOX_STUB_RELEASE"] = ReleasePath;
            return Process.Start(info)!;
        }

        public async Task<RunResult> RunAsync(string entryPoint, string extraArguments = "")
        {
            using Process process = Start(entryPoint, extraArguments: extraArguments);
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            // Keep stdin open: a mistaken pause with -NoPause must fail by timeout.
            await WaitForExitAsync(process);
            return new RunResult(process.ExitCode, await output + await error);
        }

        public Invocation[] ReadInvocations() => File.Exists(invocationLog)
            ? File.ReadAllLines(invocationLog).Select(line => JsonSerializer.Deserialize<Invocation>(line)!).ToArray()
            : [];

        public async Task WaitForPublishStartedAsync(Process process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (!File.Exists(Path.Combine(fixtureRoot, "started")))
            {
                Assert.False(process.HasExited, "The publisher exited before reaching dotnet publish.");
                await Task.Delay(50, timeout.Token);
            }
        }

        public static async Task WaitForExitAsync(Process process)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                // Only terminate the child tree created by this test, never an existing app.
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                Assert.Fail("The publish test child process timed out (possible unexpected pause).");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(fixtureRoot)) Directory.Delete(fixtureRoot, recursive: true);
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "ToolsBox.slnx"))) root = root.Parent;
            Assert.NotNull(root);
            return root.FullName;
        }

        private const string StubScript = """
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
            $ErrorActionPreference = 'Stop'
            # -File binds colon arguments such as -p:Name=value before exposing $args.
            # Capture the native argv instead, exactly as a real dotnet.exe would see it.
            $nativeArguments = [Environment]::GetCommandLineArgs()
            $scriptIndex = [Array]::IndexOf($nativeArguments, $PSCommandPath)
            $dotnetArguments = @($nativeArguments | Select-Object -Skip ($scriptIndex + 1))
            $record = @{ WorkingDirectory = (Get-Location).Path; Arguments = $dotnetArguments } | ConvertTo-Json -Compress
            [System.IO.File]::AppendAllText($env:TOOLSBOX_STUB_LOG, $record + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
            if ($dotnetArguments[0] -eq '--list-sdks') {
                if ($env:TOOLSBOX_STUB_BEHAVIOR -eq 'sdk-list-fails') { exit 11 }
                if ($env:TOOLSBOX_STUB_BEHAVIOR -ne 'runtime-only') { Write-Output '8.0.408 [C:\stub\sdk]' }
                exit 0
            }
            if ($dotnetArguments[0] -ne 'publish') { exit 98 }
            if ($env:TOOLSBOX_STUB_BEHAVIOR -eq 'publish-fails') {
                Write-Output 'STUB PUBLISH FAILURE'
                exit 37
            }
            if ($env:TOOLSBOX_STUB_BEHAVIOR -eq 'hold-publish') {
                [System.IO.File]::WriteAllText($env:TOOLSBOX_STUB_STARTED, 'started')
                $deadline = [DateTime]::UtcNow.AddSeconds(30)
                while (-not (Test-Path -LiteralPath $env:TOOLSBOX_STUB_RELEASE)) {
                    if ([DateTime]::UtcNow -gt $deadline) { exit 97 }
                    Start-Sleep -Milliseconds 50
                }
            }
            $outputIndex = [Array]::IndexOf($dotnetArguments, '--output')
            if ($outputIndex -lt 0) { exit 96 }
            $outputDirectory = $dotnetArguments[$outputIndex + 1]
            [System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
            if ($env:TOOLSBOX_STUB_BEHAVIOR -ne 'missing-exe') {
                [byte[]]$bytes = @(77, 90, 1, 2)
                if ($env:TOOLSBOX_STUB_BEHAVIOR -eq 'empty-exe') { $bytes = @() }
                [System.IO.File]::WriteAllBytes((Join-Path $outputDirectory '宝哥工具箱.exe'), $bytes)
            }
            Write-Output 'STUB PUBLISH OUTPUT'
            exit 0
            """;
    }
}
