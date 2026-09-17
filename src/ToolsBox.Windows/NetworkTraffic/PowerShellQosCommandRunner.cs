using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ToolsBox.Windows.NetworkTraffic;

public enum QosCommandOperation
{
    Query,
    Create,
    Update,
    Remove
}

public sealed record QosPolicyRecord(
    string Name,
    string ExecutablePath,
    ulong BitsPerSecond,
    string Owner);

public interface IQosCommandRunner
{
    Task<IReadOnlyList<QosPolicyRecord>> GetPoliciesAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(string name, string executablePath, ulong bitsPerSecond, CancellationToken cancellationToken = default);
    Task UpdateAsync(string name, ulong bitsPerSecond, CancellationToken cancellationToken = default);
    Task RemoveAsync(string name, CancellationToken cancellationToken = default);
}

public sealed class PowerShellQosCommandRunner : IQosCommandRunner
{
    public const string NameEnvironmentName = "BAOGE_QOS_NAME";
    public const string PathEnvironmentName = "BAOGE_QOS_PATH";
    public const string RateEnvironmentName = "BAOGE_QOS_RATE";

    private const string QueryScript = "$ErrorActionPreference='Stop'; $items=@(Get-NetQosPolicy | Where-Object { -not [string]::IsNullOrWhiteSpace($_.AppPathName) } | ForEach-Object { [pscustomobject]@{ Name=[string]$_.Name; ExecutablePath=[string]$_.AppPathName; BitsPerSecond=([UInt64]$_.ThrottleRateAction * 8); Owner=[string]$_.Owner } }); ConvertTo-Json -InputObject $items -Compress";
    private const string CreateScript = "$ErrorActionPreference='Stop'; New-NetQosPolicy -Name $env:BAOGE_QOS_NAME -AppPathNameMatchCondition $env:BAOGE_QOS_PATH -IPProtocolMatchCondition Both -NetworkProfile All -ThrottleRateActionBitsPerSecond ([UInt64]$env:BAOGE_QOS_RATE) | Out-Null";
    private const string UpdateScript = "$ErrorActionPreference='Stop'; Set-NetQosPolicy -Name $env:BAOGE_QOS_NAME -ThrottleRateActionBitsPerSecond ([UInt64]$env:BAOGE_QOS_RATE) | Out-Null";
    private const string RemoveScript = "$ErrorActionPreference='Stop'; Remove-NetQosPolicy -Name $env:BAOGE_QOS_NAME -Confirm:$false | Out-Null";

    public async Task<IReadOnlyList<QosPolicyRecord>> GetPoliciesAsync(CancellationToken cancellationToken = default)
    {
        string output = await RunAsync(CreateStartInfo(QosCommandOperation.Query), cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
        {
            return [];
        }

        return JsonSerializer.Deserialize<QosPolicyRecord[]>(output, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? [];
    }

    public async Task CreateAsync(
        string name,
        string executablePath,
        ulong bitsPerSecond,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnedName(name);
        ValidatePath(executablePath);
        ValidateRate(bitsPerSecond);
        _ = await RunAsync(
            CreateStartInfo(QosCommandOperation.Create, name, executablePath, bitsPerSecond),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(
        string name,
        ulong bitsPerSecond,
        CancellationToken cancellationToken = default)
    {
        ValidateOwnedName(name);
        ValidateRate(bitsPerSecond);
        _ = await RunAsync(
            CreateStartInfo(QosCommandOperation.Update, name, null, bitsPerSecond),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        ValidateOwnedName(name);
        _ = await RunAsync(CreateStartInfo(QosCommandOperation.Remove, name), cancellationToken).ConfigureAwait(false);
    }

    public static ProcessStartInfo CreateStartInfo(
        QosCommandOperation operation,
        string? name = null,
        string? executablePath = null,
        ulong? bitsPerSecond = null)
    {
        string script = operation switch
        {
            QosCommandOperation.Query => QueryScript,
            QosCommandOperation.Create => CreateScript,
            QosCommandOperation.Update => UpdateScript,
            QosCommandOperation.Remove => RemoveScript,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(script);

        if (name is not null)
        {
            startInfo.Environment[NameEnvironmentName] = name;
        }

        if (executablePath is not null)
        {
            startInfo.Environment[PathEnvironmentName] = executablePath;
        }

        if (bitsPerSecond.HasValue)
        {
            startInfo.Environment[RateEnvironmentName] = bitsPerSecond.Value.ToString(CultureInfo.InvariantCulture);
        }

        return startInfo;
    }

    private static async Task<string> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 Windows QoS 管理命令。");
        Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }

            throw;
        }

        string output = await outputTask.ConfigureAwait(false);
        string error = await errorTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "Windows QoS 操作失败。"
                : error.Trim());
        }

        return output.Trim();
    }

    private static void ValidateOwnedName(string name)
    {
        if (!QosPolicyName.IsOwned(name) || name.IndexOf('\0') >= 0)
        {
            throw new ArgumentException("QoS 规则不属于宝哥工具箱。", nameof(name));
        }
    }

    private static void ValidatePath(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) ||
            executablePath.IndexOf('\0') >= 0 ||
            !Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("程序路径无效。", nameof(executablePath));
        }
    }

    private static void ValidateRate(ulong bitsPerSecond)
    {
        if (bitsPerSecond == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSecond), "限速值必须大于零。");
        }
    }
}
