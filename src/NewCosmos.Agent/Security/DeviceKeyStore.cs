using NewCosmos.Agent.Configuration;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Security;

/// <summary>
/// 设备密钥的持久化。两种布局：
/// 1) DeviceKeyFile == 配置文件（默认）：密文放在 agent.json 的 deviceKeyProtected / deviceKey 字段；
/// 2) DeviceKeyFile == 独立文件：文件内容为带前缀的 DPAPI 密文，失败退化为带前缀的明文。
/// 任何路径失败都会记录警告，但不会把密钥写进日志。
/// </summary>
public static class DeviceKeyStore
{
    public static string? Load(AgentSettings settings, string configFilePath, ILoggerService logger)
    {
        var keyFile = settings.ResolveDeviceKeyFile(configFilePath);

        if (PathsEqual(keyFile, configFilePath))
        {
            if (!string.IsNullOrWhiteSpace(settings.DeviceKeyProtected))
            {
                try
                {
                    var payload = Convert.FromBase64String(settings.DeviceKeyProtected);
                    if (DeviceKeyProtector.TryUnprotect(payload, out var key))
                        return key;
                }
                catch (Exception ex)
                {
                    logger.Warn($"[DeviceKey] 配置内 DPAPI 密文解码失败: {ex.Message}");
                }
            }

            if (!string.IsNullOrWhiteSpace(settings.DeviceKey))
            {
                logger.Warn("[DeviceKey] 检测到降级明文设备密钥（当前用户 ACL 文件），建议重新注册以恢复 DPAPI 加密。");
                return settings.DeviceKey.Trim();
            }

            return null;
        }

        if (!File.Exists(keyFile))
            return null;

        try
        {
            var payload = File.ReadAllBytes(keyFile);
            if (DeviceKeyProtector.TryUnprotect(payload, out var key))
                return key;

            logger.Warn($"[DeviceKey] 设备密钥文件格式无效或 DPAPI 解密失败: {keyFile}");
            return null;
        }
        catch (Exception ex)
        {
            logger.Warn($"[DeviceKey] 读取设备密钥文件失败: {keyFile} - {ex.Message}");
            return null;
        }
    }

    public static void Save(AgentSettings settings, string configFilePath, string deviceKey, ILoggerService logger)
    {
        if (string.IsNullOrWhiteSpace(deviceKey))
            throw new ArgumentException("设备密钥不能为空", nameof(deviceKey));

        var keyFile = settings.ResolveDeviceKeyFile(configFilePath);
        settings.DeviceKeyFile = keyFile;
        settings.DeviceKeyProtected = null;
        settings.DeviceKey = null;

        if (PathsEqual(keyFile, configFilePath))
        {
            var needsAclRestriction = false;
            if (DeviceKeyProtector.TryProtect(deviceKey, out var payload))
            {
                settings.DeviceKeyProtected = Convert.ToBase64String(payload);
            }
            else
            {
                settings.DeviceKey = deviceKey;
                needsAclRestriction = true;
                logger.Warn("[DeviceKey] DPAPI 不可用，设备密钥降级为当前用户 ACL 文件（agent.json 明文字段）。请确保该 Windows 账户受控。");
            }

            AgentConfigStore.Save(settings, configFilePath, logger);

            if (needsAclRestriction)
                DeviceKeyProtector.TryRestrictToCurrentUser(configFilePath, logger);

            return;
        }

        var dir = Path.GetDirectoryName(keyFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        byte[] filePayload;
        var protectedOk = DeviceKeyProtector.TryProtect(deviceKey, out var protectedPayload);
        if (protectedOk)
        {
            filePayload = protectedPayload;
        }
        else
        {
            filePayload = DeviceKeyProtector.BuildPlainPayload(deviceKey);
            logger.Warn($"[DeviceKey] DPAPI 不可用，设备密钥降级为当前用户 ACL 明文文件: {keyFile}");
        }

        var tempPath = keyFile + ".tmp";
        File.WriteAllBytes(tempPath, filePayload);
        File.Move(tempPath, keyFile, overwrite: true);

        if (!protectedOk)
            DeviceKeyProtector.TryRestrictToCurrentUser(keyFile, logger);

        // 独立密钥文件时，配置里只记录路径
        AgentConfigStore.Save(settings, configFilePath, logger);
    }

    public static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}
