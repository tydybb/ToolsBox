using Microsoft.Data.Sqlite;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ToolsBox.Core.Attendance;

/// <summary>Optional version-limited local card reader; no plaintext files or server-authoritative claims.</summary>
public sealed class DingTalkAttendanceChecker : IAttendanceChecker
{
    private readonly DingTalkDataLocator _locator;
    // Retained for source compatibility; no temporary directory is read, created or cleaned.
    public DingTalkAttendanceChecker(DingTalkDataLocator? locator = null, string? tempDirectory = null)
        => _locator = locator ?? new DingTalkDataLocator();

    public AttendanceStatus Check(DateTime now)
    {
        byte[]? key = null;
        byte[]? plain = null;
        try
        {
            var located = _locator.Locate();
            if (located.Paths is not { } paths) return AttendanceStatus.Failure(located.Error ?? "未找到数据目录。");
            string? salt = DingTalkKeyVault.ReadSalt(paths.UserConfigPath);
            if (salt is null) return AttendanceStatus.Failure("无法读取受支持的钉钉账号配置。");
            // 三种失败分开提示：无日志目录（未生成/被清理/布局不同）、有日志但没认出 uid、认得出但解不开（版本不受支持）。
            IReadOnlyList<string> uids = DingTalkKeyVault.UidCandidates(paths);
            if (uids.Count is 0)
                return AttendanceStatus.Failure(paths.LogDir is null || !Directory.Exists(paths.LogDir)
                    ? "未能识别登录账号：未找到本机钉钉日志目录，无法取得 uid。"
                    : "未能识别登录账号：账号配置与本机日志中均未找到 uid，日志可能已被清理。");
            key = DingTalkKeyVault.FindVerifiedKey(uids, salt, DingTalkKeyVault.ReadSharedHead(paths.DbPath, 16));
            if (key is null) return AttendanceStatus.Failure("识别到候选账号，但密钥与本机数据不匹配；钉钉版本可能不受支持。");
            plain = DingTalkDbDecryptor.DecryptToMemory(key, paths.DbPath, paths.WalPath);
            return QueryToday(plain, now);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or SqliteException or ArgumentException or InvalidOperationException or NotSupportedException or CryptographicException or System.Security.SecurityException or DllNotFoundException or EntryPointNotFoundException)
        {
            return AttendanceStatus.Failure("本地读取未完成：数据正在使用、格式不受支持或文件不完整；请在钉钉核对。");
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
            if (plain is not null) CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static AttendanceStatus QueryToday(byte[] plain, DateTime now)
    {
        // The stable snapshot includes all checksum-validated committed WAL pages; open it without WAL-mode headers.
        plain[18] = plain[19] = 1;
        var pinned = GCHandle.Alloc(plain, GCHandleType.Pinned);
        try
        {
            using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False");
            connection.Open();
            if (Deserialize(connection.Handle!.DangerousGetHandle(), "main", pinned.AddrOfPinnedObject(), plain.Length, plain.Length, 4) != 0)
                throw new InvalidDataException();
            using (var check = connection.CreateCommand())
            {
                check.CommandText = "PRAGMA query_only=ON; PRAGMA temp_store=MEMORY; PRAGMA quick_check(1)";
                if (!string.Equals(check.ExecuteScalar() as string, "ok", StringComparison.Ordinal)) throw new InvalidDataException();
            }
            var names = new List<string>();
            using (var tables = connection.CreateCommand())
            {
                tables.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name GLOB 'tbmsg*' LIMIT 1025";
                using var reader = tables.ExecuteReader();
                while (reader.Read()) names.Add(reader.GetString(0));
            }
            if (names.Count is 0 or > 1024) throw new InvalidDataException();
            long low = new DateTimeOffset(now.Date).ToUnixTimeSeconds(), high = new DateTimeOffset(now).ToUnixTimeSeconds();
            var messages = new List<(string? Content, DateTime ReceivedAt)>();
            foreach (var name in names)
            {
                using var query = connection.CreateCommand();
                query.CommandText = $"SELECT CASE WHEN length(content)<=65536 THEN content ELSE NULL END, createdAt FROM \"{name.Replace("\"", "\"\"")}\" WHERE contentType=2950 AND ((createdAt BETWEEN $low AND $high) OR (createdAt BETWEEN $lowMs AND $highMs)) LIMIT 10001";
                query.Parameters.AddWithValue("$low", low); query.Parameters.AddWithValue("$high", high);
                query.Parameters.AddWithValue("$lowMs", low * 1000); query.Parameters.AddWithValue("$highMs", new DateTimeOffset(now).ToUnixTimeMilliseconds());
                using var reader = query.ExecuteReader();
                while (reader.Read())
                {
                    long stamp = reader.GetInt64(1);
                    var received = stamp > 100_000_000_000L ? DateTimeOffset.FromUnixTimeMilliseconds(stamp).LocalDateTime : DateTimeOffset.FromUnixTimeSeconds(stamp).LocalDateTime;
                    if (received.Date == now.Date && received <= now) messages.Add((reader.IsDBNull(0) ? null : reader.GetString(0), received));
                    if (messages.Count > 10000) throw new InvalidDataException();
                }
            }
            return AttendanceStatus.FromRecords(messages.Count, AttendanceMessageParser.ParseAll(messages), now);
        }
        finally { pinned.Free(); }
    }

    [DllImport("e_sqlite3", EntryPoint = "sqlite3_deserialize", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Deserialize(IntPtr db, [MarshalAs(UnmanagedType.LPUTF8Str)] string schema, IntPtr buffer, long length, long capacity, uint flags);
}
