using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace ToolsBox.MediaDownloads;
internal sealed class CookieFile : IDisposable
{
    public string Path { get; }
    private readonly string _directory;
    private CookieFile(string directory) { _directory = directory; Path = System.IO.Path.Combine(directory, "cookies.txt"); }
    public static CookieFile? Create(IReadOnlyList<MediaCookie>? cookies)
    {
        if (cookies is null || cookies.Count == 0) return null;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("登录态下载仅支持 Windows。");
        var text = new StringBuilder("# Netscape HTTP Cookie File\n");
        foreach (var c in cookies)
        {
            if (new[] { c.Name, c.Value, c.Domain, c.Path }.Any(s => s.Any(char.IsControl)) ||
                (string.IsNullOrEmpty(c.Name) && string.IsNullOrEmpty(c.Value)) ||
                !c.Path.StartsWith('/') ||
                Uri.CheckHostName(c.Domain.TrimStart('.')) == UriHostNameType.Unknown)
                throw new ArgumentException("Cookie 格式无效。");
            // Chromium allows nameless cookies. Netscape's empty name field preserves the
            // bare value for yt-dlp/MozillaCookieJar; do not rename it or add an '=' prefix.
            string domain = c.HostOnly ? c.Domain.TrimStart('.') : "." + c.Domain.TrimStart('.');
            text.Append(domain).Append('\t').Append(c.HostOnly ? "FALSE" : "TRUE").Append('\t')
                .Append(c.Path).Append('\t').Append(c.Secure ? "TRUE" : "FALSE").Append('\t')
                .Append(c.Expires?.ToUnixTimeSeconds() ?? 0).Append('\t').Append(c.Name).Append('\t').Append(c.Value).Append('\n');
        }
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ToolsBox-cookies-" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(true, false);
        using var identity = WindowsIdentity.GetCurrent();
        security.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(directory).Create(security);
        var file = new CookieFile(directory);
        try { File.WriteAllText(file.Path, text.ToString(), new UTF8Encoding(false)); return file; }
        catch { file.Dispose(); throw; }
    }
    public void Dispose()
    {
        if (File.Exists(Path)) File.Delete(Path);
        if (Directory.Exists(_directory)) Directory.Delete(_directory, false);
    }
}
