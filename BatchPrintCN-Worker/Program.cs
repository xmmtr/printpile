using System;
using System.IO;

namespace BatchPrintCN
{
    /// <summary>
    /// 批量打印助手 · 文档转换子进程（V3.0 崩溃隔离 / V3.1 双引擎）。
    /// 用法：BatchPrintCNWorker.exe --in <源文件> --out <输出.pdf> --kind word|excel|ppt [--engine minipdf|com]
    /// 引擎规则：--engine minipdf 且扩展名为 docx/xlsx/pptx → MiniPdf；否则 COM（老格式自动回退，日志标注「引擎：COM(回退)」）。
    /// 退出码：0=成功；1=转换失败（日志含原因）；2=参数错误。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            AppPaths.Init();   // V4.1 便携化：Worker 日志也走程序目录\logs\
            string input = null, output = null, kind = "word", engine = "com";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--in" && i + 1 < args.Length) input = args[i + 1];
                else if (args[i] == "--out" && i + 1 < args.Length) output = args[i + 1];
                else if (args[i] == "--kind" && i + 1 < args.Length) kind = args[i + 1];
                else if (args[i] == "--engine" && i + 1 < args.Length) engine = args[i + 1];
            }

            FileLogger.Write("Worker 启动：" + string.Join(" ", args));
            if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output) || !File.Exists(input))
            {
                FileLogger.Write("Worker 参数不足或源文件不存在。");
                return 2;
            }

            string ext = Path.GetExtension(input).ToLowerInvariant();
            try
            {
                string err;
                bool wantMiniPdf = string.Equals(engine, "minipdf", StringComparison.OrdinalIgnoreCase);
                if (wantMiniPdf && MiniPdfConvert.CanHandle(input))
                {
                    err = MiniPdfConvert.Export(input, output);
                }
                else
                {
                    if (wantMiniPdf) FileLogger.Write("引擎：COM(回退)——MiniPdf 不支持 " + ext + " 老格式，回退本机 Office/WPS。");
                    err = OfficeExport.Export(input, output, kind);
                }

                if (err != null)
                {
                    FileLogger.Write("Worker 转换失败：" + err);
                    return 1;
                }
                FileLogger.Write("Worker 转换成功：" + output);
                return 0;
            }
            catch (Exception ex)
            {
                FileLogger.Write("Worker 异常：" + ex);
                return 1;
            }
        }
    }
}
