using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace BatchPrintCN
{
    /// <summary>
    /// Office/WPS 静默导出 PDF（V3.0 起由独立 Worker 子进程调用，与主程序 UI 无关）。
    /// 各方法返回 null = 成功，非 null = 错误消息。全程无任何弹窗（COM 不可见 + 文件转换）。
    /// 日志统一写 FileLogger（%LOCALAPPDATA%\BatchPrintCN\logs）。
    /// </summary>
    public static class OfficeExport
    {
        /// <summary>统一入口：kind 取值 word / excel / ppt（文本 txt 也走 word 引擎）。</summary>
        public static string Export(string input, string output, string kind)
        {
            switch (kind)
            {
                case "excel": return ExportExcel(input, output);
                case "ppt": return ExportPpt(input, output);
                default: return ExportWord(input, output);
            }
        }

        public static string ExportWord(string input, string output)
        {
            Type t = Type.GetTypeFromProgID("Kwps.Application");
            if (t == null) t = Type.GetTypeFromProgID("Word.Application");
            if (t == null) return "未检测到 WPS 文字或 Microsoft Word，无法转换 Word/文本文档。";
            bool isWps = t.FullName.IndexOf("Kwps", StringComparison.OrdinalIgnoreCase) >= 0;
            FileLogger.Write("Worker 引擎：" + (isWps ? "WPS 文字 (Kwps)" : "Microsoft Word"));
            dynamic app = Activator.CreateInstance(t);
            dynamic doc = null;
            try
            {
                app.Visible = false;
                try { app.DisplayAlerts = 0; } catch { }
                doc = app.Documents.Open(input, ReadOnly: true);
                try { doc.ExportAsFixedFormat(output, 17); }   // wdExportFormatPDF = 17
                catch { doc.SaveAs2(output, 17); }             // 兼容无 ExportAsFixedFormat 的 WPS 版本
                return File.Exists(output) && new FileInfo(output).Length > 0 ? null : "导出未生成 PDF 文件。";
            }
            catch (Exception ex) { return "Word 导出失败：" + ex.Message; }
            finally
            {
                if (doc != null) { try { doc.Close(false); } catch { } Release(doc); }
                try { app.Quit(); } catch { }
                Release(app);
                WaitComExit(isWps ? "wps" : "WINWORD");
            }
        }

        public static string ExportExcel(string input, string output)
        {
            Type t = Type.GetTypeFromProgID("Ket.Application");
            if (t == null) t = Type.GetTypeFromProgID("Excel.Application");
            if (t == null) return "未检测到 WPS 表格或 Microsoft Excel，无法转换 Excel 文档。";
            bool isWps = t.FullName.IndexOf("Ket", StringComparison.OrdinalIgnoreCase) >= 0;
            FileLogger.Write("Worker 引擎：" + (isWps ? "WPS 表格 (Ket)" : "Microsoft Excel"));
            dynamic app = Activator.CreateInstance(t);
            dynamic wb = null;
            try
            {
                app.Visible = false;
                try { app.DisplayAlerts = false; } catch { }
                wb = app.Workbooks.Open(input, ReadOnly: true, UpdateLinks: 0);
                try { wb.ExportAsFixedFormat(0, output); }     // xlTypePDF = 0
                catch { wb.SaveAs(output, 57); }               // xlTypePDF = 57（SaveAs 变体）
                return File.Exists(output) && new FileInfo(output).Length > 0 ? null : "导出未生成 PDF 文件。";
            }
            catch (Exception ex) { return "Excel 导出失败：" + ex.Message; }
            finally
            {
                if (wb != null) { try { wb.Close(false); } catch { } Release(wb); }
                try { app.Quit(); } catch { }
                Release(app);
                WaitComExit(isWps ? "et" : "EXCEL");
            }
        }

        public static string ExportPpt(string input, string output)
        {
            Type t = Type.GetTypeFromProgID("Kwpp.Application");
            if (t == null) t = Type.GetTypeFromProgID("PowerPoint.Application");
            if (t == null) return "未检测到 WPS 演示或 Microsoft PowerPoint，无法转换 PPT 文档。";
            bool isWps = t.FullName.IndexOf("Kwpp", StringComparison.OrdinalIgnoreCase) >= 0;
            FileLogger.Write("Worker 引擎：" + (isWps ? "WPS 演示 (Kwpp)" : "Microsoft PowerPoint"));
            dynamic app = Activator.CreateInstance(t);
            dynamic pres = null;
            try
            {
                app.Visible = false;
                pres = app.Presentations.Open(input, ReadOnly: true, WithWindow: false);
                try { pres.ExportAsFixedFormat(output, 2); }   // ppFixedFormatTypePDF = 2
                catch { pres.SaveAs(output, 32); }             // ppSaveAsPDF = 32
                return File.Exists(output) && new FileInfo(output).Length > 0 ? null : "导出未生成 PDF 文件。";
            }
            catch (Exception ex) { return "PPT 导出失败：" + ex.Message; }
            finally
            {
                if (pres != null) { try { pres.Close(); } catch { } Release(pres); }
                try { app.Quit(); } catch { }
                Release(app);
                WaitComExit(isWps ? "wpp" : "POWERPNT");
            }
        }

        // COM 应用 Quit 后等待进程真正退出，避免残留僵尸进程
        // 注意：WPS 文字进程名是 "wps"（不是 "KWPS"），表格 "et"、演示 "wpp"
        private static void WaitComExit(string processName)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(processName))
                {
                    try { if (!p.HasExited) p.WaitForExit(6000); } catch { }
                    try { p.Dispose(); } catch { }
                }
            }
            catch { }
        }

        private static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
        }
    }
}
