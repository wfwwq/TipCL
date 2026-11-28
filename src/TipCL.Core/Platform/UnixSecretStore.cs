// SEC-AUTH-1:Linux / macOS 上的令牌存储。
// 之前 PlatformServices.SecretStore 在非 Windows 上从没赋值,Auth.SealSecret 只能写 "unavailable:",
// 微软正版账号的 access / refresh token 每次重启都丢、要重新登录。
// 这里按顺序尝试:系统密钥环(libsecret 的 secret-tool)→ 数据目录下权限 600 的 secrets.json。
// 环境变量 TIPCL_SECRET_STORE=file 可强制只用文件(无桌面会话的机器、测试)。
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using TipCL.Core.Launcher;

namespace TipCL.Core.Platform;

public static class UnixSecretStore
{
    /// <summary>非 Windows 的默认凭据库:能用 secret-tool 就优先用它,写失败或读不到时回落到文件。</summary>
    public static ISecretStore CreateDefault()
    {
        var file = new FileSecretStore(() => Path.Combine(Utils.ROOT, "secrets.json"));
        string mode = (Environment.GetEnvironmentVariable("TIPCL_SECRET_STORE") ?? "").Trim().ToLowerInvariant();
        if (mode == "file")
        {
            return file;
        }
        if (SecretToolStore.IsUsable())
        {
            return new FallbackSecretStore(new SecretToolStore(), file);
        }
        return file;
    }
}

/// <summary>先写/读主库,失败时用备库。</summary>
public sealed class FallbackSecretStore : ISecretStore
{
    private readonly ISecretStore _primary;
    private readonly ISecretStore _secondary;

    public FallbackSecretStore(ISecretStore primary, ISecretStore secondary)
    {
        _primary = primary;
        _secondary = secondary;
    }

    public string? GetPassword(string service, string username)
    {
        try
        {
            string? v = _primary.GetPassword(service, username);
            if (!string.IsNullOrEmpty(v))
            {
                return v;
            }
        }
        catch (Exception e)
        {
            Utils.log.Warning("系统密钥环读取失败,改读本地凭据文件: %s", e.Message);
        }
        return _secondary.GetPassword(service, username);
    }

    public void SetPassword(string service, string username, string password)
    {
        try
        {
            _primary.SetPassword(service, username, password);
            return;
        }
        catch (Exception e)
        {
            Utils.log.Warning("系统密钥环写入失败,改存本地凭据文件(权限 600): %s", e.Message);
        }
        _secondary.SetPassword(service, username, password);
    }

    public void DeletePassword(string service, string username)
    {
        try { _primary.DeletePassword(service, username); }
        catch (Exception e) { Utils.log.Warning("系统密钥环删除失败: %s", e.Message); }
        _secondary.DeletePassword(service, username);
    }
}

/// <summary>通过 libsecret 自带的 secret-tool 命令访问 GNOME Keyring / KWallet 等 Secret Service。</summary>
public sealed class SecretToolStore : ISecretStore
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>有 D-Bus 会话、PATH 里有 secret-tool,且一次查询能正常返回(0 = 找到,1 = 没找到)。</summary>
    public static bool IsUsable()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
        {
            return false;
        }
        try
        {
            var (code, _, _) = Run(new[] { "lookup", "service", "tipcl-probe", "username", "probe" }, null, TimeSpan.FromSeconds(5));
            return code == 0 || code == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string? GetPassword(string service, string username)
    {
        var (code, stdout, stderr) = Run(new[] { "lookup", "service", service, "username", username }, null, Timeout);
        if (code == 0)
        {
            return stdout;
        }
        if (code == 1 && string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }
        throw new IOException("secret-tool lookup 失败(" + code + "): " + stderr.Trim());
    }

    public void SetPassword(string service, string username, string password)
    {
        var (code, _, stderr) = Run(new[] { "store", "--label=TipCL: " + service, "service", service, "username", username }, password, Timeout);
        if (code != 0)
        {
            throw new IOException("secret-tool store 失败(" + code + "): " + stderr.Trim());
        }
    }

    public void DeletePassword(string service, string username)
    {
        Run(new[] { "clear", "service", service, "username", username }, null, Timeout);
    }

    private static (int code, string stdout, string stderr) Run(string[] args, string? stdin, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo("secret-tool")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }
        using Process p = Process.Start(psi) ?? throw new IOException("无法启动 secret-tool");
        // 密码只走标准输入,不进命令行(ps 里看不到)
        p.StandardInput.Write(stdin ?? "");
        p.StandardInput.Close();
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); } catch (Exception) { }
            throw new TimeoutException("secret-tool 超时(可能在等密钥环解锁)");
        }
        p.WaitForExit();
        return (p.ExitCode, outTask.Result, errTask.Result);
    }
}

/// <summary>数据目录下的 JSON 凭据文件;Unix 上权限固定为 600(只有当前用户可读写)。</summary>
public sealed class FileSecretStore : ISecretStore
{
    private static readonly object Gate = new object();
    private readonly Func<string> _path;

    public FileSecretStore(Func<string> path)
    {
        _path = path;
    }

    public string Path => _path();

    public string? GetPassword(string service, string username)
    {
        lock (Gate)
        {
            return Load().TryGetValue(Key(service, username), out string? v) ? v : null;
        }
    }

    public void SetPassword(string service, string username, string password)
    {
        lock (Gate)
        {
            Dictionary<string, string> all = Load();
            all[Key(service, username)] = password;
            Save(all);
        }
    }

    public void DeletePassword(string service, string username)
    {
        lock (Gate)
        {
            Dictionary<string, string> all = Load();
            if (all.Remove(Key(service, username)))
            {
                Save(all);
            }
        }
    }

    private static string Key(string service, string username) => service + "\n" + username;

    private Dictionary<string, string> Load()
    {
        string p = Path;
        if (!File.Exists(p))
        {
            return new Dictionary<string, string>();
        }
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(p)) ?? new Dictionary<string, string>();
        }
        catch (Exception e)
        {
            Utils.log.Warning("本地凭据文件损坏,已忽略: %s", e.Message);
            return new Dictionary<string, string>();
        }
    }

    private void Save(Dictionary<string, string> all)
    {
        string p = Path;
        string? dir = System.IO.Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        string tmp = p + ".tmp";
        // 先建空文件并收紧权限,再写内容,避免令牌有一瞬间以 644 落盘
        File.WriteAllText(tmp, "");
        UnixPerms.OwnerOnly(tmp);
        File.WriteAllText(tmp, JsonSerializer.Serialize(all));
        File.Move(tmp, p, overwrite: true);
        UnixPerms.OwnerOnly(p);
    }
}

/// <summary>Unix 文件权限小工具。</summary>
public static class UnixPerms
{
    /// <summary>把文件权限设为 600;Windows 上什么都不做。</summary>
    public static void OwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows() || !File.Exists(path))
        {
            return;
        }
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception e)
        {
            Utils.log.Warning("收紧文件权限失败 %s: %s", path, e.Message);
        }
    }
}
