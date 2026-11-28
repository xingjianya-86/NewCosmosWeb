using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace NewCosmos.Helpers;

/// <summary>
/// 多份 PDF 真合并（PDF 有交叉引用表/对象编号，字节拼接会产出无效文件）。
/// 名册类模板按索引槽分页后需要合并成一个预览/打印件。
/// </summary>
public static class PdfMergeHelper
{
    static PdfMergeHelper()
    {
        PdfSharpLicenseBootstrap.EnsureLoaded();
    }

    public static byte[] Merge(IReadOnlyList<byte[]> pdfs)
    {
        if (pdfs.Count == 0)
        {
            return [];
        }

        if (pdfs.Count == 1)
        {
            return pdfs[0];
        }

        using var output = new PdfDocument();
        foreach (var pdf in pdfs)
        {
            using var inputStream = new MemoryStream(pdf);
            using var input = PdfReader.Open(inputStream, PdfDocumentOpenMode.Import);
            for (var index = 0; index < input.PageCount; index++)
            {
                output.AddPage(input.Pages[index]);
            }
        }

        using var buffer = new MemoryStream();
        output.Save(buffer);
        return buffer.ToArray();
    }
}
