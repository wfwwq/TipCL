using System;
using System.IO;
using TipCL.Core.Launcher;
using TipCL.Core.Platform;
using Xunit;

namespace TipCL.Tests.Core;

/// <summary>SEC-AUTH-1:非 Windows 上令牌要能落盘、重启后能读回,文件权限 600。</summary>
[Collection(TipCL.Tests.GlobalStateCollection.Name)]
public class UnixSecretStoreTests
{
    private static string TempFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "tipcl-secrets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "secrets.json");
    }

    [Fact]
    public void FileStore_RoundTrip_And_Delete()
    {
        string path = TempFile();
        var store = new FileSecretStore(() => path);
        Assert.Null(store.GetPassword("svc", "u"));
        store.SetPassword("svc", "u", "s3cr3t");
        Assert.Equal("s3cr3t", new FileSecretStore(() => path).GetPassword("svc", "u"));
        store.DeletePassword("svc", "u");
        Assert.Null(store.GetPassword("svc", "u"));
    }

    [Fact]
    public void FileStore_IsOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string path = TempFile();
        new FileSecretStore(() => path).SetPassword("svc", "u", "x");
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Fact]
    public void FileStore_CorruptFile_IsIgnored()
    {
        string path = TempFile();
        File.WriteAllText(path, "{not json");
        var store = new FileSecretStore(() => path);
        Assert.Null(store.GetPassword("svc", "u"));
        store.SetPassword("svc", "u", "ok");
        Assert.Equal("ok", store.GetPassword("svc", "u"));
    }

    [Fact]
    public void Fallback_UsesSecondary_WhenPrimaryThrows()
    {
        string path = TempFile();
        var store = new FallbackSecretStore(new ThrowingStore(), new FileSecretStore(() => path));
        store.SetPassword("svc", "u", "v");
        Assert.Equal("v", store.GetPassword("svc", "u"));
    }

    [Fact]
    public void Auth_SealAndOpen_RoundTrip_WithStore()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // Windows 走 DPAPI,不经凭据库
        }
        ISecretStore saved = PlatformServices.SecretStore;
        try
        {
            string path = TempFile();
            PlatformServices.SecretStore = new FileSecretStore(() => path);
            string sealedValue = (string)Auth.SealSecret("refresh-token-abc");
            Assert.StartsWith("keyring:", sealedValue);
            Assert.DoesNotContain("refresh-token-abc", sealedValue);
            Assert.Equal("refresh-token-abc", (string)Auth.OpenSecret(sealedValue));
        }
        finally
        {
            PlatformServices.SecretStore = saved;
        }
    }

    private sealed class ThrowingStore : ISecretStore
    {
        public string GetPassword(string service, string username) => throw new IOException("no keyring");
        public void SetPassword(string service, string username, string password) => throw new IOException("no keyring");
        public void DeletePassword(string service, string username) => throw new IOException("no keyring");
    }
}
