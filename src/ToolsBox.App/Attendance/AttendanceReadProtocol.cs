using System.Buffers.Binary;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ToolsBox.Core.Attendance;

namespace ToolsBox.App.Attendance;

public enum AttendanceReadError
{
    None,
    BootstrapFailed,
    ReadFailed,
    InvalidRequest,
    ElevatedProcess,
    DirectoryUnavailable,
    MultipleAccounts,
    LegacyUnsupported,
    AccountConfigurationUnavailable,
    AccountUnavailable,
    UnsupportedData,
    ProcessFailed,
    InvalidResponse,
    TimedOut,
    Cancelled,
    Disposed
}

public sealed record AttendanceReadRequest(int Version, Guid Id, string? Directory, DateTime Now);

// Keep this wire record independent of AttendanceStatus so private card text can never be serialized.
public sealed record AttendanceReadResponse(int Version, Guid Id, DateTime Now, bool Obtained,
    bool ClockedIn, string? ClockInTime, int TodayMessages, AttendanceReadError Error);

public static class AttendanceReadProtocol
{
    public const int RequestLimit = 32768;
    public const int ResponseLimit = 4096;
    private static readonly string[] RequestFields = ["Version", "Id", "Directory", "Now"];
    private static readonly string[] ResponseFields =
        ["Version", "Id", "Now", "Obtained", "ClockedIn", "ClockInTime", "TodayMessages", "Error"];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 4,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<AttendanceReadError>(allowIntegerValues: false) }
    };

    public static AttendanceReadRequest CreateRequest(string? directory, DateTime now)
    {
        var request = new AttendanceReadRequest(1, Guid.NewGuid(), directory, now);
        ValidateRequest(request);
        return request;
    }

    public static Task WriteRequestAsync(Stream stream, AttendanceReadRequest request, CancellationToken token)
    {
        ValidateRequest(request);
        return WriteFrameAsync(stream, request, RequestLimit, token);
    }

    public static async Task<AttendanceReadRequest> ReadRequestAsync(Stream stream, CancellationToken token)
    {
        var request = await ReadFrameAsync<AttendanceReadRequest>(stream, RequestLimit, RequestFields, token).ConfigureAwait(false);
        ValidateRequest(request);
        return request;
    }

    public static AttendanceReadResponse CreateResponse(AttendanceReadRequest request, AttendanceStatus status)
    {
        ValidateRequest(request);
        var response = new AttendanceReadResponse(1, request.Id, request.Now, status.Obtained, status.ClockedIn,
            status.ClockInTime, status.TodayMessages, status.Obtained ? AttendanceReadError.None : MapCheckerError(status.Error));
        try
        {
            if (status.Obtained && status.Error is not null) throw new InvalidDataException();
            ValidateResponse(response, request);
            return response;
        }
        catch (InvalidDataException) { return CreateFailureResponse(request, AttendanceReadError.ReadFailed); }
    }

    public static AttendanceReadResponse CreateFailureResponse(AttendanceReadRequest request, AttendanceReadError error)
    {
        ValidateRequest(request);
        if (error == AttendanceReadError.None || !Enum.IsDefined(error)) throw new InvalidDataException();
        return new(1, request.Id, request.Now, false, false, null, 0, error);
    }

    public static Task WriteResponseAsync(Stream stream, AttendanceReadResponse response, CancellationToken token)
    {
        ValidateResponse(response, new(response.Version, response.Id, null, response.Now));
        return WriteFrameAsync(stream, response, ResponseLimit, token);
    }

    public static async Task<AttendanceStatus> ReadResponseAsync(Stream stream, AttendanceReadRequest request, CancellationToken token)
    {
        ValidateRequest(request);
        var response = await ReadFrameAsync<AttendanceReadResponse>(stream, ResponseLimit, ResponseFields, token).ConfigureAwait(false);
        ValidateResponse(response, request);
        return response.Obtained
            ? new(true, response.ClockedIn, response.ClockInTime, null, response.TodayMessages, null)
            : Failure(response.Error);
    }

    public static AttendanceStatus Failure(AttendanceReadError error) => AttendanceStatus.Failure(error switch
    {
        AttendanceReadError.BootstrapFailed => "本地读取组件初始化失败，请稍后重新检测；请在钉钉核对。",
        AttendanceReadError.InvalidRequest => "本地读取请求无效，请重新选择账号目录或稍后重新检测。",
        AttendanceReadError.ElevatedProcess => "打卡读取需要普通权限，请从普通权限的打卡助手重新检测。",
        AttendanceReadError.DirectoryUnavailable => "未找到有效的钉钉账号数据目录，请先登录钉钉或重新选择账号目录。",
        AttendanceReadError.MultipleAccounts => "发现多个钉钉账号，请明确选择账号数据目录。",
        AttendanceReadError.LegacyUnsupported => "仅发现旧版钉钉数据目录（_v2），暂不支持解密，请升级钉钉后重试。",
        AttendanceReadError.AccountConfigurationUnavailable => "无法读取受支持的钉钉账号配置。",
        AttendanceReadError.AccountUnavailable => "未能识别登录账号，请重新登录钉钉后重试；请在钉钉核对。",
        AttendanceReadError.UnsupportedData => "识别到候选账号，但密钥与本机数据不匹配；钉钉版本可能不受支持。",
        AttendanceReadError.ProcessFailed => "本地读取进程未完成，请稍后重新检测；请在钉钉核对。",
        AttendanceReadError.InvalidResponse => "本地读取结果无效，请稍后重新检测；请在钉钉核对。",
        AttendanceReadError.TimedOut => "本地读取超时，请稍后重新检测；请在钉钉核对。",
        AttendanceReadError.Cancelled => "本地读取已取消。",
        AttendanceReadError.Disposed => "本地读取已停止。",
        _ => "本地读取未完成：数据正在使用、格式不受支持或文件不完整；请在钉钉核对。"
    });

    private static AttendanceReadError MapCheckerError(string? error) => error switch
    {
        "未找到钉钉数据目录，请先安装并登录钉钉电脑版。" or
        "所选目录不是钉钉账号数据目录，请重新选择。" or
        "所选账号数据目录已不存在，请重新识别或手动选择。" or
        "候选目录过多，请直接选择账号数据目录。" or
        "候选目录不可读，请直接选择账号数据目录。" or
        "候选目录无访问权限，请直接选择账号数据目录。" => AttendanceReadError.DirectoryUnavailable,
        "发现多个钉钉账号，请明确选择账号数据目录。" => AttendanceReadError.MultipleAccounts,
        "仅发现旧版钉钉数据目录（_v2），暂不支持解密，请升级钉钉后重试。" => AttendanceReadError.LegacyUnsupported,
        "无法读取受支持的钉钉账号配置。" => AttendanceReadError.AccountConfigurationUnavailable,
        "未能识别登录账号：未找到本机钉钉日志目录，无法取得 uid。" or
        "未能识别登录账号：账号配置与本机日志中均未找到 uid，日志可能已被清理。" => AttendanceReadError.AccountUnavailable,
        "识别到候选账号，但密钥与本机数据不匹配；钉钉版本可能不受支持。" => AttendanceReadError.UnsupportedData,
        _ => AttendanceReadError.ReadFailed
    };

    private static void ValidateRequest(AttendanceReadRequest request)
    {
        if (request.Version != 1 || request.Id == Guid.Empty || request.Now == default) throw new InvalidDataException();
        if (request.Directory is not { } directory) return;
        if (directory.Length is 0 or > 4096 || directory.StartsWith(@"\\", StringComparison.Ordinal) ||
            directory.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(directory))
            throw new InvalidDataException();
        try
        {
            string? root = Path.GetPathRoot(directory);
            if (root is null || root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':')
                throw new InvalidDataException();
            _ = Path.GetFullPath(directory);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { throw new InvalidDataException(); }
    }

    private static void ValidateResponse(AttendanceReadResponse response, AttendanceReadRequest request)
    {
        ValidateRequest(request);
        if (response.Version != 1 || response.Id != request.Id || response.Now.Ticks != request.Now.Ticks ||
            response.Now.Kind != request.Now.Kind || response.TodayMessages is < 0 or > 10000 || !Enum.IsDefined(response.Error))
            throw new InvalidDataException();
        if (!response.Obtained)
        {
            if (response.ClockedIn || response.ClockInTime is not null || response.TodayMessages != 0 || response.Error == AttendanceReadError.None)
                throw new InvalidDataException();
            return;
        }
        if (response.Error != AttendanceReadError.None) throw new InvalidDataException();
        if (!response.ClockedIn)
        {
            if (response.ClockInTime is not null) throw new InvalidDataException();
            return;
        }
        if (response.TodayMessages == 0 || response.ClockInTime is not { Length: 5 } time ||
            !TimeSpan.TryParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture, out TimeSpan clockIn) ||
            clockIn < TimeSpan.Zero || clockIn >= TimeSpan.FromDays(1) || request.Now.Date.Add(clockIn) > request.Now)
            throw new InvalidDataException();
    }

    private static async Task WriteFrameAsync<T>(Stream stream, T value, int limit, CancellationToken token)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
        if (body.Length is 0 || body.Length > limit) throw new InvalidDataException();
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, body.Length);
        await stream.WriteAsync(prefix, token).ConfigureAwait(false);
        await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    private static async Task<T> ReadFrameAsync<T>(Stream stream, int limit, string[] fields, CancellationToken token)
    {
        byte[] prefix = new byte[4];
        await ReadExactlyAsync(stream, prefix, token).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (size <= 0 || size > limit) throw new InvalidDataException();
        byte[] body = new byte[size];
        await ReadExactlyAsync(stream, body, token).ConfigureAwait(false);
        try
        {
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
            if (json.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
                if (!fields.Contains(property.Name, StringComparer.Ordinal) || !seen.Add(property.Name)) throw new InvalidDataException();
            if (seen.Count != fields.Length) throw new InvalidDataException();
            return JsonSerializer.Deserialize<T>(body, JsonOptions) ?? throw new InvalidDataException();
        }
        catch (JsonException) { throw new InvalidDataException(); }
    }

    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), token).ConfigureAwait(false);
            if (read == 0) throw new InvalidDataException();
            offset += read;
        }
    }
}
