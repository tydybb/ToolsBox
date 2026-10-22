using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using ToolsBox.Core.Attendance;

namespace ToolsBox.Core.Tests.Attendance;

public sealed class AttendanceSqliteRuntimeTests
{
    private const string TrustedHash = "B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E";
    private const string ResourceName = "ToolsBox.Core.Attendance.Native.win-x64.e_sqlite3.dll";
    private static readonly Assembly CoreAssembly = typeof(DingTalkAttendanceChecker).Assembly;

    [Fact]
    public void EmbeddedLibraryIsTheTrustedWindowsX64PackageContent()
    {
        using Stream? resource = CoreAssembly.GetManifestResourceStream(ResourceName);

        Assert.NotNull(resource);
        Assert.Equal(1_978_880, resource.Length);
        Assert.Equal(TrustedHash, Convert.ToHexString(SHA256.HashData(resource)));
    }

    [Fact]
    public void RuntimeExposesExplicitInitializationBeforeTheSqliteConnectionIsUsed()
    {
        Type? runtime = CoreAssembly.GetType("ToolsBox.Core.Attendance.AttendanceSqliteRuntime");

        Assert.NotNull(runtime);
        MethodInfo? initialize = runtime.GetMethod("EnsureInitialized", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(initialize);
        Assert.Empty(initialize.GetParameters());
        Assert.Equal(typeof(void), initialize.ReturnType);
    }

    [Fact]
    public void MissingLibraryIsRecoveredToTheContentHashDirectory()
    {
        using var directory = new TestDirectory();
        string cacheRoot = Path.Combine(directory.Root, "cache");
        object cache = CreateCache(cacheRoot);

        string library = EnsureLibrary(cache);

        Assert.Equal(Path.Combine(cacheRoot, TrustedHash, "e_sqlite3.dll"), library);
        Assert.True(Path.IsPathFullyQualified(library));
        AssertTrustedLibrary(library);
    }

    [Fact]
    public void CorruptLibraryIsReplacedWithTrustedContent()
    {
        using var directory = new TestDirectory();
        string cacheRoot = Path.Combine(directory.Root, "cache");
        string library = Path.Combine(cacheRoot, TrustedHash, "e_sqlite3.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        File.WriteAllText(library, "incomplete native library");
        object cache = CreateCache(cacheRoot);

        Assert.Equal(library, EnsureLibrary(cache));

        AssertTrustedLibrary(library);
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(library)!),
            path => path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VerifiedLibraryIsReusedWithoutRewritingIt()
    {
        using var directory = new TestDirectory();
        object cache = CreateCache(Path.Combine(directory.Root, "cache"));
        string library = EnsureLibrary(cache);
        DateTime originalTimestamp = new(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(library, originalTimestamp);

        Assert.Equal(library, EnsureLibrary(cache));

        AssertTrustedLibrary(library);
        Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(library));
    }

    [Fact]
    public async Task ConcurrentCacheInstancesReturnOnlyCompleteTrustedContent()
    {
        using var directory = new TestDirectory();
        string cacheRoot = Path.Combine(directory.Root, "cache");
        object[] caches = Enumerable.Range(0, 16).Select(_ => CreateCache(cacheRoot)).ToArray();
        using var start = new ManualResetEventSlim();
        Task<string>[] tasks = caches.Select(cache => Task.Run(() =>
        {
            start.Wait();
            string library = EnsureLibrary(cache);
            AssertTrustedLibrary(library);
            return library;
        })).ToArray();

        start.Set();
        string[] libraries = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Single(libraries.Distinct(StringComparer.OrdinalIgnoreCase));
        AssertTrustedLibrary(libraries[0]);
    }

    [Fact]
    public void CacheCanRetryAfterAnIoFailureWithoutRecreatingTheInstance()
    {
        using var directory = new TestDirectory();
        string cacheRoot = Path.Combine(directory.Root, "cache");
        File.WriteAllText(cacheRoot, "a file blocks directory creation");
        object cache = CreateCache(cacheRoot);

        Assert.ThrowsAny<IOException>(() => EnsureLibrary(cache));
        File.Delete(cacheRoot);

        string library = EnsureLibrary(cache);

        AssertTrustedLibrary(library);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifiedLibraryLeasePreventsAncestorRenameUntilItIsDisposed(bool renameParent)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        string cacheParent = Path.Combine(directory.Root, "cache-parent");
        string cacheRoot = Path.Combine(cacheParent, "cache");
        object cache = CreateCache(cacheRoot);
        string library = EnsureLibrary(cache);
        string original = renameParent ? cacheParent : cacheRoot;
        string relocated = Path.Combine(directory.Root, "relocated");

        using (FileStream verified = OpenVerifiedLibrary(cache, library))
        {
            Assert.ThrowsAny<IOException>(() => Directory.Move(original, relocated));
            Assert.True(File.Exists(library));
        }

        Directory.Move(original, relocated);
        string relocatedLibrary = renameParent
            ? Path.Combine(relocated, "cache", TrustedHash, "e_sqlite3.dll")
            : Path.Combine(relocated, TrustedHash, "e_sqlite3.dll");
        AssertTrustedLibrary(relocatedLibrary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryLeasePinsEmptyExtractionDirectoryAndAncestorsUntilDisposed(bool renameParent)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        string cacheParent = Path.Combine(directory.Root, "cache-parent");
        string cacheRoot = Path.Combine(cacheParent, "cache");
        string contentDirectory = Path.Combine(cacheRoot, TrustedHash);
        string original = renameParent ? cacheParent : cacheRoot;
        string relocated = Path.Combine(directory.Root, "relocated");

        using (IDisposable lease = AcquireDirectoryLease(contentDirectory))
        {
            Assert.True(Directory.Exists(contentDirectory));
            Assert.Empty(Directory.EnumerateFileSystemEntries(contentDirectory));
            Assert.ThrowsAny<IOException>(() => Directory.Move(original, relocated));
        }

        Directory.Move(original, relocated);
        Assert.True(Directory.Exists(renameParent
            ? Path.Combine(relocated, "cache", TrustedHash)
            : Path.Combine(relocated, TrustedHash)));
    }

    [Fact]
    public void RegularFileHandleReadsTrustedLibraryAndBlocksRemovalUntilDisposed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        object cache = CreateCache(Path.Combine(directory.Root, "cache"));
        string library = EnsureLibrary(cache);

        using (SafeFileHandle handle = OpenRegularFile(library))
        using (var content = new FileStream(handle, FileAccess.Read))
        {
            Assert.Equal(TrustedHash, Convert.ToHexString(SHA256.HashData(content)));
            Assert.ThrowsAny<IOException>(() => File.Delete(library));
        }

        File.Delete(library);
        Assert.False(File.Exists(library));
    }

    [Fact]
    public void RegularFileHandleRejectsAJunctionAtTheLibraryLeaf()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Root, "outside-cache");
        Directory.CreateDirectory(target);
        string junction = Path.Combine(directory.Root, "e_sqlite3.dll");
        CreateJunction(junction, target);
        try
        {
            Assert.ThrowsAny<IOException>(() => OpenRegularFile(junction));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void CoordinationFileHandleCreatesAnEmptyFileAndExcludesOtherOpenersUntilDisposed()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        string coordination = Path.Combine(directory.Root, ".extract.lock");

        using (SafeFileHandle handle = OpenCoordinationFile(coordination))
        using (var content = new FileStream(handle, FileAccess.ReadWrite))
        {
            Assert.Equal(0, content.Length);
            Assert.ThrowsAny<IOException>(() => OpenCoordinationFile(coordination));
        }

        using SafeFileHandle reopened = OpenCoordinationFile(coordination);
        Assert.False(reopened.IsInvalid);
    }

    [Fact]
    public void CoordinationFileHandleRejectsAJunctionWithoutWritingToItsTarget()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Root, "outside-cache");
        Directory.CreateDirectory(target);
        string junction = Path.Combine(directory.Root, ".extract.lock");
        CreateJunction(junction, target);
        try
        {
            Assert.ThrowsAny<IOException>(() => OpenCoordinationFile(junction));
            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Fact]
    public void RuntimePinsTheDirectoryBeforeOpeningAndLoadingTheVerifiedLibrary()
    {
        string source = ReadRuntimeSource();
        int directoryLease = source.IndexOf("AttendanceNativeDirectoryLease.Acquire(", StringComparison.Ordinal);
        int verify = source.IndexOf("cache.OpenVerifiedLibrary(", StringComparison.Ordinal);
        int load = source.IndexOf("NativeLibrary.Load(", StringComparison.Ordinal);

        Assert.True(directoryLease >= 0, "The runtime must hold a directory lease across verified-file opening and native loading.");
        Assert.True(directoryLease < verify && verify < load);
        Assert.Contains("using AttendanceNativeDirectoryLease", source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReparseDirectoryInTheCachePathIsRejectedWithoutWritingToItsTarget(bool hashDirectory)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var directory = new TestDirectory();
        string target = Path.Combine(directory.Root, "outside-cache");
        Directory.CreateDirectory(target);
        string cacheRoot = Path.Combine(directory.Root, "cache");
        string junction = hashDirectory ? Path.Combine(cacheRoot, TrustedHash) : cacheRoot;
        if (hashDirectory) Directory.CreateDirectory(cacheRoot);
        CreateJunction(junction, target);
        try
        {
            object cache = CreateCache(cacheRoot);

            Assert.ThrowsAny<IOException>(() => EnsureLibrary(cache));

            Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    // Reflection keeps the initial RED run buildable while the new public responsibilities are absent.
    private static object CreateCache(string cacheRoot)
    {
        Type? cacheType = CoreAssembly.GetType("ToolsBox.Core.Attendance.AttendanceSqliteNativeCache");
        Assert.NotNull(cacheType);
        ConstructorInfo? constructor = cacheType.GetConstructor(new[] { typeof(string) });
        Assert.NotNull(constructor);
        return Invoke(() => constructor.Invoke(new object[] { cacheRoot }));
    }

    private static string EnsureLibrary(object cache)
    {
        MethodInfo? ensure = cache.GetType().GetMethod("EnsureLibrary", BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(ensure);
        Assert.Empty(ensure.GetParameters());
        return Assert.IsType<string>(Invoke(() => ensure.Invoke(cache, null)!));
    }

    private static FileStream OpenVerifiedLibrary(object cache, string library)
    {
        MethodInfo? open = cache.GetType().GetMethod("OpenVerifiedLibrary", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(open);
        return Assert.IsAssignableFrom<FileStream>(Invoke(() => open.Invoke(cache, new object[] { library })!));
    }

    private static IDisposable AcquireDirectoryLease(string directory)
    {
        Type? leaseType = CoreAssembly.GetType("ToolsBox.Core.Attendance.AttendanceNativeDirectoryLease");
        Assert.NotNull(leaseType);
        MethodInfo? acquire = leaseType.GetMethod("Acquire", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(acquire);
        return Assert.IsAssignableFrom<IDisposable>(Invoke(() => acquire.Invoke(null, new object[] { directory })!));
    }

    private static SafeFileHandle OpenRegularFile(string path)
    {
        Type? leaseType = CoreAssembly.GetType("ToolsBox.Core.Attendance.AttendanceNativeDirectoryLease");
        Assert.NotNull(leaseType);
        MethodInfo? open = leaseType.GetMethod("OpenRegularFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(open);
        return Assert.IsType<SafeFileHandle>(Invoke(() => open.Invoke(null, new object[] { path })!));
    }

    private static SafeFileHandle OpenCoordinationFile(string path)
    {
        Type? leaseType = CoreAssembly.GetType("ToolsBox.Core.Attendance.AttendanceNativeDirectoryLease");
        Assert.NotNull(leaseType);
        MethodInfo? open = leaseType.GetMethod("OpenCoordinationFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(open);
        return Assert.IsType<SafeFileHandle>(Invoke(() => open.Invoke(null, new object[] { path })!));
    }

    private static string ReadRuntimeSource()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string source = Path.Combine(directory.FullName, "src", "ToolsBox.Core", "Attendance", "AttendanceSqliteRuntime.cs");
            if (File.Exists(source)) return File.ReadAllText(source);
        }
        throw new InvalidOperationException("Could not locate the runtime source for the loading-order contract.");
    }

    private static object Invoke(Func<object> action)
    {
        try { return action(); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static void AssertTrustedLibrary(string library)
    {
        using var content = File.OpenRead(library);
        Assert.Equal(1_978_880, content.Length);
        Assert.Equal(TrustedHash, Convert.ToHexString(SHA256.HashData(content)));
    }

    private static void CreateJunction(string junction, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.Arguments = $"/d /c mklink /J \"{junction}\" \"{target}\"";
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "Timed out creating the isolated test junction.");
        Assert.True(process.ExitCode == 0, $"Could not create isolated test junction: {output} {error}");
        Assert.True((File.GetAttributes(junction) & FileAttributes.ReparsePoint) != 0);
    }

    private sealed class TestDirectory : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "ToolsBoxAttendanceNativeTests-" + Guid.NewGuid().ToString("N"));
        public TestDirectory() => Directory.CreateDirectory(Root);
        public void Dispose()
        {
            string taskRoot = Path.GetFullPath(Root);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
            if (!string.Equals(Path.GetDirectoryName(taskRoot), tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(taskRoot).StartsWith("ToolsBoxAttendanceNativeTests-", StringComparison.Ordinal))
                throw new InvalidOperationException("Refusing to clean a path outside this test's temporary directory.");
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
