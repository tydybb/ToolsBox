using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace ToolsBox.Core.Attendance;

/// <summary>Recovers the embedded, pinned SQLite library into a verified content-addressed cache.</summary>
public sealed class AttendanceSqliteNativeCache
{
    private const string ResourceName = "ToolsBox.Core.Attendance.Native.win-x64.e_sqlite3.dll";
    // SQLitePCLRaw.lib.e_sqlite3 2.1.13, runtimes/win-x64/native/e_sqlite3.dll.
    private const string TrustedHash = "B7385D722C83FB52142A00477A726723745916D22A555711EE89834C1111FB2E";
    private const int TrustedLength = 1_978_880;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private readonly string _cacheRoot;

    public AttendanceSqliteNativeCache(string cacheRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        if (!Path.IsPathFullyQualified(cacheRoot)) throw new ArgumentException("An absolute cache directory is required.", nameof(cacheRoot));
        _cacheRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cacheRoot));
    }

    public string EnsureLibrary()
    {
        byte[] resource = ReadTrustedResource();
        string contentDirectory = Path.Combine(_cacheRoot, TrustedHash);
        using AttendanceNativeDirectoryLease directoryLease = AttendanceNativeDirectoryLease.Acquire(contentDirectory);

        string library = Path.Combine(contentDirectory, "e_sqlite3.dll");
        using FileStream coordination = AcquireLock(Path.Combine(contentDirectory, ".extract.lock"));
        RejectReparseComponents(library);
        if (IsTrustedLibrary(library)) return library;

        string pending = Path.Combine(contentDirectory, ".e_sqlite3-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            RejectReparseComponents(pending);
            using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                output.Write(resource);
                output.Flush(flushToDisk: true);
                output.Position = 0;
                VerifyContent(output);
            }
            RejectReparseComponents(library);
            RejectReparseComponents(pending);
            File.Move(pending, library, overwrite: true);
            using FileStream verified = OpenVerifiedLibrary(library);
            return library;
        }
        finally
        {
            // The temporary filename is unique to this attempt, inside its verified hash directory.
            RejectReparseComponents(pending);
            if (File.Exists(pending)) File.Delete(pending);
        }
    }

    // Keeping this handle open prevents another process from replacing the DLL between verification and load.
    internal FileStream OpenVerifiedLibrary(string library)
    {
        string expected = Path.Combine(_cacheRoot, TrustedHash, "e_sqlite3.dll");
        if (!string.Equals(library, expected, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The native library is outside the verified cache.");
        RejectReparseComponents(library);
        FileStream input = CreateOwnedStream(AttendanceNativeDirectoryLease.OpenRegularFile(library), FileAccess.Read);
        try
        {
            VerifyContent(input);
            RejectReparseComponents(library);
            return input;
        }
        catch
        {
            input.Dispose();
            throw;
        }
    }

    private static byte[] ReadTrustedResource()
    {
        using Stream resource = typeof(AttendanceSqliteNativeCache).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("The embedded SQLite library is missing.");
        var bytes = new byte[TrustedLength];
        resource.ReadExactly(bytes);
        if (resource.ReadByte() != -1 || !HasTrustedHash(SHA256.HashData(bytes)))
            throw new InvalidDataException("The embedded SQLite library failed verification.");
        return bytes;
    }

    private static bool IsTrustedLibrary(string library)
    {
        try
        {
            using FileStream input = CreateOwnedStream(AttendanceNativeDirectoryLease.OpenRegularFile(library), FileAccess.Read);
            return input.Length == TrustedLength && HasTrustedHash(SHA256.HashData(input));
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void VerifyContent(Stream content)
    {
        if (content.Length != TrustedLength || !HasTrustedHash(SHA256.HashData(content)))
            throw new InvalidDataException("The cached SQLite library failed verification.");
    }

    private static bool HasTrustedHash(byte[] hash)
        => string.Equals(Convert.ToHexString(hash), TrustedHash, StringComparison.Ordinal);

    private static FileStream AcquireLock(string path)
    {
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            RejectReparseComponents(path);
            try { return CreateOwnedStream(AttendanceNativeDirectoryLease.OpenCoordinationFile(path), FileAccess.ReadWrite); }
            catch (IOException error)
            {
                if (elapsed.Elapsed >= LockTimeout)
                    throw new IOException("Timed out coordinating the SQLite native cache.", error);
                Thread.Sleep(25);
            }
        }
    }

    private static FileStream CreateOwnedStream(SafeFileHandle handle, FileAccess access)
    {
        try { return new FileStream(handle, access); }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static void RejectReparseComponents(string path)
    {
        string? component = Path.GetFullPath(path);
        while (component is not null)
        {
            try
            {
                if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Reparse points are not supported in the native cache path.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            component = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(component));
        }
    }
}
