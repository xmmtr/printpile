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
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号（V4.9.2）断言：改1 转换超界检测（两口径）·改2 逐文件作业摘要日志·改3 打印预览。
    ///
    /// 全程离线：样张由本文件自己构造（合成 PDF 与合成 docx），不读仓库里的取证原件、不联网、不启动 Office；
    /// 唯一可能碰 COM 的是预览缓存——测试里用"预置缓存产物"的方式验缓存逻辑，不做真转换（真转换链路由
    /// V4.4 双纸虚拟打印那一段既有断言覆盖）。
    /// 红线复核：打印引擎判定逻辑一行未改（PrintCore 里新增的只有"只读扫描 + 日志行"，见 00-项目说明）。
    /// </summary>
    public static partial class WpfTests
    {
        internal static string V492Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            var mode0 = Application.Current.ShutdownMode;
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV492-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                sb.AppendLine("======== 45 号·改1 B 口径：PDF 墨迹体检（真越界 / 进入页边不可打印带）========");
                bad.AddRange(V492Scan(sb, dir));
                sb.AppendLine("======== 45 号·改1 A 口径：源文档版面体检（docx 版心 vs 表格栅格）========");
                bad.AddRange(V492Source(sb, dir));
                sb.AppendLine("======== 45 号·改1 状态列「内容超界」黄警告（行高不动）========");
                bad.AddRange(V492Status(sb, dir));
                sb.AppendLine("======== 45 号·改2 逐文件作业摘要行与转换行字段 ======== ");
                bad.AddRange(V492Log(sb, dir));
                sb.AppendLine("======== 45 号·改3 打印预览（双击入口·范围联动·超界标注·缓存复用）========");
                bad.AddRange(V492Preview(sb, dir));

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("V4.92 异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally { Application.Current.ShutdownMode = mode0; }

            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V492三项定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            try { Directory.Delete(dir, true); } catch { }
            return bad.Count == 0
                ? "通过（改1：合成样张逐条命中——正常样张不误报、贴边样张报进带深度、双页样张按页数汇总，扫描全程只读（扫描前后文件字节数与修改时间一字不变），"
                  + "A 口径按表格所在节的版心算（多分节文档不再拿错页边距），非 docx 如实回\"暂不体检\"；状态列新增种类走 40 号同一套结构化上色、六行行高仍只剩 36.8；"
                  + "改2：作业摘要行逐字段齐（类型/引擎/页数/纸张方向/缩放生效值/黑白彩色/份数/打印机/结果）、转换行含耗时与页面尺寸、两级警告行措辞分得开、无 {0} 残留；"
                  + "改3：预览标题与信息条逐字段、页序与打印同源（范围/奇偶联动只显将打印的页）、超界标注数值与改1 同源、点缩略图切页、"
                  + "「加入打印队列」＝勾选该行 + 一句日志 + 关窗、缓存命中第二次不再转换；全程离线不启动 Office）"
                : "失败：" + string.Join("；", bad);
        }

        // ===== 改1 B 口径：PDF 墨迹体检 =====
        private static List<string> V492Scan(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            string ok = Path.Combine(dir, "ok.pdf");
            string near = Path.Combine(dir, "near-edge.pdf");
            string beyond = Path.Combine(dir, "beyond-page.pdf");
            string two = Path.Combine(dir, "two-pages.pdf");
            BuildInkPdf(ok, 1, 20, 80);            // 墨迹 20..80mm：四边留白充足
            BuildInkPdf(near, 1, 10, 209.5);       // 墨迹贴到右缘（距边 0.5mm）
            BuildInkPdf(beyond, 1, 10, 225);       // 声明画到框外 15mm（pdfium 仍裁在页内 → 只能报贴边）
            BuildInkPdf(two, 2, 10, 209.5);        // 两页都贴边

            OverflowScan sOk = PageOverflowScanner.Scan(ok);
            OverflowScan sNear = PageOverflowScanner.Scan(near);
            OverflowScan sBeyond = PageOverflowScanner.Scan(beyond);
            OverflowScan sTwo = PageOverflowScanner.Scan(two);

            sb.AppendLine("— 正常样张：Scanned=" + sOk.Scanned + " 纸张=" + sOk.PaperText + " 问题页=" + sOk.OverflowPageCount
                + "；贴边样张：问题页=" + sNear.OverflowPageCount + " 级别=" + (sNear.HasOverflow ? sNear.Pages[0].Kind.ToString() : "无")
                + " 最严重=" + (sNear.HasOverflow ? sNear.Pages[0].WorstEdgeText : "-") + " " + (sNear.HasOverflow ? sNear.Pages[0].MaxMm.ToString("0.0") : "0") + "mm"
                + " 距页边=" + (sNear.HasOverflow ? sNear.Pages[0].GapRightMm.ToString("0.0") : "-") + "mm");
            Check(bad, "改1 正常样张不误报（四边留白都超过不可打印带），且页面尺寸认成 A4 竖版",
                sOk.Scanned && sOk.OverflowPageCount == 0 && sOk.PaperText.StartsWith("A4 竖版") && sOk.PaperText.Contains("210.0×297.0mm"));
            Check(bad, "改1 贴边样张命中：级别＝进入不可打印带（NearEdge）、边＝右缘、深度按 5mm 带宽算出来（4.5~5.0mm）",
                sNear.Scanned && sNear.OverflowPageCount == 1
                && sNear.Pages[0].Kind == PageOverflowKind.NearEdge
                && sNear.Pages[0].WorstEdge == OverflowEdge.Right
                && sNear.Pages[0].IntoRightMm > 4.0 && sNear.Pages[0].IntoRightMm <= OverflowScan.UnprintableBandMm
                && sNear.Pages[0].GapRightMm >= 0 && sNear.Pages[0].GapRightMm < OverflowScan.UnprintableBandMm);
            Check(bad, "改1 如实：画到框外的样张在 PDF 层面也只能报贴边（pdfium 把内容裁在页面框内，随包 DLL 的取对象边界接口一调就崩）——"
                  + "所以 A 口径（源文档体检）才是附7 这类案子的主信号",
                sBeyond.Scanned && sBeyond.OverflowPageCount == 1 && sBeyond.BeyondPageCount == 0
                && sBeyond.Pages[0].Kind == PageOverflowKind.NearEdge);
            Check(bad, "改1 多页：两页都贴边时报 2 页（状态列与日志按页数汇总）",
                sTwo.Scanned && sTwo.OverflowPageCount == 2 && sTwo.PageCount == 2);

            // 只读性：扫描前后文件字节数与修改时间一字不变
            var fi1 = new FileInfo(near);
            long len1 = fi1.Length;
            DateTime wt1 = fi1.LastWriteTimeUtc;
            PageOverflowScanner.Scan(near);
            var fi2 = new FileInfo(near);
            sb.AppendLine("— 只读复核：扫描前后字节 " + len1 + "→" + fi2.Length + " 修改时间不变=" + (wt1 == fi2.LastWriteTimeUtc));
            Check(bad, "改1 扫描全程只读（不 Save、不落盘：文件字节数与修改时间一字不变）",
                fi2.Length == len1 && fi2.LastWriteTimeUtc == wt1);

            OverflowScan miss = PageOverflowScanner.Scan(Path.Combine(dir, "没有这个文件.pdf"));
            sb.AppendLine("— 打不开时如实回报：Scanned=" + miss.Scanned + " err=" + miss.Error);
            Check(bad, "改1 扫不成时不谎报\"没有超界\"（Scanned=false 且带原因）",
                !miss.Scanned && !miss.HasOverflow && !string.IsNullOrEmpty(miss.Error));
            return bad;
        }

        // ===== 改1 A 口径：源文档版面体检 =====
        private static List<string> V492Source(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            // 版心 = (11906 − 720 − 720) twip = 184.6mm；表格栅格合计 10750 twip ≈ 189.7mm → 超出约 5.1mm
            string wide = Path.Combine(dir, "wide-table.docx");
            BuildDocx(wide, 11906, 720, 720, new[] { 3000, 3000, 2375, 2375 });
            string fit = Path.Combine(dir, "fits.docx");
            BuildDocx(fit, 11906, 720, 720, new[] { 2600, 2600, 2633, 2633 });
            // 多分节：第一节边距 720（管表格），末节边距 1800（不该拿它算）
            string multi = Path.Combine(dir, "multi-section.docx");
            BuildDocxSections(multi, new[] { Tuple.Create(11906, 720, 720, 10750) }, new[] { Tuple.Create(11906, 1800, 1800) });

            SourceLayoutFinding fWide = SourceLayoutProbe.Probe(wide, DocKind.Word);
            SourceLayoutFinding fFit = SourceLayoutProbe.Probe(fit, DocKind.Word);
            SourceLayoutFinding fMulti = SourceLayoutProbe.Probe(multi, DocKind.Word);
            SourceLayoutFinding fPdf = SourceLayoutProbe.Probe(wide.Replace(".docx", ".pdf"), DocKind.Pdf);
            sb.AppendLine("— 超宽 docx：Checked=" + fWide.Checked + " Found=" + fWide.Found + " 页面=" + fWide.PageText
                + " 版心=" + fWide.TextWidthMm.ToString("0.0") + "mm 最宽表=" + fWide.WidestTableMm.ToString("0.0")
                + "mm（第 " + fWide.OffendingTable + " 张）超出=" + fWide.OverMm.ToString("0.0") + "mm");
            sb.AppendLine("— 刚好放得下：Found=" + fFit.Found + " 版心=" + fFit.TextWidthMm.ToString("0.0") + " 表宽=" + fFit.WidestTableMm.ToString("0.0"));
            sb.AppendLine("— 多分节：Found=" + fMulti.Found + " 版心=" + fMulti.TextWidthMm.ToString("0.0") + " 超出=" + fMulti.OverMm.ToString("0.0")
                + " 多节标记=" + fMulti.MultipleSections);
            sb.AppendLine("— 非 docx：" + fPdf.Error + "（Checked=" + fPdf.Checked + "）");
            Check(bad, "改1 A 口径命中：表格栅格合计比版心宽 5mm 时 Found=true 且超出量在 4.5~6.0mm（附7 原件同口径实测 5.2mm）",
                fWide.Checked && fWide.Found && fWide.OverMm >= 4.5 && fWide.OverMm <= 6.0
                && fWide.PageText.StartsWith("A4") && fWide.TableCount == 1);
            Check(bad, "改1 A 口径不误报：表格正好放得进版心时 Found=false",
                fFit.Checked && !fFit.Found);
            Check(bad, "改1 A 口径按表格所在节算：多分节文档不再拿末节页边距（拿错会把 5.1mm 报成 43mm）",
                fMulti.Found && fMulti.MultipleSections && fMulti.OverMm >= 4.5 && fMulti.OverMm <= 6.0);
            Check(bad, "改1 A 口径覆盖范围如实限定：非 docx 回\"暂不体检\"，不猜、也不算\"没有超宽\"",
                !fPdf.Checked && fPdf.Error != null && fPdf.Error.Contains("暂不体检"));
            string warn = JobLog.SourceLayoutWarning(fWide);
            Check(bad, "改1 A 口径警告行带得出三个数（表宽/版心宽/超出量）并给处置建议",
                warn != null && warn.Contains("源文档版面体检") && warn.Contains("189") && warn.Contains("184.6")
                && warn.Contains("mm") && warn.Contains("根据窗口调整表格"));
            return bad;
        }

        // ===== 改1 状态列 =====
        private static List<string> V492Status(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            string one = Path.Combine(dir, "one.pdf");
            string two = Path.Combine(dir, "two2.pdf");
            BuildInkPdf(one, 1, 10, 209.5);
            BuildInkPdf(two, 2, 10, 209.5);
            string wide = Path.Combine(dir, "wide2.docx");
            BuildDocx(wide, 11906, 720, 720, new[] { 3000, 3000, 2375, 2375 });

            var jobOne = new PrintJob { FilePath = one, Kind = DocKind.Pdf };
            jobOne.Diag = new PrintDiagnostics { Overflow = PageOverflowScanner.Scan(one), Pages = 1 };
            var jobTwo = new PrintJob { FilePath = two, Kind = DocKind.Pdf };
            jobTwo.Diag = new PrintDiagnostics { Overflow = PageOverflowScanner.Scan(two), Pages = 2 };
            var jobSrc = new PrintJob { FilePath = wide, Kind = DocKind.Word };
            jobSrc.Diag = new PrintDiagnostics { SourceLayout = SourceLayoutProbe.Probe(wide, DocKind.Word) };
            var jobClean = new PrintJob { FilePath = Path.Combine(dir, "ok2.pdf"), Kind = DocKind.Pdf };
            BuildInkPdf(jobClean.FilePath, 1, 20, 80);
            jobClean.Diag = new PrintDiagnostics { Overflow = PageOverflowScanner.Scan(jobClean.FilePath) };
            var jobNone = new PrintJob { FilePath = one, Kind = DocKind.Pdf };

            string t1, t2, t3, t4, t5;
            bool b1 = MainWindow.OverflowStatusText(jobOne, out t1);
            bool b2 = MainWindow.OverflowStatusText(jobTwo, out t2);
            bool b3 = MainWindow.OverflowStatusText(jobSrc, out t3);
            bool b4 = MainWindow.OverflowStatusText(jobClean, out t4);
            bool b5 = MainWindow.OverflowStatusText(jobNone, out t5);
            sb.AppendLine("— 中文态：单页=\"" + t1 + "\" 多页=\"" + t2 + "\" 仅 A 口径=\"" + t3 + "\" 没问题=" + b4 + " 没体检=" + b5);
            Check(bad, "改1 状态列三种情形各一条：单页报第几页、多页报数量、只有源文档体检时报超出毫米数",
                b1 && t1.Contains("第 1 页") && t1.Contains("裁切")
                && b2 && t2.Contains("2 页") && t2.Contains("详见日志")
                && b3 && t3.Contains("源文档表格") && t3.Contains("mm")
                && !b4 && !b5);
            App.ApplyLanguage(AppLanguage.EnUS);
            string e1, e2, e3;
            MainWindow.OverflowStatusText(jobOne, out e1);
            MainWindow.OverflowStatusText(jobTwo, out e2);
            MainWindow.OverflowStatusText(jobSrc, out e3);
            App.ApplyLanguage(AppLanguage.ZhCN);
            sb.AppendLine("— 英文态：单页=\"" + e1 + "\" 多页=\"" + e2 + "\" 源文档=\"" + e3 + "\"");
            Check(bad, "改1 状态列文案中英成对（都走资源键，不出现未解析键名）",
                e1.Contains("page edge") && e2.Contains("pages") && e3.Contains("wider")
                && !e1.StartsWith("s.Status") && !e2.StartsWith("s.Status") && !e3.StartsWith("s.Status"));

            // 行高铁律：新状态种类写进真实一行的状态列后，六行行高仍只剩一档
            var w = new MainWindow();
            w.UpdateLayoutForTest(1120, 700);
            w.ShowPanelForTest("print");
            var rows = new List<RowVm>();
            for (int i = 0; i < 6; i++)
            {
                string f = Path.Combine(dir, "row" + i + ".pdf");
                BuildInkPdf(f, 1, i == 2 ? 10 : 20, i == 2 ? 209.5 : 80);
                var j = new PrintJob { FilePath = f, Kind = DocKind.Pdf };
                j.Diag = new PrintDiagnostics { Overflow = PageOverflowScanner.Scan(f) };
                var r = w.AddRowForTest(j);
                string txt;
                r.SetStatus(MainWindow.OverflowStatusText(j, out txt) ? txt : Res("s.StatusDoneRow"),
                            false, MainWindow.OverflowStatusText(j, out txt) ? RowStatusKind.ContentOverflow : RowStatusKind.Done);
                rows.Add(r);
            }
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            var grid = w.FindName("Grid") as DataGrid;
            var heights = new List<double>();
            if (grid != null)
                foreach (RowVm r in rows)
                {
                    var rowObj = grid.ItemContainerGenerator.ContainerFromItem(r);
                    var dr = rowObj as DataGridRow ?? V47Ancestor(rowObj as DependencyObject, typeof(DataGridRow)) as DataGridRow;
                    if (dr != null && dr.ActualHeight > 0) heights.Add(dr.ActualHeight);
                }
            var distinct = heights.Count == 0 ? new List<double>() : heights.Distinct().OrderBy(x => x).ToList();
            sb.AppendLine("— 行高复量（六行含两行超界黄警告）：取到 " + heights.Count + " 格，去重后=" + string.Join("/", distinct.Select(x => x.ToString("0.0")))
                + "（40 号口径 36~37.6）");
            Check(bad, "改1 铁律复量：状态列写\"内容超界\"文案后六行行高仍只剩一档（40 号 36~37.6 区间，不撑高）",
                heights.Count >= 4 && distinct.Count == 1 && distinct[0] >= 36 && distinct[0] <= 37.6);
            return bad;
        }

        // ===== 改2 作业摘要行与转换行 =====
        private static List<string> V492Log(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            var opt = new PrintOptions
            {
                Paper = System.Drawing.Printing.PaperKind.A4,
                PaperDisplayName = "A4",
                Orientation = OrientationMode.Portrait,
                Color = ColorMode.BlackWhite,
                Duplex = DuplexMode.LongEdge,
                Scale = ScaleMode.FitPage,
            };
            string sum = JobLog.Summary("表.docx", "Word", "WPS 文字 (Kwps)（com(自动)）", 2, opt, 3, "Microsoft Print to PDF", "成功");
            string sumNoEngine = JobLog.Summary("件.pdf", "PDF", "-", 1, opt, 1, "", "失败");
            string done = JobLog.ConversionDone("表.docx", "Microsoft Word", 3.42, PageOverflowScanner.Scan(Path.Combine(dir, "ok.pdf")));
            string probe = JobLog.LayoutProbe("件.pdf", PageOverflowScanner.Scan(Path.Combine(dir, "near-edge.pdf")));
            sb.AppendLine("— 摘要行：" + sum);
            sb.AppendLine("— 摘要行（无引擎/默认打印机/失败）：" + sumNoEngine);
            sb.AppendLine("— 转换行：" + done);
            sb.AppendLine("— 体检行：" + probe);
            Check(bad, "改2 作业摘要行字段齐：文件名/类型/引擎/页数/纸张方向/缩放生效值/黑白彩色/份数/打印机/结果 十项一次说全",
                sum.StartsWith("完成：表.docx") && sum.Contains("Word") && sum.Contains("WPS 文字 (Kwps)")
                && sum.Contains("2 页") && sum.Contains("A4 竖版") && sum.Contains("缩放 缩放到合适页面")
                && sum.Contains("黑白") && sum.Contains("3 份") && sum.Contains("→ Microsoft Print to PDF") && sum.EndsWith("：成功"));
            Check(bad, "改2 不谎报：非 Office 文件如实写\"不涉及转换\"、打印机为空写\"系统默认打印机\"、失败态结果字段说失败",
                sumNoEngine.Contains("不涉及转换") && sumNoEngine.Contains("系统默认打印机") && sumNoEngine.EndsWith("：失败"));
            Check(bad, "改2 转换行补齐三件事：实际调用的转换程序、耗时（一位小数）、页数与页面尺寸",
                done.Contains("Microsoft Word") && done.Contains("耗时 3.4 秒") && done.Contains("共 1 页") && done.Contains("A4 竖版（210.0×297.0mm）"));
            Check(bad, "改1/改2 体检行（PDF 直入没有转换这一步）也带页数与页面尺寸",
                probe.Contains("版面体检") && probe.Contains("共 1 页") && probe.Contains("210.0×297.0mm"));
            Check(bad, "改2 全部新行无 {0} 残留（占位符都换成了实际值）",
                !sum.Contains("{0}") && !sumNoEngine.Contains("{0}") && !done.Contains("{0}") && !probe.Contains("{0}"));

            var scan = PageOverflowScanner.Scan(Path.Combine(dir, "near-edge.pdf"));
            string warn = JobLog.OverflowWarning(scan.Pages[0]);
            Check(bad, "改1 两级警告行分得开：贴边说\"已进入…不可打印带\"并带墨迹距页边的实测留白，真越界才说\"超出…将被裁切\"",
                warn.Contains("已进入右缘") && warn.Contains("不可打印带") && warn.Contains("墨迹距页边") && !warn.Contains("超出右缘"));
            var fake = new PageBoundsReport { Page = 3, OverRightMm = 18.0, GapRightMm = -18.0 };
            string warn2 = JobLog.OverflowWarning(fake);
            Check(bad, "改1 真越界那一级的行照开工令原话（超出右缘约 18.0mm，打印将被裁切）",
                warn2.Contains("第 3 页内容超出右缘约 18.0mm") && warn2.Contains("打印将被裁切"));
            string note = JobLog.ScanFailedNote("x.pdf", OverflowScan.Fail("打不开"));
            Check(bad, "改1 没扫成时日志如实补一句\"不代表没有超界\"",
                note != null && note.Contains("打不开") && note.Contains("不代表没有超界"));
            return bad;
        }

        // ===== 改3 预览 =====
        private static List<string> V492Preview(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            string near = Path.Combine(dir, "near-edge.pdf");
            string four = Path.Combine(dir, "four.pdf");
            BuildInkPdf(four, 4, 10, 209.5);

            var w = new MainWindow();
            w.ShowPanelForTest("print");
            w.UpdateLayoutForTest(1120, 700);
            var job = new PrintJob { FilePath = near, Kind = DocKind.Pdf };
            job.Diag = new PrintDiagnostics { Overflow = PageOverflowScanner.Scan(near) };
            var row = w.AddRowForTest(job);
            var pv = w.OpenPreviewForTest(row);
            pv.LoadForTest(near, PageOverflowScanner.Scan(near), SourceLayoutProbe.Probe(near, DocKind.Pdf));
            PumpToLoaded();
            sb.AppendLine("— 标题：" + pv.Title);
            sb.AppendLine("— 信息条：" + pv.ChipsText);
            sb.AppendLine("— 警告条：" + pv.WarnText);
            sb.AppendLine("— 显示页：" + string.Join(",", pv.Pages.Select(x => (x + 1)).ToArray()) + " 缩略图 " + pv.Thumbs.Items.Count + " 张 当前=" + pv.ShownIndex);
            Check(bad, "改3 预览打开：标题带文件名、信息条六段齐（类型/纸张/缩放/打印范围/输出 + 竖版/份数）、警告条与改1 同源",
                pv.Title.Contains("near-edge.pdf")
                && pv.ChipsText.Contains("类型") && pv.ChipsText.Contains("纸张") && pv.ChipsText.Contains("缩放")
                && pv.ChipsText.Contains("打印范围") && pv.ChipsText.Contains("输出") && pv.ChipsText.Contains("1 份")
                && pv.WarnText.Contains("第 1 页") && pv.WarnText.Contains("右缘"));
            Check(bad, "改3 缩略图按\"将会打印的页\"出，点第 2 页能切过去",
                pv.Pages.Count == 1 && pv.Thumbs.Items.Count == 1 && pv.ShownIndex == 0);

            // 范围/奇偶联动：与打印同一份页序算法
            var opt = new PrintOptions { PageParity = PageParityMode.All };
            var pAll = PreviewWindow.PagesToShow(4, "", opt);
            var pRange = PreviewWindow.PagesToShow(4, "1,3", opt);
            var pOdd = PreviewWindow.PagesToShow(4, "", new PrintOptions { PageParity = PageParityMode.Odd });
            var pBad = PreviewWindow.PagesToShow(4, "99", opt);
            sb.AppendLine("— 页序联动：全部=" + string.Join(",", pAll.ToArray()) + " 范围 1,3=" + string.Join(",", pRange.ToArray())
                + " 仅奇数=" + string.Join(",", pOdd.ToArray()) + " 范围全无效=" + string.Join(",", pBad.ToArray()) + "（引擎回退打全部，预览同口径）");
            Check(bad, "改3 预览只显示\"将会打印\"的页：无范围全显、范围 1,3 只显两页、仅奇数页只显 1/3、范围全无效按引擎既有口径回退显全部",
                pAll.Count == 4 && pRange.Count == 2 && pRange[0] == 0 && pRange[1] == 2
                && pOdd.Count == 2 && pOdd[0] == 0 && pOdd[1] == 2 && pBad.Count == 4);

            // 超界标注数值与改1 同源（毫米算，不靠渲染）
            var rep = PageOverflowScanner.Scan(near).Pages[0];
            var strips = PreviewWindow.HatchRects(rep, rep.WidthMm, rep.HeightMm, OverflowScan.UnprintableBandMm);
            sb.AppendLine("— 叠加层：版心带宽=" + OverflowScan.UnprintableBandMm + "mm 红斜纹条 " + strips.Count + " 条 第一条 X="
                + (strips.Count > 0 ? strips[0].Xmm.ToString("0.0") : "-") + " 宽=" + (strips.Count > 0 ? strips[0].Wmm.ToString("0.0") : "-")
                + " 页宽=" + rep.WidthMm.ToString("0.0"));
            Check(bad, "改3 超界标注与改1 同源：贴到右缘就只在右侧画一条带（起点＝页宽−带宽，宽度＝带宽），不画满页",
                strips.Count == 1 && Math.Abs(strips[0].Xmm - (rep.WidthMm - OverflowScan.UnprintableBandMm)) < 0.2
                && Math.Abs(strips[0].Wmm - OverflowScan.UnprintableBandMm) < 0.01
                && Math.Abs(strips[0].Hmm - rep.HeightMm) < 0.01);

            // 双语：信息条与警告条都走资源键
            App.ApplyLanguage(AppLanguage.EnUS);
            var pvEn = w.OpenPreviewForTest(row);
            pvEn.LoadForTest(near, PageOverflowScanner.Scan(near), null);
            PumpToLoaded();
            sb.AppendLine("— 英文态信息条：" + pvEn.ChipsText);
            Check(bad, "改3 预览双语：英文态信息条与警告条都取到键值（不出现未解析键名）",
                pvEn.ChipsText.Contains("Type") && pvEn.ChipsText.Contains("Paper") && pvEn.ChipsText.Contains("Scaling")
                && !pvEn.ChipsText.Contains("s.Pv") && !pvEn.WarnText.StartsWith("s.Pv") && pvEn.WarnText.Contains("edge"));
            App.ApplyLanguage(AppLanguage.ZhCN);
            pvEn.Close();

            // ===== 57 号令·B：预览窗参数编辑条（第二个参数入口；数据源与主列表参数卡同一个 RowVm.Override）=====
            var eds = pv.PvEditorsForTest;
            int edsHave = eds.Count(c => c != null);
            var eDuplex = (System.Windows.Controls.ComboBox)eds[0];
            var eColor = (System.Windows.Controls.ComboBox)eds[1];
            var ePaper = (System.Windows.Controls.ComboBox)eds[2];
            var eOrient = (System.Windows.Controls.ComboBox)eds[3];
            var eScale = (System.Windows.Controls.ComboBox)eds[4];
            var eCopies = (System.Windows.Controls.TextBox)eds[5];
            var eRange = (System.Windows.Controls.TextBox)eds[6];
            bool zeroed = eDuplex.SelectedIndex == 0 && eColor.SelectedIndex == 0 && ePaper.SelectedIndex == 0
                && eOrient.SelectedIndex == 0 && eScale.SelectedIndex == 0
                && string.IsNullOrEmpty(eCopies.Text) && string.IsNullOrEmpty(eRange.Text)
                && eDuplex.Items.Count == 4 && eColor.Items.Count == 3 && ePaper.Items.Count == 7
                && eOrient.Items.Count == 4 && eScale.Items.Count == 6;
            sb.AppendLine("— 57 号·B 编辑条：控件 " + edsHave + "/7 在位、重置按钮文字=\"" + pv.PvResetText
                + "\"、打开时全部＝跟随全局（含项数 4/3/7/4/6）=" + zeroed);
            Check(bad, "57 号·B 编辑条在位：七项可调（双面/色彩/纸张/方向/缩放/份数/范围）＋「重置为跟随全局」，打开时全部回到第一项＝跟随全局，下拉表与参数卡同一张表",
                edsHave == 7 && pv.PvResetForTest != null && !string.IsNullOrEmpty(pv.PvResetText) && zeroed);

            DuplexMode gDuplexBefore = w.ActiveOptions.Duplex;
            OrientationMode gOrientBefore = w.ActiveOptions.Orientation;

            // ① 改双面＋方向 → 即写回该行覆盖 → 生效摘要与主列表「参数」列同步
            eDuplex.SelectedIndex = (int)DuplexMode.LongEdge + 1;
            eOrient.SelectedIndex = (int)OrientationMode.Landscape + 1;
            PumpToLoaded();
            string chipsEdit = pv.ChipsText;
            sb.AppendLine("— 57 号·B 改双面＋方向：行覆盖＝"
                + (row.Override == null ? "null" : row.Override.Duplex + "/" + row.Override.Orientation)
                + "、IsCustom=" + row.IsCustom + "、摘要行=" + chipsEdit);
            Check(bad, "57 号·B 预览改参数＝写回该行覆盖并经 EffectiveOptions 生效（摘要行同时出现「双面-长边」「横版」）、主列表该行进入自定义态",
                row.Override != null && row.Override.Duplex == DuplexMode.LongEdge
                && row.Override.Orientation == OrientationMode.Landscape && row.IsCustom
                && EffectiveOptions.Resolve(w.ActiveOptions, row.Override).Orientation == OrientationMode.Landscape
                && chipsEdit.Contains("双面-长边") && chipsEdit.Contains("横版"));
            Check(bad, "57 号·B 只改这一行、不写回全局设置（与参数卡同语义；全局仍是打开前那两个值）",
                w.ActiveOptions.Duplex == gDuplexBefore && w.ActiveOptions.Orientation == gOrientBefore);

            // ② 份数与范围（文本框＝回车/失焦提交，自检走同一个提交口）
            eCopies.Text = "3";
            eRange.Text = "1-2";
            pv.PvCommitTextForTest();
            PumpToLoaded();
            int ovCopies = row.Override != null && row.Override.Copies.HasValue ? row.Override.Copies.Value : -1;
            string ovRange = row.Override != null && row.Override.PageRange != null ? row.Override.PageRange : "";
            sb.AppendLine("— 57 号·B 改份数＋范围：覆盖份数=" + ovCopies + " 覆盖范围=\"" + ovRange + "\" 摘要行=" + pv.ChipsText);
            Check(bad, "57 号·B 份数/范围写进同一个覆盖对象（3 份＋范围 1-2），摘要行的份数段与范围段同步改口",
                ovCopies == 3 && ovRange == "1-2"
                && pv.ChipsText.Contains("3 份") && pv.ChipsText.Contains("页（按范围/奇偶）"));

            // ③ 预览联动：改范围真的重算页序与缩略图（四页文档选 1,3 → 只显两页，与打印同一份 PagesToShow 口径）
            var job4 = new PrintJob { FilePath = four, Kind = DocKind.Pdf };
            var row4 = w.AddRowForTest(job4);
            var pv4 = w.OpenPreviewForTest(row4);
            pv4.LoadForTest(four, PageOverflowScanner.Scan(four), null);
            PumpToLoaded();
            int pagesBefore = pv4.Pages.Count;
            var r4 = (System.Windows.Controls.TextBox)pv4.PvEditorsForTest[6];
            r4.Text = "1,3";
            pv4.PvCommitTextForTest();
            PumpToLoaded();
            sb.AppendLine("— 57 号·B 范围联动：四页文档 全部=" + pagesBefore + " 页 → 范围 1,3 → 显示 " + pv4.Pages.Count
                + " 页（页序 " + string.Join(",", pv4.Pages.Select(x => x + 1).ToArray()) + "）、缩略图 " + pv4.Thumbs.Items.Count
                + " 张、摘要行=" + pv4.ChipsText);
            Check(bad, "57 号·B 预览按新范围重算页序与缩略图（复用 45~47 号同一份 PagesToShow，不另算一遍），且摘要行的页数当次就跟上（不留上一屏的旧页数）",
                pagesBefore == 4 && pv4.Pages.Count == 2 && pv4.Pages[0] == 0 && pv4.Pages[1] == 2
                && pv4.Thumbs.Items.Count == 2 && pv4.ChipsText.Contains("2 页（按范围/奇偶）"));

            // ④ 重置为跟随全局：丢弃该行覆盖，页序/摘要/控件三处同时回到跟随全局
            pv4.PvResetForTest.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpToLoaded();
            bool cleared = row4.Override == null && !row4.IsCustom && pv4.Pages.Count == 4
                && ((System.Windows.Controls.ComboBox)pv4.PvEditorsForTest[0]).SelectedIndex == 0
                && pv4.ChipsText.Contains("全部（4 页）");
            sb.AppendLine("— 57 号·B 重置：行覆盖=" + (row4.Override == null ? "null（回到跟随全局）" : "仍有值！")
                + "、页序回到 " + pv4.Pages.Count + " 页、下拉回到第 0 项、摘要行=" + pv4.ChipsText);
            Check(bad, "57 号·B「重置为跟随全局」＝丢弃该行覆盖并即时生效（页序/摘要/控件三处同时回跟随全局）", cleared);
            pv4.Close();
            // 复位第一行的覆盖，免得留给后面的「加入打印队列」段与渲染段一个脏行
            pv.PvResetForTest.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            PumpToLoaded();

            // 「加入打印队列」＝勾选该行 + 一句日志 + 关窗（需求方拍板口径）
            row.Checked = false;
            pv.QueueForTest();
            PumpToLoaded();
            string logAfter = FileLogger.RecentLines(8) ?? "";
            sb.AppendLine("— 点「加入打印队列」：勾选=" + row.Checked + " 窗口已关=" + !pv.IsVisible + " 新日志行=\"" + logAfter + "\"");
            Check(bad, "改3 按钮语义照拍板：勾选该行 + 写一行日志 + 关窗（发送仍由主页「开始打印」掌握）",
                row.Checked && !pv.IsVisible && logAfter.Contains("预览后加入打印队列") && logAfter.Contains("near-edge.pdf"));

            // 缓存复用：预置一份产物后第二次不再转换（不启动 Office，只验缓存逻辑）
            string docx = Path.Combine(dir, "cached.docx");
            BuildDocx(docx, 11906, 720, 720, new[] { 2600, 2600, 2633, 2633 });
            var cjob = new PrintJob { FilePath = docx, Kind = DocKind.Word };
            string key = PreviewCacheKeyOf(cjob);
            string cachePath = Path.Combine(PreviewCacheDir(), key + ".pdf");
            BuildInkPdf(cachePath, 1, 20, 80);
            bool fc1;
            string err1;
            string got = PreviewCacheEnsure(cjob, out fc1, out err1);
            sb.AppendLine("— 缓存命中：返回=" + Path.GetFileName(got ?? "") + " fromCache=" + fc1 + " err=" + err1);
            Check(bad, "改3 转换缓存复用：命中缓存时第二次预览直接取缓存产物、不再转换（不启动 Office）",
                got != null && fc1 && got == cachePath && string.IsNullOrEmpty(err1));
            // 源文件一改（写入时间变）→ 键变 → 不再复用旧产物
            File.SetLastWriteTimeUtc(docx, DateTime.UtcNow.AddSeconds(3));
            string key2 = PreviewCacheKeyOf(cjob);
            Check(bad, "改3 缓存键随源文件走（改过文件不复用旧产物）", key2 != key);
            return bad;
        }

        // ===== 渲染取证 =====
        internal static int V492Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV492R-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string near = Path.Combine(dir, "near-edge.pdf");
                string wide = Path.Combine(dir, "wide-table.docx");
                BuildInkPdf(near, 2, 10, 209.5);
                BuildDocx(wide, 11906, 720, 720, new[] { 3000, 3000, 2375, 2375 });

                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";
                    var w = new MainWindow();
                    w.ShowPanelForTest("print");
                    var job = new PrintJob { FilePath = near, Kind = DocKind.Pdf };
                    job.Diag = new PrintDiagnostics
                    {
                        Overflow = PageOverflowScanner.Scan(near),
                        SourceLayout = SourceLayoutProbe.Probe(wide, DocKind.Word),
                    };
                    var row = w.AddRowForTest(job);
                    var pv = w.OpenPreviewForTest(row);
                    pv.LoadForTest(near, job.Diag.Overflow, job.Diag.SourceLayout);
                    PumpToLoaded();
                    code |= RenderWindow("V492" + ts + "预览自检.txt", "V492预览窗口-" + ts + ".png", pv, 1080, 720, new string[0], 0);
                    sb.AppendLine("—" + ts + "：预览窗口（信息条 + 顶部警告条 + 左缩略图 + 大页叠版心虚线框与超界红斜纹）出图");
                    pv.Close();
                    // RenderWindow 会把窗口 Content 摘到垫色宿主里（一次出图后窗口已空），所以"参数编辑态"必须另开一扇窗出图
                    var pv2 = w.OpenPreviewForTest(row);
                    pv2.LoadForTest(near, job.Diag.Overflow, job.Diag.SourceLayout);
                    PumpToLoaded();
                    ((System.Windows.Controls.ComboBox)pv2.PvEditorsForTest[0]).SelectedIndex = (int)DuplexMode.LongEdge + 1;
                    ((System.Windows.Controls.ComboBox)pv2.PvEditorsForTest[3]).SelectedIndex = (int)OrientationMode.Landscape + 1;
                    ((System.Windows.Controls.TextBox)pv2.PvEditorsForTest[5]).Text = "2";
                    ((System.Windows.Controls.TextBox)pv2.PvEditorsForTest[6]).Text = "1";
                    pv2.PvCommitTextForTest();
                    PumpToLoaded();
                    code |= RenderWindow("V492" + ts + "预览参数编辑态自检.txt", "V492预览窗口-参数编辑态-" + ts + ".png", pv2, 1080, 720, new string[0], 0);
                    sb.AppendLine("—" + ts + "：参数编辑态（双面-长边／横版／2 份／范围 1→只显 1 页）出图，摘要行=" + pv2.ChipsText);
                    pv2.Close();
                    // 主窗口这里不 Close：41 号起 Close 会走"关闭行为"分支，自检/渲染环境里可能弹模态提示窗把整轮挂住
                }

                // 状态列超界黄警告特写（浅色，含中英各一行以证双语）
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var mw = new MainWindow();
                mw.ShowPanelForTest("print");
                for (int i = 0; i < 6; i++)
                {
                    string f = Path.Combine(dir, "s" + i + ".pdf");
                    BuildInkPdf(f, 1, i == 1 ? 10 : 20, i == 1 ? 209.5 : 80);
                    var j = new PrintJob { FilePath = f, Kind = DocKind.Pdf };
                    j.Diag = new PrintDiagnostics { Overflow = PageOverflowScanner.Scan(f) };
                    var r = mw.AddRowForTest(j);
                    string t;
                    if (MainWindow.OverflowStatusText(j, out t)) r.SetStatus(t, false, RowStatusKind.ContentOverflow);
                    else r.SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
                }
                mw.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V492状态列超界自检.txt", "V492状态列超界黄警告-浅色.png", mw, 1120, 700, new string[0], 0);
                sb.AppendLine("—浅色：任务清单第 2 行状态列＝「⚠ 第 1 页内容贴到页边，打印可能被裁切」（76 宽单行截断 + 悬停看全文），六行等高");
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染异常：" + ex);
                code |= 1;
            }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V492三项渲染自检.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            try { Directory.Delete(dir, true); } catch { }
            return code;
        }

        // ===== 样张构造（合成，不读仓库取证原件、不启动 Office） =====
        /// <summary>A4 单页/多页样张：墨迹矩形横向从 inkLeftMm 画到 inkRightMm（页宽 210mm）。</summary>
        private static void BuildInkPdf(string path, int pages, double inkLeftMm, double inkRightMm)
        {
            double x0 = inkLeftMm * 72.0 / 25.4;
            double x1 = inkRightMm * 72.0 / 25.4;
            string content = "BT /F1 18 Tf 72 780 Td (V492 sample) Tj ET\n"
                + "0.15 0.15 0.15 rg " + x0.ToString("0.##") + " 300 "
                + (x1 - x0).ToString("0.##") + " 120 re f\n";
            var objs = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => (3 + i * 2) + " 0 R").ToArray()) + "] /Count " + pages + " >>"
            };
            int fontObj = 3 + pages * 2;
            for (int i = 0; i < pages; i++)
            {
                objs.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.3 841.9] /Resources << /Font << /F1 " + fontObj
                    + " 0 R >> >> /Contents " + (4 + i * 2) + " 0 R >>");
                objs.Add("<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream");
            }
            objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var sb = new StringBuilder("%PDF-1.4\n");
            long pos = Encoding.ASCII.GetByteCount(sb.ToString());
            var offsets = new List<long>();
            for (int i = 0; i < objs.Count; i++)
            {
                offsets.Add(pos);
                string s = (i + 1) + " 0 obj\n" + objs[i] + "\nendobj\n";
                sb.Append(s);
                pos += Encoding.ASCII.GetByteCount(s);
            }
            long xref = pos;
            sb.Append("xref\n0 " + (objs.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) sb.Append(o.ToString("0000000000") + " 00000 n \n");
            sb.Append("trailer\n<< /Size " + (objs.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        /// <summary>单节 docx：一张表（栅格列宽合计 = gridTwips 之和）+ 末尾节属性。</summary>
        private static void BuildDocx(string path, int pageTwips, int mlTwips, int mrTwips, int[] gridTwips)
        {
            WriteDocx(path, new[] { gridTwips }, new[] { Tuple.Create(pageTwips, mlTwips, mrTwips) });
        }

        /// <summary>多节 docx：第 i 张表放在第 i 节之前，最后一段节属性再补上（分节符顺序＝文档顺序）。</summary>
        private static void BuildDocxSections(string path, Tuple<int, int, int, int>[] first, Tuple<int, int, int>[] tails)
        {
            var grids = new List<int[]>();
            var sects = new List<Tuple<int, int, int>>();
            foreach (var t in first)
            {
                int total = t.Item4 > 0 ? t.Item4 : 10750;
                grids.Add(new[] { total / 4, total / 4, total / 4, total - 3 * (total / 4) });
                sects.Add(Tuple.Create(t.Item1, t.Item2, t.Item3));
            }
            if (tails != null) foreach (var s in tails) sects.Add(s);
            WriteDocx(path, grids.ToArray(), sects.ToArray());
        }

        /// <summary>写一份最小可解析的 docx（zip：[Content_Types].xml + word/document.xml）。</summary>
        private static void WriteDocx(string path, int[][] grids, Tuple<int, int, int>[] sections)
        {
            var body = new StringBuilder();
            for (int i = 0; i < grids.Length; i++)
            {
                body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/></w:tblPr><w:tblGrid>");
                foreach (int g in grids[i]) body.Append("<w:gridCol w:w=\"" + g + "\"/>");
                body.Append("</w:tblGrid><w:tr><w:tc><w:tcPr><w:tcW w:w=\"0\" w:type=\"auto\"/></w:tcPr><w:p/></w:tc></w:tr></w:tbl>");
                if (i < sections.Length - 1)
                    body.Append("<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"" + sections[i].Item1 + "\" w:h=\"16838\"/>"
                        + "<w:pgMar w:left=\"" + sections[i].Item2 + "\" w:right=\"" + sections[i].Item3 + "\" w:top=\"1440\" w:bottom=\"1440\"/>"
                        + "</w:sectPr></w:pPr></w:p>");
            }
            var lastS = sections[sections.Length - 1];
            body.Append("<w:p><w:pPr><w:sectPr><w:pgSz w:w=\"" + lastS.Item1 + "\" w:h=\"16838\"/><w:pgMar w:left=\""
                + lastS.Item2 + "\" w:right=\"" + lastS.Item3 + "\" w:top=\"1440\" w:bottom=\"1440\"/></w:sectPr></w:pPr></w:p>");
            string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><w:document xmlns:w=\"" + W + "\"><w:body>"
                + body.ToString() + "</w:body></w:document>";
            string types = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>";
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var z = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
            {
                PutEntry(z, "[Content_Types].xml", types);
                PutEntry(z, "word/document.xml", xml);
            }
        }

        private static void PutEntry(System.IO.Compression.ZipArchive z, string name, string text)
        {
            var e = z.CreateEntry(name);
            using (var s = e.Open())
            using (var w = new StreamWriter(s, new UTF8Encoding(false))) w.Write(text);
        }

        // ===== 预览缓存的三个小口子（自检直接调，不复制逻辑） =====
        private static string PreviewCacheKeyOf(PrintJob job) { return PreviewCache.KeyOf(job); }
        private static string PreviewCacheDir() { return PreviewCache.Directory4Test; }
        private static string PreviewCacheEnsure(PrintJob job, out bool fromCache, out string error)
        {
            return PreviewCache.EnsurePdf(job, null, out fromCache, out error);
        }

    }
}
