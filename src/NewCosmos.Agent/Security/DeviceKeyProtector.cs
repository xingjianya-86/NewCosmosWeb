using System.Diagnostics;
using System.Security.Cryptography;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Security;

/// <summary>
/// 设备密钥保护：优先 Windows DPAPI（CurrentUser，绑定当前 Windows 账户），
/// DPAPI 不可用时退化为"当前用户 ACL 的本地文件"，并记录警告。
/// </summary>
public static class DeviceKeyProtector
{
    private static readonly byte[] DpapiHeader = "NCKEY-DPAPI1\n"u8.ToArray();
    private static readonly byte[] PlainHeader = "NCKEY-PLAIN1\n"u8.ToArray();

    public static bool TryProtect(string plaintext, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (string.IsNullOrEmpty(plaintext)) return false;

        try
        {
            var raw = System.Text.Encoding.UTF8.GetBytes(plaintext);
            var encrypted = ProtectedData.Protect(raw, optionalEntropy: null, DataProtectionScope.CurrentUser);
            payload = Combine(DpapiHeader, encrypted);
            return true;
        }
        catch (Exception)
        {
            // 平台不支持/DPAPI 受损——由调用方走 ACL 文件降级
            return false;
        }
    }

    public static bool TryUnprotect(byte[] payload, out string plaintext)
    {
        plaintext = string.Empty;
        if (payload == null || payload.Length == 0) return false;

        try
        {
            if (StartsWith(payload, DpapiHeader))
            {
                var body = payload[DpapiHeader.Length..];
                var raw = ProtectedData.Unprotect(body, optionalEntropy: null, DataProtectionScope.CurrentUser);
                plaintext = System.Text.Encoding.UTF8.GetString(raw);
                return plaintext.Length > 0;
            }

            if (StartsWith(payload, PlainHeader))
            {
                plaintext = System.Text.Encoding.UTF8.GetString(payload[PlainHeader.Length..]);
                return plaintext.Length > 0;
            }

            // 兼容无前缀的旧文件：按 UTF-8 明文尝试
            plaintext = System.Text.Encoding.UTF8.GetString(payload).Trim();
            return plaintext.Length > 0 && IsPlausibleKey(plaintext);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static byte[] BuildPlainPayload(string plaintext) =>
        Combine(PlainHeader, System.Text.Encoding.UTF8.GetBytes(plaintext));

    /// <summary>把文件 ACL 收紧为当前 Windows 用户（icacls；失败只告警，不抛）。</summary>
    public static void TryRestrictToCurrentUser(string filePath, ILoggerService logger)
    {
        try
        {
            var user = string.IsNullOrWhiteSpace(Environment.UserDomainName)
                ? Environment.UserName
                : $"{Environment.UserDomainName}\\{Environment.UserName}";

            var psi = new ProcessStartInfo("icacls")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(filePath);
            psi.ArgumentList.Add("/inheritance:r");
            psi.ArgumentList.Add("/grant:r");
            psi.ArgumentList.Add($"{user}:F");

            using var process = Process.Start(psi);
            if (process == null)
            {
                logger.Warn($"[DeviceKey] 无法启动 icacls 收紧 ACL，文件仍位于当前用户 AppData: {filePath}");
                return;
            }

            if (!process.WaitForExit(5000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
                logger.Warn($"[DeviceKey] 收紧 ACL 超时（文件仍位于当前用户 AppData）: {filePath}");
                return;
            }

            if (process.ExitCode != 0)
                logger.Warn($"[DeviceKey] 收紧 ACL 失败（exit={process.ExitCode}），文件仍位于当前用户 AppData: {filePath}");
        }
        catch (Exception ex)
        {
            logger.Warn($"[DeviceKey] 收紧 ACL 异常（文件仍位于当前用户 AppData）: {ex.Message}");
        }
    }

    private static bool IsPlausibleKey(string value) =>
        value.Length is > 0 and < 4096 && value.All(c => !char.IsControl(c));

    private static byte[] Combine(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, result, 0, a.Length);
        Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
        return result;
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i]) return false;
        }
        return true;
    }
}
