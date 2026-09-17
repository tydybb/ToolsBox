using System.Text;
using System.Text.Json;
using ToolsBox.Core.FileUnlocking;

namespace ToolsBox.Windows.FileUnlocking;

public sealed record ElevatedActionRequest(
    string Action,
    FileLockEntry? Entry = null,
    FileLockTarget? Target = null,
    string? ResultPipe = null)
{
    public string Encode() => Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this)));

    public static ElevatedActionRequest Decode(string value)
    {
        byte[] bytes = Convert.FromBase64String(value);
        return JsonSerializer.Deserialize<ElevatedActionRequest>(Encoding.UTF8.GetString(bytes))
            ?? throw new InvalidDataException("提权请求内容无效。");
    }
}
