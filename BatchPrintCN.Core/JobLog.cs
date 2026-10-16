// This file is part of PrintPile (https://printpile.cn).
//
// Copyright (C) 2026 the PrintPile developers
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号·改1/改2：日志行的**唯一拼装处**（纯函数，不写盘、不打网络，自检直接逐字段核对）。
    ///
    /// 口径：日志一律中文（与 V1~V4.9 既有日志行同口径，日志文件里没有双语机制，界面文案才走资源键）；
    /// 既有行（正在打印／转换子进程退出码／完成：{名}／失败：{名}）一字未改，本文件产出的都是**新增行**。
    /// </summary>
    public static class JobLog
    {
        private static string N(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        /// <summary>转换完成行（Office→PDF）：引擎哪家 + 耗时 + 页数 + 页面尺寸（改2 的"转换行补充"与改1 的"页数入日志"同一行）。</summary>
        public static string ConversionDone(string name, string engineName, double seconds, OverflowScan scan)
        {
            var sb = new StringBuilder();
            sb.Append("转换完成：").Append(name).Append("（引擎 ").Append(EngineText(engineName))
              .Append("，耗时 ").Append(N(Math.Max(0, seconds))).Append(" 秒");
            AppendPagesAndSize(sb, scan);
            sb.Append("）");
            return sb.ToString();
        }

        /// <summary>版面体检行（PDF 直入，没有转换这一步）：页数 + 页面尺寸。</summary>
        public static string LayoutProbe(string name, OverflowScan scan)
        {
            var sb = new StringBuilder();
            sb.Append("版面体检：").Append(name).Append("（");
            bool sizeOnly = scan == null || !scan.Scanned;
            if (!sizeOnly) sb.Append("共 ").Append(scan.PageCount).Append(" 页，页面 ").Append(scan.PaperText);
            else sb.Append("页面尺寸未取到");
            sb.Append("）");
            return sb.ToString();
        }

        private static void AppendPagesAndSize(StringBuilder sb, OverflowScan scan)
        {
            if (scan == null || !scan.Scanned) { sb.Append("，页面尺寸未取到"); return; }
            sb.Append("，共 ").Append(scan.PageCount).Append(" 页，页面 ").Append(scan.PaperText);
        }

        /// <summary>
        /// 超界警告行（逐页一条）。两级措辞不同、各自说各自的依据：
        /// 真越出页面框 →「⚠ 第 X 页内容超出右缘约 18.0mm，打印将被裁切（源文档内容宽于页面）」；
        /// 只是贴进页边不可打印带 →「⚠ 第 X 页内容已贴到右缘（距页边 0.0mm，进入约 5.0mm 不可打印带），这一带多数打印机打不出来」。
        /// 多边都有问题时按量从大到小并列。
        /// </summary>
        public static string OverflowWarning(PageBoundsReport p)
        {
            if (p == null || !p.HasOverflow) return null;
            var edges = p.EdgesOver();
            var sb = new StringBuilder();
            sb.Append("⚠ 第 ").Append(p.Page).Append(" 页内容");
            if (p.Kind == PageOverflowKind.BeyondPage)
            {
                sb.Append("超出");
                AppendEdges(sb, edges);
                sb.Append("，打印将被裁切（源文档内容宽于页面）");
            }
            else
            {
                sb.Append("已进入");
                AppendEdges(sb, edges);
                sb.Append("（墨迹距页边 ").Append(N(Math.Max(0, GapOf(p)))).Append("mm，落在约 ")
                  .Append(N(OverflowScan.UnprintableBandMm)).Append("mm 宽的不可打印带内），这一带多数打印机打不出来");
            }
            return sb.ToString();
        }

        private static void AppendEdges(StringBuilder sb, List<KeyValuePair<OverflowEdge, double>> edges)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (i > 0) sb.Append("、");
                sb.Append(PageBoundsReport.EdgeText(edges[i].Key)).Append("约 ").Append(N(edges[i].Value)).Append("mm");
            }
        }

        /// <summary>贴边那一级的"距页边多少毫米"：取最严重那条边的留白。</summary>
        private static double GapOf(PageBoundsReport p)
        {
            double g = p.GapRightMm;
            if (p.GapLeftMm < g) g = p.GapLeftMm;
            if (p.GapTopMm < g) g = p.GapTopMm;
            if (p.GapBottomMm < g) g = p.GapBottomMm;
            return g;
        }

        /// <summary>
        /// A 口径（源文档版面体检）警告行：「⚠ 源文档版面体检：第 1 张表格宽 189.8mm 超出 A4 版心（184.6mm）约 5.2mm…」
        /// </summary>
        public static string SourceLayoutWarning(SourceLayoutFinding f)
        {
            if (f == null || !f.Checked || !f.Found) return null;
            return "⚠ 源文档版面体检：第 " + f.OffendingTable + " 张表格宽 " + N(f.WidestTableMm) + "mm 超出 "
                + f.PageText + " 版心（" + N(f.TextWidthMm) + "mm）约 " + N(f.OverMm) + "mm"
                + (f.MultipleSections ? "（文档有多个分节，按该表所在节的页面设置算）" : "")
                + "——按原文打印右侧会被裁掉。可在 Word 里选中表格用\"根据窗口调整表格\"，或把列宽合计压进版心。";
        }

        public static List<string> OverflowWarnings(OverflowScan scan)
        {
            var list = new List<string>();
            if (scan == null || !scan.Scanned) return list;
            foreach (PageBoundsReport p in scan.Pages) list.Add(OverflowWarning(p));
            if (scan.Truncated)
                list.Add("⚠ 超界检测只扫了前 " + scan.ScannedPages + " 页（全文 " + scan.PageCount + " 页），后面的页未检测");
            return list;
        }

        /// <summary>扫描没做成也要说一句（不静默、不谎报"没有超界"）。</summary>
        public static string ScanFailedNote(string name, OverflowScan scan)
        {
            if (scan != null && scan.Scanned) return null;
            return "版面体检未完成：" + PathName(name) + "（" + (scan == null ? "无结果" : scan.Error) + "）——不代表没有超界";
        }

        private static string PathName(string name) { return string.IsNullOrEmpty(name) ? "该文件" : name; }

        private static string EngineText(string engineName)
        {
            return string.IsNullOrWhiteSpace(engineName) ? "未回报" : engineName;
        }

        /// <summary>
        /// 作业摘要行（改2）：字段齐——文件名／类型／实际引擎／页数与纸张方向／缩放实际生效值／黑白彩色／份数／打印机／结果。
        /// 例：完成：表.docx（Word，引擎 WPS 文字 (Kwps)，2 页 A4 竖版，缩放 缩放到合适页面，黑白，1 份 → Microsoft Print to PDF）：成功
        /// </summary>
        public static string Summary(string name, string kindText, string engineName, int pages,
                                    PrintOptions opt, int copies, string printer, string result)
        {
            var sb = new StringBuilder();
            sb.Append("完成：").Append(PathName(name)).Append("（")
              .Append(kindText ?? "其他").Append("，引擎 ").Append(EngineText(EngineShort(engineName)))
              .Append("，").Append(pages).Append(" 页 ").Append(PaperText(opt))
              .Append("，缩放 ").Append(opt == null ? "未取到" : opt.ScalingText())
              .Append("，").Append(ColorText(opt))
              .Append("，").Append(Math.Max(1, copies)).Append(" 份 → ").Append(PrinterText(printer))
              .Append("）：").Append(string.IsNullOrEmpty(result) ? "成功" : result);
            return sb.ToString();
        }

        /// <summary>引擎字段：非 Office 文件通道记 "-"＝这次不涉及转换；空值如实说没回报，不猜。</summary>
        private static string EngineShort(string engineName)
        {
            if (string.IsNullOrWhiteSpace(engineName)) return "未回报";
            if (engineName.Trim() == "-") return "不涉及转换";
            return engineName;
        }

        public static string PaperText(PrintOptions opt)
        {
            if (opt == null) return "纸张未取到";
            string orient = opt.Orientation == OrientationMode.Portrait ? "竖版"
                : opt.Orientation == OrientationMode.Landscape ? "横版" : "方向自动";
            return (string.IsNullOrWhiteSpace(opt.PaperDisplayName) ? "纸张" : opt.PaperDisplayName) + " " + orient;
        }

        public static string ColorText(PrintOptions opt)
        {
            return opt != null && opt.Color == ColorMode.BlackWhite ? "黑白" : "彩色";
        }

        private static string PrinterText(string printer)
        {
            return string.IsNullOrWhiteSpace(printer) ? "系统默认打印机" : printer;
        }
    }

    /// <summary>
    /// 45 号·改1/改2：单个文件本次打印的"版面体检"结果——挂在 <see cref="PrintJob.Diag"/> 上，
    /// 纯观测数据：**没有任何一处打印决策读它**（打印仍忠实原文，不自动缩放、不改页序、不改份数）。
    /// </summary>
    public sealed class PrintDiagnostics
    {
        /// <summary>实际调用的转换程序（Microsoft Word / WPS 文字 (Kwps) / MiniPdf）；未转换＝null。</summary>
        public string EngineName;
        /// <summary>转换耗时（秒）；未转换＝0。</summary>
        public double ConvertSeconds;
        /// <summary>超界扫描结论（null＝没扫；<c>Scanned=false</c>＝扫了但没成，两者都不等于"没有超界"）。</summary>
        public OverflowScan Overflow;
        /// <summary>A 口径：源文档版面体检（目前只有 .docx 会真检；其余如实标"暂不体检"）。</summary>
        public SourceLayoutFinding SourceLayout;
        /// <summary>本次实际出纸的页数（Office 走转换后 PDF 的页数）。</summary>
        public int Pages;
        /// <summary>
        /// Office 无感通道内部会再起一台 BatchPrinter 打临时 PDF——那一层是"同一次打印的内层"，
        /// 它那份体检行/摘要行与外层重复且会把引擎写成"不涉及转换"，所以内层挂这个旗标，只打日志的外层说话。
        /// </summary>
        public bool Silent;

        /// <summary>任一口径报了问题就算有问题（B 口径看 PDF 墨迹，A 口径看源文档表格宽）。</summary>
        public bool HasOverflow { get { return (Overflow != null && Overflow.HasOverflow) || (SourceLayout != null && SourceLayout.Found); } }
        public int OverflowPageCount { get { return Overflow == null ? 0 : Overflow.OverflowPageCount; } }
    }
}
