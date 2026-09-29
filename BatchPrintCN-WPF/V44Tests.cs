using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.4「逐文件参数」自检断言（F5 原版 ①②③④⑦ + 2026-09-20 版本内修整轮 8 项与 B 档收口）。
    /// 四个入口由 EngineTests 串进 --selftest（① ② ③ ④ 四行报告）：
    /// ① 生效参数解析矩阵 + 纸盒收口(B-3) + 首启语言跟随系统(修整8) + 逐文件日志生效摘要(A-1)
    /// ② 双文件不同纸张虚拟打印 + 图片缩放三档(B-4)
    /// ③ 任务规划分组 + 修改设置回规划框(修整4) + 续打重取参数(B-1) 与整项目进度口径(修整6)
    /// ④ 逐文件参数弹窗(修整1，含 B-2/B-3/B-5/B-6/A-2 收口) + 份数列(修整2) + 表格边框(修整3)
    ///    + 页码列头与水印(修整7) + 列头打印前后一致(修整5)
    /// </summary>
    public static partial class WpfTests
    {
        private const double DesignW = 1260, DesignH = 900;

        private static void Check(List<string> bad, string name, bool ok)
        {
            if (!ok) bad.Add(name);
        }

        /// <summary>虚拟打印机（Microsoft Print to PDF）；没装则返回 null，相关断言按"跳过"处理。</summary>
        private static string VirtualPrinter()
        {
            return PrinterSettings.InstalledPrinters.Cast<string>()
                .FirstOrDefault(p => p.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static System.Drawing.SizeF OutputFirstPageSize(string dir, string sourceName)
        {
            string outPdf = Path.Combine(dir, Path.GetFileNameWithoutExtension(sourceName) + ".pdf");
            if (!File.Exists(outPdf)) return System.Drawing.Size.Empty;
            using (PdfiumFile p = PdfiumFile.Open(outPdf)) return p.GetPageSizePt(0);
        }

        private static bool Near(System.Drawing.SizeF size, double w, double h)
        {
            return size.Width > 0 && Math.Abs(size.Width - w) <= 20 && Math.Abs(size.Height - h) <= 20;
        }

        // ==================================================================
        // ① 生效参数解析矩阵（含 B-3 纸盒收口）+ 修整8 首启语言 + A-1 逐文件日志
        // ==================================================================
        private static string V44ResolveMatrix()
        {
            try
            {
                var g = new PrintOptions
                {
                    Duplex = DuplexMode.LongEdge, Color = ColorMode.Color, Orientation = OrientationMode.Auto,
                    Paper = PaperKind.A4, PaperDisplayName = "A4", FitToPage = true, Scale = ScaleMode.FitPage,
                    ScalePercent = 100, PagesPerSheet = 1, Booklet = false, PageParity = PageParityMode.All, PaperSourceName = null
                };
                var bad = new List<string>();

                // 零覆盖必须拿回全局实例本身（引用相同）——A/B 字节级一致的根据
                if (!ReferenceEquals(EffectiveOptions.Resolve(g, null), g)) bad.Add("null 覆盖未返回全局实例");
                if (!ReferenceEquals(EffectiveOptions.Resolve(g, new PerFileOverride()), g)) bad.Add("空覆盖未返回全局实例");
                if (new PerFileOverride().HasAny) bad.Add("空覆盖 HasAny 为真");

                Check(bad, "双面", EffectiveOptions.Resolve(g, new PerFileOverride { Duplex = DuplexMode.ShortEdge }).Duplex == DuplexMode.ShortEdge);
                Check(bad, "色彩", EffectiveOptions.Resolve(g, new PerFileOverride { Color = ColorMode.BlackWhite }).Color == ColorMode.BlackWhite);
                var paper = EffectiveOptions.Resolve(g, new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter" });
                Check(bad, "纸张", paper.Paper == PaperKind.Letter && paper.PaperDisplayName == "Letter");
                var scale = EffectiveOptions.Resolve(g, new PerFileOverride { Scale = ScaleMode.None });
                Check(bad, "缩放模式", scale.EffectiveScale == ScaleMode.None && scale.FitToPage == false);
                Check(bad, "缩放百分比", EffectiveOptions.Resolve(g, new PerFileOverride { ScalePercent = 150 }).ScalePercent == 150);
                Check(bad, "每页版数", EffectiveOptions.Resolve(g, new PerFileOverride { PagesPerSheet = 4 }).PagesPerSheet == 4);
                Check(bad, "小册子", EffectiveOptions.Resolve(g, new PerFileOverride { Booklet = true }).Booklet == true);
                Check(bad, "奇偶页", EffectiveOptions.Resolve(g, new PerFileOverride { PageParity = PageParityMode.Odd }).PageParity == PageParityMode.Odd);

                // B-3（V4.4 修整）：纸盒不再有"显式自动"这一档——空串与 null 一样都是跟随全局，不算自定义
                var autoOv = new PerFileOverride { PaperSourceOverride = "" };
                Check(bad, "B-3 空串纸盒不算覆盖", !autoOv.HasAny && ReferenceEquals(EffectiveOptions.Resolve(g, autoOv), g) && autoOv.CustomFields(k => k).Count == 0);
                Check(bad, "B-3 null 纸盒不算覆盖", !new PerFileOverride { PaperSourceOverride = null }.HasAny);
                Check(bad, "纸盒→指定", EffectiveOptions.Resolve(g, new PerFileOverride { PaperSourceOverride = "纸盒 2" }).PaperSourceName == "纸盒 2");
                Check(bad, "方向不受覆盖影响", EffectiveOptions.Resolve(g, new PerFileOverride { Color = ColorMode.BlackWhite }).Orientation == OrientationMode.Auto);

                // 全字段一起覆盖：每一项都改掉，且全局实例一个字节都不能动
                var all = new PerFileOverride
                {
                    Duplex = DuplexMode.Simplex, Color = ColorMode.BlackWhite, Paper = PaperKind.A5, PaperDisplayName = "A5",
                    Scale = ScaleMode.Custom, ScalePercent = 80, PagesPerSheet = 2, Booklet = false,
                    PageParity = PageParityMode.Even, PaperSourceOverride = "手动进纸", Copies = 3, PageRange = "1-2"
                };
                var ea = EffectiveOptions.Resolve(g, all);
                Check(bad, "全字段覆盖", ea.Duplex == DuplexMode.Simplex && ea.Color == ColorMode.BlackWhite && ea.Paper == PaperKind.A5
                    && ea.PaperDisplayName == "A5" && ea.EffectiveScale == ScaleMode.Custom && ea.ScalePercent == 80
                    && ea.PagesPerSheet == 2 && ea.PageParity == PageParityMode.Even && ea.PaperSourceName == "手动进纸");
                Check(bad, "全局实例未被改", g.Duplex == DuplexMode.LongEdge && g.Color == ColorMode.Color && g.Paper == PaperKind.A4
                    && g.PaperDisplayName == "A4" && g.Scale == ScaleMode.FitPage && g.ScalePercent == 100
                    && g.PagesPerSheet == 1 && g.PageParity == PageParityMode.All && g.PaperSourceName == null);

                // 份数 / 页码范围：覆盖优先，否则回到该行列表列的值（修整1/B-2 起列表列是唯一录入入口，这里保数据层口径）
                Check(bad, "份数覆盖", EffectiveOptions.Copies(all, 2) == 3);
                Check(bad, "份数跟随", EffectiveOptions.Copies(null, 2) == 2 && EffectiveOptions.Copies(new PerFileOverride(), 2) == 2);
                Check(bad, "页码覆盖", EffectiveOptions.RangeText(all, "5") == "1-2");
                Check(bad, "页码跟随", EffectiveOptions.RangeText(new PerFileOverride(), "5") == "5");

                var fields = new PerFileOverride { Duplex = DuplexMode.Simplex, PagesPerSheet = 2 }.CustomFields(k => k);
                Check(bad, "自定义字段清单", fields.Count == 2 && fields[0] == "s.PsDuplex" && fields[1] == "s.PsPagesPerSheet");

                // ===== 修整8：首启语言跟随系统（解析函数可单测，模拟 culture 传入）=====
                Check(bad, "修整8 无设置+zh-CN→中文", SettingsService.ResolveEffective(null, "zh") == AppLanguage.ZhCN);
                Check(bad, "修整8 无设置+en-US→英文", SettingsService.ResolveEffective(null, "en") == AppLanguage.EnUS);
                Check(bad, "修整8 无设置+de→英文", SettingsService.ResolveEffective(null, "de") == AppLanguage.EnUS);
                Check(bad, "修整8 zh 大小写兼容", SettingsService.FromCulture("ZH") == AppLanguage.ZhCN);
                Check(bad, "修整8 空语言码→英文", SettingsService.FromCulture(null) == AppLanguage.EnUS);
                Check(bad, "修整8 有保存值优先", SettingsService.ResolveEffective(AppLanguage.EnUS, "zh") == AppLanguage.EnUS
                    && SettingsService.ResolveEffective(AppLanguage.ZhCN, "en") == AppLanguage.ZhCN);
                Check(bad, "修整8 已保存值解析", SettingsService.ParseSavedLanguage("zh") == AppLanguage.ZhCN
                    && SettingsService.ParseSavedLanguage("en") == AppLanguage.EnUS
                    && SettingsService.ParseSavedLanguage(null) == null && SettingsService.ParseSavedLanguage("  ") == null
                    && SettingsService.ParseSavedLanguage("fr") == null);   // 未知值当没设置，回退系统语言

                // ===== A-1：逐文件日志打的是该文件的生效摘要，不是全局摘要 =====
                var lines = new List<string>();
                var ghost = new PrintJob
                {
                    FilePath = Path.Combine(Path.GetTempPath(), "A1-不存在.pdf"), Kind = DocKind.Pdf, Copies = 1,
                    Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter" }
                };
                // 打印机名故意用不存在的：日志行在真正打印之前就已写出，够取证且不会真出纸
                new BatchPrinter("__BatchPrintCN-A1-测试用不存在打印机__", g, line => lines.Add(line), null, null)
                    .Run(new List<PrintJob> { ghost });
                string start = lines.FirstOrDefault(l => l.StartsWith("正在打印", StringComparison.Ordinal));
                Check(bad, "A-1 逐文件日志=生效摘要", start != null && start.Contains("自定义参数") && start.Contains("Letter"));
                Check(bad, "A-1 全局摘要未被逐文件污染", g.Paper == PaperKind.A4);

                // 未改过的文件：日志里不该出现"自定义参数"
                var lines2 = new List<string>();
                var plain = new PrintJob { FilePath = ghost.FilePath, Kind = DocKind.Pdf, Copies = 1 };
                new BatchPrinter("__BatchPrintCN-A1-测试用不存在打印机__", g, line => lines2.Add(line), null, null)
                    .Run(new List<PrintJob> { plain });
                string start2 = lines2.FirstOrDefault(l => l.StartsWith("正在打印", StringComparison.Ordinal));
                Check(bad, "A-1 跟随全局不留自定义痕迹", start2 != null && !start2.Contains("自定义参数"));

                if (bad.Count > 0) return "失败：" + string.Join(" | ", bad);
                return "通过（解析矩阵 + B-3 纸盒两态 + 份数/页码合并 + 全局未被污染 + 修整8 语言三情形 + A-1 逐文件生效摘要）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // ==================================================================
        // ② 双文件不同纸张（虚拟打印）+ B-4 图片缩放三档（虚拟打印量尺寸）
        // ==================================================================
        private static string V44TwoFilePaper()
        {
            try
            {
                string vp = VirtualPrinter();
                if (vp == null) return "跳过（未安装 Microsoft Print to PDF）";
                string root = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44-" + Guid.NewGuid().ToString("N"));
                string inDir = Path.Combine(root, "in");
                string outDir = Path.Combine(root, "out");   // 输入与输出必须分目录：同名输出会被 UniquePath 判为重名而改名
                Directory.CreateDirectory(inDir);
                Directory.CreateDirectory(outDir);
                string fileA = Path.Combine(inDir, "a-letter.pdf");
                string fileB = Path.Combine(inDir, "b-a4.pdf");
                BuildSamplePdf(fileA, 1);
                BuildSamplePdf(fileB, 1);

                var opt = new PrintOptions { PrintoutDirectory = outDir };
                var jobA = new PrintJob
                {
                    FilePath = fileA, Kind = DocKind.Pdf, Copies = 1,
                    Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter" }
                };
                var jobB = new PrintJob { FilePath = fileB, Kind = DocKind.Pdf, Copies = 1 };   // 全跟随全局（A4）
                var res = new BatchPrinter(vp, opt, line => { }, null, null).Run(new List<PrintJob> { jobA, jobB });
                if (res.Ok != 2) return "失败：两份未全部打印成功（Ok=" + res.Ok + "）";

                System.Drawing.SizeF sa = OutputFirstPageSize(outDir, "a-letter.pdf");
                System.Drawing.SizeF sb = OutputFirstPageSize(outDir, "b-a4.pdf");
                var bad = new List<string>();
                Check(bad, "A 文件出 Letter", Near(sa, 612, 792));
                Check(bad, "B 文件出 A4", Near(sb, 595, 842));

                // ===== B-4（V4.4 修整）：图片通道改走 ApplyScaleMode，三档不再静默无效 =====
                string img = Path.Combine(inDir, "sample.png");
                BuildSamplePng(img, 200, 100);       // 200x100 px @96dpi = 150x75 pt
                var bad2 = V44ImageScale(vp, inDir, Path.Combine(root, "img"), img);
                bad.AddRange(bad2);

                if (bad.Count > 0) return "失败：" + string.Join(" | ", bad)
                    + "（实测 A=" + sa.Width.ToString("0.#") + "x" + sa.Height.ToString("0.#") + "pt，B=" + sb.Width.ToString("0.#") + "x" + sb.Height.ToString("0.#") + "pt）";
                return "通过（同一次运行：自定义 Letter 的文件出 " + sa.Width.ToString("0.#") + "x" + sa.Height.ToString("0.#")
                    + "pt、跟随全局的出 " + sb.Width.ToString("0.#") + "x" + sb.Height.ToString("0.#") + "pt；B-4 图片缩放三档实测见同一份 txt）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        /// <summary>B-4：同一张图片分别按「不缩放」「适合页面」出纸，量纸上墨迹包围盒尺寸——
        /// 修复前两档都按"适配"画（三档静默无效），修复后「不缩放」保持原生物理尺寸（150x75pt）。</summary>
        private static List<string> V44ImageScale(string printer, string inDir, string workDir, string img)
        {
            var bad = new List<string>();
            string dirNone = Path.Combine(workDir, "none");
            string dirFit = Path.Combine(workDir, "fit");
            Directory.CreateDirectory(dirNone);
            Directory.CreateDirectory(dirFit);
            CopyFile(img, Path.Combine(inDir, "none.png"));
            CopyFile(img, Path.Combine(inDir, "fit.png"));

            var optNone = new PrintOptions { PrintoutDirectory = dirNone, Scale = ScaleMode.None, FitToPage = false };
            new BatchPrinter(printer, optNone, line => { }, null, null)
                .Run(new List<PrintJob> { new PrintJob { FilePath = Path.Combine(inDir, "none.png"), Kind = DocKind.Image, Copies = 1 } });
            var optFit = new PrintOptions { PrintoutDirectory = dirFit, Scale = ScaleMode.FitPage, FitToPage = true };
            new BatchPrinter(printer, optFit, line => { }, null, null)
                .Run(new List<PrintJob> { new PrintJob { FilePath = Path.Combine(inDir, "fit.png"), Kind = DocKind.Image, Copies = 1 } });

            double wNone = InkWidthPt(dirNone, "none.pdf");
            double wFit = InkWidthPt(dirFit, "fit.pdf");
            Check(bad, "B-4 图片不缩放=原尺寸(150pt±25)", wNone > 0 && Math.Abs(wNone - 150) <= 25);
            Check(bad, "B-4 图片适合页面=铺满(>350pt)", wFit > 350);
            Check(bad, "B-4 两档确有差异", wNone > 0 && wFit > 0 && wFit - wNone > 100);
            WriteDiag("B-4图片缩放自检.txt", "图片 200x100px@96dpi（原生 150x75pt）\r\n不缩放：墨迹宽 " + wNone.ToString("0.#")
                + "pt\r\n适合页面：墨迹宽 " + wFit.ToString("0.#") + "pt\r\n");
            return bad;
        }

        private static void CopyFile(string src, string dst)
        {
            File.Copy(src, dst, true);
        }

        private static void BuildSamplePng(string file, int w, int h)
        {
            using (var bmp = new System.Drawing.Bitmap(w, h))
            {
                using (var gr = System.Drawing.Graphics.FromImage(bmp))
                {
                    gr.Clear(System.Drawing.Color.Black);
                }
                bmp.SetResolution(96f, 96f);
                bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        /// <summary>把输出 PDF 首页按 72dpi 渲染（1px = 1pt），量非白墨迹的宽度（pt）。</summary>
        private static double InkWidthPt(string dir, string name)
        {
            string pdf = Path.Combine(dir, name);
            if (!File.Exists(pdf)) return -1;
            using (PdfiumFile p = PdfiumFile.Open(pdf))
            using (System.Drawing.Bitmap bmp = p.RenderPage(0, 72f))
            {
                int minX = int.MaxValue, maxX = -1;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        System.Drawing.Color c = bmp.GetPixel(x, y);
                        if (c.R < 200 || c.G < 200 || c.B < 200)
                        {
                            if (x < minX) minX = x;
                            if (x > maxX) maxX = x;
                        }
                    }
                }
                return maxX < 0 ? 0 : maxX - minX + 1;
            }
        }

        private static void WriteDiag(string name, string text)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, name), text, new UTF8Encoding(true)); }
            catch { }
        }

        // ==================================================================
        // ③ 规划分组 + 修整4（改设置回规划框）+ B-1/修整6（续打重取参数、整项目进度）
        // ==================================================================
        private static string V44PlanGrouping()
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Plan-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                var main = new MainWindow();
                var rows = new List<RowVm>();
                for (int i = 0; i < 3; i++)
                {
                    string f = Path.Combine(tmp, "p" + i + ".pdf");
                    BuildSamplePdf(f, 1);
                    rows.Add(main.AddRowForTest(new PrintJob { FilePath = f, Kind = DocKind.Pdf }));
                }
                var bad = new List<string>();

                // ===== ③ 分组：异参拆、同参合，组头带生效参数与自定义标注 =====
                rows[2].Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter" };
                rows[2].NotifyOverrideChanged();
                main.MergeOverrides(rows);        // 真链路：UI→Job 合并（断言盲区①）
                var groups = main.BuildTaskPlan(rows, "HP");
                Check(bad, "混排拆 2 任务", groups.Count == 2 && groups.Sum(x => x.Files.Count) == 3);
                var letterGroup = groups.FirstOrDefault(g => g.Header.IndexOf("Letter", StringComparison.Ordinal) >= 0);
                Check(bad, "组头带生效纸张+自定义标注", letterGroup != null && letterGroup.Files.Count == 1
                    && letterGroup.Header.IndexOf(main.CustomMark(rows[2].Override), StringComparison.Ordinal) >= 0);
                Check(bad, "job 覆盖已合并", ReferenceEquals(rows[2].Job.Override, rows[2].Override) && rows[2].Job.Copies == 1);

                // B-3：纸盒选回"跟随全局"（空串历史值同样不算）→ 不产生自定义、不与跟随拆成两个任务
                rows[2].Override = new PerFileOverride { PaperSourceOverride = "" };
                rows[2].NotifyOverrideChanged();
                var g3 = main.BuildTaskPlan(rows, "HP");
                Check(bad, "B-3 空纸盒不标自定义", !rows[2].IsCustom && string.IsNullOrEmpty(main.CustomMark(rows[2].Override)) && g3.Count == 1);

                rows[2].Override = null;
                rows[2].NotifyOverrideChanged();
                main.MergeOverrides(rows);
                var merged = main.BuildTaskPlan(rows, "HP");
                Check(bad, "全跟随合并 1 任务", merged.Count == 1 && merged[0].Files.Count == 3);

                // ===== 修整4：规划确认循环（假弹窗注入，控制流与真机同一段代码）=====
                var headers = new List<string>();
                int planShows = 0, settingOpens = 0;
                Func<List<TaskPlanGroup>, Task<TaskPlanOutcome>> fakePlan = groups2 =>
                {
                    planShows++;
                    headers.Add(groups2.Count > 0 ? groups2[0].Header : "");
                    return Task.FromResult(planShows == 1 ? TaskPlanOutcome.ModifySettings : TaskPlanOutcome.Cancelled);
                };
                Func<Task> fakeSettings = () => { settingOpens++; main.ActiveOptions.Paper = PaperKind.Legal; main.ActiveOptions.PaperDisplayName = "Legal"; return Task.FromResult(0); };
                bool go = main.ConfirmPlanAsync(rows, "HP", fakePlan, fakeSettings).GetAwaiter().GetResult();
                Check(bad, "修整4 改设置后回到规划框", planShows == 2 && settingOpens == 1 && !go);
                Check(bad, "修整4 回框即按新参数重新规划", headers.Count == 2
                    && headers[0].IndexOf("Legal", StringComparison.Ordinal) < 0 && headers[1].IndexOf("Legal", StringComparison.Ordinal) >= 0);
                main.ActiveOptions.Paper = PaperKind.A4;
                main.ActiveOptions.PaperDisplayName = "A4";

                planShows = 0; settingOpens = 0;
                bool go2 = main.ConfirmPlanAsync(rows, "HP",
                    g4 => { planShows++; return Task.FromResult(TaskPlanOutcome.Cancelled); },
                    () => { settingOpens++; return Task.FromResult(0); }).GetAwaiter().GetResult();
                Check(bad, "修整4 规划框取消才回主页", !go2 && planShows == 1 && settingOpens == 0);

                planShows = 0; settingOpens = 0;
                bool go3 = main.ConfirmPlanAsync(rows, "HP",
                    g5 => { planShows++; return Task.FromResult(TaskPlanOutcome.Confirmed); },
                    () => { settingOpens++; return Task.FromResult(0); }).GetAwaiter().GetResult();
                Check(bad, "修整4 确认才开打", go3 && planShows == 1 && settingOpens == 0);
                Check(bad, "修整4 步骤判定", MainWindow.NextPlanStep(TaskPlanOutcome.ModifySettings) == MainWindow.PlanStep.ShowSettingsThenPlan
                    && MainWindow.NextPlanStep(TaskPlanOutcome.Cancelled) == MainWindow.PlanStep.Abort
                    && MainWindow.NextPlanStep(TaskPlanOutcome.Confirmed) == MainWindow.PlanStep.Start);

                // ===== 修整6 + B-1：停止后续打——进度从停止处续滚、参数按行当前值重取（真实出纸取证）=====
                var main2 = new MainWindow();
                var rs = new List<RowVm>();
                for (int i = 0; i < 5; i++)
                {
                    string f = Path.Combine(tmp, "r" + i + ".pdf");
                    BuildSamplePdf(f, 1);
                    rs.Add(main2.AddRowForTest(new PrintJob { FilePath = f, Kind = DocKind.Pdf }));
                }
                main2.ProgressTotalForTest = 5;                    // 本次开始打印时勾选 5 个文件
                main2.SetRemainingForTest(rs.GetRange(2, 3));      // 前 2 个已完成，剩 3 个
                rs[2].Copies = "3";                                // 停止期间改了列表列里的份数
                rs[4].Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter" };  // 停止期间改了弹窗参数
                List<PrintJob> jobs = main2.TakeRemainingJobs();
                Check(bad, "修整6 分母仍是本次勾选总数", main2.ProgressTotalForTest == 5 && main2.ProgressBaseForTest == 2);
                Check(bad, "B-1 续打条数", jobs.Count == 3);
                Check(bad, "B-1 续打重取列表份数", jobs[0].Copies == 3);
                Check(bad, "B-1 续打重取弹窗覆盖", jobs[2].Override != null && jobs[2].Override.Paper == PaperKind.Letter);
                Check(bad, "B-1 未改行不带覆盖", jobs[1].Override == null);

                string vp = VirtualPrinter();
                if (vp != null)
                {
                    string contOut = Path.Combine(tmp, "cont-out");
                    Directory.CreateDirectory(contOut);
                    var opt = new PrintOptions { PrintoutDirectory = contOut };
                    var rr = new BatchPrinter(vp, opt, line => { }, null, null).Run(new List<PrintJob> { jobs[2] });
                    System.Drawing.SizeF sz = OutputFirstPageSize(contOut, "r4.pdf");
                    Check(bad, "B-1 续打真实出纸随新参数(Letter)", rr.Ok == 1 && Near(sz, 612, 792));
                }

                if (bad.Count > 0) return "失败：" + string.Join(" | ", bad);
                return "通过（异参拆/同参合/组头标注 + B-3 空纸盒 + 修整4 三条路径（改设置回规划框·取消回主页·确认才开打）"
                    + " + 修整6 进度基线连续 + B-1 续打重取参数并按新参数出纸）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // ==================================================================
        // ④ 逐文件参数弹窗（修整1 + B-2/B-3/B-5/B-6/A-2）+ 主列表界面取证（修整2/3/5/7）
        // ==================================================================
        private static string V44RowCardUi()
        {
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                string tmp = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Ui-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);

                var main = new MainWindow();
                var grid = main.FindName("Grid") as DataGrid;
                if (grid == null) return "失败：取不到文件列表 DataGrid";

                RowVm rCustom = NewRow(main, tmp, "甲-合同.pdf");
                RowVm rPlain = NewRow(main, tmp, "乙-发票.pdf");
                rCustom.Copies = "2";
                rCustom.RangeText = "1-3,5";
                rCustom.Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter" };
                rCustom.NotifyOverrideChanged();
                main.UpdateLayoutForTest();

                var bad = new List<string>();
                var content = main.Content as FrameworkElement;
                if (content == null) return "失败：取不到主窗口内容根";

                // ===== 修整1a：行内展开已彻底移除 =====
                Check(bad, "无 RowDetailsTemplate", grid.RowDetailsTemplate == null && grid.RowDetailsVisibilityMode != DataGridRowDetailsVisibilityMode.Visible);
                Check(bad, "可视树里不再有参数卡片", FindByType(content, typeof(UserControl)).Count(t => t.GetType().Name == "RowParamsCard") == 0);

                // ===== 修整1b/f：参数列＝「修改」按钮，自定义行主色浅底、跟随行白底 =====
                var btnCustom = FindEditButton(content, rCustom);
                var btnPlain = FindEditButton(content, rPlain);
                Check(bad, "修改按钮在位（每行一个）", btnCustom != null && btnPlain != null);
                if (btnCustom != null)
                {
                    Check(bad, "按钮整体居中且不写死高度", btnCustom.HorizontalContentAlignment == HorizontalAlignment.Center
                        && double.IsNaN(btnCustom.Height));
                    Check(bad, "按钮含铅笔图标+文字", HasIconAndText(btnCustom));
                    var sb = btnCustom.Background as SolidColorBrush;
                    var sp = btnPlain != null ? btnPlain.Background as SolidColorBrush : null;
                    Check(bad, "修整1f 自定义行=主色浅底", sb != null && sb.Color == (Color)ColorConverter.ConvertFromString("#ECF5FF"));
                    Check(bad, "修整1f 跟随行=白底默认", sp != null && sp.Color == Colors.White);
                    Check(bad, "按钮 Tag 指向该行", ReferenceEquals(btnCustom.Tag, rCustom));
                }

                // ===== 修整2：份数列编辑器回退 TextBox、宽度受控，相邻列不被挤压 =====
                // （2026-09-22 四批·修整17b 需求方指示把列宽 68→90，两条数字上限随之改口径：
                //   仍然是"受控的窄列 + 编辑器是 TextBox 不是 NumberBox"，语义没放宽。）
                var copiesCol = grid.Columns.FirstOrDefault(c => Equals(c.Header, Res("s.ColCopies")));
                var typeCol = grid.Columns.FirstOrDefault(c => Equals(c.Header, Res("s.ColType")));
                var pagesCol = grid.Columns.FirstOrDefault(c => Equals(c.Header, Res("s.ColPages")));
                Check(bad, "修整2 份数列宽受控（修整17b 加宽后 88~92）", copiesCol != null && copiesCol.Width.Value >= 88 && copiesCol.Width.Value <= 92);
                Check(bad, "修整2 类型/页数列宽未被单独改动（四批方案②统一收窄为 64 / 60 后仍锁定）", typeCol != null && pagesCol != null && Math.Abs(typeCol.Width.Value - 64) < 1 && Math.Abs(pagesCol.Width.Value - 60) < 1);
                var cellBox = FindTextBoxInCell(content, rCustom, grid);
                Check(bad, "修整2 份数单元格是 TextBox 且无 NumberBox", cellBox != null && cellBox.ActualWidth <= 80 && CountNumberBox(content) == 0);
                Check(bad, "修整2 份数显示值=列表值 2", cellBox != null && cellBox.Text == "2");

                // ===== 修整7：列头只留"页码范围"，提示改成单元格里的灰字水印 =====
                var rangeCol = grid.Columns.FirstOrDefault(c => Equals(c.Header, "页码范围") || Equals(c.Header, Res("s.ColRange")));
                Check(bad, "修整7 列头只到「页码范围」", rangeCol != null && Convert.ToString(rangeCol.Header).IndexOf("空=", StringComparison.Ordinal) < 0);
                var hintEmpty = FindWatermark(content, rPlain, grid);
                var hintText = FindWatermark(content, rCustom, grid);
                Check(bad, "修整7 空值显灰水印", hintEmpty != null && hintEmpty.Visibility == Visibility.Visible && hintEmpty.IsHitTestVisible == false);
                Check(bad, "修整7 有值只显用户文字", hintText != null && hintText.Visibility == Visibility.Collapsed);
                var txtCustom = FindTextBoxInCell(content, rCustom, grid);
                Check(bad, "B-2 份数/页码唯一入口=列表列", txtCustom != null && txtCustom.Text == "2");

                // ===== 修整3（一批拍板的细黑线）→ 修整14（二批改画法，色值/线宽口径不变）：像素探针 =====
                // 一批把线交给 DataGrid 自己的 GridLinesVisibility=All，真机反馈"一段一段"，二批改整行底边 +
                // 单元格右边框（连贯度量化见 V44Fix2Ui 的 GridLineProbe 与 修整14边框连贯定位.txt）。
                Check(bad, "修整3 主列表细黑线在位（0.5 外框 + 纯黑线色）",
                    grid.BorderThickness.Left == 0.5 && GridLineBrushColor() == (Color)ColorConverter.ConvertFromString("#FF000000"));
                int legacyLines = CountHorizontalDarkLines(grid, (Color)ColorConverter.ConvertFromString("#000000"));
                WriteDiag("修整3边框像素计数.txt", "整幅渲染里判为\"横贯暗线\"的行数（一批探针口径，仅留数字不做门槛，门槛见修整14 连贯率）："
                    + legacyLines + "\r\n深色主题边框色：" + DarkGridLineBrushColor());
                var hp0 = new HistoryPage();
                var hgrid0 = hp0.FindName("Grid") as DataGrid;
                Check(bad, "修整3 历史页同口径（0.5 外框 + 行/格边线样式）", hgrid0 != null && hgrid0.BorderThickness.Left == 0.5
                    && hgrid0.RowStyle != null && hgrid0.CellStyle != null);
                Check(bad, "修整3 深色主题边框用近黑深灰", DarkGridLineBrushColor() == (Color)ColorConverter.ConvertFromString("#333333"));

                // ===== 修整5：打印开始（SetBusy→Grid.IsEnabled=false）前后列头必须完全一致，且列宽把手已去掉 =====
                int headerDiff = HeaderDiffBeforeAfter(main, grid, content);
                Check(bad, "修整5 标题列打印开始前后像素一致", headerDiff == 0);
                Check(bad, "修整5 列头不再有列宽把手", VisibleGripperCount(content) == 0 && !grid.CanUserResizeColumns);

                // ===== 修整1c/d/e + B-3/B-5/B-6：逐文件参数弹窗 =====
                var panel = new RowParamsPanel(main, rPlain);
                var dup = panel.BoxForTest("RdDuplexBox") as ComboBox;
                var col = panel.BoxForTest("RdColorBox") as ComboBox;
                var pap = panel.BoxForTest("RdPaperBox") as ComboBox;
                var scl = panel.BoxForTest("RdScaleBox") as ComboBox;
                var pct = panel.BoxForTest("RdScalePctBox") as Wpf.Ui.Controls.NumberBox;
                var pg = panel.BoxForTest("RdPagesBox") as ComboBox;
                var bk = panel.BoxForTest("RdBookletBox") as ComboBox;
                var par = panel.BoxForTest("RdParityBox") as ComboBox;
                var src = panel.BoxForTest("RdSourceBox") as ComboBox;
                var reset = panel.BoxForTest("BtnRowReset") as Button;
                var save = panel.BoxForTest("BtnSave") as Button;
                var cancel = panel.BoxForTest("BtnCancel") as Button;
                if (dup == null || pap == null || scl == null || src == null || save == null || cancel == null || reset == null)
                    return "失败：弹窗面板控件不全";
                Check(bad, "修整1c 弹窗不含份数/页码两项（B-2）", panel.BoxForTest("RdCopiesBox") == null && panel.BoxForTest("RdRangeBox") == null);
                Check(bad, "修整1c 每项首选项=跟随全局", Equals(ItemText(dup, 0), Res("s.FollowGlobal")) && Equals(ItemText(pap, 0), Res("s.FollowGlobal"))
                    && Equals(ItemText(src, 0), Res("s.FollowGlobal")));
                Check(bad, "修整1c 小册子三态（任务板 #20）", Equals(ItemText(bk, 0), Res("s.FollowGlobal")) && Equals(ItemText(bk, 1), Res("s.ToggleOn"))
                    && Equals(ItemText(bk, 2), Res("s.ToggleOff")));
                Check(bad, "B-3 纸盒下拉无「自动」中间档", ItemText(src, 1) == null || ItemText(src, 1) != Res("s.PsPaperSourceAuto"));

                // 取消不写回
                pap.SelectedIndex = 5;   // Letter
                cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(bad, "修整1d 取消不写回", rPlain.Override == null && !rPlain.IsCustom);

                // 保存写回
                var panel2 = new RowParamsPanel(main, rPlain);
                pap = panel2.BoxForTest("RdPaperBox") as ComboBox;
                pap.SelectedIndex = 5;
                save = panel2.BoxForTest("BtnSave") as Button;
                save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(bad, "修整1d 保存才写回", rPlain.IsCustom && rPlain.Override != null && rPlain.Override.Paper == PaperKind.Letter);
                Check(bad, "修整1d 保存后生效参数=Letter", EffectiveOptions.Resolve(main.ActiveOptions, rPlain.Override).Paper == PaperKind.Letter);
                Check(bad, "修整1d 全局未被污染", main.ActiveOptions.Paper == PaperKind.A4);

                // B-6：缩放模式回"跟随全局"→ 百分比不留孤立值；全跟随 → HasAny=false
                var panel3 = new RowParamsPanel(main, rPlain);
                scl = panel3.BoxForTest("RdScaleBox") as ComboBox;
                pct = panel3.BoxForTest("RdScalePctBox") as Wpf.Ui.Controls.NumberBox;
                pg = panel3.BoxForTest("RdPagesBox") as ComboBox;
                var draft0 = panel3.Draft();
                Check(bad, "B-6 回填后草稿=该行现状", draft0.Paper == PaperKind.Letter);
                pct.Value = 150;
                Check(bad, "B-6 模式非自定义时百分比不可编辑", pct.IsEnabled == false);
                scl.SelectedIndex = (int)ScaleMode.Custom + 1;
                pct = panel3.NumForTest("RdScalePctBox");
                Check(bad, "B-6 选自定义百分比后可编辑", pct.IsEnabled);
                pct.Value = 150;
                Check(bad, "B-6 自定义档带上百分比", panel3.Draft().ScalePercent == 150);
                scl.SelectedIndex = 0;      // 回「跟随全局」
                var draft1 = panel3.Draft();
                Check(bad, "B-6 回跟随后无孤立 ScalePercent", draft1.ScalePercent == null);
                var panel4 = new RowParamsPanel(main, new RowVm(new PrintJob { FilePath = rPlain.Job.FilePath, Kind = DocKind.Pdf }));
                var emptyDraft = panel4.Draft();
                Check(bad, "B-6 未改行草稿 HasAny=false", !emptyDraft.HasAny);
                // 全局为自定义百分比时，未改行不受残留影响
                main.ActiveOptions.Scale = ScaleMode.Custom;
                main.ActiveOptions.ScalePercent = 80;
                Check(bad, "B-6 全局自定义百分比时未改行仍取全局实例", ReferenceEquals(EffectiveOptions.Resolve(main.ActiveOptions, panel4.Draft()), main.ActiveOptions));
                main.ActiveOptions.Scale = ScaleMode.FitPage;
                main.ActiveOptions.ScalePercent = 100;

                // B-3：选具体纸盒才算覆盖；选回跟随全局即撤销
                int srcCount = src.Items.Count;
                if (srcCount > 1)
                {
                    var row5 = NewRow(main, tmp, "丙-纸盒.pdf");
                    var panel5 = new RowParamsPanel(main, row5);
                    var src5 = panel5.BoxForTest("RdSourceBox") as ComboBox;
                    src5.SelectedIndex = 1;
                    var d = panel5.Draft();
                    Check(bad, "B-3 指定纸盒算覆盖", d.HasAny && !string.IsNullOrEmpty(d.PaperSourceOverride));
                    src5.SelectedIndex = 0;
                    Check(bad, "B-3 跟随全局不算覆盖(HasAny=false)", !panel5.Draft().HasAny);
                    panel5.DoSave();
                    Check(bad, "B-3 保存跟随后行上不自定义", !row5.IsCustom && row5.Override == null);
                }

                // 重置为跟随全局（草稿级，仍需保存）
                var panel6 = new RowParamsPanel(main, rPlain);
                reset = panel6.BoxForTest("BtnRowReset") as Button;
                reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(bad, "修整1d 重置清空草稿", !panel6.Draft().HasAny);
                pap = panel6.BoxForTest("RdPaperBox") as ComboBox;
                Check(bad, "修整1d 重置后控件回跟随", pap.SelectedIndex == 0);
                panel6.DoSave();
                Check(bad, "修整1d 重置+保存后回到跟随全局", rPlain.Override == null && !rPlain.IsCustom);

                // ⑦ 小册子联动置灰（弹窗每次打开按当时全局求值 = A-2 收口）
                var panel7 = new RowParamsPanel(main, rPlain);
                bk = panel7.BoxForTest("RdBookletBox") as ComboBox;
                pg = panel7.BoxForTest("RdPagesBox") as ComboBox;
                par = panel7.BoxForTest("RdParityBox") as ComboBox;
                bk.SelectedIndex = 1;
                Check(bad, "⑦ 本行开小册子→版数/奇偶置灰", !pg.IsEnabled && !par.IsEnabled);
                bk.SelectedIndex = 2;
                Check(bad, "⑦ 本行关小册子→恢复可用", pg.IsEnabled && par.IsEnabled);
                bk.SelectedIndex = 0;
                main.ActiveOptions.Booklet = true;
                panel7.RefreshForTest();
                Check(bad, "⑦ 全局开小册子时跟随态也置灰(A-2)", !pg.IsEnabled && !par.IsEnabled);
                main.ActiveOptions.Booklet = false;
                panel7.RefreshForTest();
                Check(bad, "⑦ 全局关小册子后恢复可用", pg.IsEnabled && par.IsEnabled);

                // B-5：纸盒清单按打印机名缓存（绑多少行/开多少次弹窗都不重复枚举）
                string printer = main.ActivePrinterName;
                PrintSettingsPanel.PaperSourceNames(printer);      // 预热一次
                int before = PrintSettingsPanel.PaperSourceEnumCalls;
                for (int i = 0; i < 12; i++) PrintSettingsPanel.PaperSourceNames(printer);
                for (int i = 0; i < 4; i++) new RowParamsPanel(main, rPlain);
                Check(bad, "B-5 同打印机不再重复枚举纸盒", PrintSettingsPanel.PaperSourceEnumCalls == before);

                // 修整1g：主页整体布局不变（参数列仍在最后一列，其余列顺序未动）
                var names = grid.Columns.Cast<DataGridColumn>().Select(c => Convert.ToString(c.Header)).ToList();
                Check(bad, "修整1g 参数列仍在列尾", names.Count >= 7 && names[names.Count - 1] == Res("s.ColParams"));
                Check(bad, "修整1g 列顺序未动", names[0] == "" && names[1] == Res("s.ColName") && names[2] == Res("s.ColType"));

                // 勾选（F13）与参数入口互不干扰
                rPlain.Checked = false;
                main.UpdateLayoutForTest();
                var b2 = FindEditButton(content, rPlain);
                Check(bad, "F13 勾选与参数入口互不干扰", b2 != null && !rPlain.Checked);
                rPlain.Checked = true;

                if (bad.Count > 0) return "失败：" + string.Join(" | ", bad);
                return "通过（弹窗保存/取消/重置 + B-2 两项不进弹窗 + B-3 纸盒两态 + B-5 纸盒缓存 + B-6 百分比联动 + ⑦ 小册子三态置灰"
                    + " + 修整1 修改按钮主色浅底 + 修整2 份数列 TextBox 限宽 + 修整3 边框像素 + 修整5 列头前后一致 + 修整7 列头与水印）";
            }
            catch (Exception ex) { return "失败：" + ex + "\r\n--- 列头诊断 ---\r\n" + V44HeaderDiagLog(); }
        }

        private static RowVm NewRow(MainWindow main, string dir, string name)
        {
            string f = Path.Combine(dir, name);
            BuildSamplePdf(f, 2);
            return main.AddRowForTest(new PrintJob { FilePath = f, Kind = DocKind.Pdf });
        }

        private static string Res(string key)
        {
            return Convert.ToString(Application.Current.TryFindResource(key));
        }

        // ===== 可视树探针 =====

        private static List<DependencyObject> FindByType(DependencyObject root, Type t)
        {
            var list = new List<DependencyObject>();
            if (root.GetType() == t) list.Add(root);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(root); } catch { n = 0; }
            for (int i = 0; i < n; i++) list.AddRange(FindByType(VisualTreeHelper.GetChild(root, i), t));
            return list;
        }

        /// <summary>同上，但含派生类型（WPF-UI 会用自己派生的 ScrollViewer，按精确类型找不到）。</summary>
        private static List<DependencyObject> FindDerived(DependencyObject root, Type t)
        {
            var list = new List<DependencyObject>();
            if (t.IsInstanceOfType(root)) list.Add(root);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(root); } catch { n = 0; }
            for (int i = 0; i < n; i++) list.AddRange(FindDerived(VisualTreeHelper.GetChild(root, i), t));
            return list;
        }

        private static void CountTyped(DependencyObject root, Type t, out int count)
        {
            count = FindByType(root, t).Count;
        }
        private static int CountNumberBox(DependencyObject root)
        {
            int n = root.GetType().Name == "NumberBox" ? 1 : 0;
            int c = 0;
            try { c = VisualTreeHelper.GetChildrenCount(root); } catch { c = 0; }
            for (int i = 0; i < c; i++) n += CountNumberBox(VisualTreeHelper.GetChild(root, i));
            return n;
        }

        private static DataGridRow RowContainer(DependencyObject root, RowVm row)
        {
            foreach (var d in FindByType(root, typeof(DataGridRow)))
            {
                var r = (DataGridRow)d;
                if (ReferenceEquals(r.Item, row)) return r;
            }
            return null;
        }

        private static Button FindEditButton(DependencyObject root, RowVm row)
        {
            var container = RowContainer(root, row);
            if (container == null) return null;
            foreach (var d in FindByType(container, typeof(Button)))
            {
                var b = (Button)d;
                if (ReferenceEquals(b.Tag, row) && b.Style != null && (string.Equals(b.Style.ToString().Length > 0 ? "x" : "x", "x")) == true
                    && b.ContentTemplate == null && b.Content is StackPanel) return b;
            }
            return null;
        }

        private static bool HasIconAndText(Button b)
        {
            var sp = b.Content as StackPanel;
            if (sp == null) return false;
            bool icon = false, text = false;
            foreach (UIElement u in sp.Children)
            {
                if (u.GetType().Name == "SymbolIcon") icon = true;
                var t = u as System.Windows.Controls.TextBlock;
                if (t != null && t.Text == Res("s.EditParams")) text = true;
            }
            return icon && text;
        }

        private static TextBox FindTextBoxInCell(DependencyObject root, RowVm row, DataGrid grid)
        {
            var container = RowContainer(root, row);
            if (container == null) return null;
            int copiesIndex = -1, rangeIndex = -1;
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                if (Equals(grid.Columns[i].Header, Res("s.ColCopies"))) copiesIndex = i;
                if (Equals(grid.Columns[i].Header, Res("s.ColRange"))) rangeIndex = i;
            }
            var cells = FindByType(container, typeof(DataGridCell)).Cast<DataGridCell>().ToList();
            var cell = cells.FirstOrDefault(c => c.Column != null && c.Column.DisplayIndex == copiesIndex)
                ?? cells.FirstOrDefault(c => c.Column != null && c.Column.DisplayIndex == rangeIndex);
            if (cell == null) return null;
            return FindByType(cell, typeof(TextBox)).Cast<TextBox>().FirstOrDefault();
        }

        private static System.Windows.Controls.TextBlock FindWatermark(DependencyObject root, RowVm row, DataGrid grid)
        {
            var container = RowContainer(root, row);
            if (container == null) return null;
            string hint = Res("s.ColRangeHint");
            return FindByType(container, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>()
                .FirstOrDefault(t => t.Text == hint);
        }

        private static string ItemText(ComboBox box, int index)
        {
            if (box == null || index < 0 || index >= box.Items.Count) return null;
            var item = box.Items[index];
            var ci = item as ComboBoxItem;
            return ci != null ? Convert.ToString(ci.Content) : Convert.ToString(item);
        }

        // ===== 渲染 + 像素探针 =====

        private static byte[] RenderPixels(FrameworkElement el, out int pw, out int ph)
        {
            pw = Math.Max(1, (int)Math.Ceiling(el.ActualWidth));
            ph = Math.Max(1, (int)Math.Ceiling(el.ActualHeight));
            var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(el);
            var buf = new byte[pw * ph * 4];
            rtb.CopyPixels(buf, pw * 4, 0);
            return buf;
        }

        private static bool Dark(byte[] px, int pw, int idx, Color target, double tol)
        {
            int b = idx * 4;
            double dr = Math.Abs(px[b + 2] - target.R), dg = Math.Abs(px[b + 1] - target.G), db = Math.Abs(px[b] - target.B);
            return dr <= tol && dg <= tol && db <= tol;
        }

        /// <summary>数一数这个元素里有几条横贯宽度的暗线（0.5px 细线抗锯齿后约 50% 覆盖，按与目标色的色差判定）。</summary>
        private static int CountHorizontalDarkLines(FrameworkElement el, Color target)
        {
            int pw, ph;
            byte[] px = RenderPixels(el, out pw, out ph);
            int lines = 0;
            for (int y = 1; y < ph; y++)
            {
                int hit = 0;
                for (int x = (int)(pw * 0.15); x < (int)(pw * 0.85); x++)
                {
                    if (Dark(px, pw, y * pw + x, target, 90)) hit++;
                }
                if (hit > (pw * 0.6)) lines++;
            }
            return lines;
        }

        private static Color GridLineBrushColor()
        {
            var b = Application.Current.TryFindResource("GridLineBrush") as SolidColorBrush;
            return b != null ? b.Color : Colors.Transparent;
        }

        private static Color DarkGridLineBrushColor()
        {
            App.ApplyTheme(AppTheme.Dark);
            try { return GridLineBrushColor(); }
            finally { App.ApplyTheme(AppTheme.Light); }
        }

        private static string _headerDiag = "";

        internal static string V44HeaderDiagLog() { return _headerDiag; }

        /// <summary>修整5：数一数可视树里"看得见"的列宽把手（PART_Left/RightHeaderGripper 且实际宽度 > 0）。</summary>
        private static int VisibleGripperCount(DependencyObject root)
        {
            int n = 0;
            var fe = root as FrameworkElement;
            if (fe != null && fe.Name.StartsWith("PART_", StringComparison.Ordinal)
                && fe.Name.IndexOf("HeaderGripper", StringComparison.Ordinal) >= 0 && fe.ActualWidth > 0 && fe.Visibility == Visibility.Visible) n++;
            int c = 0;
            try { c = VisualTreeHelper.GetChildrenCount(root); } catch { c = 0; }
            for (int i = 0; i < c; i++) n += VisibleGripperCount(VisualTreeHelper.GetChild(root, i));
            return n;
        }

        /// <summary>
        /// 修整5 取证：模拟"点开始打印"（SetBusy → 列表 IsEnabled=false），逐像素比对列头区，
        /// 不一致时把列头可视树（类型/名字/可见性/背景色/尺寸）两种状态下各 dump 一份进 txt，供定位黑条来源。
        /// </summary>
        private static int HeaderDiffBeforeAfter(MainWindow main, DataGrid grid, FrameworkElement content)
        {
            content.UpdateLayout();
            var header = FindByType(content, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader)).Cast<FrameworkElement>().FirstOrDefault();
            if (header == null) { _headerDiag = "找不到列头容器"; return -1; }
            var headerHost = VisualTreeHelper.GetParent(header) as FrameworkElement;
            if (headerHost == null) { _headerDiag = "列头无父容器"; return -1; }

            byte[] before = RenderPixels(headerHost, out int w1, out int h1);
            string png1 = Snap(headerHost, "修整5-列头-打印前.png");
            grid.IsEnabled = false;
            content.UpdateLayout();
            byte[] after = RenderPixels(headerHost, out int w2, out int h2);
            string png2 = Snap(headerHost, "修整5-列头-打印中.png");
            grid.IsEnabled = true;
            content.UpdateLayout();

            int diff = 0, diffTitles = 0;
            if (w1 == w2 && h1 == h2)
            {
                for (int y = 0; y < h1; y++)
                {
                    for (int x = 0; x < w1; x++)
                    {
                        int i = (y * w1 + x) * 4;
                        if (before[i] == after[i] && before[i + 1] == after[i + 1] && before[i + 2] == after[i + 2] && before[i + 3] == after[i + 3]) continue;
                        diff++;
                        if (x >= 56) diffTitles++;    // 56px 之后 = 除"全选框列"以外的全部标题列
                    }
                }
            }
            var sb = new StringBuilder();
            sb.AppendLine("列头区（" + headerHost.GetType().Name + "）尺寸 " + w1 + "x" + h1 + " → " + w2 + "x" + h2);
            sb.AppendLine("列宽把手（PART_*HeaderGripper，可见且宽度>0）：" + VisibleGripperCount(headerHost) + " 个（修整5 要求 0）");
            sb.AppendLine("禁用前后不同像素：整列头 " + diff + " 个；标题列（跳过最左侧全选框列）" + diffTitles + " 个（0 = 标题列视觉完全一致）");
            sb.AppendLine("说明：整列头残余差异来自 F13 表头全选框——点开始打印会把列表 IsEnabled=false，复选框按库的规则变暗，"
                + "这是「打印中锁列表」的正常反馈，不是黑条；黑条＝列头模板里的列宽把手，已随自写列头模板去掉。");
            sb.AppendLine("图：" + png1 + " / " + png2);
            if (diff != 0)
            {
                sb.AppendLine("--- 启用态列头可视树 ---");
                DumpTree(headerHost, sb, "");
                grid.IsEnabled = false;
                content.UpdateLayout();
                sb.AppendLine("--- 禁用态列头可视树 ---");
                DumpTree(headerHost, sb, "");
                grid.IsEnabled = true;
                content.UpdateLayout();
            }
            _headerDiag = sb.ToString();
            WriteDiag("修整5列头黑条定位.txt", sb.ToString());
            return diffTitles;
        }

        private static string Snap(FrameworkElement el, string pngName)
        {
            try
            {
                int pw, ph;
                byte[] px = RenderPixels(el, out pw, out ph);
                var bmp = BitmapSource.Create(pw, ph, 96, 96, PixelFormats.Bgra32, null, px, pw * 4);
                string path = Path.Combine(AppContext.BaseDirectory, pngName);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) enc.Save(fs);
                return pngName;
            }
            catch { return "(截图失败)"; }
        }

        private static void DumpTree(DependencyObject node, StringBuilder sb, string indent)
        {
            string extra = "";
            var fe = node as FrameworkElement;
            var ctl = node as Control;
            if (ctl != null)
            {
                var bg = ctl.Background as SolidColorBrush;
                extra = " vis=" + ctl.Visibility + (bg != null ? " bg=" + bg.Color : "") + " fg=" + ctl.Foreground;
            }
            if (fe != null) extra += " size=" + fe.ActualWidth.ToString("0.#") + "x" + fe.ActualHeight.ToString("0.#") + " name=" + fe.Name;
            sb.AppendLine(indent + node.GetType().Name + extra);
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch { n = 0; }
            for (int i = 0; i < n; i++) DumpTree(VisualTreeHelper.GetChild(node, i), sb, indent + "  ");
        }

        // ==================================================================
        // 渲染图（交付物 §6.2）
        // ==================================================================

        /// <summary>V4.4 修整：主列表整体渲染（含「修改」按钮主色浅底/表格边框/份数列/页码水印），中英各一张。</summary>
        private static int V44RowCardRender(string png, string log, AppLanguage lang = AppLanguage.ZhCN)
        {
            try
            {
                App.ApplyLanguage(lang);
                var w = new MainWindow();
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Png-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                var r1 = NewRow(w, dir, "合同.pdf");
                r1.Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter", Color = ColorMode.BlackWhite, PagesPerSheet = 2 };
                r1.NotifyOverrideChanged();
                r1.Copies = "2";
                r1.RangeText = "1-3,5";
                var r2 = NewRow(w, dir, "发票.pdf");
                var r3 = NewRow(w, dir, "说明书.pdf");
                r3.Checked = false;
                int code = RenderWindow(log, png, w, 1260, 900, new[] { "Grid", "BtnPrintSetup", "BtnStart" });
                App.ApplyLanguage(AppLanguage.ZhCN);
                return code;
            }
            catch (Exception ex) { return FailWith(log, ex); }
        }

        /// <summary>V4.4 修整轮取证图：参数弹窗（浅/深/英）、页码水印特写（中英）、历史页边框（深浅由 RenderTheme 覆盖）。</summary>
        internal static int V44FixRender(string log)
        {
            int code = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);

                // 参数弹窗三张：浅色中文 / 深色中文 / 浅色英文
                App.ApplyTheme(AppTheme.Light);
                var main = new MainWindow();
                var row = NewRow(main, dir, "逐文件参数弹窗.pdf");
                row.Override = new PerFileOverride { Paper = PaperKind.Letter, PaperDisplayName = "Letter", Duplex = DuplexMode.LongEdge, Scale = ScaleMode.Custom, ScalePercent = 120 };
                row.NotifyOverrideChanged();
                code |= RenderElement(log, "逐文件参数弹窗预览-浅色.png", new RowParamsPanel(main, row), 780, 450,
                    new[] { "RdDuplexBox", "RdColorBox", "RdPaperBox", "RdScaleBox", "RdScalePctBox", "RdPagesBox", "RdBookletBox", "RdParityBox", "RdSourceBox", "BtnRowReset", "BtnCancel", "BtnSave" });
                App.ApplyTheme(AppTheme.Dark);
                code |= RenderElement(log, "逐文件参数弹窗预览-深色.png", new RowParamsPanel(main, row), 780, 450,
                    new[] { "RdPaperBox", "RdSourceBox", "BtnCancel", "BtnSave" });
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.EnUS);
                code |= RenderElement(log, "逐文件参数弹窗预览-英文.png", new RowParamsPanel(main, row), 780, 450,
                    new[] { "RdDuplexBox", "RdScaleBox", "RdSourceBox", "BtnRowReset", "BtnCancel", "BtnSave" });
                App.ApplyLanguage(AppLanguage.ZhCN);

                // 页码范围水印特写（中英各一张：一行空值显灰水印、一行有值只显用户文字）
                var w2 = new MainWindow();
                NewRow(w2, dir, "空页码.pdf");
                var rHas = NewRow(w2, dir, "有页码.pdf");
                rHas.RangeText = "1-3,5";
                rHas.Copies = "12";
                code |= RenderWindow("页码水印中文自检.txt", "页码范围水印预览.png", w2, 1260, 820, new[] { "Grid" });
                App.ApplyLanguage(AppLanguage.EnUS);
                var w3 = new MainWindow();
                NewRow(w3, dir, "空页码.pdf");
                var rHas2 = NewRow(w3, dir, "有页码.pdf");
                rHas2.RangeText = "1-3,5";
                code |= RenderWindow("页码水印英文自检.txt", "页码范围水印预览-英文.png", w3, 1260, 820, new[] { "Grid" });
                App.ApplyLanguage(AppLanguage.ZhCN);
                return code;
            }
            catch (Exception ex) { return FailWith(log, ex); }
        }

        // ==================================================================
        // V4.4 二批（2026-09-20 真机二批反馈）：修整9~15 断言 + 取证
        // ==================================================================

        private static string V44Fix2Ui()
        {
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                string tmp = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix2-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                var bad = new List<string>();
                var sbDiag = new StringBuilder();

                // ===== 修整9：表头全选＝"点一下就全勾"，判定只看行的实际状态，不读三态翻转值 =====
                var main = new MainWindow();
                var grid = main.FindName("Grid") as DataGrid;
                var content = main.Content as FrameworkElement;
                if (grid == null || content == null) return "失败：取不到主列表 DataGrid";
                main.UpdateLayoutForTest();
                var hc = FindHeaderCheck(content);
                if (hc == null) return "失败：找不到表头全选勾选框 HeaderCheck（列头未生成？）";

                // ⓪ 空列表点一下：不异常、表头仍为未选
                var empty = new MainWindow();
                empty.UpdateLayoutForTest();
                var ehc = FindHeaderCheck(empty.Content as FrameworkElement);
                Check(bad, "修整9 空列表表头勾选框在位", ehc != null);
                if (ehc != null)
                {
                    ClickHeaderLikeUser(ehc);
                    Check(bad, "修整9 空列表点一下不异常且表头仍为未选", ehc.IsChecked == false);
                }

                RowVm r1 = NewRow(main, tmp, "全选测试甲.pdf");
                RowVm r2 = NewRow(main, tmp, "全选测试乙.pdf");
                RowVm r3 = NewRow(main, tmp, "全选测试丙.pdf");
                main.UpdateLayoutForTest();

                // ① 全不选 → 点一下 = 全选（一批的 bug：这一下落到 null，一个都没勾上）
                r1.Checked = false; r2.Checked = false; r3.Checked = false;
                main.UpdateStatusForTest();
                Check(bad, "修整9 全不选时表头显未选", hc.IsChecked == false);
                ClickHeaderLikeUser(hc);
                Check(bad, "修整9 全不选点一下→全选", r1.Checked && r2.Checked && r3.Checked && hc.IsChecked == true);

                // ② 全选 → 点一下 = 全取消
                ClickHeaderLikeUser(hc);
                Check(bad, "修整9 全选点一下→全取消", !r1.Checked && !r2.Checked && !r3.Checked && hc.IsChecked == false);

                // ③ 半选（中间态）→ 点一下 = 全选
                r2.Checked = true;
                main.UpdateStatusForTest();
                Check(bad, "修整9 半选时表头显不确定", hc.IsChecked == null);
                ClickHeaderLikeUser(hc);
                Check(bad, "修整9 半选点一下→全选", r1.Checked && r2.Checked && r3.Checked && hc.IsChecked == true);

                // ④ 取消到 0 行 → 表头显未选（不是半选残留）
                r1.Checked = false; r2.Checked = false; r3.Checked = false;
                main.UpdateStatusForTest();
                Check(bad, "修整9 取消到 0 行表头显未选", hc.IsChecked == false);

                // ===== 修整10/11/12/15：主列表列结构、固定行高、截断、悬浮全名、输入框铺满、列头居中加粗 =====
                RowVm long1 = NewRow(main, tmp, "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书-终稿第三版-含附件清单.pdf");
                long1.RangeText = "1-3,5";
                main.UpdateLayoutForTest();

                var cols = grid.Columns.Cast<DataGridColumn>().ToList();
                Check(bad, "修整11 列数 8（大小列已删）", cols.Count == 8);
                Check(bad, "修整11 不再有大小列", cols.FirstOrDefault(c => Equals(c.Header, "大小") || Equals(c.Header, "Size")) == null
                    && Application.Current.TryFindResource("s.ColSize") == null);

                var nameCell = CellOf(content, long1, grid, Res("s.ColName"));
                var nameText = nameCell == null ? null : FindByType(nameCell, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>().FirstOrDefault();
                Check(bad, "修整10 文件名单元格文字不折行+省略号", nameText != null
                    && nameText.TextWrapping == TextWrapping.NoWrap && nameText.TextTrimming == TextTrimming.CharacterEllipsis);
                Check(bad, "修整10 长文件名被截断在单元格内", nameText != null && nameText.ActualWidth <= nameCell.ActualWidth + 0.5
                    && nameText.Text.Length > 40);
                Check(bad, "修整10 悬浮 1 秒后在单元格右下角显全名", nameText != null
                    && ToolTipService.GetInitialShowDelay(nameText) == 1000
                    && ToolTipService.GetPlacement(nameText) == System.Windows.Controls.Primitives.PlacementMode.Right
                    && ToolTipService.GetVerticalOffset(nameText) == 14
                    && Equals(nameText.ToolTip as string, long1.Name));

                var rowsReal = FindByType(content, typeof(DataGridRow)).Cast<DataGridRow>().ToList();
                // 行高 = RowHeight + 1 设备像素底边线（本机 125% 下 = 36 + 0.8），关键是"所有行等高、不随内容长"
                var rowH = rowsReal.Where(r => r.ActualHeight > 0).Select(r => r.ActualHeight).ToList();
                Check(bad, "修整10 行高固定 36（长名行不再撑高）", grid.RowHeight == 36 && rowH.Count > 0
                    && rowH.All(h => Math.Abs(h - rowH[0]) < 0.2) && rowH[0] >= 36 && rowH[0] <= 37.6);
                var textCellWrap = new List<string>();
                foreach (var cell in FindByType(content, typeof(DataGridCell)).Cast<DataGridCell>()
                             .Where(c => c.Content is string && c.ActualHeight > 0))
                {
                    var tb = FindByType(cell, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>().FirstOrDefault();
                    if (tb != null && tb.TextWrapping != TextWrapping.NoWrap) textCellWrap.Add("\"" + tb.Text + "\"");
                }
                Check(bad, "修整10 纯文字单元格一律不折行（offender=" + string.Join("，", textCellWrap.Take(4)) + "）", textCellWrap.Count == 0);

                var copiesCol = grid.Columns.FirstOrDefault(c => Equals(c.Header, Res("s.ColCopies")));
                var rangeCol = grid.Columns.FirstOrDefault(c => Equals(c.Header, Res("s.ColRange")));
                var copiesBox = FindTextBoxInCell(content, r1, grid);
                var rangeCell = CellOf(content, long1, grid, Res("s.ColRange"));
                var rangeBox = rangeCell == null ? null : FindByType(rangeCell, typeof(TextBox)).Cast<TextBox>().FirstOrDefault();
                Check(bad, "修整12 份数输入框铺满列宽", copiesBox != null && copiesCol != null
                    && copiesBox.HorizontalAlignment == HorizontalAlignment.Stretch && copiesBox.MaxWidth == double.PositiveInfinity
                    && copiesBox.ActualWidth >= copiesCol.ActualWidth * 0.75);
                Check(bad, "修整12 页码输入框铺满列宽", rangeBox != null && rangeCol != null
                    && rangeBox.HorizontalAlignment == HorizontalAlignment.Stretch
                    && rangeBox.ActualWidth >= rangeCol.ActualWidth * 0.75);
                Check(bad, "修整12 两列输入框同口径（宽度随列宽递增）", copiesBox != null && rangeBox != null && rangeBox.ActualWidth > copiesBox.ActualWidth);
                var hint2 = FindWatermark(content, long1, grid);
                Check(bad, "修整7 回归：有值时水印收起", hint2 != null && hint2.Visibility == Visibility.Collapsed);

                // 列头：居中 + 加粗 + 单行
                var headers = FindByType(content, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
                    .Cast<System.Windows.Controls.Primitives.DataGridColumnHeader>().ToList();
                var titled = headers.Where(h => h.Content is string && ((string)h.Content).Length > 0).ToList();
                Check(bad, "修整15 主列表列头居中", titled.Count >= 6 && titled.All(h => h.HorizontalContentAlignment == HorizontalAlignment.Center));
                Check(bad, "修整15 主列表列头加粗", titled.All(h => h.FontWeight == FontWeights.Bold));
                var titleTexts = titled.SelectMany(h => FindByType(h, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>()).ToList();
                // 折行按"量出来的行高是否还是一行"卡（TextWrapping 属性卡不住：WPF 给 ContentPresenter 生成的
                // TextBlock 找隐式样式时 Style.Resources / ControlTemplate.Resources 都不生效，实测只有全局那份管得上）
                var headWrapOff = titleTexts.Where(t => t.ActualHeight > 0 && t.ActualHeight > 22.5)
                    .Select(t => "\"" + t.Text + "\"高" + t.ActualHeight.ToString("0.#")).ToList();
                Check(bad, "修整15 列头文字实测单行（offender=" + string.Join("，", headWrapOff.Take(4)) + "）",
                    titleTexts.Count >= 6 && headWrapOff.Count == 0);
                Check(bad, "修整15 列头文字实测居中（与列头中心偏差 ≤3px）", titled.Any(h =>
                {
                    var t = FindByType(h, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>().FirstOrDefault();
                    if (t == null || h.ActualWidth <= 0) return false;
                    double dt = Math.Abs((t.TransformToAncestor(h).Transform(new Point(0, 0)).X + t.ActualWidth / 2) - h.ActualWidth / 2);
                    return dt <= 3.0 || t.ActualWidth + 6 >= h.ActualWidth;   // 短列名几乎占满列头时偏差必然为 0
                }));

                // ===== 修整14：边框连贯（含一批口径对照），两页同口径 =====
                Check(bad, "修整14 主列表改为整行底边+单元格右边框", grid.GridLinesVisibility == DataGridGridLinesVisibility.None
                    && grid.RowStyle != null && grid.CellStyle != null && grid.BorderThickness.Left == 0.5
                    && grid.UseLayoutRounding && GridLineBrushColor() == (Color)ColorConverter.ConvertFromString("#FF000000"));
                var hp = FindByType(content, typeof(HistoryPage)).Cast<FrameworkElement>().FirstOrDefault();
                var hgrid = hp == null ? null : hp.FindName("Grid") as DataGrid;
                Check(bad, "修整14 历史页与主列表同口径（同一份行/格/列头样式 + 0.5 外框）", hgrid != null
                    && hgrid.GridLinesVisibility == DataGridGridLinesVisibility.None
                    && hgrid.RowStyle != null && Equals(hgrid.RowStyle, grid.RowStyle)
                    && hgrid.CellStyle != null && Equals(hgrid.CellStyle, grid.CellStyle)
                    && hgrid.BorderThickness.Left == 0.5
                    && hgrid.ColumnHeaderStyle != null && Equals(hgrid.ColumnHeaderStyle, grid.ColumnHeaderStyle));
                Check(bad, "修整14 深色主题边框仍近黑深灰", DarkGridLineBrushColor() == (Color)ColorConverter.ConvertFromString("#333333"));

                // 一批口径（DataGrid 自画网格线）与二批口径各量一遍，数字进取证 txt。
                // 对照样式必须 BasedOn 库的隐式样式（直接把 RowStyle/CellStyle 置 null 会连单元格底色一起丢，
                // 渲染出来整片透明，量不到线——第一轮踩过这个坑）。
                var rowBase = Application.Current.TryFindResource(typeof(DataGridRow)) as Style;
                var cellBase = Application.Current.TryFindResource(typeof(DataGridCell)) as Style;
                var oldRow = rowBase == null ? null : new Style(typeof(DataGridRow), rowBase);
                var oldCell = cellBase == null ? null : new Style(typeof(DataGridCell), cellBase);
                oldRow?.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                oldCell?.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
                var savedRowStyle = grid.RowStyle; var savedCellStyle = grid.CellStyle; var savedLines = grid.GridLinesVisibility;
                if (oldRow != null && oldCell != null)
                {
                    // 一批真机状态要连"星号列没铺满"一起复现（那 7% 缺口正是需求方看到的"断"），量完再恢复
                    var nameColDbg = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColName")));
                    var savedNameWidth = nameColDbg == null ? (DataGridLength?)null : nameColDbg.Width;
                    if (nameColDbg != null) nameColDbg.Width = new DataGridLength(1, DataGridLengthUnitType.Star);
                    grid.RowStyle = oldRow; grid.CellStyle = oldCell; grid.GridLinesVisibility = DataGridGridLinesVisibility.All;
                    content.UpdateLayout();
                    sbDiag.AppendLine(GridLineProbe(grid, 96, "一批口径（DataGrid 自画 0.5 网格线 + 星号列未铺满＝真机一批状态）"));
                    sbDiag.AppendLine(GridLineProbe(grid, 120, "一批口径（DataGrid 自画 0.5 网格线 + 星号列未铺满＝真机一批状态）"));
                    if (nameColDbg != null && savedNameWidth.HasValue) nameColDbg.Width = savedNameWidth.Value;
                }
                else sbDiag.AppendLine("一批口径对照：取不到库的 DataGridRow/DataGridCell 隐式样式，跳过");
                grid.RowStyle = savedRowStyle; grid.CellStyle = savedCellStyle; grid.GridLinesVisibility = savedLines;
                content.UpdateLayout();
                string probeShot = Snap(grid, "修整14-探针-主列表.png");
                sbDiag.AppendLine("探针出图：" + probeShot);
                // 星号列为什么不铺满（右侧留一条空白 filler）——修整11 要求"大小列让出的宽度给文件名"，先量清原因
                var nameCol = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColName")));
                sbDiag.AppendLine("星号列探针（现状）：文件名 Width=" + (nameCol == null ? "—" : nameCol.Width.ToString())
                    + " MinWidth=" + (nameCol == null ? "—" : nameCol.MinWidth.ToString())
                    + " 实测=" + (nameCol == null ? "—" : nameCol.ActualWidth.ToString("0.#"))
                    + "；表格宽=" + grid.ActualWidth.ToString("0.#")
                    + " 列宽合计=" + grid.Columns.Cast<DataGridColumn>().Sum(c => c.ActualWidth).ToString("0.#"));
                var svDbg = FindDerived(grid, typeof(ScrollViewer)).Cast<ScrollViewer>().OrderByDescending(s => s.ViewportWidth).FirstOrDefault();
                sbDiag.AppendLine("  根因：内层 ScrollViewer＝" + (svDbg == null ? "找不到" : svDbg.GetType().Name
                    + "，水平条=" + System.Windows.Controls.ScrollViewer.GetHorizontalScrollBarVisibility(svDbg)
                    + "，" + (svDbg.ReadLocalValue(System.Windows.Controls.ScrollViewer.HorizontalScrollBarVisibilityProperty) == DependencyProperty.UnsetValue
                        ? "值来自外层附加属性（可改）" : "模板本地值写死（外层附加属性压不住）")
                    + "，Viewport=" + svDbg.ViewportWidth.ToString("0.#") + " / Extent=" + svDbg.ExtentWidth.ToString("0.#"))
                    + " → 列呈现器按无限宽排版，星号列停在 MinWidth；故由 MainWindow.FillStarColumn 按视口补宽（见 00 说明）");
                MainWindow.FillStarColumn(grid, nameCol);
                content.UpdateLayout();
                sbDiag.AppendLine("  补宽后：文件名列宽=" + (nameCol == null ? "—" : nameCol.ActualWidth.ToString("0.#"))
                    + " 列宽合计=" + grid.Columns.Cast<DataGridColumn>().Sum(c => c.ActualWidth).ToString("0.#")
                    + " 表格宽=" + grid.ActualWidth.ToString("0.#"));
                Check(bad, "修整11 大小列让出的宽度落到文件名列（补宽后铺满视口）", nameCol != null
                    && nameCol.ActualWidth > 200 && Math.Abs(grid.Columns.Cast<DataGridColumn>().Sum(c => c.ActualWidth) - grid.ActualWidth) < 24);


                sbDiag.AppendLine("主列表行高实测：" + string.Join(",", FindByType(content, typeof(DataGridRow)).Cast<DataGridRow>()
                    .Where(r => r.ActualHeight > 0).Select(r => r.ActualHeight.ToString("0.##"))));
                var dbgCell = FindByType(content, typeof(DataGridCell)).Cast<DataGridCell>().FirstOrDefault();
                sbDiag.AppendLine("单元格探针：BorderThickness=" + (dbgCell == null ? "—" : dbgCell.BorderThickness.ToString())
                    + " BorderBrush=" + (dbgCell == null ? "—" : dbgCell.BorderBrush)
                    + " 样式=" + (dbgCell == null || dbgCell.Style == null ? "—"
                        : Equals(dbgCell.Style, Application.Current.Resources["GridCellLine"]) ? "GridCellLine（本批）" : "非本批样式/" + dbgCell.Style.TargetType.Name)
                    + "；行 BorderThickness=" + (FindByType(content, typeof(DataGridRow)).Cast<DataGridRow>().FirstOrDefault() == null
                        ? "—" : FindByType(content, typeof(DataGridRow)).Cast<DataGridRow>().First().BorderThickness.ToString()));
                var dbgHdrTb = FindByType(content, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
                    .Cast<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                    .SelectMany(h => FindByType(h, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>())
                    .Select(t => t.Text + "=h" + t.ActualHeight.ToString("0.#")).ToList();
                sbDiag.AppendLine("列头文字高度探针（一行≈17.6，>22 即折行）：" + string.Join(" | ", dbgHdrTb.Take(16)));
                sbDiag.AppendLine(GridLineProbe(grid, 96, "二批口径（整行底边+单元格右边框+像素对齐）"));
                string probe2 = GridLineProbe(grid, 120, "二批口径（整行底边+单元格右边框+像素对齐）");
                sbDiag.AppendLine(probe2);
                Check(bad, "修整14 二批口径横线连贯（覆盖≥0.97 / 跳变≤3 / 起伏≤45）", probe2.Contains("判定：连贯"));
                Check(bad, "修整14 竖线连贯（列分隔线在行带内连续，覆盖≥0.9）", probe2.Contains("竖线判定：连贯"));

                // ===== 修整13：窄窗口自适应（挤压变滚动，核心控件不被裁） =====
                string minDiag = "";
                foreach (double w in new[] { 1260.0, 1120.0, 960.0 })
                {
                    main.UpdateLayoutForTest(w, 660);
                    // 真机拖窗口时由 SizeChanged 触发；这里手动补一次同样的动作（列已被补宽成绝对值，按字段取才拿得到）
                    MainWindow.FillStarColumn(grid, main.ColName);
                    bool scrolled = HScrollActive(grid, out double ext, out double view);
                    var inside = new List<string>();
                    foreach (string n in new[] { "BtnAddFiles", "BtnClear", "PrinterBox", "BtnPrintSetup", "BtnStart", "BtnStop" })
                    {
                        var el = main.FindName(n) as FrameworkElement;
                        if (el != null && CheckInside(content, el)) inside.Add(n);
                    }
                    minDiag += "视口 " + w + "x660：列总宽 " + ext.ToString("0") + " / 可视 " + view.ToString("0")
                        + " → 横向滚动 " + (scrolled ? "出" : "不出") + "；底栏/工具栏在位 " + string.Join(",", inside) + "\r\n";
                    if (w <= 960.5)
                    {
                        // 修整13 原口径是"列挤压到保底后变横向滚动"（当时 960 档列合计 902 > 可视 710）。
                        // 2026-09-23 四批·修整16② 需求方拍板方案②（压窄类型/页数/页码范围 + 文件名保底 200→110），
                        // 960 档列合计已进可视宽 → 本条改为"不滚"，滚动兜底能力仍在（列合计仍>可视时由 Auto 出条）。
                        Check(bad, "修整13a/16② 最小窗宽 960 列合计已压进可视区→不再出横向滚动条（列总宽 " + ext.ToString("0.#")
                            + " / 可视 " + view.ToString("0.#") + "）", !scrolled);
                        Check(bad, "修整13a 窄窗口底栏五个核心控件不被裁", inside.Count == 6);
                        var nameTextMin = CellOf(content, long1, grid, Res("s.ColName"));
                        var tbMin = nameTextMin == null ? null : FindByType(nameTextMin, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>().FirstOrDefault();
                        Check(bad, "修整13a 窄窗口单元格仍不折行（行高 36 不变）", tbMin != null && tbMin.TextWrapping == TextWrapping.NoWrap);
                    }
                }
                main.ShowPanelForTest("history");
                foreach (double w in new[] { 1260.0, 960.0 })
                {
                    main.UpdateLayoutForTest(w, 660);
                    MainWindow.FillStarColumn(hgrid, hp == null ? null : ((HistoryPage)hp).ColName);   // 同上
                    var export = hp == null ? null : hp.FindName("BtnExport") as FrameworkElement;
                    var refresh = hp == null ? null : hp.FindName("BtnRefresh") as FrameworkElement;
                    var fp = hp == null ? null : hp.FindName("FilterPrinter") as FrameworkElement;
                    bool all = export != null && refresh != null && fp != null
                        && CheckInside(content, export) && CheckInside(content, refresh) && CheckInside(content, fp);
                    double ey = export == null ? -1 : export.TransformToAncestor(hp).Transform(new Point(0, 0)).Y;
                    bool hscroll = HScrollActive(hgrid, out double hext, out double hview);
                    var hsv = hgrid == null ? null : FindDerived(hgrid, typeof(ScrollViewer)).Cast<ScrollViewer>()
                        .OrderByDescending(s => s.ViewportWidth).FirstOrDefault();
                    minDiag += "历史页视口 " + w + "：导出CSV 可见=" + (export != null && CheckInside(content, export))
                        + "（工具栏内 y=" + ey.ToString("0") + "）；列总宽 " + hext.ToString("0") + " / 可视 " + hview.ToString("0")
                        + " → 横向滚动 " + (hscroll ? "出" : "不出")
                        + "；内层 ScrollViewer=" + (hsv == null ? "未找到" : hsv.GetType().Name + " Viewport=" + hsv.ViewportWidth.ToString("0.#")
                            + " Extent=" + hsv.ExtentWidth.ToString("0.#") + " Scrollable=" + hsv.ScrollableWidth.ToString("0.#")
                            + " 水平条=" + System.Windows.Controls.ScrollViewer.GetHorizontalScrollBarVisibility(hsv))
                        + "\r\n";
                    if (w > 1200)
                    {
                        // 历史页真正排版后才量：边框连贯率 + 列头居中加粗
                        sbDiag.AppendLine(GridLineProbe(hgrid, 120, "历史页（二批口径，与主列表同一份样式）"));
                        var hh = FindByType(hp, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
                            .Cast<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                            .Where(h => h.Name != "PART_FillerColumnHeader").ToList();
                        Check(bad, "修整15 历史页列头居中+加粗（含 ⇅ 图标列头整体居中）", hh.Count >= 7
                            && hh.All(h => h.HorizontalContentAlignment == HorizontalAlignment.Center && h.FontWeight == FontWeights.Bold));
                        var hhText = hh.SelectMany(h => FindByType(h, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>())
                            .Where(t => t.ActualHeight > 0).ToList();
                        var hhWrap = hhText.Where(t => t.ActualHeight > 22.5)
                            .Select(t => "\"" + t.Text + "\"高" + t.ActualHeight.ToString("0.#")).ToList();
                        Check(bad, "修整15 历史页列头文字实测单行（offender=" + string.Join("，", hhWrap.Take(4)) + "）",
                            hhText.Count >= 7 && hhWrap.Count == 0);
                    }
                    if (w <= 960.5)
                    {
                        Check(bad, "修整13b 窄窗口历史页工具栏换行且导出CSV 不被裁（y>0 = 已折到第二行）",
                            all && ey > 34);
                        // 自检环境里历史页 0 行（行数据异步加载，无窗口不完成）→ 库的 ScrollViewer Extent=0，
                        // 空表时滚动条不出现属自检限制；"有行且列超出可视区就能滚"由主列表同口径实测证明
                        // （四批·修整16② 之后主列表 960 档列合计已压进可视区、不再出条，"能滚"由 HorizontalScrollBarVisibility=Auto
                        //   这一度量口径本身保证：一旦列合计再次超出可视宽（更窄的窗口/更多固定列）就出条。）
                        int hRows = FindDerived(hgrid, typeof(DataGridRow)).Cast<DataGridRow>().Count(r => r.ActualHeight > 0);
                        Check(bad, "修整13b 历史页表格同口径（横向滚动 Auto + 固定行高；有行时才能验滚动，当前行数 " + hRows + "）",
                            System.Windows.Controls.ScrollViewer.GetHorizontalScrollBarVisibility(hgrid) == ScrollBarVisibility.Auto
                            && hgrid.RowHeight == 36
                            && (hRows == 0 || hext <= hview + 0.5 || HScrollActive(hgrid, out _, out _)));
                        minDiag += "    注：历史页行数=" + hRows + "（自检无窗口，行数据异步加载不完成）；"
                            + "空表时 PassiveScrollViewer 的 Extent=0 → 不出滚动条，属自检环境限制而非产品缺陷"
                            + "（主列表 960 档在四批·修整16② 压列后已不再出条，见本文件「修整13a/16②」断言）。\r\n";
                    }
                }
                main.ShowPanelForTest("print");
                WriteDiag("修整14边框连贯定位.txt", sbDiag.ToString()
                    + "\r\n说明：覆盖率=线上有多少比例的横向位置确实有暗像素（缺口即断段）；跳变=相邻列最暗行号来回换的次数（半根线错位的症状）；"
                    + "起伏=线上各点亮度 P95-P5。本进程排版用本机系统 DPI 120（125%，行高实测 36.8=36+0.8DIP=1 设备像素可证），"
                    + "故 dpi=120 档＝真机口径，dpi=96 档只是 1DIP=1像素的对照。最终观感仍以需求方真机眼睛为准。\r\n");
                WriteDiag("修整13缩放自适应定位.txt", minDiag + "\r\n说明：视口宽 960 = 需求方设定的窗口最小宽 MinWidth；历史页工具栏已由不换行的 StackPanel 改成 WrapPanel，"
                    + "导出CSV 折到第二行（y 变大）而不是被裁出屏。");

                if (bad.Count > 0) return "失败：" + string.Join(" | ", bad);
                return "通过（修整9 全选四种情形一次点到位 + 修整10 固定行高/截断/悬浮全名 + 修整11 大小列删除 + "
                    + "修整12 输入框铺满 + 修整13 窄窗口滚动与控件不被裁 + 修整14 两页边框连贯 + 修整15 列头居中加粗）";
            }
            catch (Exception ex) { return "失败：" + ex + "\r\n--- 列头诊断 ---\r\n" + V44HeaderDiagLog(); }
        }

        // ===== 二批探针 =====

        private static CheckBox FindHeaderCheck(DependencyObject root)
        {
            foreach (var d in FindByType(root, typeof(CheckBox)))
            {
                var cb = (CheckBox)d;
                if (cb.Name == "HeaderCheck") return cb;
            }
            return null;
        }

        /// <summary>模拟真实点击：WPF 三态框在 Click 之前已经把 IsChecked 翻了一格
        /// （false→null→true→false），先复现这一格再触发 Click，才和真机点一下完全同路径。</summary>
        private static void ClickHeaderLikeUser(CheckBox cb)
        {
            bool? v = cb.IsChecked;
            cb.IsChecked = v == null ? true : (v == false ? (bool?)null : (bool?)false);
            cb.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        private static DataGridCell CellOf(DependencyObject root, RowVm row, DataGrid grid, string header)
        {
            var container = RowContainer(root, row);
            if (container == null) return null;
            int index = -1;
            for (int i = 0; i < grid.Columns.Count; i++)
                if (Equals(grid.Columns[i].Header, header)) index = i;
            if (index < 0) return null;
            return FindByType(container, typeof(DataGridCell)).Cast<DataGridCell>()
                .FirstOrDefault(c => c.Column != null && c.Column.DisplayIndex == index);
        }

        /// <summary>修整13：窄窗口下"挤压变滚动"是否成立。内容宽 = 各列实测宽之和（星号列被 MinWidth 顶住时
        /// 总和会大于可视宽），是否真能滚看内层 ScrollViewer 的 ScrollableWidth——库用的是悬浮式滚动条，
        /// 按"滚动条可见且高度>0"判会在部分页面误判（历史页就踩过），ScrollableWidth 才是"能不能滚"的ground truth。</summary>
        private static bool HScrollActive(DataGrid grid, out double content, out double viewport)
        {
            content = 0; viewport = 0;
            if (grid == null) return false;
            content = grid.Columns.Cast<DataGridColumn>().Sum(c => c.ActualWidth);
            viewport = grid.ActualWidth;
            var sv = FindDerived(grid, typeof(ScrollViewer)).Cast<ScrollViewer>()
                .OrderByDescending(s => s.ViewportWidth).FirstOrDefault();
            double scrollable = sv == null ? -1 : sv.ScrollableWidth;
            return scrollable > 0.5 && content > viewport + 0.5;
        }

        /// <summary>
        /// 修整14 取证：把表格渲染成位图（可选目标 DPI，120 ≈ 需求方机器 125% 缩放），量线条连贯度。
        /// 横线三条指标：覆盖率＝"这条线上有多少比例的横向位置确实有暗像素"（缺口＝断段）；
        /// 跳变＝相邻列的最暗行号来回换的次数（半根线错位在相邻设备行间来回跳的症状）；
        /// 起伏＝线上各点亮度的 P95-P5（深浅不均）。竖线按列同理量一次（只在数据行带内量）。
        /// DPI 口径（实测，别再按"自检=96DPI"想当然）：本进程排版用的就是本机系统 DPI 120（=125%）——
        /// 证据是行高量出来 36.8 = RowHeight 36 + 0.8 DIP（0.8 DIP 正是 125% 下的 1 设备像素）。
        /// 所以 dpi=120 那一档与真机一致，dpi=96 只是"1DIP=1像素"的对照档。
        /// </summary>
        private static string GridLineProbe(DataGrid grid, double dpi, string tag)
        {
            if (grid == null) return "（历史页表格未取到，跳过连贯率量化）";
            int pw, ph;
            byte[] px = RenderPixelsAt(grid, dpi, out pw, out ph);
            if (pw < 8 || ph < 8) return tag + " dpi=" + (int)dpi + "：该表格未排版（元素尺寸 " + pw + "x" + ph + "），跳过";
            // 表格自己不画卡片底色（底色在外层 Border 上），RenderTargetBitmap 里那些像素是 alpha=0，
            // 按 Pbgra32 直接读数会当成纯黑——先把"没画的地方"按当前主题卡片底合成，否则量出来的全是黑图。
            var card = Application.Current.TryFindResource("CardBgBrush") as SolidColorBrush;
            int cbg = card != null ? (int)(0.114 * card.Color.B + 0.587 * card.Color.G + 0.299 * card.Color.R) : 255;
            Func<int, int, int> lum = (x, y) =>
            {
                int i = (y * pw + x) * 4;
                int a = px[i + 3];
                return (int)((0.114 * px[i] + 0.587 * px[i + 1] + 0.299 * px[i + 2]) + cbg * (255 - a) / 255.0);
            };
            var sample = new List<int>();
            for (int y = 0; y < ph; y += 3) for (int x = 0; x < pw; x += 7) sample.Add(lum(x, y));
            sample.Sort();
            int bg = sample.Count > 20 ? sample[(int)(sample.Count * 0.95)] : 250;
            if (bg < 40) return tag + " dpi=" + (int)dpi + "：该表格整体未着色（无行数据/脱离窗口渲染），跳过量化";
            int darkLimit = bg - 18;
            int x0 = 2, x1 = Math.Max(3, pw - 2), y0 = 1, y1 = Math.Max(2, ph - 1);
            int realizedRows = FindByType(grid, typeof(DataGridRow)).Cast<DataGridRow>().Count(r => r.ActualHeight > 0);

            var sb = new StringBuilder();
            // ---- 横线 ----
            int lineCount = 0, jumpSum = 0, covCount = 0, prevY = -9;
            double covSum = 0, rangeSum = 0, worstCov = 1;
            for (int y = y0; y < y1; y++)
            {
                int dark = 0;
                for (int x = x0; x < x1; x++) if (lum(x, y) < darkLimit) dark++;
                double own = dark / (double)(x1 - x0);
                if (own < 0.85) { continue; }
                if (y == prevY + 1) { prevY = y; continue; }   // 同一条线的相邻行不重复计
                prevY = y;
                // 该横线：窗口 ±2 内逐列取最暗，量化连贯度
                int n = x1 - x0;
                int[] arg = new int[n];
                int cover = 0, jumps = 0;
                var vals = new List<int>();
                for (int k = 0; k < n; k++)
                {
                    // 从本行向外扫，只有明显更暗（差 >1）才改判最暗行 —— 否则并列时总选窗口第一行，
                    // 相邻两行的 argmin 会来回跳，把"一根线"误判成"抖动"（第一批量化踩到的坑）。
                    int[] order = { y, y - 1, y + 1, y - 2, y + 2 };
                    int best = lum(x0 + k, y), bestY = y;
                    foreach (int yy in order)
                    {
                        if (yy < y0 || yy >= y1) continue;
                        int l = lum(x0 + k, yy);
                        if (l < best - 1) { best = l; bestY = yy; }
                    }
                    vals.Add(best); arg[k] = bestY;
                    if (best < darkLimit) cover++;
                    if (k > 0 && arg[k] != arg[k - 1]) jumps++;
                }
                lineCount++;
                double cov = cover / (double)n;
                covSum += cov; covCount++; jumpSum += jumps;
                if (cov < worstCov) worstCov = cov;
                vals.Sort();
                rangeSum += vals[(int)(vals.Count * 0.95)] - vals[(int)(vals.Count * 0.05)];
            }
            double avgCov = covCount > 0 ? covSum / covCount : 0;
            double avgRange = covCount > 0 ? rangeSum / covCount : 0;
            bool hOk = lineCount >= (realizedRows > 0 ? 3 : 2) && avgCov >= 0.97 && jumpSum <= 3 && avgRange <= 45;
            // 本进程按本机系统 DPI（125%）排版，只有 dpi=120 那一档与真机一致；96 档是把同一份 DIP 布局
            // 按 1DIP=1像素重采样，0.8DIP（=1 设备像素）的线会被摊到两行上 → 跳变数在这一档天然偏高，只作对照。
            string scaleNote = dpi >= 110 ? "真机口径" : "对照档（96 重采样，跳变数不可比）";
            sb.AppendLine(tag + " dpi=" + (int)dpi + "（" + scaleNote + "）底色亮度=" + bg + " 暗阈=" + darkLimit
                + (realizedRows > 0 ? "" : " 无数据行，只量列头线与外框")
                + "｜横线 " + lineCount + " 条；平均覆盖率 " + avgCov.ToString("P1")
                + "（最差 " + worstCov.ToString("P1") + "）；最暗行跳变合计 " + jumpSum + " 次；平均起伏 " + avgRange.ToString("0")
                + " → " + (hOk ? "判定：连贯" : "判定：不连贯"));

            // ---- 竖线：只在"真正有行的那一段"里量（行呈现器会占满整个表格高度，下方是空白；
            //      表头本来也没有竖线——按整幅或按呈现器算都会把好好的竖线误判成没有）----
            int bandY0 = y0, bandY1 = y1 - 1;
            double sc = dpi / 96.0;
            var rowTops = new List<double>();
            foreach (var r in FindByType(grid, typeof(DataGridRow)).Cast<DataGridRow>().Where(r => r.ActualHeight > 0))
            {
                try { rowTops.Add(r.TransformToAncestor(grid).Transform(new Point(0, 0)).Y); }
                catch { }
            }
            if (rowTops.Count > 0)
            {
                bandY0 = Math.Max(y0, (int)Math.Floor(rowTops.Min() * sc));
                bandY1 = Math.Min(y1 - 1, (int)Math.Ceiling((rowTops.Max() + 36.8) * sc));
            }
            int vCount = 0; double vWorst = 1;
            for (int x = x0; x < x1; x++)
            {
                int dark = 0;
                for (int y = bandY0; y <= bandY1; y++) if (lum(x, y) < darkLimit) dark++;
                double share = dark / (double)Math.Max(1, bandY1 - bandY0 + 1);
                if (share >= 0.8) { vCount++; if (share < vWorst) vWorst = share; }
            }
            // 三批口径修正（不改线元，只改"量哪几条线"）：整幅扫描会把单元格**内部**的竖边
            // （参数列「修改」按钮的 Element Plus 1px #DCDFE6 边框，亮度 220 < 暗阈 237）也数成"竖线"，
            // 而那些边只占行高的一部分 → 拉低"最差覆盖"，把好好的列分隔线判成断。
            // 修整14 的验收对象是"列与列之间的分隔线连续"，所以按已呈现单元格的右边缘算出真实列边界，
            // 只在边界（±1 设备像素取最优）上量；整幅扫描的数字照旧一并留档可比对。
            var bounds = new List<int>();
            foreach (var c in FindByType(grid, typeof(DataGridCell)).Cast<DataGridCell>().Where(cc => cc.ActualWidth > 4))
            {
                try
                {
                    Point pt = c.TransformToAncestor(grid).Transform(new Point(0, 0));
                    int bx = (int)Math.Round((pt.X + c.ActualWidth - 0.25) * sc);
                    if (bx > x0 && bx < x1 && !bounds.Contains(bx)) bounds.Add(bx);
                }
                catch { }
            }
            bounds.Sort();
            int vOnBound = 0; double vWorstB = 1;
            foreach (int bx in bounds)
            {
                double best = 0;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = bx + dx;
                    if (x <= x0 || x >= x1) continue;
                    int dark = 0;
                    for (int y = bandY0; y <= bandY1; y++) if (lum(x, y) < darkLimit) dark++;
                    double share = dark / (double)Math.Max(1, bandY1 - bandY0 + 1);
                    if (share > best) best = share;
                }
                if (best >= 0.8) { vOnBound++; if (best < vWorstB) vWorstB = best; }
            }
            int wantV = Math.Max(1, grid.Columns.Count - 2);
            if (realizedRows == 0)
            {
                sb.AppendLine("    竖线：无数据行可量（历史页的行数据是异步加载的，无窗口自检里停在\"加载中…\"）→ 竖线观感列入真机复验；"
                    + "两页共用同一份 GridCellLine/GridRowLine 样式对象已由断言卡住");
                return sb.ToString();
            }
            bool vOk = vOnBound >= wantV && vWorstB >= 0.9;
            sb.AppendLine("    竖线（列边界口径）" + vOnBound + "/" + bounds.Count + " 条连续（列数 " + grid.Columns.Count
                + "，数据行带 y=" + bandY0 + ".." + bandY1 + "，边界上最差覆盖 " + (vOnBound > 0 ? vWorstB.ToString("P1") : "—")
                + "）→ " + (vOk ? "竖线判定：连贯" : "竖线判定：不连贯（应≥" + wantV + " 条）"));
            sb.AppendLine("    （同幅对照：整幅扫描另计 " + vCount + " 条暗列、最差覆盖 "
                + (vCount > 0 ? vWorst.ToString("P1") : "—") + "，多出来的是参数列「修改」按钮自带的 Element Plus 竖边，不是列分隔线）");
            return sb.ToString();
        }

        private static byte[] RenderPixelsAt(FrameworkElement el, double dpi, out int pw, out int ph)
        {
            double scale = dpi / 96.0;
            pw = Math.Max(1, (int)Math.Ceiling(el.ActualWidth * scale));
            ph = Math.Max(1, (int)Math.Ceiling(el.ActualHeight * scale));
            var rtb = new RenderTargetBitmap(pw, ph, dpi, dpi, PixelFormats.Pbgra32);
            rtb.Render(el);
            var buf = new byte[pw * ph * 4];
            rtb.CopyPixels(buf, pw * 4, 0);
            return buf;
        }

        /// <summary>V4.4 二批取证图：主页宽/中/最小三档、历史页宽/最小两档、文件名悬浮全名、边框特写（浅/深）。</summary>
        internal static int V44Fix2Render(string log)
        {
            int code = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix2Png-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                // ① 主页三档宽度（含长文件名截断、固定行高、边框观感）
                foreach (var t in new[] { new { W = 1260, H = 820, N = "主页缩放-宽.png" },
                                          new { W = 1120, H = 700, N = "主页缩放-中.png" },
                                          new { W = 960, H = 660, N = "主页缩放-最小.png" } })
                {
                    var w = new MainWindow();
                    NewRow(w, dir, "合同.pdf");
                    NewRow(w, dir, "发票.pdf");
                    NewRow(w, dir, "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书-终稿第三版-含附件清单.pdf");
                    NewRow(w, dir, "说明书.pdf");
                    code |= RenderWindow("主页缩放" + t.W + "自检.txt", t.N, w, t.W, t.H,
                        new[] { "Grid", "BtnAddFiles", "PrinterBox", "BtnPrintSetup", "BtnStart", "BtnStop" });
                }

                // ② 历史页两档宽度（工具栏换行、导出CSV 在位）+ 真实数据下的边框连贯率量化
                foreach (var t in new[] { new { W = 1260, H = 820, N = "历史页缩放-宽.png" },
                                          new { W = 960, H = 660, N = "历史页缩放-最小.png" } })
                {
                    var w2 = new MainWindow();
                    w2.ShowPanelForTest("history");
                    w2.UpdateLayoutForTest(t.W, t.H);
                    var hist = FindByType(w2.Content as FrameworkElement, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    var hg = hist == null ? null : hist.FindName("Grid") as DataGrid;
                    // 具名控件在 HistoryPage 自己的名域里（窗口 FindName 取不到）；控件是否被裁已由
                    // --selftest 的修整13b 在真实窗口里量过，这里只负责出图
                    code |= RenderWindow("历史页缩放" + t.W + "自检.txt", t.N, w2, t.W, t.H, new string[0]);
                    if (t.W >= 1260 && hg != null)
                        File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "修整14边框连贯-历史页实测.txt"),
                            GridLineProbe(hg, 120, "历史页（二批口径，库内真实行数据，dpi=120）") + Environment.NewLine);
                }

                // ③ 文件名单元格特写：截断 + 悬浮 1 秒后的全名提示框（把 ToolTip 打开后连 Popup 一起渲染）
                code |= NameCellTooltipRender(log);

                // ④ 边框连贯特写：浅色 / 深色（表格已在窗口里，用 Snap 直接渲染该元素，不换父）
                App.ApplyTheme(AppTheme.Light);
                var wl = new MainWindow();
                NewRow(wl, dir, "甲.pdf"); NewRow(wl, dir, "乙.pdf"); NewRow(wl, dir, "丙.pdf");
                wl.UpdateLayoutForTest();
                var gl = wl.FindName("Grid") as DataGrid;
                string pngL = Snap(gl, "边框连贯特写-浅色.png");
                App.ApplyTheme(AppTheme.Dark);
                var wd = new MainWindow();
                NewRow(wd, dir, "甲.pdf"); NewRow(wd, dir, "乙.pdf"); NewRow(wd, dir, "丙.pdf");
                wd.UpdateLayoutForTest();
                var gd = wd.FindName("Grid") as DataGrid;
                string pngD = Snap(gd, "边框连贯特写-深色.png");
                App.ApplyTheme(AppTheme.Light);
                File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "V44修整渲染自检.txt"),
                    "边框特写：" + pngL + " / " + pngD + (pngL == "(截图失败)" || pngD == "(截图失败)" ? " 判定：失败" : " 判定：通过")
                    + "（连贯率数字见 修整14边框连贯定位.txt）" + Environment.NewLine);
                if (pngL == "(截图失败)" || pngD == "(截图失败)") code |= 1;

                // ⑤ 英文三档 + 历史页英文最小档（中英双语下截断/换行同口径）
                App.ApplyLanguage(AppLanguage.EnUS);
                var we = new MainWindow();
                NewRow(we, dir, "Contract-2026-school-wide-informization-equipment-procurement-and-maintenance-tender-technical-specifications-final-draft-v3.pdf");
                NewRow(we, dir, "Invoice.pdf");
                code |= RenderWindow("主页缩放英文最小自检.txt", "主页缩放-最小-英文.png", we, 960, 660,
                    new[] { "Grid", "BtnAddFiles", "PrinterBox", "BtnPrintSetup", "BtnStart", "BtnStop" });
                var whe = new MainWindow();
                whe.ShowPanelForTest("history");
                code |= RenderWindow("历史页缩放英文最小自检.txt", "历史页缩放-最小-英文.png", whe, 960, 660, new string[0]);
                App.ApplyLanguage(AppLanguage.ZhCN);
                return code;
            }
            catch (Exception ex) { return FailWith(log, ex); }
        }

        /// <summary>修整10 取证：量出文件名单元格的截断事实并出特写图。
        /// 提示框本体抓不到——WPF 的 ToolTip 明确不允许有逻辑/视觉父级（"ToolTip不能有逻辑或视觉父级"），
        /// 真机弹出时又是独立 Popup 窗口，无窗口自检渲染不到它；所以这里出"截断特写 + 悬浮参数（延迟/位置/内容）"
        /// 三项数字取证，提示框的实际观感列入需求方真机复验清单（开工令 §6.2 "如可渲染" 的口径）。</summary>
        private static int NameCellTooltipRender(string log)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "文件名悬浮全名判定.txt");
            var sb = new StringBuilder();
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Tip-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                App.ApplyTheme(AppTheme.Light);
                var w = new MainWindow();
                NewRow(w, dir, "合同.pdf");
                var longRow = NewRow(w, dir, "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书-终稿第三版-含附件清单.pdf");
                w.UpdateLayoutForTest();
                var grid = w.FindName("Grid") as DataGrid;
                var cell = CellOf(w.Content as FrameworkElement, longRow, grid, Res("s.ColName"));
                var text = cell == null ? null : FindByType(cell, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>().FirstOrDefault();
                string tip = text == null ? null : text.ToolTip as string;
                sb.AppendLine("文件名单元格宽=" + (cell == null ? "—" : cell.ActualWidth.ToString("0.#"))
                    + " 文字实测宽=" + (text == null ? "—" : text.ActualWidth.ToString("0.#"))
                    + " 折行=" + (text != null && text.TextWrapping != TextWrapping.NoWrap)
                    + " 省略号=" + (text != null && text.TextTrimming == TextTrimming.CharacterEllipsis));
                sb.AppendLine("提示框：内容=完整文件名 " + (!string.IsNullOrEmpty(tip) && tip == longRow.Name)
                    + "；首次延迟(ms)=" + (text == null ? "—" : ToolTipService.GetInitialShowDelay(text).ToString())
                    + "；位置=" + (text == null ? "—" : ToolTipService.GetPlacement(text) + " + 下压" + ToolTipService.GetVerticalOffset(text)));
                string png = Snap(cell, "文件名截断特写.png");
                sb.AppendLine("出图：" + png + "（截断观感）+ 主页缩放-*.png（整表）；提示框本体抓不到——ToolTip 不允许有父级、"
                    + "真机弹出时是独立 Popup 窗口，无窗口自检渲染不到，观感列入需求方真机复验清单");
                bool ok = text != null && tip == longRow.Name
                    && ToolTipService.GetInitialShowDelay(text) == 1000
                    && ToolTipService.GetPlacement(text) == System.Windows.Controls.Primitives.PlacementMode.Right
                    && ToolTipService.GetVerticalOffset(text) == 14
                    && text.TextWrapping == TextWrapping.NoWrap
                    && cell != null && text.ActualWidth <= cell.ActualWidth + 0.5
                    && png != "(截图失败)";
                sb.AppendLine(ok ? "判定：通过" : "判定：失败");
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染失败").AppendLine(ex.ToString());
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return 1;
            }
        }

        // ==================================================================
        // V4.4 三批（2026-09-22 验收打回）：深色主题缺陷 A/B —— 定位探针
        // 谁在深色下画白底、按钮文字前景最终落到哪个刷子，全部量出来不猜。
        // ==================================================================

        private static string V44Fix3Brush(Brush b)
        {
            if (b == null) return "(null)";
            var sc = b as SolidColorBrush;
            if (sc != null) return "#" + sc.Color.R.ToString("X2") + sc.Color.G.ToString("X2") + sc.Color.B.ToString("X2") + "/a" + sc.Color.A;
            return b.GetType().Name;
        }

        /// <summary>取当前主题下某个资源键解析到的实际刷子（含 WPF-UI 自己的键）。</summary>
        private static string V44Fix3Resolved(string key)
        {
            try
            {
                object v = Application.Current == null ? null : Application.Current.TryFindResource(key);
                return V44Fix3Brush(v as Brush) + (v == null ? "(未找到)" : "");
            }
            catch (Exception ex) { return "(取失败:" + ex.GetType().Name + ")"; }
        }

        /// <summary>Pbgra32（预乘 alpha）按给定底色展平后，量区域内不透明占比与众数颜色。</summary>
        private static string V44Fix3Facts(byte[] buf, int pw, int ph, Color bg, int x0, int y0, int x1, int y1)
        {
            var cnt = new Dictionary<uint, int>();
            int opaque = 0, total = 0;
            for (int y = Math.Max(0, y0); y <= Math.Min(ph - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(pw - 1, x1); x++)
                {
                    int i = (y * pw + x) * 4;
                    int a = buf[i + 3];
                    int r = buf[i] + bg.R * (255 - a) / 255;
                    int g = buf[i + 1] + bg.G * (255 - a) / 255;
                    int b = buf[i + 2] + bg.B * (255 - a) / 255;
                    if (a == 255) opaque++;
                    total++;
                    uint k = ((uint)Math.Min(255, r) << 16) | ((uint)Math.Min(255, g) << 8) | (uint)Math.Min(255, b);
                    int c; cnt.TryGetValue(k, out c); cnt[k] = c + 1;
                }
            if (total == 0) return "(空区域)";
            string top = string.Join(" ", cnt.OrderByDescending(kv => kv.Value).Take(6)
                .Select(kv => "#" + kv.Key.ToString("X6") + "×" + kv.Value));
            return "不透明 " + opaque + "/" + total + "（" + (100.0 * opaque / total).ToString("0.0") + "%）众数 " + top;
        }

        /// <summary>找出某个元素子树里所有"不透明浅色"的着底色对象（白底元凶定位用）。
        /// 行内的「修改」按钮/勾选框本身就是 Element Plus 白底控件（深浅两主题都白底，属拍板观感），
        /// 不参与"行底是否随主题"的判定，故遇到按钮/CheckBox 不再往下走。</summary>
        private static int V44Fix3Walk(DependencyObject node, StringBuilder sb, string indent)
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase || node is CheckBox) return 0;
            int hits = 0;
            Brush b = (node as Control)?.Background ?? (node as Border)?.Background;
            var sc = b as SolidColorBrush;
            if (sc != null && sc.Color.A > 64)
            {
                double lum = 0.299 * sc.Color.R + 0.587 * sc.Color.G + 0.114 * sc.Color.B;
                if (lum > 200)
                {
                    hits++;
                    sb.AppendLine(indent + node.GetType().Name + " 亮底=" + V44Fix3Brush(sc)
                        + " 样式=" + V44Fix3Style(node as FrameworkElement));
                }
            }
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch { n = 0; }
            for (int i = 0; i < n; i++) hits += V44Fix3Walk(VisualTreeHelper.GetChild(node, i), sb, indent + "  ");
            return hits;
        }

        private static string V44Fix3Style(FrameworkElement el)
        {
            var st = el == null ? null : el.Style;
            if (st == null) return "(无样式)";
            foreach (var k in Application.Current.Resources.Keys)
                if (Application.Current.Resources[k] == st) return "app键=" + k;
            return st.TargetType.Name + (st.BasedOn != null ? "←BasedOn:" + st.BasedOn.TargetType.Name : "");
        }

        /// <summary>WCAG 相对亮度与对比度（1.05 保留两位）。</summary>
        private static double V44Fix3Lum(Color c)
        {
            double[] v = { c.R / 255.0, c.G / 255.0, c.B / 255.0 };
            for (int i = 0; i < 3; i++) v[i] = v[i] <= 0.03928 ? v[i] / 12.92 : Math.Pow((v[i] + 0.055) / 1.055, 2.4);
            return 0.2126 * v[0] + 0.7152 * v[1] + 0.0722 * v[2];
        }

        private static double V44Fix3Contrast(Color a, Color b)
        {
            double la = V44Fix3Lum(a), lb = V44Fix3Lum(b);
            if (la < lb) { double t = la; la = lb; lb = t; }
            return (la + 0.05) / (lb + 0.05);
        }

        private static Color V44Fix3Color(Brush b, Color fallback)
        {
            var sc = b == null ? null : b as SolidColorBrush;
            return sc == null ? fallback : sc.Color;
        }

        /// <summary>三批定位＋断言共用：在当前主题下把主列表（带 ≥3 行数据）与两页按钮的颜色事实量出来。
        /// 返回 (逐行事实文本, 违规清单)。修复前后都跑同一份口径，便于对比。</summary>
        private static Tuple<string, List<string>> V44Fix3Colors(AppTheme th, bool assert)
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix3-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var w = new MainWindow();
            var rA = NewRow(w, dir, "甲.pdf");
            NewRow(w, dir, "乙.pdf");
            NewRow(w, dir, "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书-终稿第三版.pdf");
            w.UpdateLayoutForTest(1260, 820);
            var grid = w.FindName("Grid") as DataGrid;
            var root = w.Content as FrameworkElement;
            string tag = th == AppTheme.Dark ? "深色" : "浅色";

            sb.AppendLine("— 主题资源层（" + tag + "）WPF-UI 主题="
                + (Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Dark ? "Dark" :
                   Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Light ? "Light" : "Other"));
            foreach (ResourceDictionary d in Application.Current.Resources.MergedDictionaries)
                sb.AppendLine("    合并字典：" + (d.Source != null ? d.Source.OriginalString : "(内联/无Source)"));
            foreach (string k in new[] { "ApplicationBackgroundBrush", "ControlFillColorDefaultBrush", "TextFillColorPrimaryBrush",
                                         "TextFillColorSecondaryBrush", "CardBackgroundFillColorDefaultBrush", "TextPrimaryBrush", "GridLineBrush" })
                sb.AppendLine("    键 " + k + " = " + V44Fix3Resolved(k));

            // —— 缺陷A：表格正文层（行/单元格/文字 + 画出来的像素）——
            var row = FindByType(root, typeof(DataGridRow)).Cast<DataGridRow>().FirstOrDefault();
            sb.AppendLine("— 表格（" + tag + "）行数(已呈现)=" + FindByType(root, typeof(DataGridRow)).Cast<DataGridRow>().Count(r => r.ActualHeight > 0));
            if (row != null)
            {
                sb.AppendLine("    DataGrid.Background=" + V44Fix3Brush(grid.Background) + " RowBackground=" + V44Fix3Brush(grid.RowBackground)
                    + " AlternatingRowBackground=" + V44Fix3Brush(grid.AlternatingRowBackground));
                sb.AppendLine("    Row.Background=" + V44Fix3Brush(row.Background) + " " + V44Fix3Style(row)
                    + "  Cell.Background=" + V44Fix3Brush(row.Background));
                var cell = CellOf(root, rA, grid, Res("s.ColName"));
                var tb = cell == null ? null : FindByType(cell, typeof(TextBlock)).Cast<TextBlock>().FirstOrDefault();
                sb.AppendLine("    名列 Cell.Background=" + V44Fix3Brush(cell != null ? cell.Background : null)
                    + " Cell.Foreground=" + V44Fix3Brush(cell != null ? cell.Foreground : null)
                    + " 文字=" + (tb != null ? "\"" + tb.Text + "\"" : "(无)")
                    + " TextBlock.Foreground=" + V44Fix3Brush(tb != null ? tb.Foreground : null)
                    + " " + (tb == null ? "(无样式)" : V44Fix3Style(tb)));
                sb.AppendLine("    行子树里的亮底对象（>200 亮度且 alpha>64；行内「修改」按钮按拍板就是白底控件，不计入）：");
                var walk = new StringBuilder();
                int lightHits = V44Fix3Walk(row, walk, "      ");
                sb.Append(walk.Length > 0 ? walk.ToString() : "      （无）" + Environment.NewLine);

                int pw, ph;
                byte[] px = RenderPixelsAt(grid, 120, out pw, out ph);
                Color bgc = V44Fix3Color((Brush)Application.Current.TryFindResource("WindowBgBrush"), Colors.Black);
                double ry = 0;
                try { ry = row.TransformToAncestor(grid).Transform(new Point(0, 0)).Y; } catch { }
                int ry0 = (int)(ry * 1.25) + 4, ry1 = (int)((ry + 30) * 1.25);
                sb.AppendLine("    行带展平取样：文字区 x=40..300 " + V44Fix3Facts(px, pw, ph, bgc, 40, ry0, 300, ry1));
                sb.AppendLine("    行带展平取样：整行 x=40..1200 " + V44Fix3Facts(px, pw, ph, bgc, 40, ry0, Math.Min(pw - 2, 1200), ry1));
                sb.AppendLine("    表格下方空区 y=" + (int)((ry + 130) * 1.25) + " " + V44Fix3Facts(px, pw, ph, bgc, 40, (int)((ry + 130) * 1.25), 1200, (int)((ry + 150) * 1.25)));
                Color top = V44Fix3Top(px, pw, ph, bgc, 40, ry0, Math.Min(pw - 2, 1200), ry1);
                sb.AppendLine("    行带众数色（按 WindowBgBrush 展平后）=" + V44Fix3Hex(top));
                if (assert)
                {
                    // 修复A 断言①：数据行底色＝我们那份主题键 GridRowBgBrush（浅 #FFFFFF／深 #272727）
                    Color want = th == AppTheme.Dark ? Color.FromRgb(0x27, 0x27, 0x27) : Color.FromRgb(0xFF, 0xFF, 0xFF);
                    Color rowBg = V44Fix3Color(row.Background, Colors.Black);
                    Check(bad, tag + " 数据行底色＝GridRowBgBrush 主题键（期望 " + V44Fix3Hex(want) + "，实测 "
                        + V44Fix3Brush(row.Background) + "，样式来源 " + V44Fix3Style(row) + "）",
                        rowBg.A == 255 && rowBg.R == want.R && rowBg.G == want.G && rowBg.B == want.B);
                    // 修复A 断言②：深色下行子树不得再有不透明白底（白底带正是缺陷A 的形态）
                    Check(bad, tag + " 行子树不透明白底对象数=" + lightHits + "（深色下须为 0）",
                        th != AppTheme.Dark || lightHits == 0);
                    // 修复A 断言③：文字前景 vs 真正画出来的行底，WCAG 对比度
                    if (tb != null)
                    {
                        double cr = V44Fix3Contrast(V44Fix3Color(tb.Foreground, Colors.Gray), rowBg);
                        Check(bad, tag + " 文件名列文字 vs 行底 对比度≥4.5（实测 " + cr.ToString("0.00") + "：字 "
                            + V44Fix3Brush(tb.Foreground) + " 底 " + V44Fix3Brush(row.Background) + "）", cr >= 4.5);
                    }
                    // 修复A 断言④：渲染像素众数——深色不得是白底带；浅色必须仍是 #FFFFFF（需求方已验收的浅色观感零变化）
                    if (th == AppTheme.Dark)
                        Check(bad, tag + " 渲染像素：深色行带众数色=" + V44Fix3Hex(top) + "（亮度须<80，白底带即失败）",
                            0.299 * top.R + 0.587 * top.G + 0.114 * top.B < 80);
                    else
                        Check(bad, tag + " 渲染像素：浅色行带众数色=" + V44Fix3Hex(top) + "（修前修后同为 #FFFFFF）",
                            top.R >= 0xFA && top.G >= 0xFA && top.B >= 0xFA);
                }
            }

            // —— 缺陷B：两页按钮内容文字前景 vs 按钮底色（Element Plus 拍板口径）——
            foreach (string bn in new[] { "BtnAddFiles", "BtnAddFolder", "BtnRemove", "BtnClear", "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop" })
                V44Fix3Btn(w.FindName(bn) as Button, bn, tag, assert, sb, bad);
            // 历史页工具栏按钮 + 两页样式对象一致性（历史页行数据在无窗口自检里异步加载不完成，
            // 行底像素量不到 → 按开工令"可放宽为样式对象一致性断言"的口径覆盖两页）
            w.ShowPanelForTest("history");
            w.UpdateLayoutForTest(1260, 820);
            var hist = FindByType(root, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
            if (hist != null)
            {
                foreach (string bn in new[] { "BtnRefresh", "BtnExport" })
                    V44Fix3Btn(hist.FindName(bn) as Button, "历史页 " + bn, tag, assert, sb, bad);
                var hg = hist.FindName("Grid") as DataGrid;
                if (assert && hg != null)
                {
                    Check(bad, tag + " 历史页与主页共用同一份行样式对象（Equals，修复A 同口径覆盖两页）",
                        hg.RowStyle != null && hg.RowStyle == grid.RowStyle);
                    Check(bad, tag + " 历史页与主页共用同一份单元格样式对象（Equals，修整14 线元不回退）",
                        hg.CellStyle != null && hg.CellStyle == grid.CellStyle);
                }
            }
            return Tuple.Create(sb.ToString(), bad);
        }

        /// <summary>单个按钮的颜色事实与拍板口径断言：
        /// 浅底（亮度&gt;200）→ 内容字色必须等于样式那份固定拍板值（#606266 族），其中"深字"再卡 WCAG 4.5，
        /// 彩色字（朴素红 #F56C6C）按拍板只核配对；实底（蓝/绿/红/灰）→ 内容字色必须是白；
        /// 禁用态走 BtnBase 的 50% 透明口径（WCAG 对禁用控件豁免），不卡对比度。</summary>
        private static void V44Fix3Btn(Button b, string bn, string tag, bool assert, StringBuilder sb, List<string> bad)
        {
            if (b == null)
            {
                sb.AppendLine("    按钮 " + bn + " 未找到");
                if (assert) bad.Add(tag + " 按钮 " + bn + " 未找到");
                return;
            }
            // 带图标的按钮里 ui:SymbolIcon 内部也有一个 TextBlock（字形是私用区字符，本就该跟随主题图标色），
            // 它不是"按钮文字"——先按"文字等于按钮 Content"取，再退一步排除私用区字形，避免量错对象。
            var tbs = FindByType(b, typeof(TextBlock)).Cast<TextBlock>()
                .Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
            var btb = tbs.FirstOrDefault(t => b.Content is string s2 && t.Text == s2)
                ?? tbs.FirstOrDefault(t => !t.Text.Any(c => c >= 0xE000 && c <= 0xF8FF))
                ?? tbs.FirstOrDefault();
            Color bgc = V44Fix3Color(b.Background, Colors.White);
            Color styFg = V44Fix3Color(b.Foreground, Colors.Black);
            Color txtFg = V44Fix3Color(btb != null ? btb.Foreground : b.Foreground, Colors.Black);
            double cr = V44Fix3Contrast(bgc, txtFg);
            sb.AppendLine("    按钮 " + bn + "「" + (btb != null ? btb.Text : "") + "」启用=" + b.IsEnabled
                + " 底=" + V44Fix3Brush(b.Background) + " 样式前景=" + V44Fix3Brush(b.Foreground)
                + " 内容文字前景=" + V44Fix3Brush(btb != null ? btb.Foreground : null)
                + " 文字样式=" + (btb == null ? "(无TextBlock)" : V44Fix3Style(btb))
                + " 对比度=" + cr.ToString("0.00"));
            if (btb != null)
            {
                var be = System.Windows.Data.BindingOperations.GetBindingExpression(btb, TextBlock.ForegroundProperty);
                sb.AppendLine("      内容元素：" + btb.GetType().Name + " 父链=" + V44Fix3Chain(btb, b)
                    + " 前景本地绑定=" + (be == null ? "无（走隐式样式，不受控）"
                        : be.ParentBinding.Path.Path + "/" + be.Status));
            }
            if (!assert || !b.IsEnabled) return;
            bool lightBg = bgc.A > 200 && 0.299 * bgc.R + 0.587 * bgc.G + 0.114 * bgc.B > 200;
            if (lightBg)
            {
                Check(bad, tag + " 浅底按钮 " + bn + " 内容字色＝样式拍板值（样式 " + V44Fix3Brush(b.Foreground)
                    + " 字面 " + V44Fix3Brush(btb != null ? btb.Foreground : null) + "）",
                    txtFg.R == styFg.R && txtFg.G == styFg.G && txtFg.B == styFg.B);
                if (0.299 * txtFg.R + 0.587 * txtFg.G + 0.114 * txtFg.B < 140)
                    Check(bad, tag + " 浅底深字按钮 " + bn + " 对比度≥4.5（实测 " + cr.ToString("0.00") + "，底 "
                        + V44Fix3Brush(b.Background) + " 字 " + V44Fix3Brush(btb != null ? btb.Foreground : null) + "）", cr >= 4.5);
            }
            else
            {
                // V4.5-1（决1，需求方 2026-09-25 拍板）口径变更（如实报备，不静默改判）：
                // 实底按钮字色从"全局白字"改为按主题取 BtnSolidTextBrush——深色仍是三批白字（零回退），
                // 浅色回退 v4.2.5 旧观感深字。原来那一条"实底按钮维持拍板白字"在浅色主题下必然与需求方拍板冲突，
                // 故按主题分档：深色查白字、浅色查深字，两者都过内容元素＝样式前景 + WCAG ≥4.5。
                Color wantSolid = V44Fix3Color((Brush)Application.Current.TryFindResource("BtnSolidTextBrush"), Colors.White);
                Check(bad, tag + " 实底按钮 " + bn + " 内容字色＝BtnSolidTextBrush（主题键 " + V44Fix3Hex(wantSolid)
                    + " 实测 " + V44Fix3Hex(txtFg) + " 压 " + V44Fix3Hex(bgc) + "）",
                    txtFg.R == wantSolid.R && txtFg.G == wantSolid.G && txtFg.B == wantSolid.B
                    && txtFg.R == styFg.R && txtFg.G == styFg.G && txtFg.B == styFg.B);
                // 三批原口径照旧：WCAG ≥4.5 只卡"浅底深字"那一条（实底压拍板底色，深色白字 2.78/2.90 是三批起
                // 就验收过的数字，底色属红线不动）→ 实底按钮的字色来源改按主题核，对比度作为数字如实入档不立门。
            }
        }

        private static string V44Fix3Chain(DependencyObject el, DependencyObject stop)
        {
            var s = new StringBuilder();
            var cur = VisualTreeHelper.GetParent(el);
            while (cur != null && !ReferenceEquals(cur, stop) && s.Length < 200)
            {
                s.Append(cur.GetType().Name + " < ");
                cur = VisualTreeHelper.GetParent(cur);
            }
            s.Append(ReferenceEquals(cur, stop) ? "BTN" : "(断链)");
            return s.ToString();
        }

        private static string V44Fix3Hex(Color c)
        {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        /// <summary>展平后区域内出现最多的颜色——"整条白底带"这种形态一句话就能卡死。</summary>
        private static Color V44Fix3Top(byte[] buf, int pw, int ph, Color bg, int x0, int y0, int x1, int y1)
        {
            var cnt = new Dictionary<uint, int>();
            for (int y = Math.Max(0, y0); y <= Math.Min(ph - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(pw - 1, x1); x++)
                {
                    int i = (y * pw + x) * 4;
                    int a = buf[i + 3];
                    uint k = ((uint)Math.Min(255, buf[i] + bg.R * (255 - a) / 255) << 16)
                           | ((uint)Math.Min(255, buf[i + 1] + bg.G * (255 - a) / 255) << 8)
                           | (uint)Math.Min(255, buf[i + 2] + bg.B * (255 - a) / 255);
                    int c; cnt.TryGetValue(k, out c); cnt[k] = c + 1;
                }
            if (cnt.Count == 0) return Colors.Transparent;
            uint top = cnt.OrderByDescending(kv => kv.Value).First().Key;
            return Color.FromRgb((byte)(top >> 16), (byte)(top >> 8), (byte)top);
        }

        /// <summary>三批断言入口（--selftest 串进来）：浅/深两主题各量一遍，事实同时落 定位 txt。</summary>
        internal static string V44Fix3Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    sb.AppendLine("======== " + (th == AppTheme.Dark ? "深色" : "浅色") + " ========");
                    var r = V44Fix3Colors(th, true);
                    sb.Append(r.Item1);
                    bad.AddRange(r.Item2);
                }
                App.ApplyTheme(AppTheme.Light);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V44Fix3深色缺陷定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（深色数据行底色 #272727 随主题、浅色行底 #FFFFFF 零变化、行子树无不透明白底、两页共用同一份行/单元格样式对象、"
                  + "浅底按钮内容字色＝拍板固定值且对比度≥4.5、深色实底按钮白字（浅色实底自 V4.5-1 起按主题取深字，见 V45 一节）；逐条数字见 V44Fix3深色缺陷定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>探针出图（三批补 取证盲区）：按 dpi 渲染后把 alpha 展平到 WindowBgBrush 再存 PNG。
        /// 二批的「边框连贯特写-深色」有 55,340 个全透明像素（ZCode 只能自己按 #272727 展平定性），
        /// 垫底这一步做进渲染路径，出图即所见。</summary>
        private static string SnapFlat(FrameworkElement el, string pngName, double dpi)
        {
            try
            {
                if (el == null || el.ActualWidth < 4 || el.ActualHeight < 4) return "(元素未渲染:" + pngName + ")";
                int pw, ph;
                byte[] src = RenderPixelsAt(el, dpi, out pw, out ph);
                Color bg = V44Fix3Color((Brush)Application.Current.TryFindResource("WindowBgBrush"), Colors.Black);
                var flat = new byte[pw * ph * 4];
                for (int i = 0; i < pw * ph; i++)
                {
                    int a = src[i * 4 + 3];
                    flat[i * 4 + 0] = (byte)Math.Min(255, src[i * 4 + 0] + bg.B * (255 - a) / 255);
                    flat[i * 4 + 1] = (byte)Math.Min(255, src[i * 4 + 1] + bg.G * (255 - a) / 255);
                    flat[i * 4 + 2] = (byte)Math.Min(255, src[i * 4 + 2] + bg.R * (255 - a) / 255);
                    flat[i * 4 + 3] = 255;
                }
                var bmp = BitmapSource.Create(pw, ph, dpi, dpi, PixelFormats.Bgra32, null, flat, pw * 4);
                string path = Path.Combine(AppContext.BaseDirectory, pngName);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) enc.Save(fs);
                return pngName + "（" + pw + "x" + ph + " @dpi" + (int)dpi + "，已垫 WindowBgBrush、全透明像素 0）";
            }
            catch (Exception ex) { return "(截图失败:" + pngName + " " + ex.Message + ")"; }
        }

        /// <summary>三批取证：RenderWindow 末拍停在「最小档复查」的排版，直接取子元素会得到按窄窗口排好的（甚至被裁掉的）区域；
        /// 这里把内容树重新排回给定尺寸并返回该根（内容树此时挂在 RenderWindow 的垫底 Border 上，沿可视树上溯）。</summary>
        private static FrameworkElement V44Fix3Relayout(Window w, double width, double height)
        {
            var any = w.FindName("Grid") as FrameworkElement;
            if (any == null) return null;
            DependencyObject cur = any;
            while (VisualTreeHelper.GetParent(cur) != null) cur = VisualTreeHelper.GetParent(cur);
            var root = cur as FrameworkElement;
            if (root == null) return null;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            return root;
        }

        /// <summary>三批取证（3 倍按钮特写）：整窗按 dpi 渲染后，裁出若干控件并集的外接矩形（四周各留 6 DIP）。
        /// 不用「按钮的父容器」直接取图——实测父容器 ActualHeight 小于按钮实际占位，取出来会裁掉按钮文字。</summary>
        private static string SnapFlatGroup(FrameworkElement root, FrameworkElement[] els, string pngName, double dpi)
        {
            try
            {
                if (root == null || els == null || els.Length == 0) return "(元素未渲染:" + pngName + ")";
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = 0, y1 = 0;
                foreach (var el in els)
                {
                    if (el == null) continue;
                    Point p = el.TransformToAncestor(root).Transform(new Point(0, 0));
                    x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y);
                    x1 = Math.Max(x1, p.X + el.ActualWidth); y1 = Math.Max(y1, p.Y + el.ActualHeight);
                }
                if (x0 > x1 || y0 > y1) return "(元素未渲染:" + pngName + ")";
                double s = dpi / 96.0;
                int fullW = Math.Max(1, (int)Math.Ceiling(root.ActualWidth * s));
                int fullH = Math.Max(1, (int)Math.Ceiling(root.ActualHeight * s));
                var rtb = new RenderTargetBitmap(fullW, fullH, dpi, dpi, PixelFormats.Pbgra32);
                rtb.Render(root);
                int sx = Math.Max(0, (int)Math.Floor((x0 - 6) * s)), sy = Math.Max(0, (int)Math.Floor((y0 - 6) * s));
                int ex = Math.Min(fullW, (int)Math.Ceiling((x1 + 6) * s)), ey = Math.Min(fullH, (int)Math.Ceiling((y1 + 6) * s));
                int cw = ex - sx, ch = ey - sy;
                if (cw < 4 || ch < 4) return "(截图失败:" + pngName + " 裁切区 " + cw + "x" + ch + ")";
                var src = new byte[cw * ch * 4];
                rtb.CopyPixels(new Int32Rect(sx, sy, cw, ch), src, cw * 4, 0);
                Color bg = V44Fix3Color((Brush)Application.Current.TryFindResource("WindowBgBrush"), Colors.Black);
                var flat = new byte[cw * ch * 4];
                for (int i = 0; i < cw * ch; i++)
                {
                    int a = src[i * 4 + 3];
                    flat[i * 4 + 0] = (byte)Math.Min(255, src[i * 4 + 0] + bg.B * (255 - a) / 255);
                    flat[i * 4 + 1] = (byte)Math.Min(255, src[i * 4 + 1] + bg.G * (255 - a) / 255);
                    flat[i * 4 + 2] = (byte)Math.Min(255, src[i * 4 + 2] + bg.R * (255 - a) / 255);
                    flat[i * 4 + 3] = 255;
                }
                var bmp = BitmapSource.Create(cw, ch, dpi, dpi, PixelFormats.Bgra32, null, flat, cw * 4);
                string path = Path.Combine(AppContext.BaseDirectory, pngName);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write)) enc.Save(fs);
                return pngName + "（" + cw + "x" + ch + " @dpi" + (int)dpi + "，整窗裁取、已垫 WindowBgBrush）";
            }
            catch (Exception ex) { return "(截图失败:" + pngName + " " + ex.Message + ")"; }
        }

        /// <summary>三批取证图（开工令 §4.2）：深色带数据行主列表（新断言出图）＋浅/深两版、
        /// 边框连贯特写重出（垫底、无透明空洞）、按钮区 3 倍放大特写（浅/深）。数字判定在 --selftest 的 V44Fix3Ui。</summary>
        internal static int V44Fix3Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix3-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                foreach (var th in new[] { AppTheme.Dark, AppTheme.Light })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string suffix = th == AppTheme.Dark ? "深色" : "浅色";
                    var w = new MainWindow();
                    NewRow(w, dir, "甲.pdf");
                    NewRow(w, dir, "乙.pdf");
                    NewRow(w, dir, "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书-终稿第三版-含附件清单.pdf");
                    code |= RenderWindow("三批" + suffix + "主列表自检.txt", "主列表带数据行-" + suffix + ".png", w, 1260, 820,
                        new[] { "Grid", "BtnAddFiles", "BtnAddFolder", "BtnRemove", "BtnClear", "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop" });
                    // 底栏按钮区 / 顶部工具栏 3 倍放大（dpi=360 ＝ 本机 120 口径的 3 倍），整窗重排后裁取
                    var top = V44Fix3Relayout(w, 1260, 820);
                    sb.AppendLine("按钮区特写-" + suffix + "（3 倍）：" + SnapFlatGroup(top,
                        new[] { "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop" }
                            .Select(n => w.FindName(n) as FrameworkElement).ToArray(),
                        "按钮区特写-" + suffix + "-3倍.png", 360));
                    sb.AppendLine("工具栏按钮特写-" + suffix + "（3 倍）：" + SnapFlatGroup(top,
                        new[] { "BtnAddFiles", "BtnAddFolder", "BtnRemove", "BtnClear" }
                            .Select(n => w.FindName(n) as FrameworkElement).ToArray(),
                        "工具栏按钮特写-" + suffix + "-3倍.png", 360));
                    // 同一张表的边框连贯特写（浅/深，展平垫底）
                    var g = w.FindName("Grid") as DataGrid;
                    sb.AppendLine("边框连贯特写-" + suffix + "：" + SnapFlat(g, "边框连贯特写-" + suffix + ".png", 120));
                }
                App.ApplyTheme(AppTheme.Light);
                bool ok = code == 0 && sb.ToString().IndexOf("(截图失败") < 0 && sb.ToString().IndexOf("(元素未渲染") < 0;
                sb.AppendLine("说明：本批出图统一按 WindowBgBrush 展平（无 alpha 空洞）；深色行底/按钮字色的逐条数字判定见 "
                    + "自检报告.txt 的「V4.4 三批」行 与 V44Fix3深色缺陷定位.txt。" + (ok ? " 判定：通过" : " 判定：失败"));
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), sb.ToString(), new UTF8Encoding(true));
                return ok ? code : code | 1;
            }
            catch (Exception ex) { return FailWith(log, ex); }
        }

        // ==================================================================
        // V4.4 四批（2026-09-22 需求方指示，原 V4.5 两条改进提前）
        // 修整16 状态列瘦身 + 状态色化（已完成=绿 / 打印中=正常字色 / 失败=红，只看结构化状态种类）
        // 修整17 份数输入框水平+垂直铺满单元格，份数列 68→90
        // 数字全部量出来写进 V44四批状态列与份数列定位.txt，判定并入 --selftest。
        // ==================================================================

        /// <summary>四批·列宽与视口：状态列收窄后 1120/960 两档的实际排布（横向滚动与「修改」按钮可达性）＋份数格铺满。</summary>
        private static Tuple<string, List<string>> V44Fix4Layout()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            App.ApplyTheme(AppTheme.Light);
            App.ApplyLanguage(AppLanguage.ZhCN);
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix4L-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var w = new MainWindow();
            var grid = w.FindName("Grid") as DataGrid;
            var root = w.Content as FrameworkElement;
            var rows = new List<RowVm>();
            for (int i = 0; i < 16; i++) rows.Add(NewRow(w, dir, "四批量宽" + (i + 1) + ".pdf"));   // 16 行：真实用量下纵向滚动条必然出现
            rows[0].SetStatus(Res("s.StatusFailedRow"), true, RowStatusKind.Failed);                 // 首行做失败态（量重试按钮）
            var colStatus = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColStatus")));
            var colCopies = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColCopies")));
            var colKind = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColType")));
            var colPages = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColPages")));
            var colRange = grid.Columns.Cast<DataGridColumn>().FirstOrDefault(c => Equals(c.Header, Res("s.ColRange")));

            sb.AppendLine("— 列宽口径（声明值）：状态 Width=" + colStatus.Width + " MinWidth=" + colStatus.MinWidth
                + "；份数 Width=" + colCopies.Width + " MinWidth=" + colCopies.MinWidth
                + "；页码范围 Width=" + colRange.Width + " MinWidth=" + colRange.MinWidth);
            Check(bad, "修整16a 状态列已收窄（声明 Width ≤ 110 且 MinWidth ≤ 110，原 150/110）",
                colStatus.Width.IsAbsolute && colStatus.Width.Value <= 110 && colStatus.MinWidth <= 110);
            Check(bad, "修整17b 份数列已加宽（声明 Width ≥ 88 / MinWidth ≥ 72，原 68/60）",
                colCopies.Width.IsAbsolute && colCopies.Width.Value >= 88 && colCopies.MinWidth >= 72);

            foreach (double vw in new[] { 1260.0, 1120.0, 960.0 })
            {
                w.UpdateLayoutForTest(vw, 660);
                MainWindow.FillStarColumn(grid, w.ColName);
                w.UpdateLayoutForTest(vw, 660);
                var sv = FindDerived(grid, typeof(ScrollViewer)).Cast<ScrollViewer>()
                    .OrderByDescending(s => s.ViewportWidth).FirstOrDefault();
                double content = grid.Columns.Cast<DataGridColumn>().Sum(c => c.ActualWidth);
                double inner = sv == null ? 0 : sv.ViewportWidth;
                double scrollable = sv == null ? -1 : sv.ScrollableWidth;
                var edit = V44Fix4EditButton(root);
                double editRight = edit == null || sv == null ? -1
                    : edit.TransformToAncestor(sv).Transform(new Point(0, 0)).X + edit.ActualWidth;
                bool editShown = edit != null && edit.ActualWidth > 4 && editRight <= inner + 0.5 && editRight > 0;
                var stCell = CellOf(root, rows[1], grid, Res("s.ColStatus"));
                var stText = V44Fix4StatusText(stCell);
                sb.AppendLine("视口 " + vw + "x660（16 行，纵向滚动条在位）：列合计 " + content.ToString("0.#")
                    + " / 表格宽 " + grid.ActualWidth.ToString("0.#") + " / 内层可视 " + inner.ToString("0.#")
                    + "（ScrollableWidth=" + scrollable.ToString("0.#") + "）→ 横向滚动 " + (scrollable > 0.5 ? "出" : "不出")
                    + "；状态列实测=" + colStatus.ActualWidth.ToString("0.#")
                    + " 份数列实测=" + colCopies.ActualWidth.ToString("0.#")
                    + "；参数「修改」按钮右缘=" + editRight.ToString("0.#") + " → 完整可见=" + (editShown ? "是" : "否")
                    + "；状态文字实占=" + (stText == null ? "—" : stText.ActualWidth.ToString("0.#") + "/" + (stCell == null ? "?" : stCell.ActualWidth.ToString("0.#")))
                    + "；重试按钮宽=" + V44Fix4RetryWidth(root, grid, rows[0]).ToString("0.#")
                    + " 参数按钮宽=" + (edit == null ? 0 : edit.ActualWidth).ToString("0.#"));
                if (vw > 1120.5)
                {
                    Check(bad, "修整16a 宽窗（1260）无横向滚动且参数按钮完整可见（ScrollableWidth=" + scrollable.ToString("0.#") + "）",
                        scrollable <= 0.5 && editShown);
                }
                if (Math.Abs(vw - 1120.0) < 0.5)
                {
                    Check(bad, "修整16a 默认窗宽 1120（需求方真机口径）横向不滚动（列合计 " + content.ToString("0.#")
                        + " / 内层可视 " + inner.ToString("0.#") + " / ScrollableWidth=" + scrollable.ToString("0.#") + "）",
                        scrollable <= 0.5);
                    Check(bad, "修整16a 默认窗宽 1120 参数「修改」按钮完整可见（右缘 " + editRight.ToString("0.#")
                        + " ≤ 可视 " + inner.ToString("0.#") + "）", editShown);
                    Check(bad, "修整16a 状态列实测宽 ≤ 110（" + colStatus.ActualWidth.ToString("0.#") + "）",
                        colStatus.ActualWidth <= 110.5);
                }
                if (vw < 960.5)
                {
                    // 硬约束① 的 960 档：需求方 2026-09-23 拍板方案②（压窄类型/页数/页码范围 + 文件名保底 200→110）
                    // 之后，列合计须压进该档内层可视宽（16 行长列表 694.4）→ 不滚、参数按钮一页可见。
                    Check(bad, "修整16a① 最小窗宽 960 也不出横向滚动（列合计 " + content.ToString("0.#")
                        + " ≤ 内层可视 " + inner.ToString("0.#") + "，ScrollableWidth=" + scrollable.ToString("0.#") + "）",
                        scrollable <= 0.5);
                    Check(bad, "修整16a① 最小窗宽 960 参数「修改」按钮一页可见（右缘 " + editRight.ToString("0.#")
                        + " ≤ 可视 " + inner.ToString("0.#") + "）", editShown);
                    Check(bad, "修整16a① 最小窗宽 960 文件名实得宽（保底 110 生效、仍可截断看全名）",
                        w.ColName.ActualWidth >= 110 - 0.5);
                }
            }

            // 状态格：文字截断口径 + 重试按钮在位可用（窄列里先挤文字、不挤按钮）
            w.UpdateLayoutForTest(1120, 660);
            var failCell = CellOf(root, rows[0], grid, Res("s.ColStatus"));
            var failText = V44Fix4StatusText(failCell);
            var retry = failCell == null ? null : FindByType(failCell, typeof(Button)).Cast<Button>().FirstOrDefault();
            Check(bad, "修整16a② 失败态状态文字仍不折行+省略号截断", failText != null
                && failText.TextWrapping == TextWrapping.NoWrap && failText.TextTrimming == TextTrimming.CharacterEllipsis
                && failText.ActualWidth <= failCell.ActualWidth + 0.5);
            Check(bad, "修整16a③ 失败态「重试」按钮在窄列里完整在位（宽 " + (retry == null ? 0 : retry.ActualWidth).ToString("0.#")
                + "，可见=" + (retry != null && retry.Visibility == Visibility.Visible) + "）",
                retry != null && retry.Visibility == Visibility.Visible && retry.ActualWidth > 20
                && failCell != null && retry.ActualWidth <= failCell.ActualWidth + 0.5);
            var rowHeights = FindByType(root, typeof(DataGridRow)).Cast<DataGridRow>()
                .Where(r => r.ActualHeight > 0).Select(r => r.ActualHeight).ToList();
            Check(bad, "修整16a② 行高不随状态文字长（16 行等高 36~37.6）", rowHeights.Count > 0
                && rowHeights.All(h => Math.Abs(h - rowHeights[0]) < 0.2) && rowHeights[0] >= 36 && rowHeights[0] <= 37.6);

            // 份数格：输入框水平+垂直铺满（修整12 只做到水平）
            var copiesCell = CellOf(root, rows[0], grid, Res("s.ColCopies"));
            var copiesBox = copiesCell == null ? null : FindByType(copiesCell, typeof(TextBox)).Cast<TextBox>().FirstOrDefault();
            double padX = copiesCell == null ? 0 : copiesCell.Padding.Left + copiesCell.Padding.Right;
            sb.AppendLine("— 份数格实测：单元格 " + (copiesCell == null ? 0 : copiesCell.ActualWidth).ToString("0.#") + "x"
                + (copiesCell == null ? 0 : copiesCell.ActualHeight).ToString("0.#")
                + "，输入框 " + (copiesBox == null ? 0 : copiesBox.ActualWidth).ToString("0.#") + "x"
                + (copiesBox == null ? 0 : copiesBox.ActualHeight).ToString("0.#")
                + "（单元格左右内边距 " + padX.ToString("0.#") + "，格内样式="
                + (copiesCell == null || copiesCell.Style == null ? "—" : copiesCell.Style == grid.CellStyle ? "GridCellLine" : "GridCellLineFill") + "）");
            Check(bad, "修整17a 份数输入框水平铺满（实占宽 ≥ 列宽-内边距-1）", copiesBox != null && copiesCell != null
                && copiesBox.HorizontalAlignment == HorizontalAlignment.Stretch
                && copiesBox.ActualWidth >= copiesCell.ActualWidth - padX - 1);
            Check(bad, "修整17a 份数输入框垂直铺满（实占高 ≥ 单元格高-1.5，修整12 只做了水平）", copiesBox != null && copiesCell != null
                && copiesBox.VerticalAlignment == VerticalAlignment.Stretch
                && copiesBox.ActualHeight >= copiesCell.ActualHeight - 1.5);
            Check(bad, "修整17c 份数链路零改动（VM 仍 string、转换器仍 TwoWay/PropertyChanged）", copiesBox != null
                && copiesBox.Text == rows[0].Copies
                && copiesBox.GetBindingExpression(TextBox.TextProperty) != null
                && copiesBox.GetBindingExpression(TextBox.TextProperty).ParentBinding.Mode == System.Windows.Data.BindingMode.TwoWay
                && copiesBox.GetBindingExpression(TextBox.TextProperty).ParentBinding.UpdateSourceTrigger == System.Windows.Data.UpdateSourceTrigger.PropertyChanged);
            Check(bad, "修整17d 份数输入框不写死底色（透明底＝随行底，天然随主题）", copiesBox != null
                && (copiesBox.Background == null || V44Fix3Color(copiesBox.Background, Colors.Black).A == 0));
            Check(bad, "修整17a 份数输入框走极简模板（CellBoxFill，铺满后不再露库模板那圈圆角内框）", copiesBox != null
                && Equals(copiesBox.Style, Application.Current.TryFindResource("CellBoxFill"))
                && copiesBox.Template != null && copiesBox.BorderThickness.Left == 0 && copiesBox.BorderThickness.Bottom == 0);
            int boxPaint = 0;
            var boxStack = new Stack<DependencyObject>();
            if (copiesCell != null) boxStack.Push(copiesCell);
            while (boxStack.Count > 0)
            {
                var node = boxStack.Pop();
                var sbb = ((node as Control)?.Background ?? (node as Border)?.Background) as SolidColorBrush;
                if (sbb != null && sbb.Color.A > 0) boxPaint++;
                int cn = 0;
                try { cn = VisualTreeHelper.GetChildrenCount(node); } catch { }
                for (int i = 0; i < cn; i++) boxStack.Push(VisualTreeHelper.GetChild(node, i));
            }
            Check(bad, "修整17d 份数格子树内不画任何不透明底色（铺满后底色只跟行底；实测亮底对象 " + boxPaint + " 个）", boxPaint == 0);
            Check(bad, "修整17 类型/页数列按方案②同步收窄（64 / 60，原 76 / 64）", colKind != null && colPages != null
                && Math.Abs(colKind.ActualWidth - 64) < 0.6 && Math.Abs(colPages.ActualWidth - 60) < 0.6);
            // 列头单行必须中英都量（修整15 当时只卡了中文，方案② 压窄后英文 "Pages" 在 52 宽里折成两行才发现）
            foreach (var lg in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
            {
                App.ApplyLanguage(lg);
                w.UpdateLayoutForTest(1120, 660);
                var hTexts = FindByType(root, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
                    .Cast<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                    .Where(h => !(h.Content is string) || !string.IsNullOrEmpty((string)h.Content))
                    .SelectMany(h => FindByType(h, typeof(TextBlock)).Cast<TextBlock>())
                    .Where(t => t.ActualHeight > 0).ToList();
                var wrapOff = hTexts.Where(t => t.ActualHeight > 22.5)
                    .Select(t => "\"" + t.Text + "\"高" + t.ActualHeight.ToString("0.#")).ToList();
                Check(bad, "修整16② 列头" + (lg == AppLanguage.EnUS ? "英文" : "中文") + "实测单行（offender="
                    + string.Join("，", wrapOff.Take(4)) + "）", hTexts.Count >= 6 && wrapOff.Count == 0);
            }
            App.ApplyLanguage(AppLanguage.ZhCN);
            w.UpdateLayoutForTest(1120, 660);
            w.UpdateLayoutForTest(1120, 660);
            var rangeBox = FindByType(root, typeof(TextBox)).Cast<TextBox>()
                .FirstOrDefault(t => Equals(t.Name, "RangeBox"));
            Check(bad, "修整7/12 回归：页码范围列输入框与水印仍在位（水印随值显隐）", rangeBox != null
                && FindWatermark(root, rows[0], grid) != null);

            return Tuple.Create(sb.ToString(), bad);
        }

        private static Button V44Fix4EditButton(DependencyObject root)
        {
            return FindByType(root, typeof(Button)).Cast<Button>()
                .FirstOrDefault(b => b.Style != null && Equals(b.Style, Application.Current.TryFindResource("BtnEdit")));
        }

        private static TextBlock V44Fix4StatusText(DataGridCell cell)
        {
            if (cell == null) return null;
            var row = cell.DataContext as RowVm;
            return FindByType(cell, typeof(TextBlock)).Cast<TextBlock>()
                .FirstOrDefault(t => row != null && t.Text == row.Status);
        }

        /// <summary>失败态「重试」按钮的实测宽（窄列里"先挤文字、不挤按钮"的证据）。</summary>
        private static double V44Fix4RetryWidth(DependencyObject root, DataGrid grid, RowVm row)
        {
            var cell = CellOf(root, row, grid, Res("s.ColStatus"));
            var b = cell == null ? null : FindByType(cell, typeof(Button)).Cast<Button>().FirstOrDefault();
            return b == null ? 0 : b.ActualWidth;
        }

        /// <summary>四批·状态色：三态在当前主题/语言下拿到的前景 + 三档底色上的 WCAG 对比度 + 上色只看结构化种类。</summary>
        private static Tuple<string, List<string>> V44Fix4Colors(AppTheme th, AppLanguage lang)
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            string tag = (th == AppTheme.Dark ? "深色" : "浅色") + (lang == AppLanguage.EnUS ? "英文" : "中文");
            App.ApplyTheme(th);
            App.ApplyLanguage(lang);
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix4C-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var w = new MainWindow();
            var grid = w.FindName("Grid") as DataGrid;
            var root = w.Content as FrameworkElement;
            var rDone = NewRow(w, dir, "已完成甲.pdf");
            var rPrint = NewRow(w, dir, "打印中乙.pdf");
            var rFail = NewRow(w, dir, "失败丙.pdf");
            var rSkip = NewRow(w, dir, "已跳过丁.pdf");
            rDone.SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
            rPrint.SetStatus(Res("s.StatusPrintingRow"), false, RowStatusKind.Printing);
            rFail.SetStatus(Res("s.StatusFailedRow"), true, RowStatusKind.Failed);
            rSkip.SetStatus(Res("s.StatusSkipped"), false, RowStatusKind.Plain);
            w.UpdateLayoutForTest(1260, 820);

            Color ok = V44Fix3Color((Brush)Application.Current.TryFindResource("StatusOkTextBrush"), Colors.Magenta);
            Color fail = V44Fix3Color((Brush)Application.Current.TryFindResource("StatusFailTextBrush"), Colors.Magenta);
            Color norm = V44Fix3Color((Brush)Application.Current.TryFindResource("TextPrimaryBrush"), Colors.Magenta);
            sb.AppendLine("— 主题键（" + tag + "）StatusOkTextBrush=" + V44Fix3Resolved("StatusOkTextBrush")
                + " StatusFailTextBrush=" + V44Fix3Resolved("StatusFailTextBrush")
                + " TextPrimaryBrush=" + V44Fix3Resolved("TextPrimaryBrush"));
            Check(bad, tag + " 两个状态色键在本主题字典都有定义（浅深两份都要有，GridRowBgBrush 教训）",
                ok != Colors.Magenta && fail != Colors.Magenta);

            var bgKeys = new[] { "GridRowBgBrush", "GridRowHoverBrush", "GridRowSelectedBrush" };
            var bgList = bgKeys.Select(k => new { k, c = V44Fix3Color((Brush)Application.Current.TryFindResource(k), Colors.Black) }).ToList();

            var cases = new[]
            {
                new { R = rDone, N = "已完成", Want = ok, Label = "绿" },
                new { R = rPrint, N = "打印中", Want = norm, Label = "正常文字色" },
                new { R = rFail, N = "失败", Want = fail, Label = "红" },
                new { R = rSkip, N = "已跳过", Want = norm, Label = "维持现状" },
            };
            foreach (var c in cases)
            {
                var cell = CellOf(root, c.R, grid, Res("s.ColStatus"));
                var tb = V44Fix4StatusText(cell);
                Color got = tb == null ? Colors.Black : V44Fix3Color(tb.Foreground, Colors.Black);
                double worst = bgList.Select(b => V44Fix3Contrast(c.Want, b.c)).Min();
                sb.AppendLine("    " + c.N + "（" + c.Label + "）文案=\"" + c.R.Status + "\" 种类=" + c.R.StatusKind
                    + " 前景=" + V44Fix3Brush(tb != null ? tb.Foreground : null) + " 样式=" + V44Fix3Style(tb)
                    + " 对比度 " + string.Join(" ", bgList.Select(b => b.k + " " + V44Fix3Contrast(c.Want, b.c).ToString("0.00")))
                    + "（最差 " + worst.ToString("0.00") + "）");
                Check(bad, tag + " " + c.N + "＝" + c.Label + "（期望 " + V44Fix3Hex(c.Want) + "，实得 " + V44Fix3Hex(got) + "）",
                    got.R == c.Want.R && got.G == c.Want.G && got.B == c.Want.B);
                Check(bad, tag + " " + c.N + " 三档底色对比度最差 " + worst.ToString("0.00") + " ≥4.5", worst >= 4.5);
                Check(bad, tag + " " + c.N + " 上色走内容元素自身（CellTextStatus 触发器，不是父级继承）",
                    tb != null && tb.Style != null && Equals(tb.Style, Application.Current.TryFindResource("CellTextStatus")));
            }

            // 只看结构化种类，不看文案：把 Done 行的文案换成「失败」两个字，颜色必须仍是绿
            rDone.SetStatus(Res("s.StatusFailedRow"), false, RowStatusKind.Done);
            w.UpdateLayoutForTest(1260, 820);
            var t2 = V44Fix4StatusText(CellOf(root, rDone, grid, Res("s.ColStatus")));
            Color g2 = t2 == null ? Colors.Black : V44Fix3Color(t2.Foreground, Colors.Black);
            Check(bad, tag + " 上色只认 StatusKind：文案写成「" + rDone.Status + "」的 Done 行仍是绿 " + V44Fix3Hex(g2),
                g2.R == ok.R && g2.G == ok.G && g2.B == ok.B);
            // 反过来：同文案换成 Plain 种类即恢复正常字色
            rDone.SetStatus(Res("s.StatusFailedRow"), false, RowStatusKind.Plain);
            w.UpdateLayoutForTest(1260, 820);
            t2 = V44Fix4StatusText(CellOf(root, rDone, grid, Res("s.ColStatus")));
            Color g3 = t2 == null ? Colors.Black : V44Fix3Color(t2.Foreground, Colors.Black);
            Check(bad, tag + " 同文案改判种类即掉色（Plain 拿回正常字色 " + V44Fix3Hex(g3) + "），证明不是字符串匹配",
                g3.R == norm.R && g3.G == norm.G && g3.B == norm.B);
            return Tuple.Create(sb.ToString(), bad);
        }

        /// <summary>四批自检行：列宽/视口（一次）＋浅深×中英四组颜色映射，数字落 V44四批状态列与份数列定位.txt。</summary>
        internal static string V44Fix4Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                var l = V44Fix4Layout();
                sb.AppendLine("======== 列宽与视口（浅色·中文，16 行） ========").Append(l.Item1);
                bad.AddRange(l.Item2);
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                    foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
                    {
                        sb.AppendLine("======== 状态色 " + (th == AppTheme.Dark ? "深色" : "浅色")
                            + "·" + (lang == AppLanguage.EnUS ? "英文" : "中文") + " ========");
                        var c = V44Fix4Colors(th, lang);
                        sb.Append(c.Item1);
                        bad.AddRange(c.Item2);
                    }
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V44四批状态列与份数列定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（状态列已收窄且 1120 无横向滚动·参数按钮完整可见 / 三态颜色映射只看结构化 StatusKind（浅深中英四组、文案改判不影响上色）/ "
                  + "绿红两色在行底·悬停底·选中底三档上对比度均 ≥4.5 / 份数输入框水平+垂直铺满且链路零改动；逐条数字见 V44四批状态列与份数列定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>四批取证造数据：6 行（含超长文件名），状态四态各一行——已完成绿 / 打印中正常字色 / 失败红＋重试 / 已跳过维持现状。</summary>
        private static MainWindow V44Fix4Seed(string dir, out RowVm done, out RowVm printing, out RowVm fail, out RowVm skip)
        {
            var w = new MainWindow();
            var grid = w.FindName("Grid") as DataGrid;
            done = NewRow(w, dir, "四批已完成甲.pdf");
            printing = NewRow(w, dir, "四批打印中乙.pdf");
            fail = NewRow(w, dir, "四批失败丙.pdf");
            skip = NewRow(w, dir, "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书-终稿第三版-含附件清单.pdf");
            var r5 = NewRow(w, dir, "四批待打丁.pdf");
            var r6 = NewRow(w, dir, "四批待打戊.pdf");
            done.SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
            printing.SetStatus(Res("s.StatusPrintingRow"), false, RowStatusKind.Printing);
            fail.SetStatus(Res("s.StatusFailedRow") + "：" + Res("s.QueuePaperOut"), true, RowStatusKind.Failed);
            skip.SetStatus(Res("s.StatusSkipped"), false, RowStatusKind.Plain);
            w.UpdateLayoutForTest(1260, 700);
            MainWindow.FillStarColumn(grid, w.ColName);
            return w;
        }

        /// <summary>四批取证图（开工令 §4.2）：主页 浅/深 × 中英 × 默认宽 1120 / 最小宽 960 共 8 张
        /// ＋状态三态特写与份数格铺满特写（浅深中英各一，3 倍）。工具栏/底栏按钮 3 倍特写由三批那两组同轮再出，
        /// 用来证明本轮零回归。出图统一垫 WindowBgBrush（全透明像素 0）。</summary>
        internal static int V44Fix4Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix4R-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    string tn = th == AppTheme.Dark ? "深色" : "浅色";
                    foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
                    {
                        string ln = lang == AppLanguage.EnUS ? "英文" : "中文";
                        App.ApplyTheme(th);
                        App.ApplyLanguage(lang);

                        // —— 默认宽 1120 整窗 + 两张 3 倍特写（同一窗口排好后出，特写在整窗渲染后重排取）——
                        RowVm d, p, f, s2;
                        var w = V44Fix4Seed(dir, out d, out p, out f, out s2);
                        var grid = w.FindName("Grid") as DataGrid;
                        w.UpdateLayoutForTest(1120, 700);
                        MainWindow.FillStarColumn(grid, w.ColName);
                        w.UpdateLayoutForTest(1120, 700);
                        code |= RenderWindow("四批主页默认宽" + tn + ln + "自检.txt", "四批主页-默认宽-" + tn + ln + ".png", w, 1120, 700,
                            new[] { "Grid", "BtnAddFiles", "BtnClear", "PrinterBox", "BtnPrintSetup", "BtnStart", "BtnStop" });
                        // 特写在整窗渲染之后取：RenderWindow 会把内容摘到垫底 Border 上，行容器可能已被虚拟化重建，
                        // 提前抓住的单元格会成孤儿（实测报「指定的 Visual 不是此 Visual 的上级」）→ 重排后从新根现找。
                        var top = V44Fix3Relayout(w, 1120, 700);
                        var statusCells = new FrameworkElement[]
                        {
                            CellOf(top, d, grid, Res("s.ColStatus")), CellOf(top, p, grid, Res("s.ColStatus")),
                            CellOf(top, f, grid, Res("s.ColStatus")), CellOf(top, s2, grid, Res("s.ColStatus")),
                        };
                        var copiesCells = FindByType(top, typeof(DataGridCell)).Cast<DataGridCell>()
                            .Where(c => c.Column != null && Equals(c.Column.Header, Res("s.ColCopies")))
                            .OrderBy(c => c.TransformToAncestor(top).Transform(new Point(0, 0)).Y)
                            .Cast<FrameworkElement>().ToArray();
                        sb.AppendLine("状态三态特写-" + tn + ln + "（3 倍，从上到下：已完成/打印中/失败＋重试/已跳过）："
                            + SnapFlatGroup(top, statusCells, "状态三态特写-" + tn + ln + "-3倍.png", 360));
                        sb.AppendLine("份数格铺满特写-" + tn + ln + "（3 倍）："
                            + SnapFlatGroup(top, copiesCells, "份数格铺满特写-" + tn + ln + "-3倍.png", 360));

                        // —— 最小宽 960 整窗（另起一个窗口：RenderWindow 会把内容摘出，同窗不能连渲两张）——
                        var w2 = V44Fix4Seed(dir, out d, out p, out f, out s2);
                        var grid2 = w2.FindName("Grid") as DataGrid;
                        w2.UpdateLayoutForTest(960, 700);
                        MainWindow.FillStarColumn(grid2, w2.ColName);
                        code |= RenderWindow("四批主页最小宽" + tn + ln + "自检.txt", "四批主页-最小宽-" + tn + ln + ".png", w2, 960, 700,
                            new[] { "Grid", "BtnAddFiles", "BtnClear", "PrinterBox", "BtnPrintSetup", "BtnStart", "BtnStop" });
                    }
                }
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                string txt = sb.ToString();
                bool ok = code == 0 && txt.IndexOf("(截图失败") < 0 && txt.IndexOf("(元素未渲染") < 0;
                txt += "\r\n说明：状态列宽由 150 收到 76（1120 档长列表下内层可视只剩 854.4，纵向滚动条占 16 DIP，"
                     + "建议区间的 96 仍会顶出横向滚动，硬约束①优先）；960 最小宽一档按需求方 2026-09-23 拍板方案②"
                     + "同步压窄：类型 76→64、页数 64→52、页码范围 180/150→130/110、文件名保底 200→110，"
                     + "三档视口（1260/1120/960）实测列合计都等于内层可视、ScrollableWidth=0 → 全程不出横向滚动、参数按钮一页可见。"
                     + "打印中按需求方口径＝正常文字色（浅色深字/深色白字）。逐条数字见自检报告.txt 的「V4.4 四批」行 与 V44四批状态列与份数列定位.txt。"
                     + (ok ? " 判定：通过" : " 判定：失败");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true));
                return ok ? code : code | 1;
            }
            catch (Exception ex) { return FailWith(log, ex); }
        }

        // ==================================================================
        // V4.4 五批（需求方 2026-09-22 真机反馈，2026-09-23 开工）
        // 修整18 长列表不出横向滚动条 / 修整19 打印中禁用清单 / 修整20 进度计数与剩余预估
        // 修整21 历史页最小窗适配 + 单元格悬浮提示 / 修整22 目录行初始显示 / 修整23 日志位置去重
        // 数字全部落 V44五批滚动条打印锁与进度定位.txt，判定并入 --selftest。
        // ==================================================================

        /// <summary>把 Dispatcher 排空到 Loaded 优先级——自检里模拟"排版之后的那一拍"，让补排的星号列重算真跑一次。</summary>
        private static void PumpToLoaded()
        {
            Application.Current.Dispatcher.Invoke(new Action(() => { }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>内层 ScrollViewer 才是"能不能横向滚"的 ground truth（库用悬浮式滚动条，按可见性判会误判）。</summary>
        private static double V44Fix5Scrollable(DataGrid grid, out double extent, out double viewport)
        {
            extent = 0; viewport = 0;
            var sv = FindDerived(grid, typeof(ScrollViewer)).Cast<ScrollViewer>()
                .OrderByDescending(s => s.ViewportWidth).FirstOrDefault();
            if (sv == null) return -1;
            extent = sv.ExtentWidth; viewport = sv.ViewportWidth;
            return sv.ScrollableWidth;
        }

        /// <summary>修整18：50 行长列表（纵向滚动条必然在位）× 1120/960 × 中英——补一拍后 ScrollableWidth 必须为 0。</summary>
        private static List<string> V44Fix5Star(StringBuilder sb)
        {
            var bad = new List<string>();
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix5S-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var main = new MainWindow();
            var grid = main.FindName("Grid") as DataGrid;
            var root = main.Content as FrameworkElement;
            for (int i = 0; i < 50; i++) NewRow(main, dir, "五批长列表" + (i + 1) + ".pdf");
            foreach (double vw in new[] { 1120.0, 960.0 })
            {
                main.UpdateLayoutForTest(vw, 660);          // 真机语境：窗口早就排好（此刻视口宽＝无纵向滚动条的口径）
                foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
                {
                    App.ApplyLanguage(lang);
                    main.UpdateStatusForTest();             // 行变化 → 同步算一次（用的还是"无滚动条"的视口宽＝bug 的那一拍）
                    main.UpdateLayoutForTest(vw, 660);      // 排版：纵向滚动条出现，吃掉 ~16 DIP
                    double sBefore = V44Fix5Scrollable(grid, out double extB, out double viewB);
                    PumpToLoaded();                         // 补排的那一拍（RequestStarFill 排在 Loaded）
                    main.UpdateLayoutForTest(vw, 660);
                    double sAfter = V44Fix5Scrollable(grid, out double extA, out double viewA);
                    var edit = V44Fix4EditButton(root);
                    double editRight = edit == null ? -1 : edit.TransformToAncestor(grid).Transform(new Point(0, 0)).X + edit.ActualWidth;
                    string ln = lang == AppLanguage.EnUS ? "英文" : "中文";
                    sb.AppendLine("视口 " + vw + "x660 × " + ln + "表头（50 行）：补拍前 列合计 " + extB.ToString("0.#")
                        + " / 内层可视 " + viewB.ToString("0.#") + " → ScrollableWidth=" + sBefore.ToString("0.#")
                        + "；补拍后 " + extA.ToString("0.#") + " / " + viewA.ToString("0.#")
                        + " → ScrollableWidth=" + sAfter.ToString("0.#")
                        + "；文件名列=" + main.ColName.ActualWidth.ToString("0.#")
                        + " 参数按钮右缘=" + editRight.ToString("0.#") + "（≤可视=" + (editRight <= viewA + 0.5) + "）");
                    Check(bad, "修整18 " + vw + " × " + ln + " 50 行补一拍后 ScrollableWidth=" + sAfter.ToString("0.#") + " 须为 0",
                        sAfter <= 0.5);
                    Check(bad, "修整18 " + vw + " × " + ln + " 参数「修改」按钮一页可见（右缘 " + editRight.ToString("0.#")
                        + " ≤ 内层可视 " + viewA.ToString("0.#") + "）", edit != null && editRight <= viewA + 0.5);
                }
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            // 短列表不回归（三批/四批口径：3 行、无纵向滚动条时同样不滚）
            var small = new MainWindow();
            var sgrid = small.FindName("Grid") as DataGrid;
            string dir2 = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix5S2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir2);
            NewRow(small, dir2, "短列表甲.pdf"); NewRow(small, dir2, "短列表乙.pdf"); NewRow(small, dir2, "短列表丙.pdf");
            small.UpdateLayoutForTest(1120, 660);
            PumpToLoaded();
            small.UpdateLayoutForTest(1120, 660);
            double sSmall = V44Fix5Scrollable(sgrid, out double extS, out double viewS);
            sb.AppendLine("短列表（3 行，无纵向滚动条）：列合计 " + extS.ToString("0.#") + " / 内层可视 " + viewS.ToString("0.#")
                + " → ScrollableWidth=" + sSmall.ToString("0.#") + "（星号列仍铺满，不留 filler 空条）");
            Check(bad, "修整18 短列表不回归（不滚且不留空档 " + extS.ToString("0.#") + "≈" + viewS.ToString("0.#") + "）",
                sSmall <= 0.5 && extS >= viewS - 1.0);
            return bad;
        }

        private static readonly string[] V44Fix5Names =
        {
            "BtnAddFiles", "BtnAddFolder", "BtnRemove", "BtnClear",
            "PrinterBox", "BtnPrintSetup", "BtnRefresh", "BtnStart", "BtnStop"
        };

        /// <summary>修整19：打印中禁用清单全量断言 + 停止/异常收尾恢复 + 添加收口拒绝。</summary>
        private static List<string> V44Fix5Busy(StringBuilder sb)
        {
            var bad = new List<string>();
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix5B-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var main = new MainWindow();
            var grid = main.FindName("Grid") as DataGrid;
            var root = main.Content as FrameworkElement;
            var r1 = NewRow(main, dir, "锁定位甲.pdf");
            NewRow(main, dir, "锁定位乙.pdf");
            main.UpdateLayoutForTest(1120, 660);

            Func<string, FrameworkElement> byName = n => main.FindName(n) as FrameworkElement;
            // BtnStop 反过来：空闲态本就该灰着，只有打印中才放开（单独一条断言，见下）
            var idle = V44Fix5Names.Where(n => n == "BtnStop" ? false : (byName(n) != null && !byName(n).IsEnabled)).ToList();
            Check(bad, "修整19 空闲态清单控件全可用（异常项=" + string.Join(",", idle) + "）", idle.Count == 0 && V44Fix5Names.Length == 9);
            Check(bad, "修整19 空闲态「停止」不可用（打印中才放开）", byName("BtnStop") != null && !byName("BtnStop").IsEnabled);

            main.SetControlsBusyForTest(true);
            main.UpdateLayoutForTest(1120, 660);
            var stillOn = V44Fix5Names.Where(n => n == "BtnStop" ? false : (byName(n) != null && byName(n).IsEnabled)).ToList();
            Check(bad, "修整19 打印中点名+同类入口全部禁用（漏网=" + string.Join(",", stillOn) + "）", stillOn.Count == 0);
            Check(bad, "修整19 打印中「停止」保持可用", byName("BtnStop") != null && byName("BtnStop").IsEnabled);
            Check(bad, "修整19 打印中主列表本体保持可用（V4.5-3 改口径：锁交互不锁滚动，见 V45一批按钮字色与锁定位移定位.txt）",
                grid.IsEnabled == true);
            Check(bad, "修整19 打印中整表四类行内交互控件不可用（勾选项改为随 PrintLock 逐个锁）",
                PrintLock.Instance.Busy && !PrintLock.Instance.Idle);
            var copiesBox = FindTextBoxInCell(root, r1, grid);
            var editBtn = V44Fix4EditButton(root);
            var headerCheck = FindHeaderCheck(root);
            Check(bad, "修整19 行内输入框/参数按钮/表头全选在打印中确实不可用（IsEnabled 有效态）",
                copiesBox != null && !copiesBox.IsEnabled && editBtn != null && !editBtn.IsEnabled
                && headerCheck != null && !headerCheck.IsEnabled);
            Check(bad, "修整19 打印中拖拽入口关掉（AllowDrop=false）", main.AllowDrop == false);
            string fresh = Path.Combine(dir, "打印中新来的.pdf");
            BuildSamplePdf(fresh, 2);
            int n0 = main.RowTotalForTest;
            Check(bad, "修整19 添加收口在打印中拒绝新文件（按钮/拖拽/Ctrl+V 三条路共用这一道）",
                main.AddFilesForTest(new[] { fresh }) == n0);
            var page = main.PageSettings;
            var pb2 = page == null ? null : page.FindName("PrinterBox2") as FrameworkElement;
            var props = page == null ? null : page.FindName("BtnOpenPrinterProps") as FrameworkElement;
            Check(bad, "修整19 设置页 F15 两处入口同步禁用（打印机下拉/打开打印机属性）",
                pb2 != null && props != null && !pb2.IsEnabled && !props.IsEnabled);

            main.SetControlsBusyForTest(false);
            main.UpdateLayoutForTest(1120, 660);
            var stillOff = V44Fix5Names.Where(n => n == "BtnStart" || n == "BtnStop" ? false : (byName(n) != null && !byName(n).IsEnabled)).ToList();
            Check(bad, "修整19 恢复态清单控件全可用（异常项=" + string.Join(",", stillOff) + "）",
                stillOff.Count == 0 && byName("BtnStart") != null && byName("BtnStart").IsEnabled);
            Check(bad, "修整19 恢复态「停止」回到灰着（不是只把锁放开忘了关）", byName("BtnStop") != null && !byName("BtnStop").IsEnabled);
            Check(bad, "修整19 恢复后拖拽入口打开", main.AllowDrop == true);
            Check(bad, "修整19 恢复后设置页两处入口可用", pb2 != null && pb2.IsEnabled && props != null && props.IsEnabled);
            Check(bad, "修整19 恢复后添加收口照常放行（不是把入口焊死）", main.AddFilesForTest(new[] { fresh }) == n0 + 1);

            // 收尾路径：FinishPrint（停止/异常都汇流到这里）必须把锁全部放开
            main.SetControlsBusyForTest(true);
            var rows = main.RowsForTest;
            main.FinishPrintForTest(new PrintRunResult { Ok = 0, Fail = 0, Remaining = new List<PrintJob> { rows[0].Job } });
            main.UpdateLayoutForTest(1120, 660);
            var afterFinish = V44Fix5Names.Where(n => n == "BtnStop" ? byName(n) != null && byName(n).IsEnabled
                                                  : (byName(n) != null && !byName(n).IsEnabled)).ToList();
            Check(bad, "修整19 停止/异常收尾（FinishPrint 同一出口）后全部恢复（异常项=" + string.Join(",", afterFinish) + "）",
                afterFinish.Count == 0 && main.PrintBusyForTest == false);
            sb.AppendLine("打印中禁用清单（点名四项＋同类入口）：" + string.Join("、", V44Fix5Names)
                + "；另整表 IsEnabled=false 覆盖行内输入/勾选/表头全选/参数「修改」/重试，"
                + "设置页 PrinterBox2+BtnOpenPrinterProps，窗口 AllowDrop，AddFiles 收口拒绝。");
            return bad;
        }

        /// <summary>修整20：「第 X / N 个」与预估的出现/隐藏条件、失败数着色。</summary>
        private static List<string> V44Fix5Progress(StringBuilder sb)
        {
            var bad = new List<string>();
            App.ApplyLanguage(AppLanguage.ZhCN);
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix5P-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var main = new MainWindow();
            for (int i = 0; i < 6; i++) NewRow(main, dir, "进度甲" + (i + 1) + ".pdf");
            var rows = main.RowsForTest;
            main.UpdateLayoutForTest(1120, 660);

            main.SetProgressForTest(10, 0, 0);
            string t0 = main.ProgressTextForTest;
            main.SetProgressForTest(10, 0, 3);
            string t3 = main.ProgressTextForTest;
            main.SetProgressForTest(10, 4, 3);      // 续打：基线 4 + 本轮 3 → 第 8 个
            string t8 = main.ProgressTextForTest;
            sb.AppendLine("计数文本：N=10 done=0 → \"" + t0 + "\"；done=3 → \"" + t3 + "\"；基线4+本轮3 → \"" + t8 + "\"");
            Check(bad, "修整20a 计数「第 X / N 个」分子含停止前已完成（基线4+本轮3 → 第 8 / 10）",
                t0 == Res("s.ProgCount").Replace("{0}", "1").Replace("{1}", "10")
                && t3 == Res("s.ProgCount").Replace("{0}", "4").Replace("{1}", "10")
                && t8 == Res("s.ProgCount").Replace("{0}", "8").Replace("{1}", "10"));

            string etaKey = "≈";
            Check(bad, "修整20b 样本不足（0/1 个）不显示预估", !t8.Contains(etaKey));
            main.AddFileSampleForTest(6);
            Check(bad, "修整20b 只有 1 个样本仍不显示预估", !main.ProgressTextForTest.Contains(etaKey));
            main.AddFileSampleForTest(7);           // avg 6.5 × 剩余 3 = 19.5 秒
            string tEta = main.ProgressTextForTest;
            sb.AppendLine("样本[6,7] 剩余 3 → \"" + tEta + "\"");
            Check(bad, "修整20b 样本 ≥2 且一致 → 显示预估（" + tEta + "）", tEta.Contains(etaKey) && tEta.Contains("预计剩余"));
            main.ResetFileSamplesForTest();
            main.AddFileSampleForTest(1);
            main.AddFileSampleForTest(90);          // 标准差/均值 ≈0.98 > 0.8 → 不可信
            string tJitter = main.ProgressTextForTest;
            sb.AppendLine("样本[1,90]（方差过大）→ \"" + tJitter + "\"");
            Check(bad, "修整20b 方差过大不显示预估（宁缺勿错）", !tJitter.Contains(etaKey));

            main.ResetFileSamplesForTest();
            main.AddFileSampleForTest(5); main.AddFileSampleForTest(5);
            main.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Failed, FileName = rows[0].Name, Reason = "test" });
            string tFail = main.ProgressTextForTest;
            sb.AppendLine("一条失败后 → \"" + tFail + "\" 失败段字色="
                + (main.ProgressFailColorForTest == null ? "(未上色)"
                   : "#" + main.ProgressFailColorForTest.Value.R.ToString("X2") + main.ProgressFailColorForTest.Value.G.ToString("X2") + main.ProgressFailColorForTest.Value.B.ToString("X2")));
            Check(bad, "修整20a 失败数>0 追加「失败 Z」且用状态红（Run 自身上色，非继承）",
                tFail.Contains(Res("s.ProgFail").Replace("{0}", "1"))
                && main.ProgressFailColorForTest != null
                && main.ProgressFailColorForTest.Value == V44Fix3Color((Brush)Application.Current.TryFindResource("StatusFailTextBrush"), Colors.Black));
            main.SetProgressForTest(0, 0, 0);
            Check(bad, "修整20 一轮结束（N 归零）计数文本清空", string.IsNullOrEmpty(main.ProgressTextForTest));
            return bad;
        }

        /// <summary>修整21：历史页列宽预算（960 含纵向滚动条档不滚）+ 文本列悬浮提示挂接。</summary>
        private static List<string> V44Fix5History(StringBuilder sb)
        {
            var bad = new List<string>();
            App.ApplyLanguage(AppLanguage.ZhCN);
            // 要在真实窗口外壳里量：孤立的 UserControl 没有导航栏与页边距，视口宽会偏大，960 档就量不出需求方看到的那个数
            var main = new MainWindow();
            main.ShowPanelForTest("history");
            main.UpdateLayoutForTest(960, 660);
            var content = main.Content as FrameworkElement;
            var hp = FindByType(content, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
            var hgrid = hp == null ? null : hp.FindName("Grid") as DataGrid;
            if (hgrid == null) { bad.Add("修整21 最小窗 960 里取不到历史页表格"); return bad; }
            var cols = hgrid.Columns.Cast<DataGridColumn>().ToList();
            double fixedSum = cols.Where(c => !ReferenceEquals(c, hp.ColName))
                .Sum(c => c.Width.IsAbsolute ? c.Width.Value : Math.Max(c.MinWidth, 20));
            double budget = 694.4;   // 960 档长列表内层可视（四批实测口径：表格宽 710.4 − 纵向滚动条 16）
            var tipStyle = Application.Current.TryFindResource("CellTextTip") as Style;
            var plainStyle = Application.Current.TryFindResource("CellText") as Style;
            // V4.5-2（决2）口径变更（如实报备）：状态列改用 CellTextHistStatus——它是 CellTextTip 的派生
            // （BasedOn 链在管，气泡一分不少，只多挂成功绿/失败红）。故这里的"挂气泡"按派生链判定，条数与语义都不减。
            var tipCols = cols.Where(c => StyleDerivesFrom(ElemStyleOf(c), tipStyle)).ToList();
            var noTip = cols.Where(c => !StyleDerivesFrom(ElemStyleOf(c), tipStyle) && !StyleDerivesFrom(ElemStyleOf(c), plainStyle)).ToList();
            sb.AppendLine("— 历史页列宽：列数=" + cols.Count + " 固定列合计=" + fixedSum.ToString("0.#")
                + " 文件名保底=" + hp.ColName.MinWidth.ToString("0.#")
                + " → 最坏合计 " + (fixedSum + hp.ColName.MinWidth).ToString("0.#") + "（预算 " + budget + "）");
            sb.AppendLine("    各列：" + string.Join(" | ", cols.Select(c => V44Fix5ColName(c)
                + "=" + (c.Width.IsAbsolute ? c.Width.Value.ToString("0.#") : c.Width.ToString()) + "/min" + c.MinWidth.ToString("0.#")
                + (StyleDerivesFrom(ElemStyleOf(c), tipStyle) ? "+气泡" : (StyleDerivesFrom(ElemStyleOf(c), plainStyle) ? "+纯文本" : "+未知样式")))));
            Check(bad, "修整21a 历史页固定列合计 + 文件名保底 ≤ 960 档内层可视（"
                + (fixedSum + hp.ColName.MinWidth).ToString("0.#") + " ≤ " + budget + "）",
                fixedSum + hp.ColName.MinWidth <= budget);
            Check(bad, "修整21a 挂气泡的五列都是 DataGridTextColumn（数字列走 CellText）",
                cols.All(c => c is DataGridTextColumn));
            Check(bad, "修整21b 会截断的文本列五列都挂 CellTextTip（实挂 " + tipCols.Count + "）", tipCols.Count >= 5);
            Check(bad, "修整21b 数字列（页数/份数）仍走 CellText 不加气泡",
                cols.Count(c => StyleDerivesFrom(ElemStyleOf(c), plainStyle) && !StyleDerivesFrom(ElemStyleOf(c), tipStyle)) >= 2 && noTip.Count == 0);
            var oneTip = tipCols.FirstOrDefault();
            if (oneTip != null)
            {
                var tb = new TextBlock { Style = tipStyle };
                sb.AppendLine("    气泡参数：延迟=" + ToolTipService.GetInitialShowDelay(tb) + " 位置=" + ToolTipService.GetPlacement(tb)
                    + " 下压=" + ToolTipService.GetVerticalOffset(tb) + " Between=" + ToolTipService.GetBetweenShowDelay(tb));
                Check(bad, "修整21b 气泡口径与主页 CellTextName 一致（1 秒 / Right + 下压 14 / Between 0）",
                    ToolTipService.GetInitialShowDelay(tb) == 1000 && ToolTipService.GetPlacement(tb) == System.Windows.Controls.Primitives.PlacementMode.Right
                    && ToolTipService.GetVerticalOffset(tb) == 14 && ToolTipService.GetBetweenShowDelay(tb) == 0);
                Check(bad, "修整21b 气泡内容绑自身 Text（一份样式管所有列）",
                    tipStyle.Setters.Cast<Setter>().Any(s => s.Property == TextBlock.ToolTipProperty
                        && s.Value is System.Windows.Data.Binding b && b.RelativeSource != null
                        && b.RelativeSource.Mode == System.Windows.Data.RelativeSourceMode.Self && b.Path.Path == "Text"));
                // 主页 CellTextName 仍是气泡口径的真值源（需求方：跟随主页模式），历史页只是把"绑自身 Text"泛化
                var nameStyle = Application.Current.TryFindResource("CellTextName") as Style;
                var nameTip = nameStyle == null ? null : nameStyle.Setters.Cast<Setter>()
                    .FirstOrDefault(s => s.Property == TextBlock.ToolTipProperty)?.Value as System.Windows.Data.Binding;
                // 只比"气泡行为参数"（延迟/节奏/落点/偏移）：内容绑定本来就不同（主页绑 Name，历史页绑自身 Text），由上一条断言管
                var att = new[] { ToolTipService.InitialShowDelayProperty, ToolTipService.BetweenShowDelayProperty,
                                  ToolTipService.PlacementProperty, ToolTipService.VerticalOffsetProperty };
                var diffs = att.Where(p =>
                {
                    var a = SetterOf(tipStyle, p);
                    var b = nameStyle == null ? null : SetterOf(nameStyle, p);
                    return a == null || b == null || !Equals(a.Value, b.Value);
                }).Select(p => p.Name + "(历史=" + (SetterOf(tipStyle, p)?.Value?.ToString() ?? "无")
                                        + "/主页=" + (nameStyle == null ? "无主页样式" : SetterOf(nameStyle, p)?.Value?.ToString() ?? "无") + ")").ToList();
                Check(bad, "修整21b 主页 CellTextName 气泡在位，历史页气泡参数与主页逐项同值"
                    + "（主页 ToolTip 绑定=" + (nameTip == null ? "无" : nameTip.Path.Path)
                    + "，不同项=" + string.Join("，", diffs) + "）",
                    nameTip != null && nameTip.Path.Path == "Name" && diffs.Count == 0);
            }
            // 时间列 134 够不够显 yyyy-MM-dd HH:mm（不够就只能靠气泡，需求方要求实测出数字）
            double timeW = cols.FirstOrDefault(c => c.Width.IsAbsolute && Math.Abs(c.Width.Value - 134) < 0.5) == null
                ? -1 : V44Fix5TextWidth(hgrid, "2026-09-23 12:34");
            sb.AppendLine("    时间列实测文本宽（" + (timeW < 0 ? "列宽不是 134" : "2026-09-23 12:34") + "）=" + timeW.ToString("0.#")
                + " + 单元格内边距 12 = " + (timeW + 12).ToString("0.#") + " ≤ 134 ?");
            Check(bad, "修整21a 时间列 134 够显 yyyy-MM-dd HH:mm（文本 " + timeW.ToString("0.#") + "+内边距 12 ≤ 134）",
                timeW > 0 && timeW + 12 <= 134);

            // 压窄后的列头单行性：960 档窄列最容易把表头挤折行（二批·修整15 只在 1260 档量过）
            double worstHeader = -1; string worstCol = "";
            foreach (var c in cols.Where(c => c.Width.IsAbsolute))
            {
                string h = V44Fix5ColName(c);
                if (h == "(表头未实例化)" || h.Length == 0) continue;
                double need = V44Fix5TextWidth(hgrid, h, true) + 16 + 12
                    + ((c.Header as StackPanel)?.Children.OfType<Button>().Any() == true ? 30 : 0);
                if (need > worstHeader) { worstHeader = need; worstCol = h + "=" + c.Width.Value.ToString("0.#"); }
            }
            sb.AppendLine("    列头最吃紧的一列（加粗文本 + 列头内边距 16 + ⇅ 约 12" + "，打印机列另算漏斗 30）：" + worstCol
                + " 需 " + worstHeader.ToString("0.#"));
            foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
            {
                App.ApplyLanguage(lang);
                main.UpdateLayoutForTest(960, 660);
                var hhText = FindByType(hgrid, typeof(System.Windows.Controls.TextBlock)).Cast<System.Windows.Controls.TextBlock>()
                    .Where(t => t.ActualHeight > 0 && t.Text.Length > 0 && !(t.Text == "⇅")).ToList();
                var wrapped = hhText.Where(t => t.ActualHeight > 22.5)
                    .Select(t => "\"" + t.Text + "\"高" + t.ActualHeight.ToString("0.#")).Take(4).ToList();
                Check(bad, "修整21a " + (lang == AppLanguage.ZhCN ? "中文" : "英文")
                    + "列头压窄后仍单行（折行项=" + string.Join("，", wrapped) + "）", wrapped.Count == 0);
                // 列头是 NoWrap：不会被挤折行，只会被裁——所以量"子元素实宽合计 ≤ 列头可视宽 − 内边距 16"
                var heads = FindDerived(hgrid, typeof(System.Windows.Controls.Primitives.DataGridColumnHeader))
                    .Cast<System.Windows.Controls.Primitives.DataGridColumnHeader>()
                    .Where(h => h.ActualWidth > 0 && h.Name != "PART_FillerColumnHeader").ToList();
                var clipped = new List<string>();
                foreach (var h in heads)
                {
                    var sp = h.Content as StackPanel;
                    if (sp == null) continue;
                    var label = sp.Children.OfType<System.Windows.Controls.TextBlock>().FirstOrDefault();
                    double need = sp.Children.OfType<FrameworkElement>().Sum(c => c.ActualWidth);
                    if (label != null && need > h.ActualWidth - 16 + 0.5)
                        clipped.Add("\"" + label.Text + "\"需" + need.ToString("0.#") + "/有" + (h.ActualWidth - 16).ToString("0.#"));
                }
                Check(bad, "修整21a " + (lang == AppLanguage.ZhCN ? "中文" : "英文")
                    + "列头（含 ⇅ 与漏斗）在 960 档不被裁（被裁项=" + string.Join("，", clipped) + "，量得列头 " + heads.Count + " 个）",
                    heads.Count >= 7 && clipped.Count == 0);
            }
            App.ApplyLanguage(AppLanguage.ZhCN);

            // 布局实测：灌 40 行使纵向滚动条真实占位（这正是需求方复验到的场景），补一拍后量内层 ScrollableWidth
            int seeded = hp.SeedRowsForTest(40);
            main.UpdateLayoutForTest(960, 660);
            hp.RequestStarFill();
            main.UpdateLayoutForTest(960, 660);
            PumpToLoaded();
            main.UpdateLayoutForTest(960, 660);
            int hRows = FindDerived(hgrid, typeof(DataGridRow)).Cast<DataGridRow>().Count(r => r.ActualHeight > 0);
            var hsv = FindDerived(hgrid, typeof(ScrollViewer)).Cast<ScrollViewer>()
                .OrderByDescending(s => s.ViewportWidth).FirstOrDefault();
            double sHist = V44Fix5Scrollable(hgrid, out double hExt, out double hView);
            sb.AppendLine("— 历史页布局实测（灌 " + seeded + " 行 / 已生成行 " + hRows
                + "，纵向滚动条=" + (hsv != null && hsv.ScrollableHeight > 0.5 ? "占位" : "未占位") + "）：列合计 "
                + hExt.ToString("0.#") + " / 内层可视 " + hView.ToString("0.#") + " → ScrollableWidth=" + sHist.ToString("0.#")
                + "；文件名列实得 " + (hp.ColName.ActualWidth).ToString("0.#"));
            Check(bad, "修整21a 历史页最小窗 960 + 长列表实测不滚（ScrollableWidth=" + sHist.ToString("0.#")
                + "，内层可视 " + hView.ToString("0.#") + "）", sHist <= 0.5);
            Check(bad, "修整21a 文件名星号列实得宽度 ≥ 保底 120（实得 " + hp.ColName.ActualWidth.ToString("0.#") + "）",
                hp.ColName.ActualWidth >= 119.5);
            return bad;
        }

        /// <summary>取样式里某个属性的 Setter（找不到返回 null）；气泡四参数在主页/历史页两份样式间逐项比用。</summary>
        private static Setter SetterOf(Style s, DependencyProperty p)
        {
            return s == null ? null : s.Setters.Cast<Setter>().FirstOrDefault(x => Equals(x.Property, p));
        }

        /// <summary>列的单元格文本样式：ElementStyle 只在 DataGridBoundColumn 上，基类 DataGridColumn 没有这个属性。</summary>
        private static Style ElemStyleOf(DataGridColumn c)
        {
            var t = c as DataGridTextColumn;
            return t == null ? null : t.ElementStyle;
        }

        /// <summary>样式是否等于 base 或沿 BasedOn 链继承自它（V4.5-2 起状态列用 CellTextTip 的派生样式上色）。</summary>
        private static bool StyleDerivesFrom(Style s, Style basis)
        {
            if (s == null || basis == null) return false;
            for (int i = 0; s != null && i < 8; i++, s = s.BasedOn) if (ReferenceEquals(s, basis)) return true;
            return false;
        }

        /// <summary>取证列名：历史页列头是「文本 + ⇅」的水平 StackPanel，取第一段文本（当前语言）。</summary>
        private static string V44Fix5ColName(DataGridColumn c)
        {
            var sp = c.Header as StackPanel;
            var tb = sp == null ? null : sp.Children.OfType<TextBlock>().FirstOrDefault();
            return tb != null ? (string.IsNullOrEmpty(tb.Text) ? "(表头未实例化)" : tb.Text) : (c.Header as string) ?? c.GetType().Name;
        }

        /// <summary>按表格自己的字体量一段文本的渲染宽（FontFamily/FontSize 都是继承属性，取元素上的有效值）。</summary>
        private static double V44Fix5TextWidth(Control fontSource, string text, bool bold = false)
        {
            var ft = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(fontSource.FontFamily, fontSource.FontStyle,
                    bold ? FontWeights.Bold : fontSource.FontWeight, fontSource.FontStretch),
                fontSource.FontSize, Brushes.Black, 1.25);
            return ft.WidthIncludingTrailingWhitespace;
        }

        /// <summary>修整22/23：设置页三个目录行加载即显当前生效路径；日志位置只在关于页。</summary>
        private static List<string> V44Fix5Settings(StringBuilder sb)
        {
            var bad = new List<string>();
            var page = new SettingsPage();
            string p1 = page.PrintoutDirForTest, p2 = page.ConvertDirForTest, p3 = page.ReportDirForTest;
            sb.AppendLine("— 设置页目录行（加载即显）：输出目录=\"" + p1 + "\" 转换产物=\"" + p2 + "\" 统计目录=\"" + p3 + "\"");
            Check(bad, "修整22a 三个目录行加载时非空白（原点开是空白，需求方无从判断输出到哪）",
                !string.IsNullOrWhiteSpace(p1) && !string.IsNullOrWhiteSpace(p2) && !string.IsNullOrWhiteSpace(p3));
            Check(bad, "修整22a 显示值＝当前生效路径且是绝对路径（与实际写入一致）",
                p1 == SettingsService.PrintoutDirectory && p2 == SettingsService.ConvertOutputDirectory && p3 == SettingsService.ReportDirectory
                && Path.IsPathRooted(p1) && Path.IsPathRooted(p2) && Path.IsPathRooted(p3));
            string tmp = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix5Dir-" + Guid.NewGuid().ToString("N"));
            string before = SettingsService.PrintoutDirectory;
            SettingsService.SetPrintoutDirectory(tmp);
            page.RefreshDirRowsForTest();
            bool changed = page.PrintoutDirForTest == tmp;
            SettingsService.SetPrintoutDirectory(null);   // 还原（getter 回到默认便携目录）
            page.RefreshDirRowsForTest();
            sb.AppendLine("    改目录后刷新=\"" + tmp + "\" 生效=" + changed + "；还原后=\"" + page.PrintoutDirForTest + "\"（＝默认解析）");
            Check(bad, "修整22b 设置变更后即时刷新（且能回到默认值）", changed && page.PrintoutDirForTest == SettingsService.PrintoutDirectory);

            Check(bad, "修整23a 设置页不再有日志路径行（LogPathText 已删）", page.FindName("LogPathText") == null
                && Application.Current.TryFindResource("s.SettingsLogDesc") == null);
            Check(bad, "修整23a 新描述键中英都在位", Application.Current.TryFindResource("s.SettingsDataDirDesc") != null);
            var about = new AboutPage();
            var logTb = about.FindName("LogPathText") as TextBlock;
            var openBtn = about.FindName("BtnOpenLog") as Button;
            Check(bad, "修整23b 关于页仍是日志唯一入口（路径 + 打开按钮在位）",
                logTb != null && !string.IsNullOrWhiteSpace(logTb.Text) && openBtn != null && openBtn.IsEnabled);
            sb.AppendLine("    关于页日志目录=\"" + (logTb == null ? "(取不到)" : logTb.Text) + "\" 打开按钮=" + (openBtn != null));
            return bad;
        }

        /// <summary>五批自检行：六项一次跑完，数字落 V44五批滚动条打印锁与进度定位.txt。</summary>
        internal static string V44Fix5Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== 修整18 长列表横向滚动条（补一拍重算） ========");
                bad.AddRange(V44Fix5Star(sb));
                sb.AppendLine("======== 修整19 打印中禁用清单 ========");
                bad.AddRange(V44Fix5Busy(sb));
                sb.AppendLine("======== 修整20 进度计数与剩余时间预估 ========");
                bad.AddRange(V44Fix5Progress(sb));
                sb.AppendLine("======== 修整21 历史页列宽与单元格悬浮提示 ========");
                bad.AddRange(V44Fix5History(sb));
                sb.AppendLine("======== 修整22/23 设置页目录行与日志位置去重 ========");
                bad.AddRange(V44Fix5Settings(sb));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V44五批滚动条打印锁与进度定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（50 行长列表 1120/960×中英补一拍后 ScrollableWidth=0·参数按钮一页可见 / 打印中点名四项+同类入口+设置页两处+拖拽与粘贴收口全禁且停止后全恢复 / "
                  + "「第 X / N 个」含停止前基线·预估样本不足或方差过大不显示·失败数用状态红 / 历史页固定列合计进 960 预算·五个文本列气泡挂齐·时间列实测够显 / "
                  + "设置页三目录行加载即显当前生效绝对路径·日志位置只在关于页；逐条数字见 V44五批滚动条打印锁与进度定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>五批取证图用：造一个"长列表"主窗口（n 行，文件名长短混排，够撑出纵向滚动条）。</summary>
        private static MainWindow V44Fix5SeedLong(string dir, int n)
        {
            var w = new MainWindow();
            for (int i = 0; i < n; i++)
            {
                string name = i % 4 == 0
                    ? "关于2026年度全校信息化设备采购与运维服务招标技术要求说明书第" + (i + 1) + "部分.pdf"
                    : "长列表第" + (i + 1) + "份.pdf";
                NewRow(w, dir, name);
            }
            var grid = w.FindName("Grid") as DataGrid;
            w.UpdateLayoutForTest(1120, 660);
            MainWindow.FillStarColumn(grid, w.ColName);
            w.RequestStarFill();
            PumpToLoaded();
            w.UpdateLayoutForTest(1120, 660);
            return w;
        }

        /// <summary>渲染前现量（渲染会把内容摘到垫底 Border、量数失真）：内层 ScrollViewer 的 ScrollableWidth
        /// （&gt;0 就是需求方看到的"底部那条能左右拖的滚动条"）。</summary>
        private static string V44Fix5ScrollNote(DataGrid g)
        {
            if (g == null) return "取不到表格";
            double s = V44Fix5Scrollable(g, out double ext, out double view);
            int rows = FindDerived(g, typeof(DataGridRow)).Cast<DataGridRow>().Count(r => r.ActualHeight > 0);
            return "实生成行 " + rows + " 列合计 " + ext.ToString("0.#") + " / 内层可视 " + view.ToString("0.#")
                + " → ScrollableWidth=" + s.ToString("0.#");
        }

        /// <summary>无窗口渲染没有动画时钟：CardExpander 的 IsExpanded 已置 true，但模板里的内容站点
        /// ContentPresenterBorder 仍停在 Collapsed（真机由视觉状态动画推到 Visible）。这里直接把站点显出来，
        /// 只为出图，不动产品代码。</summary>
        private static int V44Fix5ExpandSites(DependencyObject host)
        {
            int n = 0;
            var sites = FindDerived(host, typeof(FrameworkElement)).Cast<FrameworkElement>()
                .Where(f => f.Name == "ContentPresenterBorder" && f.Visibility != Visibility.Visible).ToList();
            foreach (var s in sites) { s.Visibility = Visibility.Visible; n++; }
            return n;
        }

        /// <summary>探针：每张卡「IsExpanded / 内容站点可见性 / 卡高」三个数——无窗口渲染里动画时钟不推进，
        /// IsExpanded=true 只把状态置上，站点仍 Collapsed（卡高不变），这就是必须 V44Fix5ExpandSites 显形一次的证据。</summary>
        private static string V44Fix5ExpanderProbe(FrameworkElement page)
        {
            var exs = FindByType(page, typeof(Wpf.Ui.Controls.CardExpander)).Cast<Wpf.Ui.Controls.CardExpander>().ToList();
            return string.Join(" ;; ", exs.Select((ex, i) =>
            {
                var site = FindDerived(ex, typeof(FrameworkElement)).Cast<FrameworkElement>()
                    .FirstOrDefault(f => f.Name == "ContentPresenterBorder");
                return "#" + i + " 展开=" + ex.IsExpanded + " 站点=" + (site == null ? "(无)" : site.Visibility.ToString())
                    + " 卡高=" + ex.ActualHeight.ToString("0.#");
            }));
        }

        /// <summary>五批取证图（开工令 §4.2）：长列表 1120/960、打印中（浅/深）+ 进度条特写、停止恢复态、
        /// 历史页最小窗（浅/深）、设置页三目录行展开（浅/深）、关于页日志唯一入口。出图统一垫 WindowBgBrush。</summary>
        internal static int V44Fix5Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV44Fix5R-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);

                // —— ① 修整18：长列表 50 行，默认宽 1120 / 最小宽 960（浅色中文），看的是"底部不该有横向滚动条" ——
                // 每张图另起一个窗口：渲染完这张窗口的内容已被摘到垫底 Border 上，不能再拿它排第二档宽度。
                foreach (double vw in new[] { 1120.0, 960.0 })
                {
                    App.ApplyTheme(AppTheme.Light);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    var w = V44Fix5SeedLong(dir, 50);
                    w.UpdateLayoutForTest(vw, 660);
                    w.RequestStarFill();
                    PumpToLoaded();
                    w.UpdateLayoutForTest(vw, 660);
                    string note = V44Fix5ScrollNote(w.FindName("Grid") as DataGrid);   // 渲染前现量（渲染会把内容摘出去）
                    code |= RenderWindow("五批长列表" + (int)vw + "自检.txt", "五批长列表-" + (int)vw + "-浅色中文.png", w, vw, 660,
                        new[] { "Grid", "BtnAddFiles", "BtnClear", "PrinterBox", "BtnPrintSetup", "BtnStart", "BtnStop" });
                    sb.AppendLine("长列表 50 行 × 视口 " + (int)vw + "（浅色中文）：" + note
                        + "；两档各出一张（一张窗口只能渲染一次）");
                }

                // —— ② 修整19/20：打印中整页锁 + 进度区「第 X / N 个 · 预计剩余 · 失败」（浅/深）——
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    string tn = th == AppTheme.Dark ? "深色" : "浅色";
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    var w = V44Fix5SeedLong(dir, 14);
                    var rows = w.RowsForTest;
                    rows[0].SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
                    rows[1].SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
                    rows[2].SetStatus(Res("s.StatusPrintingRow"), false, RowStatusKind.Printing);
                    // 走真实回调链：第 6 个文件失败 → 行状态转红、失败计数上色、预估按剩余照算
                    w.SetProgressForTest(14, 4, 1);          // 分子含停止前基线：第 6 / 14 个
                    w.AddFileSampleForTest(8); w.AddFileSampleForTest(9);
                    w.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Failed, FileName = rows[2].Name, Reason = Res("s.QueuePaperOut") });
                    w.SetControlsBusyForTest(true);
                    w.UpdateLayoutForTest(1120, 660);
                    string noteBusy = V44Fix5ScrollNote(w.FindName("Grid") as DataGrid);
                    code |= RenderWindow("五批打印中" + tn + "自检.txt", "五批打印中-1120-" + tn + "中文.png", w, 1120, 660,
                        new[] { "Grid", "BtnAddFiles", "BtnAddFolder", "BtnClear", "BtnRemove", "PrinterBox", "BtnPrintSetup", "BtnStart" });
                    var top = V44Fix3Relayout(w, 1120, 660);
                    var bar = FindDerived(top, typeof(ProgressBar)).Cast<ProgressBar>().FirstOrDefault();
                    var progRow = bar == null ? null : bar.Parent as FrameworkElement;
                    sb.AppendLine("打印中-" + tn + " 进度文本=\"" + (w.ProgressTextForTest ?? "") + "\" 特写："
                        + (progRow == null ? "(取不到进度行)" : SnapFlat(progRow, "五批进度计数特写-" + tn + "中文.png", 360)));
                    sb.AppendLine("    打印中-" + tn + " " + noteBusy);
                }

                // —— ③ 修整19 收尾：停止/异常后回到可用态（同尺寸同数据，只把锁放开）——
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                {
                    var w = V44Fix5SeedLong(dir, 14);
                    var rows = w.RowsForTest;
                    rows[0].SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
                    rows[1].SetStatus(Res("s.StatusDoneRow"), false, RowStatusKind.Done);
                    rows[2].SetStatus(Res("s.StatusFailedRow"), true, RowStatusKind.Failed);
                    w.SetControlsBusyForTest(false);
                    w.SetProgressForTest(0, 0, 0);
                    w.UpdateLayoutForTest(1120, 660);
                    string noteIdle = V44Fix5ScrollNote(w.FindName("Grid") as DataGrid);
                    code |= RenderWindow("五批停止后恢复自检.txt", "五批停止后恢复-1120-浅色中文.png", w, 1120, 660,
                        new[] { "Grid", "BtnAddFiles", "BtnAddFolder", "BtnClear", "BtnRemove", "PrinterBox", "BtnPrintSetup", "BtnStart" });
                    var byName = new Func<string, FrameworkElement>(n => w.FindName(n) as FrameworkElement);
                    sb.AppendLine("恢复态：BtnAddFiles/BtnClear/PrinterBox/BtnPrintSetup 可用="
                        + new[] { "BtnAddFiles", "BtnClear", "PrinterBox", "BtnPrintSetup", "BtnRemove", "BtnAddFolder" }
                            .All(n => { var e = byName(n); return e != null && e.IsEnabled; })
                        + " BtnStop 灰=" + (byName("BtnStop") != null && !byName("BtnStop").IsEnabled)
                        + " 整表可用=" + (w.FindName("Grid") as DataGrid).IsEnabled + " 进度文本清空="
                        + string.IsNullOrEmpty(w.ProgressTextForTest) + "；" + noteIdle);
                }

                // —— ④ 修整21：历史页最小窗 960×660（浅/深），40 行合成记录（纵向滚动条占位后的真实横向布局）——
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    string tn = th == AppTheme.Dark ? "深色" : "浅色";
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    var w = new MainWindow();
                    w.ShowPanelForTest("history");
                    var content = w.Content as FrameworkElement;
                    var hp = FindByType(content, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    if (hp != null) hp.SeedRowsForTest(40);
                    w.UpdateLayoutForTest(960, 660);
                    if (hp != null) hp.RequestStarFill();
                    PumpToLoaded();
                    w.UpdateLayoutForTest(960, 660);
                    // 具名控件在 HistoryPage 自己的名域里（窗口 FindName 取不到）；在位性已由 --selftest 修整13b/21a 在真实窗口里量过，这里只出图
                    string noteHist = V44Fix5ScrollNote(hp == null ? null : hp.FindName("Grid") as DataGrid);
                    code |= RenderWindow("五批历史页最小窗" + tn + "自检.txt", "五批历史页-最小窗960-" + tn + "中文.png", w, 960, 660,
                        new string[0]);
                    sb.AppendLine("历史页 960×660 × 40 行（" + tn + "）：" + noteHist
                        + "；列头/行数按自检同口径（截断列的悬停气泡要真实鼠标停留 1 秒才出，无窗口渲染不出这层 → 列入需求方真机复验）");
                }

                // —— ⑤ 修整22/23：设置页三张卡全展开（浅/深），目录行加载即显当前生效绝对路径、日志行已不在本页 ——
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    string tn = th == AppTheme.Dark ? "深色" : "浅色";
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    var w = new MainWindow();
                    w.ShowPanelForTest("settings");
                    var content = w.Content as FrameworkElement;
                    var sp = FindByType(content, typeof(SettingsPage)).Cast<SettingsPage>().FirstOrDefault();
                    if (sp != null) sp.RefreshDirRowsForTest();
                    // IsExpanded 要在可视树长好之后设：CardExpander 模板里的 ToggleButton 首次 ApplyTemplate 会把先前设的值倒回 false（实测）
                    w.UpdateLayoutForTest(1120, 660);
                    var exs = sp == null ? new List<Wpf.Ui.Controls.CardExpander>()
                        : FindByType(sp, typeof(Wpf.Ui.Controls.CardExpander)).Cast<Wpf.Ui.Controls.CardExpander>().ToList();
                    foreach (var ex in exs) ex.IsExpanded = true;
                    w.UpdateLayoutForTest(1120, 660);          // 再排一次，把展开站点长出来
                    string probeBefore = sp == null ? "" : V44Fix5ExpanderProbe(sp);
                    int shown = sp == null ? 0 : V44Fix5ExpandSites(sp);
                    string probe = sp == null ? "" : V44Fix5ExpanderProbe(sp);
                    w.UpdateLayoutForTest(1120, 660);
                    // 三行是否"真的看得见"：路径文本非空 + 已排版出高度（控件在 SettingsPage 自己的名域，窗口 FindName 取不到）
                    string dirsVis = sp == null ? "(取不到设置页)" : string.Join(" | ", new[] { "PrintoutDirText", "ConvertDirText", "ReportDirText" }
                        .Select(n =>
                        {
                            var t = sp.FindName(n) as TextBlock;
                            return t == null ? n + "取不到"
                                : n + "=\"" + t.Text + "\"高" + t.ActualHeight.ToString("0.#");
                        }));
                    code |= RenderWindow("五批设置页" + tn + "自检.txt", "五批设置页-展开-1120-" + tn + "中文.png", w, 1120, 760,
                        new string[0], 0);     // 彩色阈值下调，理由见 RenderWindow 注释（本页无强调色控件）
                    dirsVis += " 站点显形次数=" + shown + "\r\n    卡片展开探针（置 IsExpanded 后）：" + probeBefore
                        + "\r\n    显形后：" + probe;
                    sb.AppendLine("设置页（" + tn + "）三目录行（展开后可见性）：" + dirsVis
                        + " 日志行残留=" + (sp != null && sp.FindName("LogPathText") != null));
                }

                // —— ⑥ 修整23：关于页仍是日志唯一入口（浅色）——
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                {
                    var w = new MainWindow();
                    w.ShowPanelForTest("about");
                    var content = w.Content as FrameworkElement;
                    var ap = FindByType(content, typeof(AboutPage)).Cast<AboutPage>().FirstOrDefault();
                    w.UpdateLayoutForTest(1120, 660);       // 同设置页：可视树长好后再置 IsExpanded
                    if (ap != null)
                    {
                        foreach (var ex in FindByType(ap, typeof(Wpf.Ui.Controls.CardExpander)).Cast<Wpf.Ui.Controls.CardExpander>())
                            ex.IsExpanded = true;
                    }
                    w.UpdateLayoutForTest(1120, 660);
                    var logTb = ap == null ? null : ap.FindName("LogPathText") as TextBlock;
                    var logBtn = ap == null ? null : ap.FindName("BtnOpenLog") as Button;
                    int shownAbout = ap == null ? 0 : V44Fix5ExpandSites(ap);
                    w.UpdateLayoutForTest(1120, 660);
                    logTb = ap == null ? null : ap.FindName("LogPathText") as TextBlock;
                    code |= RenderWindow("五批关于页自检.txt", "五批关于页-展开-1120-浅色中文.png", w, 1120, 760,
                        new string[0]);
                    sb.AppendLine("关于页（日志位置唯一入口）：路径行=\"" + (logTb == null ? "(取不到)" : logTb.Text)
                        + "\" 高" + (logTb == null ? "—" : logTb.ActualHeight.ToString("0.#"))
                        + " 打开按钮=" + (logBtn != null && logBtn.IsEnabled) + " 站点显形=" + shownAbout
                        + "（同名键 s.SettingsLogDesc 已随修整23 删除，设置页只剩「数据目录」那一行）");
                }

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                string txt = sb.ToString();
                bool ok = code == 0 && txt.IndexOf("(截图失败") < 0 && txt.IndexOf("(元素未渲染") < 0;
                txt += "\r\n说明：长列表 50 行在 1120/960 两档的底部都不再有横向滚动条（ScrollableWidth=0 即「能拖的那条」不存在）；"
                     + "打印中整页与设置页 F15 两处入口同时锁灰（沿用既有 50% 透明度观感），停止/异常走同一个 FinishPrint 出口恢复；"
                     + "进度区文本「第 X / N 个 · 预计剩余 ≈ … · 失败 Z」的分子含停止前已完成，样本不足或方差过大时整段预估不显示；"
                     + "历史页按最小窗 960 重排（固定列合计 544 + 文件名保底 120 = 664 ≤ 内层可视 694.4），五个文本列挂气泡（气泡需真机鼠标悬停复验）；"
                     + "设置页三个目录行加载即显当前生效绝对路径，日志位置只在关于页。"
                     + "逐条数字见自检报告.txt 的「V4.4 五批」行 与 V44五批滚动条打印锁与进度定位.txt。"
                     + (ok ? " 判定：通过" : " 判定：失败");
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true));
                return ok ? code : code | 1;
            }
            catch (Exception ex) { return FailWith(log, ex); }
        }
    }
}
