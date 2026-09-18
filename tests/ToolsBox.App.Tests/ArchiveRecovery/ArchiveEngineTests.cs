using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using ToolsBox.Core.ArchiveRecovery;
using ToolsBox.Windows.ArchiveRecovery;

namespace ToolsBox.App.Tests.ArchiveRecovery;

public class ArchiveEngineTests
{
    [Fact]
    public void EmbeddedEngineLoadsWithIntegrityAndDirectoryProtection() => EmbeddedArchiveEngine.EnsureLoaded();

    [Theory]
    [InlineData("7z-headers.7z")]
    [InlineData("Rar.encrypted_filesAndHeader.rar")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar")]
    [InlineData("7z-data.7z")]
    [InlineData("Rar.encrypted_filesOnly.rar")]
    public void AmbiguousEncryptedFailureRetainsUncertainty(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", file);
        Assert.Equal(ArchivePasswordOutcome.RejectedUncertain, new SevenZipPasswordVerifier().Verify(path, "wrong-candidate").Outcome);
    }

    [Fact]
    public void LicenseNoticesAreAvailableWithoutExtractingAnEngine()
    {
        string notices = EmbeddedArchiveEngine.ReadLicenseNotices();
        Assert.Contains("GNU", notices);
        Assert.Contains("unRAR", notices);
        Assert.Contains("f4c16a6a95ace333fa33f38c1d69e44d77812eee", notices);
    }

    [Fact]
    public void SignatureDetectorDoesNotTrustTheExtension()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", "zip-aes.zip");
        Assert.Equal("ZIP", ArchiveFormatDetector.Detect(path));
        Assert.Null(ArchiveFormatDetector.Detect("missing.zip"));
    }

    [Fact]
    public void ZipCommentContainingFakeEndRecordDoesNotChangeFormatOrVerification()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", "zip-crypto.zip");
        byte[] original = File.ReadAllBytes(source);
        int endRecord = original.Length - 22;
        Assert.Equal(new byte[] { 0x50, 0x4b, 5, 6 }, original.AsSpan(endRecord, 4).ToArray());
        byte[] bytes = new byte[original.Length + 30];
        original.CopyTo(bytes, 0);
        bytes[endRecord + 20] = 30;
        bytes[original.Length] = 0x50;
        bytes[original.Length + 1] = 0x4b;
        bytes[original.Length + 2] = 5;
        bytes[original.Length + 3] = 6;
        bytes[original.Length + 4] = 1; // Fake multi-disk EOCD, followed by comment bytes.
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Equal("ZIP", ArchiveFormatDetector.Detect(path));
            Assert.Equal(ArchivePasswordOutcome.Match, new SevenZipPasswordVerifier().Verify(path, "test-pass").Outcome);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void UnsupportedZipCompressionMethodStops()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", "zip-crypto.zip");
        byte[] bytes = File.ReadAllBytes(source);
        for (int i = 0; i < bytes.Length - 12; i++)
        {
            if (bytes[i] != 0x50 || bytes[i + 1] != 0x4b) continue;
            int offset = bytes[i + 2] == 3 && bytes[i + 3] == 4 ? 8 : bytes[i + 2] == 1 && bytes[i + 3] == 2 ? 10 : -1;
            if (offset >= 0) { bytes[i + offset] = 200; bytes[i + offset + 1] = 0; }
        }
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.Equal(ArchivePasswordOutcome.Unsupported, new SevenZipPasswordVerifier().Verify(path, "test-pass").Outcome);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CorruptEncryptedContentCannotReportMatchEvenWithCorrectPassword()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures", "zip-crypto.zip");
        byte[] bytes = File.ReadAllBytes(source);
        int start = 30 + BitConverter.ToUInt16(bytes, 26) + BitConverter.ToUInt16(bytes, 28);
        bytes[start + 13] ^= 0xff;
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, bytes);
            Assert.NotEqual(ArchivePasswordOutcome.Match, new SevenZipPasswordVerifier().Verify(path, "test-pass").Outcome);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("zip-crypto.zip", "test-pass")]
    [InlineData("zip-aes.zip", "test-pass")]
    [InlineData("7z-data.7z", "test-pass")]
    [InlineData("7z-headers.7z", "test-pass")]
    [InlineData("Rar.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar.encrypted_filesAndHeader.rar", "test")]
    [InlineData("Rar5.encrypted_filesOnly.rar", "test")]
    [InlineData("Rar5.encrypted_filesAndHeader.rar", "test")]
    public void QualifiedEncryptedFormatsRejectWrongAndFullyVerifyCorrect(string file, string password)
    {
        string folder = Path.Combine(AppContext.BaseDirectory, "ArchiveRecovery", "Fixtures");
        string path = Path.Combine(folder, file);
        var before = SHA256.HashData(File.ReadAllBytes(path));
        var files = Directory.GetFiles(folder).Order().ToArray();
        var verifier = new SevenZipPasswordVerifier();
        var rejected = verifier.Verify(path, "wrong-candidate-do-not-log");
        Assert.Contains(rejected.Outcome, new[] { ArchivePasswordOutcome.NoMatch, ArchivePasswordOutcome.RejectedUncertain });
        Assert.DoesNotContain("wrong-candidate-do-not-log", rejected.Message);
        Assert.Equal(ArchivePasswordOutcome.Match, verifier.Verify(path, password).Outcome);
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Equal(files, Directory.GetFiles(folder).Order().ToArray());
    }

    [Fact]
    public void UnencryptedZipDoesNotClaimPasswordMatch()
    {
        string path = Path.GetTempFileName();
        try
        {
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            using (var writer = new StreamWriter(zip.CreateEntry("data.txt").Open())) writer.Write("plain");
            Assert.Equal(ArchivePasswordOutcome.NotEncrypted, new SevenZipPasswordVerifier().Verify(path, "anything").Outcome);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("unsupported.bin", "4D5A000000000000", ArchivePasswordOutcome.Unsupported)]
    [InlineData("corrupt.zip", "504B030400000000", ArchivePasswordOutcome.InvalidArchive)]
    [InlineData("split.7z.001", "377ABCAF271C0000", ArchivePasswordOutcome.Unsupported)]
    [InlineData("volume.rar", "526172211A0700FFFF7301000D000000", ArchivePasswordOutcome.Unsupported)]
    [InlineData("renamed-rar5.rar", "526172211A070100000000000501000100", ArchivePasswordOutcome.Unsupported)]
    public void UnsupportedAndCorruptInputsNeverClaimWrongPassword(string name, string hex, ArchivePasswordOutcome expected)
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        try
        {
            File.WriteAllBytes(path, Convert.FromHexString(hex));
            Assert.Equal(expected, new SevenZipPasswordVerifier().Verify(path, "private-password").Outcome);
        }
        finally { File.Delete(path); Directory.Delete(directory); }
    }
}
