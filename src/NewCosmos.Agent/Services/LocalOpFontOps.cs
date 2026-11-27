using Microsoft.Win32;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Services.Core;
using SkiaSharp;

namespace NewCosmos.Agent.Services;

/// <summary>公文字体检查结果项（族名候选 + 随包源文件路径 + 文件名）。</summary>
internal sealed record OfficialFontItem(string FamilyNames, string SourceFilePath, string FileName);

/// <summary>公文字体检查结果：目录不存在时 DirExists=false、Missing为空（视为无缺失，不报错）。</summary>
internal sealed record FontCheckResult(bool DirExists, int Total, IReadOnlyList<OfficialFontItem> Missing);

/// <summary>
/// 公文字体检查/一键安装（LocalOp：<c>font.check</c> / <c>font.install</c>）。
/// <para>
/// 移植参照（只读参考，Agent 不能引用 legacy/桌面项目，故复制逻辑并适配）：
/// <c>legacy/NewCosmos.Desktop/Services/Utilities/FontInstallChecker.cs</c>（整文件 #if WINDOWS）。
/// 适配点：去 Serilog 直写/去 Result 返回值 → 改由 <see cref="LocalOpExecutor"/> 统一转
/// BusinessException（失败回传）与 RESULT JSON（成功回传）；每次任务都重新检查，不做进程内缓存
/// （桌面版缓存是因为它在启动时只查一次，而 Agent 的安装任务需要装完立刻复检）。
/// </para>
/// <para>
/// 检查：扫描安装目录下 公文字体\ 全部 ttf/ttc → <see cref="FontFamilyReader"/> 解析族名 →
/// 与系统已装族名比对（Skia 枚举 + 注册表双路合并）→ 返回缺失项。
/// 安装：复制到 %LOCALAPPDATA%\Microsoft\Windows\Fonts（免管理员）+ 写 HKCU 注册表 + 广播 WM_FONTCHANGE。
/// </para>
/// </summary>
internal static class LocalOpFontOps
{
    /// <summary>
    /// 能力快检（心跳用）：随包公文字体目录存在 + 用户字体目录所在盘可写。
    /// 探测失败一律返回 false（不上报该能力），绝不因为探测异常把能力误报出去。
    /// </summary>
    public static bool ProbeCapability()
    {
        var fontDir = ResolveFontDirectory();
        if (!Directory.Exists(fontDir))
        {
            // 未随包 公文字体\ 目录（本机资产不入库，见 AGENTS §2）：检查/安装都无从谈起。
            return false;
        }

        // 可写快检：安装目标 %LOCALAPPDATA%\Microsoft\Windows\Fonts 常在首次安装用户字体时才创建，
        // 故探测其最近的已存在祖先目录（与目标同一用户 ACL 域）；写入并删除一个探测文件。
        var userFontDir = ResolveUserFontDirectory();
        var probeDir = NearestExistingDirectory(userFontDir);
        if (probeDir is null)
        {
            return false;
        }

        var probeFile = Path.Combine(probeDir, $".nc_font_probe_{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(probeFile, string.Empty);
            return true;
        }
        catch (Exception ex)
        {
            // 不可写（权限/受控目录）→ 不上报 font 能力，安装时会有明确的逐条失败原因。
            System.Diagnostics.Debug.WriteLine($"[LocalOp] 字体目录可写探测失败: {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(probeFile))
                {
                    File.Delete(probeFile);
                }
            }
            catch
            {
                // 探测文件删不掉不影响结论，留在临时/用户目录里无害。
            }
        }
    }

    /// <summary>
    /// 检查随包公文字体与系统已装字体的差集。
    /// 目录不存在 → 返回空缺失清单（视为无缺失，不报错，与桌面版同口径）。
    /// </summary>
    public static FontCheckResult Check(ILoggerService logger)
    {
        var fontDir = ResolveFontDirectory();
        if (!Directory.Exists(fontDir))
        {
            logger.Debug("[LocalOp] 公文字体目录不存在，按无缺失处理");
            return new FontCheckResult(false, 0, Array.Empty<OfficialFontItem>());
        }

        var missing = new List<OfficialFontItem>();
        var total = 0;
        try
        {
            var files = Directory
                .EnumerateFiles(fontDir, "*" + OfficialFontConstants.TtfExtension, SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(fontDir, "*" + OfficialFontConstants.TtcExtension, SearchOption.AllDirectories));

            HashSet<string>? installed = null;
            foreach (var file in files)
            {
                total++;
                installed ??= LoadInstalledFamilies();
                var candidates = FontFamilyReader.ReadFamilyNames(file);
                var hit = candidates.Any(c => installed.Contains(c));
                if (!hit)
                {
                    missing.Add(new OfficialFontItem(string.Join(" / ", candidates), file, Path.GetFileName(file)));
                }
            }
        }
        catch (Exception ex)
        {
            // 检查失败不阻断任务（与桌面同口径），但必须留痕：否则字体缺失会伪装成"无缺失"。
            logger.Warn($"[LocalOp] 公文字体检查失败，本次按已收集结果处理: {ex.Message}");
        }

        return new FontCheckResult(true, total, missing);
    }

    /// <summary>
    /// 一键安装缺失字体（当前用户级，免管理员）：
    /// 复制到 %LOCALAPPDATA%\Microsoft\Windows\Fonts → 写 HKCU Fonts 注册表 → 广播 WM_FONTCHANGE。
    /// 目标文件已存在视为已就位（上次装到一半/同名他源），只补注册表。
    /// </summary>
    /// <returns>已安装条数与逐条失败原因（调用方决定部分成功如何回传）。</returns>
    public static (int Installed, IReadOnlyList<string> Failures) Install(
        IReadOnlyList<OfficialFontItem> missing,
        ILoggerService logger)
    {
        if (missing is null || missing.Count == 0)
        {
            return (0, Array.Empty<string>());
        }

        var userFontDir = ResolveUserFontDirectory();
        var installedCount = 0;
        var failures = new List<string>();

        foreach (var item in missing)
        {
            try
            {
                Directory.CreateDirectory(userFontDir);
                var target = Path.Combine(userFontDir, item.FileName);

                // 文件已存在（上次装到一半/同名他源）：视为已就位，只补注册表
                if (!File.Exists(target))
                {
                    File.Copy(item.SourceFilePath, target, overwrite: false);
                }

                using var key = Registry.CurrentUser.CreateSubKey(OfficialFontConstants.UserFontRegistryKey, writable: true);
                if (key is null)
                {
                    failures.Add($"{item.FileName}: 无法打开用户字体注册表");
                    continue;
                }

                key.SetValue(
                    Path.GetFileNameWithoutExtension(item.FileName) + OfficialFontConstants.RegistryValueSuffix,
                    item.FileName,
                    RegistryValueKind.String);

                installedCount++;
            }
            catch (Exception ex)
            {
                // 失败原因逐条上报（截断单条长度，避免一条超长错误把整条回传撑爆）。
                failures.Add($"{item.FileName}: {Truncate(ex.Message, 120)}");
            }
        }

        if (installedCount > 0)
        {
            BroadcastFontChange();
        }

        logger.Info($"[LocalOp] 公文字体安装: 成功 {installedCount}/{missing.Count}, 失败 {failures.Count}");
        return (installedCount, failures);
    }

    /// <summary>随包公文字体目录（安装目录下，AGENTS §2 公文字体目录）。</summary>
    private static string ResolveFontDirectory()
        => Path.Combine(AppContext.BaseDirectory, OfficialFontConstants.FontDirectoryName);

    /// <summary>一键安装目标：当前用户字体目录（免管理员，HKCU 注册表配套）。</summary>
    private static string ResolveUserFontDirectory()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OfficialFontConstants.UserFontInstallDirectoryName);

    /// <summary>返回路径链上第一个已存在的目录；全部不存在时返回 null。</summary>
    private static string? NearestExistingDirectory(string path)
    {
        try
        {
            var dir = new DirectoryInfo(path);
            while (dir is not null)
            {
                if (dir.Exists)
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // 非法/超长路径：按不可写处理
        }

        return null;
    }

    /// <summary>
    /// 系统已装字体族名集合（OrdinalIgnoreCase）。
    /// 两路【合并】（任一命中即算已装）：
    /// ① SkiaSharp 枚举（覆盖系统字体）；
    /// ② HKLM/HKCU 注册表 Fonts 值名（权威安装记录）——刚装的用户字体在 GDI/系统字体缓存
    ///    刷新前 Skia 枚举可能看不到（实测 2026-11-10：装完重启仍漏报），注册表值必然已写入，
    ///    合并后复检立即通过，且与 Word 的 GDI 字体链判定一致。
    /// Skia 枚举整体异常时仍返回注册表集合；两路都空才返回空集合。
    /// </summary>
    private static HashSet<string> LoadInstalledFamilies()
    {
        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Default 是共享单例，不可 Dispose（Dispose 会破坏后续枚举）
            var skiaFamilies = SKFontManager.Default.GetFontFamilies();
            if (skiaFamilies is not null)
            {
                foreach (var family in skiaFamilies)
                {
                    if (!string.IsNullOrWhiteSpace(family))
                    {
                        families.Add(family);
                    }
                }
            }
        }
        catch
        {
            // Skia 枚举失败：至少还有注册表一路
        }

        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            try
            {
                using var key = root.OpenSubKey(OfficialFontConstants.UserFontRegistryKey);
                if (key is null)
                {
                    continue;
                }

                foreach (var valueName in key.GetValueNames())
                {
                    var family = valueName;
                    if (family.EndsWith(OfficialFontConstants.RegistryValueSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        family = family[..^OfficialFontConstants.RegistryValueSuffix.Length];
                    }

                    if (!string.IsNullOrWhiteSpace(family))
                    {
                        families.Add(family);
                    }
                }
            }
            catch
            {
                // 单个注册表根读取失败跳过
            }
        }

        return families;
    }

    /// <summary>
    /// 广播 WM_FONTCHANGE 让运行中的应用（含 Word）刷新字体表；
    /// SendMessageTimeout 防止有窗口卡死时广播挂起本进程。
    /// </summary>
    private static void BroadcastFontChange()
    {
        try
        {
            const int WM_FONTCHANGE = 0x001D;
            const uint HWND_BROADCAST = 0xFFFF;
            const uint SMTO_ABORTIFHUNG = 0x0002;
            _ = SendMessageTimeout(HWND_BROADCAST, WM_FONTCHANGE, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, 3000, out _);
        }
        catch
        {
            // 广播失败不影响安装结果（新起进程本来就能读到新字体）
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        uint hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength] + "…";
    }
}
