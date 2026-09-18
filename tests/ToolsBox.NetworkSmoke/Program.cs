using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.ExceptionServices;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using ToolsBox.Windows.NetworkTraffic;

// Opt-in Windows integration check. Launches only the network helper (may prompt for UAC),
// exchanges loopback UDP traffic, and reads QoS rules without changing any policies.
if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the full path to the built toolbox executable.");

// Exercise metadata queries for actual running processes, including protected ones.
int metadataExceptions = 0, metadataCount = 0;
void CountMetadataException(object? sender, FirstChanceExceptionEventArgs e)
{
    if (e.Exception is Win32Exception) Interlocked.Increment(ref metadataExceptions);
}
var metadataProvider = new WindowsProcessMetadataProvider();
AppDomain.CurrentDomain.FirstChanceException += CountMetadataException;
try
{
    foreach (Process process in Process.GetProcesses())
    {
        using (process)
        {
            _ = await metadataProvider.GetAsync(process.Id, null);
            metadataCount++;
        }
    }
}
finally
{
    AppDomain.CurrentDomain.FirstChanceException -= CountMetadataException;
}
if (metadataExceptions != 0) throw new InvalidOperationException($"Metadata threw {metadataExceptions} first-chance Win32 exceptions.");
Console.WriteLine($"PASS: queried {metadataCount} process identities, no first-chance Win32 exceptions.");

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
CancellationToken token = timeout.Token;
string pipeName = $"BaoGeToolsBox-Smoke-{Guid.NewGuid():N}";
string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
await using var stream = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
var start = new ProcessStartInfo(Path.GetFullPath(args[0]))
{
    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
};
start.ArgumentList.Add("--elevated-network");
start.ArgumentList.Add(pipeName);
start.ArgumentList.Add(secret);
using var helper = Process.Start(start) ?? throw new InvalidOperationException("Helper did not start.");
await stream.WaitForConnectionAsync(token);
await using var pipe = new LengthPrefixedJsonPipe(stream, leaveOpen: true);
NetworkHelperMessage handshake = await pipe.ReadAsync(token) ?? throw new IOException("No handshake.");
if (!NetworkHelperProtocol.ValidateHandshake(handshake, secret, out string? error))
    throw new InvalidOperationException(error);
await pipe.WriteAsync(NetworkHelperMessage.Create("handshake-accepted", null, new { }), token);
await Task.Delay(500, token);
AssertHeadless();
Console.WriteLine("PASS: authenticated helper, no main window.");

for (int cycle = 1; cycle <= 2; cycle++)
{
    await CommandAsync("start-monitor");
    using var receiver = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    using var sender = new UdpClient();
    var endpoint = (IPEndPoint)receiver.Client.LocalEndPoint!;
    byte[] packet = new byte[1024];
    for (int i = 0; i < 64; i++)
    {
        await sender.SendAsync(packet, endpoint, token);
        await receiver.ReceiveAsync(token);
    }

    using var trafficTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
    trafficTimeout.CancelAfter(TimeSpan.FromSeconds(12));
    long ownBytes = 0;
    while (ownBytes == 0)
    {
        NetworkHelperMessage message = await pipe.ReadAsync(trafficTimeout.Token)
            ?? throw new IOException("Helper disconnected before traffic arrived.");
        if (message.Type == "traffic-batch")
        {
            var batch = message.Payload.Deserialize<NetworkTrafficBatchPayload>(NetworkHelperProtocol.JsonOptions)!;
            ownBytes += batch.Deltas.Where(delta => delta.ProcessId == Environment.ProcessId).Sum(delta => delta.ByteCount);
        }
    }
    AssertHeadless();
    await CommandAsync("stop-monitor");
    Console.WriteLine($"PASS: cycle {cycle}, real ETW loopback bytes for PID {Environment.ProcessId}: {ownBytes}; stopped.");
}

await CommandAsync("get-rules");
Console.WriteLine("PASS: read QoS rules (no modifications).");
await CommandAsync("shutdown");
await helper.WaitForExitAsync(token);
if (helper.ExitCode != 0) throw new InvalidOperationException($"Helper exit code: {helper.ExitCode}");
Console.WriteLine("PASS: helper exited cleanly.");

void AssertHeadless()
{
    helper.Refresh();
    if (helper.HasExited || helper.MainWindowHandle != IntPtr.Zero)
        throw new InvalidOperationException("Helper exited unexpectedly or opened a main window.");
}

async Task CommandAsync(string command)
{
    string request = Guid.NewGuid().ToString("N");
    await pipe.WriteAsync(NetworkHelperMessage.Create(command, request, new { }), token);
    while (true)
    {
        NetworkHelperMessage message = await pipe.ReadAsync(token) ?? throw new IOException($"Disconnected during {command}.");
        if (message.Type != "response" || message.RequestId != request) continue;
        var response = message.Payload.Deserialize<NetworkHelperResponse>(NetworkHelperProtocol.JsonOptions)!;
        if (!response.Succeeded) throw new InvalidOperationException($"{command}: {response.Error}");
        return;
    }
}
