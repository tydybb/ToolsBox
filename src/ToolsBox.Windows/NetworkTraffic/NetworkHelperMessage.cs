using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToolsBox.Core.NetworkTraffic;

namespace ToolsBox.Windows.NetworkTraffic;

public sealed record NetworkHelperMessage(
    int Version,
    string Type,
    string? RequestId,
    JsonElement Payload)
{
    public static NetworkHelperMessage Create<T>(string type, string? requestId, T payload) =>
        new(NetworkHelperProtocol.Version, type, requestId, JsonSerializer.SerializeToElement(payload, NetworkHelperProtocol.JsonOptions));
}

public static class NetworkHelperProtocol
{
    public const int Version = 1;
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static bool ValidateHandshake(
        NetworkHelperMessage message,
        string expectedToken,
        out string? error)
    {
        if (message.Version != Version)
        {
            error = "网络辅助进程协议版本不兼容。";
            return false;
        }

        if (!string.Equals(message.Type, "handshake", StringComparison.Ordinal))
        {
            error = "网络辅助进程握手消息无效。";
            return false;
        }

        if (!message.Payload.TryGetProperty("Token", out JsonElement tokenElement) ||
            tokenElement.ValueKind != JsonValueKind.String)
        {
            error = "网络辅助进程身份令牌缺失。";
            return false;
        }

        string suppliedToken = tokenElement.GetString() ?? string.Empty;
        byte[] suppliedBytes = Encoding.UTF8.GetBytes(suppliedToken);
        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        bool matches = suppliedBytes.Length == expectedBytes.Length &&
                       CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
        if (!matches)
        {
            error = "网络辅助进程身份验证失败。";
            return false;
        }

        error = null;
        return true;
    }
}

public sealed record NetworkHelperResponse(bool Succeeded, string? Error, JsonElement Data)
{
    public static NetworkHelperResponse Success<T>(T data) =>
        new(true, null, JsonSerializer.SerializeToElement(data, NetworkHelperProtocol.JsonOptions));

    public static NetworkHelperResponse Failure(string error) =>
        new(false, error, JsonSerializer.SerializeToElement<object?>(null, NetworkHelperProtocol.JsonOptions));
}

public sealed record NetworkTrafficBatchPayload(
    IReadOnlyList<NetworkTrafficDelta> Deltas,
    long DroppedEventCount);

public sealed record NetworkHelperPathPayload(string ExecutablePath);

public sealed record NetworkHelperSetLimitPayload(string ExecutablePath, ulong BitsPerSecond);
