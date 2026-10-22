using System.Reflection;
using System.Runtime.InteropServices;

namespace ToolsBox.Core.Attendance;

/// <summary>Explicitly bootstraps SQLite in a short-lived attendance reader process.</summary>
public static class AttendanceSqliteRuntime
{
    // Static initializers do not touch the filesystem, native loader, or SQLite provider.
    private static readonly object InitializationGate = new();
    private static IntPtr _nativeHandle;
    private static bool _providerResolverRegistered;
    private static bool _coreResolverRegistered;
    private static bool _initialized;

    public static void EnsureInitialized()
    {
        if (!OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Attendance SQLite requires a Windows x64 reader.");

        lock (InitializationGate)
        {
            if (_initialized) return;
            string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData)) throw new IOException("The local native cache directory is unavailable.");
            var cache = new AttendanceSqliteNativeCache(Path.Combine(localData, "ToolsBox", "RuntimeNative", "SQLite"));
            string library = cache.EnsureLibrary();
            using AttendanceNativeDirectoryLease directoryLease = AttendanceNativeDirectoryLease.Acquire(Path.GetDirectoryName(library)!);
            using FileStream verifiedLibrary = cache.OpenVerifiedLibrary(library);
            if (_nativeHandle == IntPtr.Zero)
            {
                _nativeHandle = NativeLibrary.Load(library, typeof(AttendanceSqliteRuntime).Assembly,
                    DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            }

            // The net6.0 SQLitePCLRaw provider uses direct DllImport. Install both resolvers before
            // Batteries_V2.Init (including SqliteConnection's later static initialization) can run.
            if (!_providerResolverRegistered)
            {
                NativeLibrary.SetDllImportResolver(typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly, ResolveLibrary);
                _providerResolverRegistered = true;
            }
            if (!_coreResolverRegistered)
            {
                NativeLibrary.SetDllImportResolver(typeof(AttendanceSqliteRuntime).Assembly, ResolveLibrary);
                _coreResolverRegistered = true;
            }

            SQLitePCL.Batteries_V2.Init();
            _initialized = true;
            // The native handle and the registered resolvers remain alive for this process lifetime.
            // Failed attempts never set _initialized, so an I/O or loader fault can be retried.
        }
    }

    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
        => string.Equals(libraryName, "e_sqlite3", StringComparison.Ordinal) ? _nativeHandle : IntPtr.Zero;
}
