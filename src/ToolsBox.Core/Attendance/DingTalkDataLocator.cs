namespace ToolsBox.Core.Attendance;

/// <summary>钉钉本机数据的账号目录布局：账号目录、消息库、盐文件与日志目录。</summary>
public sealed record DingTalkAccountPaths(string AccountDir, string DbPath, string? WalPath, string UserConfigPath, string? LogDir);

/// <summary>定位结果：命中账号目录，或给出明确失败原因（如仅发现旧版 _v2 数据）。</summary>
public sealed record DingTalkLocateResult(DingTalkAccountPaths? Paths, string? Error)
{
    public static DingTalkLocateResult Found(DingTalkAccountPaths paths) => new(paths, null);
    public static DingTalkLocateResult Failed(string error) => new(null, error);
}

/// <summary>
/// 自动发现钉钉数据目录（软件可能装在任何人电脑上，路径不确定）：
/// 1) %APPDATA%\DingTalk 与 %APPDATA%\DingTalkLite 两个默认根；
/// 2) 根下的 redirectAppData.dat 重定向（文件内容为路径，或本身是目录）；
/// 3) 根（或重定向目标）下唯一的 *_v3 账号目录；多个账号必须由用户选择；
/// 4) 仅发现旧版 *_v2 时明确报错，不猜测。
/// </summary>
public sealed class DingTalkDataLocator
{
    private readonly IReadOnlyList<string> _roots;

    public DingTalkDataLocator(IEnumerable<string>? roots = null)
        => _roots = (roots?.Where(root => !string.IsNullOrWhiteSpace(root)).ToArray() ?? DefaultRoots()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>默认候选根：%APPDATA% 下的钉钉目录族（含重定向解析）。</summary>
    public static IReadOnlyList<string> DefaultRoots()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var roots = new List<string>();
        foreach (string name in new[] { "DingTalk", "DingTalkLite" })
        {
            string root = Path.Combine(appData, name);
            roots.Add(root);
            string? redirected = ResolveRedirect(root);
            if (redirected is not null) roots.Add(redirected);
        }
        return roots;
    }

    /// <summary>读取 %APPDATA%\DingTalk\redirectAppData.dat：文件内容为路径，或该条目本身是目录。</summary>
    public static string? ResolveRedirect(string root)
    {
        string redirect = Path.Combine(root, "redirectAppData.dat");
        try
        {
            if (Directory.Exists(redirect)) return redirect;
            if (!File.Exists(redirect)) return null;
            if (new FileInfo(redirect).Length > 8192) return null;
            byte[] bytes = File.ReadAllBytes(redirect);
            if (bytes.Length == 0) return null;
            // 内容可能是 UTF-16（含内嵌 NUL）或 UTF-8/ANSI 路径，统一按启发式解码并清理引号与空白。
            string text = bytes.Length >= 2 && bytes[1] == 0
                ? System.Text.Encoding.Unicode.GetString(bytes)
                : System.Text.Encoding.UTF8.GetString(bytes);
            text = text.Trim('\0', ' ', '\t', '\r', '\n', '"');
            return text.Length > 0 && Directory.Exists(text) ? text : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>在有界候选根下定位唯一账号；不能以最近写入时间推断当前账号。</summary>
    public DingTalkLocateResult Locate()
    {
        var candidates = new List<DingTalkAccountPaths>();
        bool sawLegacy = false;
        foreach (string root in _roots)
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                if (IsAccountDir(root, out DingTalkAccountPaths? self)) { candidates.Add(self!); continue; }
                string[] directories = Directory.EnumerateDirectories(root).Take(257).ToArray();
                if (directories.Length > 256) return DingTalkLocateResult.Failed("候选目录过多，请直接选择账号数据目录。");
                foreach (string dir in directories)
                {
                    string name = Path.GetFileName(dir);
                    if (name.EndsWith("_v2", StringComparison.OrdinalIgnoreCase)) sawLegacy = true;
                    if (name.EndsWith("_v3", StringComparison.OrdinalIgnoreCase) && IsAccountDir(dir, out DingTalkAccountPaths? paths))
                        candidates.Add(paths!);
                }
            }
            catch (IOException) { return DingTalkLocateResult.Failed("候选目录不可读，请直接选择账号数据目录。"); }
            catch (UnauthorizedAccessException) { return DingTalkLocateResult.Failed("候选目录无访问权限，请直接选择账号数据目录。"); }
        }
        candidates = candidates.DistinctBy(paths => Path.GetFullPath(paths.AccountDir), StringComparer.OrdinalIgnoreCase).ToList();
        if (candidates.Count > 1) return DingTalkLocateResult.Failed("发现多个钉钉账号，请明确选择账号数据目录。");
        if (candidates.Count == 1) return DingTalkLocateResult.Found(candidates[0]);
        if (sawLegacy)
            return DingTalkLocateResult.Failed("仅发现旧版钉钉数据目录（_v2），暂不支持解密，请升级钉钉后重试。");
        return DingTalkLocateResult.Failed("未找到钉钉数据目录，请先安装并登录钉钉电脑版。");
    }

    /// <summary>账号目录判定：目录本身（重定向可能直接指向账号目录）或其下存在 DBFiles\dingtalk.db。</summary>
    private static bool IsAccountDir(string dir, out DingTalkAccountPaths? paths)
    {
        string db = Path.Combine(dir, "DBFiles", "dingtalk.db");
        if (File.Exists(db))
        {
            string? parent = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
            paths = new DingTalkAccountPaths(dir, db, File.Exists(db + "-wal") ? db + "-wal" : null,
                Path.Combine(dir, "user_config"), parent is null ? null : Path.Combine(parent, "log"));
            return true;
        }
        paths = null;
        return false;
    }
}
