using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Agent.Services;

/// <summary>打印派发：优先 Office COM（可控制打印机/份数/双面），失败降级系统 PDF 打印。</summary>
public interface IJobPrinter
{
    /// <summary>
    /// 执行打印。成功返回 null；降级到 ShellExecute 时返回 warning 文案（任务仍算完成）。
    /// 失败必须抛异常（由 JobWorker 回写 fail）。
    /// </summary>
    Task<string?> PrintAsync(
        string pdfPath,
        string sourcePath,
        string fileType,
        string? printerName,
        int copies,
        bool isDuplex,
        CancellationToken ct);
}

public sealed class JobPrinter : IJobPrinter
{
    private readonly ITemplateJobRenderer _renderer;
    private readonly ILoggerService _logger;

    public JobPrinter(ITemplateJobRenderer renderer, ILoggerService logger)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> PrintAsync(
        string pdfPath,
        string sourcePath,
        string fileType,
        string? printerName,
        int copies,
        bool isDuplex,
        CancellationToken ct)
    {
        var effectiveCopies = Math.Clamp(copies <= 0 ? 1 : copies, 1, 99);

        // 指定打印机不存在时直接失败：Office COM 内部会降级到默认打印机，可能把文书打到错误设备。
        if (!string.IsNullOrWhiteSpace(printerName) && !IsPrinterInstalled(printerName))
        {
            throw new BusinessException(
                ErrorCodes.PRINT_FAILED,
                $"指定的打印机未安装: {printerName}（请检查设备绑定或打印机名称）");
        }

        Exception? officeError = null;

        // 1) 首选：Office COM 打印渲染后的源文档。与生成 PDF 使用同一引擎，打印结果与预览一致，
        //    且能真正落实 printerName / copies / isDuplex（System.Drawing.Printing 无法直接打印 PDF）。
        if (!string.IsNullOrWhiteSpace(sourcePath) && File.Exists(sourcePath))
        {
            try
            {
                await _renderer
                    .PrintSourceAsync(sourcePath, fileType, printerName, effectiveCopies, isDuplex, ct)
                    .ConfigureAwait(false);

                _logger.Info(
                    $"[JobPrint] Office COM 打印完成: Printer={printerName ?? "(默认)"}, Copies={effectiveCopies}, Duplex={isDuplex}");
                AgentConsole.Success($"打印已提交: {printerName ?? "(默认打印机)"} × {effectiveCopies}{(isDuplex ? "（双面）" : string.Empty)}");
                return null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                officeError = ex;
                _logger.Warn($"[JobPrint] Office COM 打印失败，尝试系统 PDF 打印降级: {ex.Message}");
            }
        }
        else
        {
            officeError = new FileNotFoundException("渲染源文件不存在", sourcePath);
            _logger.Warn("[JobPrint] 渲染源文件不存在，跳过 Office COM 打印，尝试系统 PDF 打印降级");
        }

        // 2) 降级：ShellExecute 的 printto / print 动词（依赖系统已注册的 PDF 阅读器）。
        //    局限：份数/双面由阅读器/打印机默认值决定，无法保证与任务参数一致。
        if (TryPrintPdfViaShell(pdfPath, printerName, out var shellError))
        {
            var warning =
                $"已降级为系统 PDF 打印（份数/双面可能不生效）：Office COM 失败" +
                $"{(officeError is null ? string.Empty : $"（{HttpAgentClient.Truncate(officeError.Message, 300)}）")}";
            _logger.Warn(
                $"[JobPrint] 已降级为系统 PDF 打印（份数/双面可能不生效）: Printer={printerName ?? "(默认)"}, Copies={effectiveCopies}");
            AgentConsole.Warn($"已通过系统 PDF 阅读器提交打印（份数/双面可能不生效）: {printerName ?? "(默认打印机)"}");
            return warning;
        }

        var message =
            $"打印失败：Office COM 与系统 PDF 打印均不可用。" +
            $"COM={(officeError?.Message ?? "-")}; PDF={(string.IsNullOrWhiteSpace(shellError) ? "-" : shellError)}";
        _logger.Error($"[JobPrint] {message}");
        throw new BusinessException(ErrorCodes.PRINT_FAILED, HttpAgentClient.Truncate(message, 800));
    }

    /// <summary>打印机枚举失败时返回 true，交给 Office 尝试（避免误判为“未安装”）。</summary>
    private static bool IsPrinterInstalled(string printerName)
    {
        try
        {
            foreach (string? name in PrinterSettings.InstalledPrinters)
            {
                if (string.Equals(name?.Trim(), printerName.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private bool TryPrintPdfViaShell(string pdfPath, string? printerName, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
        {
            error = $"PDF 不存在: {pdfPath}";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(printerName))
        {
            if (TryStart(pdfPath, "printto", $"\"{printerName.Trim()}\"", out var printToError))
                return true;

            // 指定了打印机时不回退 "print" 动词：该动词只认默认打印机，会把文书打到错误设备。
            error = printToError;
            return false;
        }

        if (TryStart(pdfPath, "print", null, out var printError))
            return true;

        error = printError;
        return false;
    }

    private bool TryStart(string filePath, string verb, string? arguments, out string? error)
    {
        error = null;
        try
        {
            var startInfo = new ProcessStartInfo(filePath)
            {
                UseShellExecute = true,
                Verb = verb,
                Arguments = arguments ?? string.Empty,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = $"Verb={verb}: 未能启动关联程序";
                return false;
            }

            _logger.Info($"[JobPrint] 已通过 ShellExecute 提交 PDF 打印: Verb={verb}");
            return true;
        }
        catch (Win32Exception ex)
        {
            // 常见于没有 PDF 关联程序，或阅读器未注册该动词。
            error = $"Verb={verb}: {ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Verb={verb}: {ex.Message}";
            return false;
        }
    }
}
