using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ToolsBox.Core.WebResources;

namespace ToolsBox.App.WebResources;

public sealed record BrowserAddress(string Url, string Title, DateTimeOffset VisitedAt);

public sealed class BrowserLibraryStore
{
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private readonly string _filePath;
    private readonly string _mutexName;
    private LibraryData _data = new([], []);

    public BrowserLibraryStore(string filePath)
    {
        _filePath = Path.GetFullPath(filePath);
        _mutexName = "Local\\ToolsBox.BrowserLibrary." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(_filePath.ToUpperInvariant())));
        WithFileLock(() => _data = Load());
    }

    private LibraryData Load()
    {
        try
        {
            using var file = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length > MaxFileBytes) throw new InvalidDataException("浏览器收藏和历史记录文件超过 8 MB。 ");
            var loaded = JsonSerializer.Deserialize<LibraryData>(file);
            if (loaded?.Favorites is null || loaded.History is null)
                throw new InvalidDataException("浏览器收藏和历史记录文件格式无效。");
            return new(Normalize(loaded.Favorites), Normalize(loaded.History)
                .OrderByDescending(item => item.VisitedAt).Take(500).ToList());
        }
        catch (FileNotFoundException) { return new([], []); }
        catch (DirectoryNotFoundException) { return new([], []); }
        catch (JsonException exception)
        {
            throw new InvalidDataException("无法读取浏览器收藏和历史记录文件。", exception);
        }
    }

    public IReadOnlyList<BrowserAddress> Favorites => _data.Favorites.AsReadOnly();
    public IReadOnlyList<BrowserAddress> History => _data.History.AsReadOnly();

    public void AddFavorite(string url, string title)
    {
        ValidateUrl(url);
        Mutate(data =>
        {
            var index = data.Favorites.FindIndex(item => item.Url == url);
            if (index >= 0) data.Favorites[index] = data.Favorites[index] with { Title = NormalizeTitle(title) };
            else data.Favorites.Add(new(url, NormalizeTitle(title), DateTimeOffset.UtcNow));
        });
    }

    public void RenameFavorite(string url, string title)
    {
        ValidateUrl(url);
        Mutate(data =>
        {
            var index = data.Favorites.FindIndex(item => item.Url == url);
            if (index >= 0) data.Favorites[index] = data.Favorites[index] with { Title = NormalizeTitle(title) };
        });
    }

    public void RemoveFavorite(string url) => Mutate(data => data.Favorites.RemoveAll(item => item.Url == url));

    public void RecordVisit(string url, string title)
    {
        ValidateUrl(url);
        Mutate(data =>
        {
            data.History.RemoveAll(item => item.Url == url);
            data.History.Insert(0, new(url, NormalizeTitle(title), DateTimeOffset.UtcNow));
            if (data.History.Count > 500) data.History.RemoveRange(500, data.History.Count - 500);
        });
    }

    public void RemoveHistory(string url) => Mutate(data => data.History.RemoveAll(item => item.Url == url));
    public void ClearHistory() => Mutate(data => data.History.Clear());

    private void Mutate(Action<LibraryData> mutation)
        => WithFileLock(() => SaveMutation(mutation));

    private void WithFileLock(Action action)
    {
        using var mutex = new Mutex(false, _mutexName);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("其他窗口正在保存浏览器收藏和历史记录，请稍后重试。");
            action();
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    private void SaveMutation(Action<LibraryData> mutation)
    {
        // Reload while holding the cross-process lock so another window's saves and deletions survive.
        var next = Load();
        mutation(next);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next);
        if (bytes.Length > MaxFileBytes) throw new InvalidDataException("浏览器收藏和历史记录已达到 8 MB 存储上限。");
        var directory = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, ".browser-library-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(bytes);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, _filePath, overwrite: true);
            _data = next;
        }
        finally
        {
            // Only remove the temporary file owned by this transaction; never touch the saved file on failure.
            try { File.Delete(temporaryPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static List<BrowserAddress> Normalize(List<BrowserAddress> entries)
    {
        foreach (var entry in entries)
            if (entry is null || !IsValidUrl(entry.Url) || entry.Title is null)
                throw new InvalidDataException("浏览器收藏和历史记录包含无效条目。");
        return entries.DistinctBy(item => item.Url, StringComparer.Ordinal)
            .Select(item => item with { Title = NormalizeTitle(item.Title) }).ToList();
    }

    private static string NormalizeTitle(string title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (trimmed.Length <= 300) return trimmed;
        return trimmed[..(char.IsHighSurrogate(trimmed[299]) ? 299 : 300)];
    }

    private static bool IsValidUrl(string? url) => url is { Length: > 0 and <= 8192 } && WebResourceRules.IsWebUrl(url);
    private static void ValidateUrl(string url)
    {
        if (!IsValidUrl(url)) throw new ArgumentException("请输入不含账号密码、长度不超过 8192 的 HTTP(S) 地址。", nameof(url));
    }

    private sealed record LibraryData(List<BrowserAddress> Favorites, List<BrowserAddress> History);
}
