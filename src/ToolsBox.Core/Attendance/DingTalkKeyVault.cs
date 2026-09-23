using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ToolsBox.Core.Attendance;

/// <summary>
/// 钉钉 v3 本机数据密钥链候选；仅支持能够通过本地库头校验的版本：
/// PBKDF2-HMAC-SHA1(ASCII(uid+salt), ASCII"666DingT", 1000 次, 32 字节) → MD5 十六进制前 16 个字符的 ASCII → AES-128-ECB；
/// 预言机：以该密钥加密 "SQLite format 3\0"，结果应等于密文库头 16 字节。
/// salt 来自账号目录 user_config（base64→JSON["salt"]）；uid 候选先取 user_config 白名单字段（不依赖日志），再由 log 目录正则投票（real_uid / uid）补充。
/// </summary>
public static partial class DingTalkKeyVault
{
    private static readonly byte[] KeySalt = Encoding.ASCII.GetBytes("666DingT");

    /// <summary>user_config 中视为登录 uid 的字段白名单（大小写不敏感；形态校验见 IsUidShaped）。</summary>
    private static readonly string[] UidFieldNames =
        ["uid", "realUid", "real_uid", "userId", "user_id", "memberId", "member_id", "empId", "staffId", "accountId", "account_id", "loginUid"];

    /// <summary>配置侧最多收集的候选数（白名单字段优先，纯数字值兜底；猜错由预言机淘汰，只多几次试算）。</summary>
    private const int MaxConfigUidCandidates = 32;

    /// <summary>日志 uid 字段名与位数按“版本未知的其他机器”放宽：字段名前加后视界防 guid 误命中，位数 4~24。</summary>
    [GeneratedRegex(@"(?<![A-Za-z0-9_])(?:real_?uid|user_?id|member_?id|emp_?id|staff_?id|account_?id|uid)[""']?\s*[:=]\s*[""']?(\d{4,24})", RegexOptions.IgnoreCase)]
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

    /// <summary>账号级 uid 候选：user_config → 账号目录名 → 日志投票；顺序去重，候选仍需库头验证。</summary>
    public static IReadOnlyList<string> UidCandidates(DingTalkAccountPaths paths)
    {
        var combined = new List<string>(UidCandidatesFromConfig(paths.UserConfigPath));
        foreach (string uid in DirNameCandidates(paths.AccountDir))
            if (!combined.Contains(uid)) combined.Add(uid);
        foreach (string uid in UidCandidates(paths.LogDir))
            if (!combined.Contains(uid)) combined.Add(uid);
        return combined;
    }

    /// <summary>账号目录名若为 {uid}_v3 形态则直接贡献候选；散列名不会通过预言机，仅多一次试算。</summary>
    private static IEnumerable<string> DirNameCandidates(string accountDir)
    {
        string name = Path.GetFileName(accountDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (name.EndsWith("_v3", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
        name = name.Trim('_', '-', '.');
        if (name.Length is >= 4 and <= 32) yield return name;
    }

    /// <summary>读取 user_config（与 salt 同一文件、同样有界、支持明文或 base64 JSON）中的 uid 类白名单字段。</summary>
    public static IReadOnlyList<string> UidCandidatesFromConfig(string? userConfigPath)
    {
        if (userConfigPath is null) return Array.Empty<string>();
        var found = new List<string>();
        try
        {
            byte[] bytes = ReadSharedHead(userConfigPath, 65537);
            if (bytes.Length > 65536) return Array.Empty<string>();
            string raw = Encoding.UTF8.GetString(bytes).Trim();
            if (raw.Length == 0) return Array.Empty<string>();
            CollectUids(raw, found);
            if (found.Count > 0) return found;
            try { CollectUids(Encoding.UTF8.GetString(Convert.FromBase64String(raw)), found); }
            catch (FormatException) { /* 非 base64 包裹时按无候选处理。 */ }
        }
        catch (IOException) { /* 文件被占用时按无候选处理；日志投票仍可兜底。 */ }
        catch (UnauthorizedAccessException) { /* 同上。 */ }
        return found;
    }

    private static void CollectUids(string json, List<string> found)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            // 两遍：先白名单字段（任意形态、含嵌套），再任意字段的纯数字值（防字段名随版本漂移）。
            WalkForUids(document.RootElement, found, whitelistOnly: true, depth: 0);
            WalkForUids(document.RootElement, found, whitelistOnly: false, depth: 0);
        }
        catch (JsonException) { /* 配置非 JSON 时按无候选处理。 */ }
    }

    private static void WalkForUids(JsonElement element, List<string> found, bool whitelistOnly, int depth)
    {
        if (depth > 6 || found.Count >= MaxConfigUidCandidates) return;
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    bool named = UidFieldNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase);
                    string value = (property.Value.ValueKind switch
                    {
                        JsonValueKind.String => property.Value.GetString() ?? "",
                        JsonValueKind.Number => property.Value.GetRawText(),
                        _ => ""
                    }).Trim();
                    bool acceptable = whitelistOnly ? named && IsUidShaped(value, allowAlnum: true)
                                                    : IsUidShaped(value, allowAlnum: false);
                    if (acceptable && !found.Contains(value)) found.Add(value);
                    WalkForUids(property.Value, found, whitelistOnly, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    WalkForUids(item, found, whitelistOnly, depth + 1);
                break;
        }
    }

    /// <summary>uid 形态：4~32 位；白名单字段允许字母与 . _ -（防非数字 uid），兜底仅允许纯数字（排掉时间戳外的噪声由预言机把关）。</summary>
    private static bool IsUidShaped(string value, bool allowAlnum)
    {
        if (value.Length is < 4 or > 32) return false;
        foreach (char c in value)
        {
            if (char.IsAsciiDigit(c)) continue;
            if (allowAlnum && (char.IsAsciiLetter(c) || c is '.' or '-' or '_')) continue;
            return false;
        }
        return true;
    }

    /// <summary>有界递归扫描日志目录：至多 4 层、总计 256 个文件、每文件至多 256 KiB、不限扩展名（旋转/无扩展名也算）；候选仍需库头验证。</summary>
    public static IReadOnlyList<string> UidCandidates(string? logDir)
    {
        if (logDir is null || !Directory.Exists(logDir)) return Array.Empty<string>();
        var votes = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            foreach (string file in EnumerateLogFiles(logDir))
            {
                try
                {
                    foreach (string line in Encoding.UTF8.GetString(ReadSharedHead(file, 262144)).Split('\n'))
                        foreach (Match match in UidPattern().Matches(line)) Add(votes, match.Groups[1].Value);
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

    private static IEnumerable<string> EnumerateLogFiles(string root)
    {
        const int MaxDepth = 4;
        const int MaxFiles = 256;
        const int MaxFileBytes = 262144;
        int budget = MaxFiles;
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0 && budget > 0)
        {
            (string directory, int depth) = pending.Pop();
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (new FileInfo(file).Length > MaxFileBytes) continue;
                if (budget-- <= 0) yield break;
                yield return file;
            }
            if (depth >= MaxDepth) continue;
            foreach (string child in Directory.EnumerateDirectories(directory)) pending.Push((child, depth + 1));
        }
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
