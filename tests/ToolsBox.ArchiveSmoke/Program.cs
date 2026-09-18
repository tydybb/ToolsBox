using System.Diagnostics;
using System.Security.Cryptography;
using ToolsBox.Core.ArchiveRecovery;
using ToolsBox.Windows.ArchiveRecovery;

if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--without-installed-runtime")) { Console.Error.WriteLine("Usage: ToolsBox.ArchiveSmoke <published-exe> <test-fixture-directory> [--without-installed-runtime]"); return 2; }
string executable = Path.GetFullPath(args[0]), fixtures = Path.GetFullPath(args[1]);
if (args.Length == 3)
{
    // The harness is already running. Hide installed runtimes only from newly launched children.
    string emptyRuntime = Directory.CreateTempSubdirectory("ToolsBox-runtime-smoke-").FullName;
    Environment.SetEnvironmentVariable("DOTNET_ROOT", emptyRuntime);
    Environment.SetEnvironmentVariable("DOTNET_ROOT_X64", emptyRuntime);
    Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
    Environment.SetEnvironmentVariable("DOTNET_DISABLE_GUI_ERRORS", "1");
}
string[] samples = ["zip-crypto.zip", "zip-aes.zip", "7z-unicode-spaces.7z", "7z-data.7z", "7z-headers.7z", "Rar.encrypted_filesOnly.rar", "Rar.encrypted_filesAndHeader.rar", "Rar5.encrypted_filesOnly.rar", "Rar5.encrypted_filesAndHeader.rar"];
try
{
    using var client = new ArchiveRecoveryClient(executable);
    foreach (string sample in samples)
    {
        string path = Path.Combine(fixtures, sample);
        byte[] hash = SHA256.HashData(File.ReadAllBytes(path));
        var rejected = await client.VerifyAsync(path, "known-wrong-test-candidate", TimeSpan.FromSeconds(30), default);
        if (rejected.Outcome is not (ArchivePasswordOutcome.NoMatch or ArchivePasswordOutcome.RejectedUncertain)) throw new InvalidOperationException($"{sample}: {rejected.Outcome} ({rejected.Message})");
        string expected = sample == "7z-unicode-spaces.7z" ? " Aa密码1! " : sample.EndsWith(".rar", StringComparison.Ordinal) ? "test" : "test-pass";
        var result = await client.VerifyAsync(path, expected, TimeSpan.FromSeconds(30), default);
        if (result.Outcome != ArchivePasswordOutcome.Match) throw new InvalidOperationException($"{sample}: {result.Outcome} ({result.Message})");
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path)))) throw new InvalidOperationException("Archive changed.");
        Console.WriteLine($"PASS {sample}: rejected wrong, fully verified correct, source SHA-256 unchanged");
    }
    using var cancelled = new CancellationTokenSource();
    cancelled.Cancel();
    try { await client.VerifyAsync(Path.Combine(fixtures, samples[0]), "unused-test-value", TimeSpan.FromSeconds(30), cancelled.Token); throw new InvalidOperationException("Cancellation ignored."); }
    catch (OperationCanceledException) { Console.WriteLine("PASS cancellation"); }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
