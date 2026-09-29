using System;
using System.IO;
using MiniSoftware;

namespace BatchPrintCN
{
    /// <summary>
    /// V3.1 第二转换引擎：MiniPdf（mini-software/MiniPdf，Apache 2.0，纯托管，无需安装 Office/WPS）。
    /// 仅支持 .docx/.xlsx/.pptx；.doc/.xls/.ppt 等老格式不支持（由调用方回退 COM）。
    /// </summary>
    public static class MiniPdfConvert
    {
        public static bool CanHandle(string inputPath)
        {
            string ext = Path.GetExtension(inputPath ?? "").ToLowerInvariant();
            return ext == ".docx" || ext == ".xlsx" || ext == ".pptx";
        }

        /// <summary>MiniPdf 静默转换。返回 null=成功，非 null=错误消息。</summary>
        public static string Export(string inputPath, string outputPath)
        {
            try
            {
                FileLogger.Write("Worker 引擎：MiniPdf（纯托管，无 Office 依赖）");
                MiniPdf.ConvertToPdf(inputPath, outputPath, null);
                return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0 ? null : "MiniPdf 导出未生成 PDF 文件。";
            }
            catch (Exception ex)
            {
                return "MiniPdf 转换失败：" + ex.Message;
            }
        }
    }
}
