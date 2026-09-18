using System.Numerics;
using System.Text;

namespace ToolsBox.Core.ArchiveRecovery;

public static class PasswordCandidates
{
    public static IEnumerable<string> FromText(string text, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        token.ThrowIfCancellationRequested();
        using var reader = new StringReader(text.StartsWith('\uFEFF') ? text[1..] : text);
        while (reader.ReadLine() is { } line)
        {
            token.ThrowIfCancellationRequested();
            if (line.Length > 0) yield return line;
        }
    }

    public static IEnumerable<string> FromFile(string path, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(input, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
        var buffer = new char[4096];
        var line = new StringBuilder();
        bool first = true;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            int read = reader.ReadAsync(buffer.AsMemory(), token).AsTask().GetAwaiter().GetResult();
            if (read == 0) break;
            for (int index = 0; index < read; index++)
            {
                token.ThrowIfCancellationRequested();
                char value = buffer[index];
                if (first) { first = false; if (value == '\uFEFF') continue; }
                if (value is '\r' or '\n')
                {
                    if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                    continue;
                }
                if (line.Length >= 8192) throw new InvalidDataException("字典单行超过 8192 个字符，已停止读取。");
                line.Append(value);
            }
        }
        token.ThrowIfCancellationRequested();
        if (line.Length > 0) yield return line.ToString();
    }

    public static IEnumerable<string> FromRules(string prefix, string suffix, string charset, int minLength, int maxLength, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(suffix);
        token.ThrowIfCancellationRequested();
        string[] characters = ValidateRules(charset, minLength, maxLength);
        return Enumerate();

        IEnumerable<string> Enumerate()
        {
            for (int length = minLength; length <= maxLength; length++)
            {
                token.ThrowIfCancellationRequested();
                if (length == 0) { yield return prefix + suffix; continue; }
                if (characters.Length == 0) continue;
                var indices = new int[length];
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var candidate = new StringBuilder(prefix);
                    foreach (int index in indices) { token.ThrowIfCancellationRequested(); candidate.Append(characters[index]); }
                    yield return candidate.Append(suffix).ToString();
                    int digit = length - 1;
                    while (digit >= 0 && ++indices[digit] == characters.Length) indices[digit--] = 0;
                    if (digit < 0) break;
                }
            }
        }
    }

    public static BigInteger CountRules(string charset, int minLength, int maxLength)
    {
        int count = ValidateRules(charset, minLength, maxLength).Length;
        BigInteger total = 0;
        for (int length = minLength; length <= maxLength; length++) total += BigInteger.Pow(count, length);
        return total;
    }

    private static string[] ValidateRules(string charset, int minLength, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(charset);
        if (minLength < 0 || maxLength < minLength || maxLength > 12) throw new ArgumentOutOfRangeException(nameof(maxLength), "长度范围必须满足 0 ≤ 最小长度 ≤ 最大长度 ≤ 12。");
        return charset.EnumerateRunes().Distinct().Select(r => r.ToString()).ToArray();
    }
}
