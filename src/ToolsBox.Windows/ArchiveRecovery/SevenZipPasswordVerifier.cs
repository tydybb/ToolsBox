using ToolsBox.Core.ArchiveRecovery;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

namespace ToolsBox.Windows.ArchiveRecovery;

public sealed class SevenZipPasswordVerifier
{
    public ArchivePasswordResult Verify(string path, string password)
    {
        SupportedArchiveFormat? format = null;
        bool hasEncryptedData = false;
        try
        {
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            format = ArchiveFormatDetector.Detect(input, path);
            EmbeddedArchiveEngine.EnsureLoaded();
            using var archive = new SharpSevenZipExtractor(input, password);
            var entries = archive.ArchiveFileData;
            hasEncryptedData = entries.Any(entry => entry.Encrypted && !entry.IsDirectory);
            if (archive.VolumeFileNames.Count > 1 || archive.ArchiveProperties.Any(property =>
                    (property.Name == "IsVolume" && property.Value is true) ||
                    (property.Name == "Number of volumes" && property.Value != null && Convert.ToUInt64(property.Value) > 1) ||
                    (property.Name == "VolumeIndex" && property.Value != null && Convert.ToUInt64(property.Value) > 0)))
                return Result(ArchivePasswordOutcome.Unsupported);
            if ((archive.ErrorFlags & (ArchiveErrorFlags.UnsupportedMethod | ArchiveErrorFlags.UnsupportedFeature)) != 0)
                return Result(ArchivePasswordOutcome.Unsupported);
            if (archive.ErrorFlags != 0 || archive.WarningFlags != 0 || archive.HasDataAfterEnd) return Result(ArchivePasswordOutcome.InvalidArchive);
            foreach (var entry in entries.Where(entry => !entry.IsDirectory))
                archive.ExtractFile(entry.Index, Stream.Null);
            return Result(hasEncryptedData ? ArchivePasswordOutcome.Match : ArchivePasswordOutcome.NotEncrypted);
        }
        catch (NotSupportedException) { return Result(ArchivePasswordOutcome.Unsupported); }
        catch (InvalidDataException) { return Result(ArchivePasswordOutcome.InvalidArchive); }
        catch (Exception error) when (error is SharpSevenZipException || error is SharpSevenZipArchiveException || error is ExtractionFailedException)
        {
            // Inspect fixed engine diagnostics only for classification; never return exception text.
            string diagnostic = string.Join(" ", ExceptionChain(error).Select(e => e.Message));
            if (diagnostic.Contains("unsupported", StringComparison.OrdinalIgnoreCase)) return Result(ArchivePasswordOutcome.Unsupported);
            if (diagnostic.Contains("Wrong password.", StringComparison.OrdinalIgnoreCase)) return Result(ArchivePasswordOutcome.NoMatch);
            if (diagnostic.Contains("memory", StringComparison.OrdinalIgnoreCase)) return Result(ArchivePasswordOutcome.Inconclusive);
            bool dataFailure = diagnostic.Contains("Data error", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("CRC", StringComparison.OrdinalIgnoreCase) || diagnostic.Contains("corrupt", StringComparison.OrdinalIgnoreCase);
            bool ambiguousHeaderFailure = error is SharpSevenZipArchiveException && diagnostic.Contains("open/read error", StringComparison.OrdinalIgnoreCase);
            if ((hasEncryptedData && dataFailure) || (ambiguousHeaderFailure && format is SupportedArchiveFormat.SevenZip or SupportedArchiveFormat.Rar4 or SupportedArchiveFormat.Rar5))
                return Result(ArchivePasswordOutcome.RejectedUncertain);
            return Result(error is SharpSevenZipArchiveException ? ArchivePasswordOutcome.InvalidArchive : ArchivePasswordOutcome.Inconclusive);
        }
        catch (Exception) { return Result(ArchivePasswordOutcome.Inconclusive); }
    }

    private static IEnumerable<Exception> ExceptionChain(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException) yield return current;
    }

    private static ArchivePasswordResult Result(ArchivePasswordOutcome outcome) => new(outcome, outcome switch
    {
        ArchivePasswordOutcome.Match => "密码匹配，已完整校验加密内容。",
        ArchivePasswordOutcome.NoMatch => "密码不匹配。",
        ArchivePasswordOutcome.RejectedUncertain => "密码可能不符或压缩包损坏。",
        ArchivePasswordOutcome.NotEncrypted => "压缩包没有可验证的加密文件内容。",
        ArchivePasswordOutcome.Unsupported => "不支持此格式、压缩方法、自解压包或分卷压缩包。",
        ArchivePasswordOutcome.InvalidArchive => "压缩包无效、结构损坏或不完整。",
        _ => "校验未能完成，可能存在访问、引擎或资源限制。"
    });
}
