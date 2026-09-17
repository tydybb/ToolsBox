namespace ToolsBox.Core.FileUnlocking;

public sealed record FileUnlockResult(bool Succeeded, string Message)
{
    public static FileUnlockResult Success(string message) => new(true, message);
    public static FileUnlockResult Failure(string message) => new(false, message);
}
