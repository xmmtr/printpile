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

        /// <summary>
        /// 45 号·改2：本机某类文档会挑到哪家转换程序（WPS 优先、再 Microsoft，与上面 ExportWord/ExportExcel/ExportPpt
        /// 里的 ProgID 探测顺序同一份名单——改那边记得改这边，两处不一致时日志会同时出现"Worker 引擎："与"引擎 …"两行，一眼能看出来）。
        /// 只查注册表 ProgID，不启动任何 COM，因此不影响转换行为；转换真正用的是哪家，仍以 Export* 内部那次探测为准。
        /// </summary>
        public static string ProbeEngineToken(string kind)
        {
            string wps, ms;
            switch (kind)
            {
                case "excel": wps = "Ket.Application"; ms = "Excel.Application"; break;
                case "ppt": wps = "Kwpp.Application"; ms = "PowerPoint.Application"; break;
                default: wps = "Kwps.Application"; ms = "Word.Application"; break;
            }
            string fam = kind == "excel" ? "excel" : kind == "ppt" ? "ppt" : "word";
            if (Type.GetTypeFromProgID(wps) != null) return "wps-" + fam;
            if (Type.GetTypeFromProgID(ms) != null) return "ms-" + fam;
            return "none-" + fam;
        }

        /// <summary>
        /// 45 号·改2：令牌 → 给人看的引擎名。
        /// 为什么绕一圈：Worker 是另一个进程，它的 stdout 在重定向时按系统 OEM 码页（本机 936）落盘，
        /// 直接传中文父进程按 UTF-8 读就成了花脸（44 号在自检里踩过同一坑）——所以跨进程只传 ASCII 令牌，
        /// 中文名留在母程序这一侧映射。
        /// </summary>
        public static string EngineDisplayName(string token)
        {
            if (string.IsNullOrEmpty(token)) return null;
            switch (token)
            {
                case "wps-word": return "WPS 文字 (Kwps)";
                case "wps-excel": return "WPS 表格 (Ket)";
                case "wps-ppt": return "WPS 演示 (Kwpp)";
                case "ms-word": return "Microsoft Word";
                case "ms-excel": return "Microsoft Excel";
                case "ms-ppt": return "Microsoft PowerPoint";
                case "minipdf": return "MiniPdf（内置引擎）";
                default:
                    if (token.StartsWith("none-")) return "未检测到可用的 " + (token.Substring(5) == "excel" ? "表格" : token.Substring(5) == "ppt" ? "演示" : "文字") + "程序";
                    return token;
            }
        }

        // ===== v1.2.2 修（2026-11-08 拍板）：三方法同构模板化 =====
        // ExportWord/ExportExcel/ExportPpt 原是三份逐行同构的 COM 流程（探测 → 快照 → 拉起 → 打开 → 导出 → 校验 → 清退），
        // 差别只在 ProgID 对、进程名、人话引擎名、提示文案与"打开/导出/关闭"三个动作上——收进一张参数表
        // <see cref="ComRecipe"/>＋一个执行体 <see cref="ExportWithCom"/>，三方法各自变薄。
        // v1.2.2 已修的三处口径一行不变：isWps 按「探测命中的 ProgID」判（不看 FullName）、转换前 PID 快照
        // （WaitComExit 只等本次新拉起的实例）、导出成功判据 OutputOk（存在＋非 0 字节＋%PDF- 头）。
        private sealed class ComRecipe
        {
            public string WpsProgId, MsProgId;     // ProgID 探测对（WPS 优先，与 ProbeEngineToken 同一名单）
            public string WpsProc, MsProc;         // SnapshotPids/WaitComExit 的进程名（wps/et/wpp 与 WINWORD/EXCEL/POWERPNT）
            public string WpsLabel, MsLabel;       // 日志里的人话引擎名（与 EngineDisplayName 同字面）
            public string Missing;                 // 两家都没装时的返回
            public string FailPrefix;              // 导出异常消息前缀
            public bool SetDisplayAlerts;          // Word/Excel 要关告警弹窗；PPT 没有 DisplayAlerts 这一手
            public object DisplayAlertsValue;      // Word＝0（wdAlertsNone）、Excel＝false
            public Func<dynamic, string, dynamic> Open;   // (app, input) → 文档对象
            public Action<dynamic, string> Export;        // (doc, output)：先 ExportAsFixedFormat，兼容老 WPS 再 SaveAs 兜底
            public Action<dynamic> Close;                 // 文档关闭（Word/Excel＝Close(false)，PPT＝Close()）
        }

        /// <summary>同构执行体：按参数表走一遍 COM 导出；各步异常/清理的次序与抽模板前三方法逐行一致。</summary>
        private static string ExportWithCom(ComRecipe r, string input, string output)
        {
            Type t = Type.GetTypeFromProgID(r.WpsProgId);
            bool isWps = t != null;   // 按命中 ProgID 判定，不看 FullName（v1.2.2 审计 P1 的口径）
            if (t == null) t = Type.GetTypeFromProgID(r.MsProgId);
            if (t == null) return r.Missing;
            int[] pidsBefore = SnapshotPids(isWps ? r.WpsProc : r.MsProc);   // 转换前快照：只等本次新拉起的实例
            FileLogger.Write("Worker 引擎：" + (isWps ? r.WpsLabel : r.MsLabel));
            dynamic app = Activator.CreateInstance(t);
            dynamic doc = null;
            try
            {
                app.Visible = false;
                if (r.SetDisplayAlerts) { try { app.DisplayAlerts = r.DisplayAlertsValue; } catch { } }
                doc = r.Open(app, input);
                r.Export(doc, output);
                return OutputOk(output) ? null : "导出未生成 PDF 文件。";
            }
            catch (Exception ex) { return r.FailPrefix + ex.Message; }
            finally
            {
                if (doc != null) { try { r.Close(doc); } catch { } Release(doc); }
                try { app.Quit(); } catch { }
                Release(app);
                WaitComExit(isWps ? r.WpsProc : r.MsProc, pidsBefore);
            }
        }

        public static string ExportWord(string input, string output)
        {
            return ExportWithCom(new ComRecipe
            {
                WpsProgId = "Kwps.Application", MsProgId = "Word.Application",
                WpsProc = "wps", MsProc = "WINWORD",
                WpsLabel = "WPS 文字 (Kwps)", MsLabel = "Microsoft Word",
                Missing = "未检测到 WPS 文字或 Microsoft Word，无法转换 Word/文本文档。",
                FailPrefix = "Word 导出失败：",
                SetDisplayAlerts = true, DisplayAlertsValue = 0,   // wdAlertsNone
                Open = (app, inFile) => app.Documents.Open(inFile, ReadOnly: true),
                Export = (doc, outPath) =>
                {
                    try { doc.ExportAsFixedFormat(outPath, 17); }   // wdExportFormatPDF = 17
                    catch { doc.SaveAs2(outPath, 17); }             // 兼容无 ExportAsFixedFormat 的 WPS 版本
                },
                Close = doc => doc.Close(false)
            }, input, output);
        }

        public static string ExportExcel(string input, string output)
        {
            return ExportWithCom(new ComRecipe
            {
                WpsProgId = "Ket.Application", MsProgId = "Excel.Application",
                WpsProc = "et", MsProc = "EXCEL",
                WpsLabel = "WPS 表格 (Ket)", MsLabel = "Microsoft Excel",
                Missing = "未检测到 WPS 表格或 Microsoft Excel，无法转换 Excel 文档。",
                FailPrefix = "Excel 导出失败：",
                SetDisplayAlerts = true, DisplayAlertsValue = false,
                Open = (app, inFile) => app.Workbooks.Open(inFile, ReadOnly: true, UpdateLinks: 0),
                Export = (doc, outPath) =>
                {
                    try { doc.ExportAsFixedFormat(0, outPath); }    // xlTypePDF = 0
                    catch { doc.SaveAs(outPath, 57); }              // xlTypePDF = 57（SaveAs 变体）
                },
                Close = doc => doc.Close(false)
            }, input, output);
        }

        public static string ExportPpt(string input, string output)
        {
            return ExportWithCom(new ComRecipe
            {
                WpsProgId = "Kwpp.Application", MsProgId = "PowerPoint.Application",
                WpsProc = "wpp", MsProc = "POWERPNT",
                WpsLabel = "WPS 演示 (Kwpp)", MsLabel = "Microsoft PowerPoint",
                Missing = "未检测到 WPS 演示或 Microsoft PowerPoint，无法转换 PPT 文档。",
                FailPrefix = "PPT 导出失败：",
                SetDisplayAlerts = false,
                Open = (app, inFile) => app.Presentations.Open(inFile, ReadOnly: true, WithWindow: false),
                Export = (doc, outPath) =>
                {
                    try { doc.ExportAsFixedFormat(outPath, 2); }    // ppFixedFormatTypePDF = 2
                    catch { doc.SaveAs(outPath, 32); }              // ppSaveAsPDF = 32
                },
                Close = doc => doc.Close()
            }, input, output);
        }

        /// <summary>
        /// v1.2.2 修（审计 P1）：转换开始前该进程名的 PID 快照。
        /// 旧 WaitComExit 按进程名等待**全机**所有同名进程——用户自己开着的 Word/WPS 也被各等满 6 秒，
        /// 批量转换每文件最多白等 6s×N；现在只等快照之后新出现的 PID（即本次 COM 拉起的实例）。
        /// </summary>
        private static int[] SnapshotPids(string processName)
        {
            try
            {
                var ids = new List<int>();
                foreach (Process p in Process.GetProcessesByName(processName))
                {
                    try { ids.Add(p.Id); } catch { }
                    try { p.Dispose(); } catch { }
                }
                return ids.ToArray();
            }
            catch { return new int[0]; }
        }

        // COM 应用 Quit 后等待进程真正退出，避免残留僵尸进程
        // 注意：WPS 文字进程名是 "wps"（不是 "KWPS"），表格 "et"、演示 "wpp"
        private static void WaitComExit(string processName, int[] pidsBefore)
        {
            try
            {
                var before = new HashSet<int>(pidsBefore ?? new int[0]);
                foreach (Process p in Process.GetProcessesByName(processName))
                {
                    try { if (!before.Contains(p.Id) && !p.HasExited) p.WaitForExit(6000); } catch { }
                    try { p.Dispose(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// 导出成功判据（v1.2.2 加严）：文件存在、非 0 字节、且头 5 字节是 %PDF-。
        /// 旧判据只查存在+非空——转换被中断留下的残缺 PDF 会被当成功回传进打印。
        /// Office/WPS/MS 导出器写的都是干净文件头；PDF 规范允许前导垃圾但这里不放宽（导出器产物不属该形态）。
        /// </summary>
        private static bool OutputOk(string output)
        {
            try
            {
                if (!File.Exists(output) || new FileInfo(output).Length == 0) return false;
                using (var fs = new FileStream(output, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] b = new byte[5];
                    return fs.Read(b, 0, 5) == 5
                        && b[0] == 0x25 && b[1] == 0x50 && b[2] == 0x44 && b[3] == 0x46 && b[4] == 0x2D;   // %PDF-
                }
            }
            catch { return false; }
        }

        private static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.FinalReleaseComObject(o); } catch { }
        }
    }
}
