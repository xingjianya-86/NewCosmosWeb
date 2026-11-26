using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using SkiaSharp;

namespace NewCosmos.Agent.Services;

/// <summary>照片打印：解码 WebP/JPEG/PNG → 按版式排版 → System.Drawing.Printing 送打印机（不经过 Office 模板）。</summary>
public interface IPhotoJobPrinter
{
    /// <summary>
    /// 打印一张照片。失败抛异常（由 JobWorker 回写 fail）；成功即已提交到打印队列。
    /// </summary>
    void Print(
        byte[] photoData,
        string? layout,
        string? category,
        string? printerName,
        int copies,
        string jobNo,
        CancellationToken ct);
}

/// <summary>
/// 照片任务打印实现（JobKind=Photo）：
/// SkiaSharp 解码并处理 EXIF 方向，System.Drawing.Printing.PrintDocument 输出。
/// 照片只在内存中处理，不落临时文件，避免含个人信息的图片滞留在 %TEMP%。
/// 版式映射见 <see cref="PhotoLayoutMapping"/>（单一来源：Core PhotoLayoutConstants）。
/// </summary>
public sealed class PhotoJobPrinter : IPhotoJobPrinter
{
    private readonly ILoggerService _logger;

    public PhotoJobPrinter(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Print(
        byte[] photoData,
        string? layout,
        string? category,
        string? printerName,
        int copies,
        string jobNo,
        CancellationToken ct)
    {
        if (photoData is not { Length: > 0 })
            throw new BusinessException("JOB_PAYLOAD_INVALID", "照片数据为空，无法打印");

        ct.ThrowIfCancellationRequested();

        var effectiveLayout = PhotoLayoutMapping.ResolveLayout(layout, category);
        var effectiveCopies = Math.Clamp(copies <= 0 ? 1 : copies, 1, 99);
        var displayCategory = string.IsNullOrWhiteSpace(category) ? "-" : category.Trim();

        try
        {
            using var oriented = DecodeWithOrientation(photoData);
            using var png = oriented.Encode(SKEncodedImageFormat.Png, 100);
            if (png is null || png.Size == 0)
                throw new BusinessException("JOB_PAYLOAD_INVALID", "照片转码失败，无法生成打印位图");

            using var memory = new MemoryStream(png.ToArray(), writable: false);
            using var image = Image.FromStream(memory);

            PrintImage(image, effectiveLayout, printerName, effectiveCopies, jobNo, ct);

            // 只记录任务号/类别/字节数，不写姓名、身份证号等个人信息。
            _logger.Info($"[PhotoPrint] 照片打印完成: No={jobNo}, Category={displayCategory}, Bytes={photoData.Length}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error($"[PhotoPrint] 照片打印失败: No={jobNo}, Category={displayCategory}, Bytes={photoData.Length}, Error={ex.Message}");
            throw new BusinessException(ErrorCodes.PRINT_FAILED, $"照片打印失败: {ex.Message}", ex);
        }
    }

    private void PrintImage(
        Image image,
        string layout,
        string? printerName,
        int copies,
        string jobNo,
        CancellationToken ct)
    {
        using var document = new PrintDocument
        {
            DocumentName = jobNo,
            // 不显示「正在打印」对话框；照片任务不需要用户交互。
            PrintController = new StandardPrintController()
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(printerName))
            {
                document.PrinterSettings.PrinterName = printerName.Trim();
                if (!document.PrinterSettings.IsValid)
                {
                    throw new BusinessException(
                        ErrorCodes.PRINT_FAILED,
                        $"指定的打印机未安装: {printerName.Trim()}");
                }
            }
            else if (!document.PrinterSettings.IsValid)
            {
                throw new BusinessException(ErrorCodes.PRINT_FAILED, "没有可用的默认打印机");
            }

            document.PrinterSettings.Copies = (short)Math.Clamp(copies, 1, 99);

            // 照片任务固定单面；部分驱动不支持设置 Duplex，失败时沿用驱动默认值。
            try
            {
                document.PrinterSettings.Duplex = Duplex.Simplex;
            }
            catch (Exception)
            {
                // 忽略：不支持 Duplex 设置的驱动仍可正常打印。
            }

            document.DefaultPageSettings.Landscape = false;
            // 清零默认 1 英寸页边距；实际边界使用打印机的可打印区域（VisibleClipBounds）。
            document.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);

            document.PrintPage += (_, e) => DrawPhoto(e, image, layout, ct);
            document.Print();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusinessException)
        {
            throw;
        }
        catch (InvalidPrinterException ex)
        {
            throw new BusinessException(ErrorCodes.PRINT_FAILED, $"打印失败：打印机不可用: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new BusinessException(ErrorCodes.PRINT_FAILED, $"打印失败: {ex.Message}", ex);
        }
    }

    private static void DrawPhoto(PrintPageEventArgs e, Image image, string layout, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var graphics = e.Graphics;
        if (graphics is null)
        {
            e.HasMorePages = false;
            return;
        }

        // PrintDocument 会把 Graphics 定位到可打印区；PageUnit 统一为 1/100 英寸。
        graphics.PageUnit = GraphicsUnit.Display;

        var printable = graphics.VisibleClipBounds;
        if (printable.Width <= 0 || printable.Height <= 0)
        {
            var margin = e.MarginBounds;
            printable = new RectangleF(margin.X, margin.Y, margin.Width, margin.Height);
        }

        if (printable.Width <= 0 || printable.Height <= 0)
            printable = new RectangleF(0, 0, e.PageBounds.Width, e.PageBounds.Height);

        var target = PhotoLayoutMapping.GetTargetBox(layout, printable);
        if (target.Width <= 0 || target.Height <= 0 || image.Width <= 0 || image.Height <= 0)
        {
            e.HasMorePages = false;
            return;
        }

        // 等比缩放装入目标框并居中（不裁剪照片）。
        var scale = Math.Min(target.Width / image.Width, target.Height / image.Height);
        var drawWidth = image.Width * scale;
        var drawHeight = image.Height * scale;
        var drawX = target.X + (target.Width - drawWidth) / 2f;
        var drawY = target.Y + (target.Height - drawHeight) / 2f;

        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.DrawImage(image, drawX, drawY, drawWidth, drawHeight);

        e.HasMorePages = false;
    }

    /// <summary>
    /// SkiaSharp 解码 WebP/JPEG/PNG；EXIF Orientation 通过 <see cref="SKCodec.EncodedOrigin"/> 应用到像素。
    /// 统一输出 Bgra8888 位图，避免打印机/GDI+ 对源格式的差异。
    /// </summary>
    private static SKBitmap DecodeWithOrientation(byte[] photoData)
    {
        SKBitmap? decoded = null;
        var origin = SKEncodedOrigin.TopLeft;

        try
        {
            using var data = SKData.CreateCopy(photoData);
            using var codec = SKCodec.Create(data);
            if (codec is null)
                throw new BusinessException("JOB_PAYLOAD_INVALID", "照片格式无法识别（支持 WebP/JPEG/PNG）");

            origin = codec.EncodedOrigin;
            var info = new SKImageInfo(
                codec.Info.Width,
                codec.Info.Height,
                SKColorType.Bgra8888,
                SKAlphaType.Premul);

            if (info.Width <= 0 || info.Height <= 0)
                throw new BusinessException("JOB_PAYLOAD_INVALID", "照片尺寸无效");

            decoded = new SKBitmap(info);
            var result = codec.GetPixels(info, decoded.GetPixels());
            if (result != SKCodecResult.Success)
            {
                throw new BusinessException(
                    "JOB_PAYLOAD_INVALID",
                    $"照片解码失败（{result}），文件可能已损坏");
            }
        }
        catch (BusinessException)
        {
            decoded?.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            decoded?.Dispose();
            throw new BusinessException("JOB_PAYLOAD_INVALID", $"照片解码失败: {ex.Message}", ex);
        }

        try
        {
            return ApplyOrientation(decoded, origin);
        }
        finally
        {
            decoded.Dispose();
        }
    }

    /// <summary>按 EXIF 方向生成正向位图（目标尺寸在 90/270 度时交换宽高）。</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        var swap = origin is SKEncodedOrigin.LeftTop
            or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom
            or SKEncodedOrigin.LeftBottom;

        var targetWidth = swap ? source.Height : source.Width;
        var targetHeight = swap ? source.Width : source.Height;
        var target = new SKBitmap(new SKImageInfo(
            targetWidth,
            targetHeight,
            SKColorType.Bgra8888,
            SKAlphaType.Premul));

        using var canvas = new SKCanvas(target);
        // 打印到白纸：透明区域用白色填充，避免透明 PNG 打印成黑色。
        canvas.Clear(SKColors.White);
        canvas.SetMatrix(BuildOrientationMatrix(origin, source.Width, source.Height));
        canvas.DrawBitmap(source, 0, 0);
        return target;
    }

    /// <summary>
    /// EXIF 方向 → 源坐标到目标坐标的矩阵：
    /// x' = ScaleX*x + SkewX*y + TransX；y' = SkewY*x + ScaleY*y + TransY。
    /// </summary>
    private static SKMatrix BuildOrientationMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix
        {
            ScaleX = -1, SkewX = 0, TransX = width,
            SkewY = 0, ScaleY = 1, TransY = 0,
            Persp2 = 1
        },
        SKEncodedOrigin.BottomRight => new SKMatrix
        {
            ScaleX = -1, SkewX = 0, TransX = width,
            SkewY = 0, ScaleY = -1, TransY = height,
            Persp2 = 1
        },
        SKEncodedOrigin.BottomLeft => new SKMatrix
        {
            ScaleX = 1, SkewX = 0, TransX = 0,
            SkewY = 0, ScaleY = -1, TransY = height,
            Persp2 = 1
        },
        SKEncodedOrigin.LeftTop => new SKMatrix
        {
            ScaleX = 0, SkewX = 1, TransX = 0,
            SkewY = 1, ScaleY = 0, TransY = 0,
            Persp2 = 1
        },
        SKEncodedOrigin.RightTop => new SKMatrix
        {
            ScaleX = 0, SkewX = -1, TransX = height,
            SkewY = 1, ScaleY = 0, TransY = 0,
            Persp2 = 1
        },
        SKEncodedOrigin.RightBottom => new SKMatrix
        {
            ScaleX = 0, SkewX = -1, TransX = height,
            SkewY = -1, ScaleY = 0, TransY = width,
            Persp2 = 1
        },
        SKEncodedOrigin.LeftBottom => new SKMatrix
        {
            ScaleX = 0, SkewX = 1, TransX = 0,
            SkewY = -1, ScaleY = 0, TransY = width,
            Persp2 = 1
        },
        _ => SKMatrix.Identity
    };
}
