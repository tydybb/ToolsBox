using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ToolsBox.Core.Attendance;

/// <summary>
/// 钉钉 v3 本机数据密钥链候选；仅支持能够通过本地库头校验的版本：
/// PBKDF2-HMAC-SHA1(ASCII(uid+salt), ASCII"666DingT", 1000 次, 32 字节) → MD5 十六进制前 16 个字符的 ASCII → AES-128-ECB；
/// 预言机：以该密钥加密 "SQLite format 3\0"，结果应等于密文库头 16 字节。
/// salt 来自账号目录 user_config（base64→JSON["salt"]），uid 来自 log 目录正则投票（real_uid / uid）。
/// </summary>
public static partial class DingTalkKeyVault
{
    private static readonly byte[] KeySalt = Encoding.ASCII.GetBytes("666DingT");

    [GeneratedRegex(@"real_uid[""']?\s*[:=]\s*[""']?(\d{6,12})", RegexOptions.IgnoreCase)]
    private static partial Regex RealUidPattern();

    [GeneratedRegex(@"\buid[""']?\s*[:=]\s*[""']?(\d{8,11})", RegexOptions.IgnoreCase)]
    private static partial Regex UidPattern();

    /// <summary>派生 AES-128-ECB 密钥（16 字节 = MD5 十六进制前 16 字符的 ASCII）。</summary>
    public static byte[] DeriveKey(string uid, string salt)
    {
        byte[] password = Encoding.ASCII.GetBytes(uid + salt);
        byte[] stretched = Rfc2898DeriveBytes.Pbkdf2(password, KeySalt, 1000, HashAlgorithmName.SHA1, 32);
        string hex = Convert.ToHexString(MD5.HashData(stretched)).ToLowerInvariant();
        return Encoding.ASCII.GetBytes(hex[..16]);
    }

    /// <summary>预言机校验：加密标准头并与密文库头 16 字节比对。</summary>
    public static bool VerifyKey(byte[] key, ReadOnlySpan<byte> head16)
    {
        if (head16.Length < 16) return false;
        using var aes = Aes.Create();
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        using ICryptoTransform encryptor = aes.CreateEncryptor(key, null);
        byte[] oracle = encryptor.TransformFinalBlock(Encoding.ASCII.GetBytes("SQLite format 3\0"), 0, 16);
        return oracle.AsSpan(0, 16).SequenceEqual(head16);
    }

    /// <summary>从候选 uid 中找出能通过预言机的密钥；全部失败返回 null。</summary>
    public static byte[]? FindVerifiedKey(IEnumerable<string> uidCandidates, string salt, ReadOnlySpan<byte> head16)
    {
        foreach (string uid in uidCandidates.Take(64))
        {
            byte[] key = DeriveKey(uid, salt);
            if (VerifyKey(key, head16)) return key;
            CryptographicOperations.ZeroMemory(key);
        }
        return null;
    }

    /// <summary>有界读取 user_config 的显式 salt 字段，支持明文或 base64 JSON。</summary>
    public static string? ReadSalt(string userConfigPath)
    {
        try
        {
            byte[] bytes = ReadSharedHead(userConfigPath, 65537);
            if (bytes.Length > 65536) return null;
            string raw = Encoding.UTF8.GetString(bytes).Trim();
            if (raw.Length == 0) return null;
            if (TryExtractSalt(raw, out string? salt)) return salt;
            string decoded;
            try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(raw)); }
            catch (FormatException) { return null; }
            return TryExtractSalt(decoded, out salt) ? salt : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool TryExtractSalt(string json, out string? salt)
    {
        salt = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                string value = property.Value.GetString() ?? "";
                if (property.Name.Equals("salt", StringComparison.OrdinalIgnoreCase) && value.Length is > 0 and <= 256)
                {
                    salt = value;
                    return true;
                }
            }
        }
        catch (JsonException) { }
        return false;
    }

    /// <summary>扫描首层及至多 8 个直接子目录，各至多 32 文件、每文件至多 256 KiB；候选仍需库头验证。</summary>
    public static IReadOnlyList<string> UidCandidates(string? logDir)
    {
        if (logDir is null || !Directory.Exists(logDir)) return Array.Empty<string>();
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            var directories = new[] { logDir }.Concat(Directory.EnumerateDirectories(logDir).Take(8)
                .Where(dir => (File.GetAttributes(dir) & FileAttributes.ReparsePoint) == 0));
            foreach (string file in directories.SelectMany(dir => Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).Take(32)))
            {
                string name = Path.GetFileName(file);
                if (!name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    foreach (string line in Encoding.UTF8.GetString(ReadSharedHead(file, 262144)).Split('\n'))
                    {
                        foreach (Match match in RealUidPattern().Matches(line)) Add(votes, match.Groups[1].Value);
                        foreach (Match match in UidPattern().Matches(line)) Add(votes, match.Groups[1].Value);
                    }
                }
                catch (IOException) { /* 单个日志被占用时跳过。 */ }
                catch (UnauthorizedAccessException) { /* 同上。 */ }
            }
        }
        catch (IOException) { return Array.Empty<string>(); }
        catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
        return votes.OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key).ToArray();
    }

    private static void Add(Dictionary<string, int> votes, string uid)
        => votes[uid] = votes.TryGetValue(uid, out int count) ? count + 1 : 1;

    /// <summary>以共享读方式读取文件首 length 字节（钉钉运行中也可读）。</summary>
    public static byte[] ReadSharedHead(string path, int length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int chunk = stream.Read(buffer, read, length - read);
            if (chunk == 0) break;
            read += chunk;
        }
        return read == length ? buffer : buffer.AsSpan(0, read).ToArray();
    }
}
