using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using SharpSevenZip;

namespace ToolsBox.Windows.ArchiveRecovery;

/// <summary>Materializes the pinned x64 engine. All handles remain open for its process lifetime.</summary>
public sealed class EmbeddedArchiveEngine
{
    internal const string ExpectedSha256 = "65E4C1F855F9EF6E8F0F5DF8E3F27D9EB5F07311408639DA0A1CA0B8F4871B0D";
    private static readonly Lazy<EmbeddedArchiveEngine> Instance = new(() => new EmbeddedArchiveEngine());
    private readonly List<SafeFileHandle> directories = new();
    private readonly FileStream engineFile;

    internal static void EnsureLoaded() => _ = Instance.Value;

    public static string ReadLicenseNotices()
    {
        var assembly = typeof(EmbeddedArchiveEngine).Assembly;
        return string.Join(Environment.NewLine + Environment.NewLine, assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith("-LICENSE.txt", StringComparison.Ordinal) || name.EndsWith("ArchiveEngine-NOTICE.md", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }));
    }

    private EmbeddedArchiveEngine()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException();
        // Lock each ancestor before resolving its child. Open reparse points themselves and reject
        // them: checking attributes before following a path would leave a replacement race.
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var ancestors = new Stack<string>();
        for (DirectoryInfo? current = new(parent); current != null; current = current.Parent) ancestors.Push(current.FullName);
        try
        {
            while (ancestors.TryPop(out string? directory)) directories.Add(LockDirectory(directory));
            string cache = Path.Combine(parent, "ToolsBox-archive-26.03-" + ExpectedSha256[..16]);
            Directory.CreateDirectory(cache);
            directories.Add(LockDirectory(cache));
            string dll = Path.Combine(cache, "7z.dll");
            if (!File.Exists(dll))
            {
                string staging = Path.Combine(cache, "engine-" + Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (var resource = typeof(EmbeddedArchiveEngine).Assembly.GetManifestResourceStream("ToolsBox.ArchiveRecovery.7z.dll") ?? throw new InvalidDataException())
                    using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None)) resource.CopyTo(output);
                    try { File.Move(staging, dll, overwrite: false); }
                    catch (IOException) when (File.Exists(dll)) { /* Another worker published first; verify its file below. */ }
                }
                finally { if (File.Exists(staging)) File.Delete(staging); }
            }
            // Open the file itself without following a late symlink replacement, and keep it
            // non-writable/non-deletable throughout native loading and subsequent calls.
            var fileHandle = CreateFile(dll, 0x80000000, FileShare.Read, IntPtr.Zero, FileMode.Open, 0x00200000, IntPtr.Zero);
            if (fileHandle.IsInvalid) { fileHandle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
            if (!GetFileInformationByHandle(fileHandle, out var fileInfo) || (fileInfo.Attributes & (0x400 | 0x10)) != 0)
            { fileHandle.Dispose(); throw new IOException("引擎文件不是普通文件。"); }
            engineFile = new FileStream(fileHandle, FileAccess.Read);
            if (!Convert.ToHexString(SHA256.HashData(engineFile)).Equals(ExpectedSha256, StringComparison.Ordinal))
                throw new InvalidDataException("引擎完整性检查失败。");
            SharpSevenZipBase.SetLibraryPath(dll);
        }
        catch
        {
            engineFile?.Dispose();
            foreach (var directory in directories) directory.Dispose();
            throw;
        }
    }

    private static SafeFileHandle LockDirectory(string path)
    {
        var handle = CreateFile(path, 0x80, FileShare.Read, IntPtr.Zero, FileMode.Open, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new Win32Exception(Marshal.GetLastWin32Error()); }
        if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0 || (info.Attributes & 0x10) == 0)
        { handle.Dispose(); throw new IOException("引擎目录不是受支持的普通目录。"); }
        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, FileShare share, IntPtr security, FileMode creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
}
