using System.IO;
using System.Reflection;
using System.IO.Pipes;
using System.Security.Principal;
using System.Security.AccessControl;
using ToolsBox.Windows.ArchiveRecovery;

namespace ToolsBox.App.Tests.ArchiveRecovery;

public sealed class ArchivePipeTests
{
    [Fact]
    public void PipeAclGrantsOnlyTheExactCurrentUserAndDoesNotInheritBroadAccess()
    {
        using var pipe = ArchivePipeSecurity.Create("ToolsBox.Archive.Test." + Guid.NewGuid().ToString("N"));
        using var identity = WindowsIdentity.GetCurrent();
        var security = pipe.GetAccessControl();
        Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(identity.User, security.GetOwner(typeof(SecurityIdentifier)));
        var rule = Assert.IsType<PipeAccessRule>(Assert.Single(security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<AuthorizationRule>()));
        Assert.Equal(identity.User, rule.IdentityReference);
        Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
    }
    [Fact]
    public async Task Protocol_RejectsOversizedFrameBeforeAllocatingPayload()
    {
        Type? protocol = typeof(ToolsBox.Windows.FileUnlocking.WindowsFileLockService).Assembly
            .GetType("ToolsBox.Windows.ArchiveRecovery.ArchivePipe");
        Assert.NotNull(protocol);
        using var stream = new MemoryStream(BitConverter.GetBytes(int.MaxValue));
        var method = protocol!.GetMethod("ReadAsync", BindingFlags.Public | BindingFlags.Static)!.MakeGenericMethod(typeof(string));
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await (Task<string>)method.Invoke(null, [stream, CancellationToken.None])!);
    }
}
