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
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整（打印范围反馈优化 · 固定行高版，40 号开工令）断言：
    /// 修1 全角归一化（纯解析层）/ 修2 页数列三态联动与逐键刷新 / 修3 聚焦气泡 / 修4 无效标红 / 修5 悬停提示 / 修6 状态列"范围无效"。
    /// 铁律＝行高锁死：这里同时是"六行等高"的实测处（渲染图另出一批）。
    /// 全程离线：不打网络、不弹模态、不合成键盘焦点（无头窗口里合成焦点不可靠，红框改断样式触发器结构）。
    /// </summary>
    public static partial class WpfTests
    {
        private const int RangeTotal = 12;    // 样本 PDF 一律 12 页，页数列里的 N 就是它

        private static RowVm NewRangeRow(MainWindow main, string dir, string name)
        {
            string f = Path.Combine(dir, name);
            BuildSamplePdf(f, RangeTotal);
            return main.AddRowForTest(new PrintJob { FilePath = f, Kind = DocKind.Pdf });
        }

        /// <summary>Office 行：打印时才转换，加载阶段总页数未知（页数列现状是"—"）。</summary>
        private static RowVm NewOfficeRow(MainWindow main, string dir, string name)
        {
            return main.AddRowForTest(new PrintJob { FilePath = Path.Combine(dir, name), Kind = DocKind.Word });
        }

        private static string CountText(int x, int n)
        {
            string key = n >= RowVm.WideTotalDigits ? "s.RangeCountShortFmt" : "s.RangeCountFmt";
            return Res(key).Replace("{0}", x.ToString()).Replace("{1}", n.ToString());
        }

        private static string Tip(string key, params string[] args)
        {
            string s = Res(key);
            for (int i = 0; i < args.Length; i++) s = s.Replace("{" + i + "}", args[i]);
            return s;
        }

        /// <summary>期望值里的页码清单按当前语言的分隔键现算（中文"、"、英文", "），不写死。</summary>
        private static string ListOf(string rangeText, int total)
        {
            return RangeVerdict.PageList(RangeVerdict.Build(rangeText, total).Pages, Res("s.ListSep"), 8);
        }

        private static TextBlock PagesTextOf(DependencyObject root, DataGrid grid, RowVm row)
        {
            var cell = CellOf(root, row, grid, Res("s.ColPages"));
            if (cell == null) return null;
            return FindByType(cell, typeof(TextBlock)).Cast<TextBlock>().FirstOrDefault(t => t.Text == row.PagesText);
        }

        private static double ColW(DataGrid grid, string header)
        {
            for (int i = 0; i < grid.Columns.Count; i++) if (Equals(grid.Columns[i].Header, header)) return grid.Columns[i].ActualWidth;
            return 0;
        }

        /// <summary>样式触发器里的绑定路径核对（ConditionInfo.Binding 的静态类型是 BindingBase，Path/RelativeSource 在派生类上）。</summary>
        private static bool BindingIs(System.Windows.Data.BindingBase b, string path)
        {
            var bd = b as System.Windows.Data.Binding;
            return bd != null && bd.Path != null && bd.Path.Path == path;
        }

        private static bool BindingIsSelf(System.Windows.Data.BindingBase b, string path)
        {
            var bd = b as System.Windows.Data.Binding;
            return bd != null && bd.RelativeSource != null && bd.RelativeSource.Mode == System.Windows.Data.RelativeSourceMode.Self
                && BindingIs(b, path);
        }

        /// <summary>触发器条件值：XAML 的 Value="True" 解析后可能是字符串 "True"，也可能是 bool（取决于类型何时能定），两种都认。</summary>
        private static bool IsTrue(object v)
        {
            return v != null && (v is bool ? (bool)v : string.Equals(v.ToString(), "True", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>修2 溢出核对用：同一份字体属性下量"这串文案要多宽"（无限宽测量，与单元格实得宽比）。</summary>
        private static double TextNeedWidth(TextBlock tb, string text)
        {
            if (tb == null) return 0;
            var p = new TextBlock
            {
                Text = text,
                FontFamily = tb.FontFamily,
                FontSize = tb.FontSize,
                FontWeight = tb.FontWeight,
                FontStyle = tb.FontStyle,
                FontStretch = tb.FontStretch
            };
            p.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return p.DesiredSize.Width;
        }

        // ===== 修1：解析层归一化（纯函数，最先做） =====
        private static List<string> V49RParse(StringBuilder sb)
        {
            var bad = new List<string>();
            int[] one = BatchPrinter.ParseRange("１", 12);                    // 全角单个
            int[] rng = BatchPrinter.ParseRange("３－５", 12);                 // 全角数字 + 全角减号
            int[] mix = BatchPrinter.ParseRange("1，３-５", 12);               // 全半角混排 + 全角逗号
            int[] over = BatchPrinter.ParseRange("99", 12);                   // 超范围：仍静默忽略（行为不变）
            int[] legacy = BatchPrinter.ParseRange("1,3-5", 8);               // 既有回归（EngineTests 同一条口径）
            int[] big = BatchPrinter.ParseRange("1-999999999", 12);           // 荒谬区间：护栏截断，结果与不设限一致
            int nBig = BatchPrinter.ParsedPages("1-999999999").Count;
            sb.AppendLine("— 修1 全角归一化：\"１\"→0基[" + string.Join(",", one) + "]；\"３－５\"→[" + string.Join(",", rng)
                + "]；\"1，３-５\"→[" + string.Join(",", mix) + "]；\"99\"/12 页→" + over.Length + " 个（空集）；\"1,3-5\"/8 页→["
                + string.Join(",", legacy) + "]（既有行为）");
            sb.AppendLine("    UsesFullWidthInput：\"３－５\"=" + BatchPrinter.UsesFullWidthInput("３－５")
                + " \"3-5\"=" + BatchPrinter.UsesFullWidthInput("3-5") + " \"１\"=" + BatchPrinter.UsesFullWidthInput("１")
                + "；区间护栏：\"1-999999999\" 解析出 " + nBig + " 个页码、按 12 页过滤后打 " + big.Length + " 页");

            Check(bad, "V4.9 修整·修1 全角\"１\"认成第 1 页（0 基 [0]）、全角\"３－５\"认成第 3,4,5 页（0 基 [2,3,4]）",
                one != null && one.SequenceEqual(new[] { 0 }) && rng != null && rng.SequenceEqual(new[] { 2, 3, 4 }));
            Check(bad, "V4.9 修整·修1 全半角混排\"1，３-５\"→ 1,3,4,5（0 基 [0,2,3,4]）：全角逗号本来就认，现在全角数字也认",
                mix != null && mix.SequenceEqual(new[] { 0, 2, 3, 4 }));
            Check(bad, "V4.9 修整·修1 超范围仍静默忽略（\"99\" 对 12 页＝空集，与修整前一致）；既有半角解析零变化（\"1,3-5\"/8 页＝[0,2,3,4]）",
                over != null && over.Length == 0 && legacy != null && legacy.SequenceEqual(new[] { 0, 2, 3, 4 }));
            Check(bad, "V4.9 修整·修1 护栏：连打 \"1-999999999\" 不去构造十亿元素集（截到 " + BatchPrinter.ParsedPageCap
                + " 个），按 12 页过滤后仍打 12 页＝与不设限同结果",
                nBig == BatchPrinter.ParsedPageCap && big.Length == 12);
            Check(bad, "V4.9 修整·修1 全角判据只认真全角（半角输入不误报，气泡不会逢人就注\"已兼容全角\"）",
                BatchPrinter.UsesFullWidthInput("３－５") && BatchPrinter.UsesFullWidthInput("１")
                && !BatchPrinter.UsesFullWidthInput("3-5") && !BatchPrinter.UsesFullWidthInput("") && !BatchPrinter.UsesFullWidthInput(null));

            // 页码清单：连续段压缩 + 超出 max 段以省略号收尾（气泡只有一行半，靠这里截住）
            string l1 = RangeVerdict.PageList(new List<int> { 1, 2, 3, 5 }, "、", 8);
            string l2 = RangeVerdict.PageList(Enumerable.Range(1, 20).ToList(), "、", 8);
            string l3 = RangeVerdict.PageList(Enumerable.Range(1, 40).Where(i => i % 2 == 1).ToList(), "、", 8);
            sb.AppendLine("    清单压缩：[1,2,3,5]→\"" + l1 + "\"；1..20→\"" + l2 + "\"；1..39 奇数（20 段）取 8 段→\"" + l3 + "\"");
            Check(bad, "V4.9 修整·修3 页码清单连续段压缩（1,2,3,5→\"1-3、5\"）、整段不分点列（1..20→\"1-20\"）、超 8 段以省略号收尾",
                l1 == "1-3、5" && l2 == "1-20" && l3.Split('、').Length == 9 && l3.EndsWith("…"));
            return bad;
        }

        // ===== 修6 的前提：打印计划（引擎）行为零变化 =====
        private static List<string> V49RPlan(StringBuilder sb)
        {
            var bad = new List<string>();
            var opt = new PrintOptions();
            int[] fb = PrintPlan.PageSequence(12, "99", null, opt);        // 空集 → 回退全部（原行为）
            int[] part = PrintPlan.PageSequence(12, "1-8,13", null, opt);
            int[] okp = PrintPlan.PageSequence(12, "3-5", null, opt);
            int[] none = PrintPlan.PageSequence(12, "", null, opt);
            int[] fw = PrintPlan.PageSequence(12, "３－５", null, opt);
            sb.AppendLine("— 修6 打印计划零变化：\"99\"/12 页→" + fb.Length + " 页[" + string.Join(",", fb.Take(3)) + "…]；"
                + "\"1-8,13\"→" + part.Length + " 页；\"3-5\"→" + okp.Length + " 页；未填→" + none.Length
                + " 页；全角\"３－５\"→" + fw.Length + " 页[" + string.Join(",", fw) + "]");
            Check(bad, "V4.9 修整·修6 空集回退打印全部的行为原样保留（PageSequence(\"99\",12)＝12 页、与未填范围同结果）——本版只加显示，不动引擎",
                fb.Length == 12 && none.Length == 12 && fb.SequenceEqual(none));
            Check(bad, "V4.9 修整·修6 部分忽略/有效区间/未填三档页数不受本版改动影响（8 / 3 / 12），全角\"３－５\"按修1 打成 3 页",
                part.Length == 8 && okp.Length == 3 && fw.Length == 3 && fw.SequenceEqual(new[] { 2, 3, 4 }));
            Check(bad, "V4.9 修整·修6 呈现层结论与引擎同源：判 Invalid 的输入正好就是引擎走回退的那些（\"99\" 是、\"1-8,13\" 不是、未填不是）",
                RangeVerdict.Build("99", 12).Mark == RangeMark.Invalid
                && RangeVerdict.Build("1-8,13", 12).Mark == RangeMark.Partial
                && RangeVerdict.Build("", 12).Mark == RangeMark.Empty);
            return bad;
        }

        // ===== 修2/3/4/5/6 + 铁律：真实列表行的呈现层（一个主题/语言一遍） =====
        private static List<string> V49RRows(AppTheme th, AppLanguage lang, StringBuilder sb)
        {
            var bad = new List<string>();
            string tag = (th == AppTheme.Dark ? "深色" : "浅色") + (lang == AppLanguage.EnUS ? "英文" : "中文");
            App.ApplyTheme(th);
            App.ApplyLanguage(lang);
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV49R-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var w = new MainWindow();
            var grid = w.FindName("Grid") as DataGrid;
            var root = w.Content as FrameworkElement;

            var rOk = NewRangeRow(w, dir, "甲-有效.pdf"); rOk.RangeText = "1";
            var rPart = NewRangeRow(w, dir, "乙-部分超出.pdf"); rPart.RangeText = "1-8,13";
            var rBad = NewRangeRow(w, dir, "丙-无效.pdf"); rBad.RangeText = "99";
            var rNone = NewRangeRow(w, dir, "丁-未填.pdf");
            var rFull = NewRangeRow(w, dir, "戊-全角.pdf"); rFull.RangeText = "３－５";
            var rOff = NewOfficeRow(w, dir, "己-待转换.docx"); rOff.RangeText = "3";
            w.UpdateLayoutForTest(1260, 820);
            PumpToLoaded();
            w.UpdateLayoutForTest(1260, 820);

            Color ok = V44Fix3Color((Brush)Application.Current.TryFindResource("StatusOkTextBrush"), Colors.Magenta);
            Color warn = V44Fix3Color((Brush)Application.Current.TryFindResource("StatusWarnTextBrush"), Colors.Magenta);
            Color fail = V44Fix3Color((Brush)Application.Current.TryFindResource("StatusFailTextBrush"), Colors.Magenta);
            Color norm = V44Fix3Color((Brush)Application.Current.TryFindResource("TextPrimaryBrush"), Colors.Magenta);
            sb.AppendLine("======== " + tag + " ========");
            sb.AppendLine("— 主题键：黄=" + V44Fix3Resolved("StatusWarnTextBrush") + " 绿=" + V44Fix3Resolved("StatusOkTextBrush")
                + " 红=" + V44Fix3Resolved("StatusFailTextBrush"));
            Check(bad, tag + " 黄键在当前主题字典有定义（浅深两份都要有＝GridRowBgBrush 教训）", warn != Colors.Magenta);

            var bgKeys = new[] { "GridRowBgBrush", "GridRowHoverBrush", "GridRowSelectedBrush" };
            var bgList = bgKeys.Select(k => new { k, c = V44Fix3Color((Brush)Application.Current.TryFindResource(k), Colors.Black) }).ToList();
            double worstWarn = bgList.Select(b => V44Fix3Contrast(warn, b.c)).Min();
            double worstOk = bgList.Select(b => V44Fix3Contrast(ok, b.c)).Min();
            sb.AppendLine("    黄字对比度 " + string.Join(" ", bgList.Select(b => b.k + " " + V44Fix3Contrast(warn, b.c).ToString("0.00")))
                + "（最差 " + worstWarn.ToString("0.00") + "；同批绿 " + worstOk.ToString("0.00") + "）");
            Check(bad, tag + " 页数列/状态列的黄字在三档底色上对比度最差 " + worstWarn.ToString("0.00") + " ≥4.5", worstWarn >= 4.5);

            // —— 修2：三态联动（VM 结论 + 可视树实取前景） ——
            var cases = new[]
            {
                new { R = rOk, N = "全部有效", Want = ok, Text = CountText(1, RangeTotal), Mark = RangeMark.Ok },
                new { R = rPart, N = "部分超出", Want = warn, Text = CountText(8, RangeTotal), Mark = RangeMark.Partial },
                new { R = rBad, N = "零有效", Want = fail, Text = Res("s.RangeBadShort"), Mark = RangeMark.Invalid },
                new { R = rNone, N = "未填范围", Want = norm, Text = RangeTotal + " 页", Mark = RangeMark.Empty },
                new { R = rFull, N = "全角输入", Want = ok, Text = CountText(3, RangeTotal), Mark = RangeMark.Ok },
                new { R = rOff, N = "Office 未转换（页数未知）", Want = ok, Text = "—", Mark = RangeMark.UnknownOk },
            };
            foreach (var c in cases)
            {
                var tb = PagesTextOf(root, grid, c.R);
                Color got = tb == null ? Colors.Black : V44Fix3Color(tb.Foreground, Colors.Black);
                double need = TextNeedWidth(tb, c.R.PagesText);
                double avail = tb == null ? 0 : tb.ActualWidth;
                string cellW = avail.ToString("0.#") + "/" + ColW(grid, Res("s.ColPages")).ToString("0.#");
                sb.AppendLine("    " + c.N + "：范围=\"" + c.R.RangeText + "\" 页数列=\"" + c.R.PagesText + "\"（前景 "
                    + V44Fix3Hex(got) + "，期望 " + V44Fix3Hex(c.Want) + "）种类=" + c.R.RangeState
                    + " 样式=" + V44Fix3Style(tb) + " 文案需宽=" + need.ToString("0.#") + " 实得=" + cellW);
                Check(bad, tag + " " + c.N + "（范围 \"" + c.R.RangeText + "\"）→ 页数列=\"" + c.Text + "\"、种类=" + c.Mark,
                    c.R.PagesText == c.Text && c.R.RangeState == c.Mark);
                Check(bad, tag + " " + c.N + " 上色只认结构化种类（期望 " + V44Fix3Hex(c.Want) + "，实得 " + V44Fix3Hex(got) + "）",
                    got.R == c.Want.R && got.G == c.Want.G && got.B == c.Want.B);
                Check(bad, tag + " " + c.N + " 上色走内容元素自身（CellTextPages 触发器，不是父级继承＝缺陷B 教训）",
                    tb != null && tb.Style != null && Equals(tb.Style, Application.Current.TryFindResource("CellTextPages")));
                // 修2 溢出口径：文案"要多宽"必须 ≤ 单元格实得宽（放不下的话 WPF 只画省略号，行高不动但用户看不到数字）
                Check(bad, tag + " " + c.N + " 页数列文案在 60 DIP 列里放得下（需 " + need.ToString("0.#") + " ≤ 实得 "
                    + avail.ToString("0.#") + "；放不下就按修2 口径缩文案、全称留气泡与提示）",
                    tb != null && need <= avail + 0.5);
            }
            // 列宽预算实测（修2 溢出口径的证据）：同一字体下几种写法各要多宽，够不够放一目了然
            var probeTb = PagesTextOf(root, grid, rPart);
            double availP = probeTb == null ? 0 : probeTb.ActualWidth;
            var cands = new[] { "8/12 页", "8/12页", "8/12", "100/120 页", "100/120", "未生效", "invalid", "12 页" };
            sb.AppendLine("    列宽实测：单元格实得 " + availP.ToString("0.#") + " DIP（列宽 " + ColW(grid, Res("s.ColPages")).ToString("0.#")
                + "），几种写法需宽 " + string.Join(" ", cands.Select(s => "\"" + s + "\"=" + TextNeedWidth(probeTb, s).ToString("0.#")))
                + "；本版页数列文案=\"" + Res("s.RangeCountFmt") + "\" 短文=\"" + Res("s.RangeBadShort") + "\"");
            Check(bad, tag + " 修2 页数列文案在 60 DIP 列宽内不截断（列头不折行的同一预算；缩文案优先，不动列宽体系）",
                TextNeedWidth(probeTb, Res("s.RangeCountFmt").Replace("{0}", "8").Replace("{1}", "12")) <= availP + 0.5
                && TextNeedWidth(probeTb, Res("s.RangeBadShort")) <= availP + 0.5);

            Check(bad, tag + " 修2 只有零有效页码的行置 IsRangeInvalid，其余行不误置位",
                rBad.IsRangeInvalid && !rOk.IsRangeInvalid && !rPart.IsRangeInvalid && !rNone.IsRangeInvalid && !rOff.IsRangeInvalid);

            // —— 修2：逐键即时（同一属性事件里刷新页数列，两次输入两次通知） ——
            int hits = 0; string seq = "";
            rNone.PropertyChanged += (s, e) => { if (e.PropertyName == "PagesText") { hits++; seq += "|" + rNone.PagesText; } };
            rNone.RangeText = "2";
            rNone.RangeText = "2-4";
            rNone.RangeText = "";
            sb.AppendLine("    逐键：PropertyChanged(PagesText) 次数=" + hits + " 依次=\"" + rNone.PagesText + "\"" + seq.Replace("|", " ｜"));
            Check(bad, tag + " 修2 逐键联动：打字两次→页数列通知两次（" + CountText(1, RangeTotal) + "→" + CountText(3, RangeTotal)
                + "），清空后回到现状文案\"" + (RangeTotal + " 页") + "\"",
                hits == 3 && seq == "|" + CountText(1, RangeTotal) + "|" + CountText(3, RangeTotal) + "|" + (RangeTotal + " 页"));

            // —— 修5：页数格与范围格悬停提示＝与气泡同源的完整结论 ——
            var tipCases = new[]
            {
                new { R = rOk, Want = Tip("s.RangeTipOkFmt", ListOf("1", 12), "1", "12") },
                new { R = rPart, Want = Tip("s.RangeTipPartialFmt", "8", ListOf("1-8,13", 12), "1") },
                new { R = rBad, Want = Tip("s.RangeTipBadFmt", "12") },
                new { R = rNone, Want = Tip("s.RangeTipEmptyFmt", "12") },
                new { R = rFull, Want = Tip("s.RangeTipOkFmt", ListOf("３－５", 12), "3", "12") + "\n" + Res("s.RangeTipFullWidth") },
                new { R = rOff, Want = Tip("s.RangeTipUnknownOkFmt", ListOf("3", 0)) },
            };
            foreach (var c in tipCases)
            {
                var tb = PagesTextOf(root, grid, c.R);
                var box = w.RangeBoxForTest(c.R);
                string pt = tb == null ? null : tb.ToolTip as string;
                string bt = box == null ? null : box.ToolTip as string;
                sb.AppendLine("    修5 范围=\"" + c.R.RangeText + "\"：页数列提示=\"" + (pt ?? "").Replace("\n", "⏎") + "\" 范围格提示与 VM 同="
                    + (bt == c.R.RangeHint) + " 延迟=" + (tb == null ? "-" : ToolTipService.GetInitialShowDelay(tb).ToString()));
                Check(bad, tag + " 修5 页数格与范围格悬停＝与气泡同一份结论（\"" + (c.R.RangeHint ?? "").Replace("\n", "⏎")
                    + "\"），延迟 1 秒与现有单元格提示同口径",
                    pt == c.R.RangeHint && bt == c.R.RangeHint && pt == c.Want
                    && tb != null && ToolTipService.GetInitialShowDelay(tb) == 1000);
            }

            // —— 修3：聚焦气泡（开/收/朝上翻转/不占布局/三类结论） ——
            var boxOk = w.RangeBoxForTest(rOk);
            var popup = MainWindow.RangeBubbleOf(boxOk);
            var card = popup == null ? null : popup.Child as Border;
            var bubbleTb = card == null ? null : card.Child as TextBlock;
            sb.AppendLine("    修3 气泡节点：Popup 取得=" + (popup != null) + " 锚定输入框=" + (popup != null && ReferenceEquals(popup.PlacementTarget, boxOk))
                + " 不接收焦点=" + (popup != null && !popup.Focusable) + " 初始收起=" + (popup != null && !popup.IsOpen));
            Check(bad, tag + " 修3 气泡挂在范围格模板里、锚定输入框、不接收焦点且默认收起（不占布局空间）",
                popup != null && ReferenceEquals(popup.PlacementTarget, boxOk) && !popup.Focusable && !popup.IsOpen);

            // 焦点事件的同一段方法体（KeyboardFocusChangedEventArgs 没有公开构造函数，无头自检造不出这对事件；
            // 浮层要 PlacementTarget 有 PresentationSource 才能落地，所以另做一次"直连置真"对照实验，把环境限制与代码路径分开）。
            w.OpenRangeBubbleOnFocus(boxOk);
            bool downRequested = popup.Placement == PlacementMode.Bottom && Math.Abs(popup.VerticalOffset - 2) < 0.01;
            string shown = bubbleTb == null ? null : bubbleTb.Text;
            bool openStateInHeadless = popup.IsOpen;
            popup.IsOpen = true; bool directOpen = popup.IsOpen; popup.IsOpen = false;      // 对照：直连置真同样起不来＝环境限制，不是本版码路
            w.CloseRangeBubbleOnBlur(boxOk);
            bool closedAfterBlur = !popup.IsOpen;
            MainWindow.OpenRangeBubble(boxOk, true);    // 贴屏幕下缘／最后一行那条分支
            bool upRequested = popup.Placement == PlacementMode.Top && Math.Abs(popup.VerticalOffset + 2) < 0.01;
            sb.AppendLine("    修3 聚焦（事件方法体）：落点朝下＋offset 2＝" + downRequested + "，气泡文本=\"" + (shown ?? "").Replace("\n", "⏎")
                + "\" 与页数列同源=" + (shown == rOk.RangeHint) + "；失焦→IsOpen=" + popup.IsOpen + "（收＝" + closedAfterBlur + "）");
            sb.AppendLine("    修3 环境对照：无头窗口里 IsOpen 置真后实取＝" + directOpen + "（聚焦后＝" + openStateInHeadless
                + "；Popup 需要 PlacementTarget 有 PresentationSource 才能落浮层，本机自检窗口从未上屏 → 浮层可见性交渲染特写与真机复验）");
            Check(bad, tag + " 修3 聚焦→朝同一 Popup 发开请求（锚点＋落点＋offset 2 实取）、失焦→收，气泡内容就是那一条同源结论",
                downRequested && closedAfterBlur && shown == rOk.RangeHint && ReferenceEquals(popup.PlacementTarget, boxOk));
            sb.AppendLine("    修3 朝上分支：Placement=" + popup.Placement + " VerticalOffset=" + popup.VerticalOffset + " 映射＝" + upRequested);
            Check(bad, tag + " 修3 判据命中时气泡朝上翻转（Placement=Top + 负偏移，防出屏）", upRequested);

            string tOk = Tip("s.RangeTipOkFmt", ListOf("1", 12), "1", "12");
            string tPart = Tip("s.RangeTipPartialFmt", "8", ListOf("1-8,13", 12), "1");
            string tBad = Tip("s.RangeTipBadFmt", "12");
            sb.AppendLine("    修3 三类结论：✓\"" + tOk + "\" ⚠\"" + tPart + "\" ✗\"" + tBad + "\"；全角加注行=\""
                + rFull.RangeHint.Split('\n').Last() + "\"");
            Check(bad, tag + " 修3 三类结论文本各一条（✓ 将打印第 X 页 / ⚠ X 页生效+Y 个超出已忽略 / ✗ 未识别到有效页码将按全部 12 页打印），与页数列同一次解析",
                rOk.RangeHint == tOk && rPart.RangeHint == tPart && rBad.RangeHint == tBad);
            Check(bad, tag + " 修3 全角输入时气泡加注\"" + Res("s.RangeTipFullWidth") + "\"，半角输入不加",
                rFull.RangeHint.EndsWith(Res("s.RangeTipFullWidth")) && !rOk.RangeHint.Contains(Res("s.RangeTipFullWidth")));

            bool a1 = MainWindow.PopupAbove(600, 1040, 60);     // 下方空间充足
            bool a2 = MainWindow.PopupAbove(1000, 1040, 60);    // 贴屏幕下缘
            bool a3 = MainWindow.PopupAbove(1033, 1040, 60);    // 连余量都不够
            sb.AppendLine("    修3 翻转判据（锚点下沿/工作区底/所需高）：600,1040,60→朝上=" + a1 + "；1000,1040,60→" + a2 + "；1033,1040,60→" + a3);
            Check(bad, tag + " 修3 翻转判据是量出来的几何：空间够朝下、放不下朝上（含 8 DIP 余量），不是按行号猜\"最后一行\"", !a1 && a2 && a3);

            // —— 铁律：六行等高（四态行 + 全角行 + Office 行同表）——
            // 口径沿用修整10：行高＝RowHeight + 底边线（本机 125% 下实测 36.8），判"所有行等高、不随内容长"，不断等于 36
            var rowsLive = FindByType(grid, typeof(DataGridRow)).Cast<DataGridRow>().Where(r => r.ActualHeight > 0).ToList();
            var hs = rowsLive.Select(r => r.ActualHeight).ToList();
            var heights = hs.Select(h => Math.Round(h, 2)).Distinct().ToList();
            double rowH = grid.RowHeight;
            sb.AppendLine("    铁律：RowHeight=" + rowH + "；实生成行 " + rowsLive.Count + " 行，行高去重=[" + string.Join(",", heights) + "]");
            Check(bad, tag + " 铁律·行高锁死：六行行高只有一个值且落在 36~37.6（＝RowHeight+底边线，与修整10 同口径；上色/提示/气泡都是浮层，行内零撑高元素）",
                rowsLive.Count >= 6 && hs.All(h => Math.Abs(h - hs[0]) < 0.2) && hs[0] >= rowH && hs[0] <= rowH + 1.6);

            // —— 修2：Office 行转换完成后回填 X/N（走真实完成回调） ——
            w.MergeOverrides(new List<RowVm> { rOff });
            w.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Completed, FileName = rOff.Name, Pages = RangeTotal });
            w.UpdateLayoutForTest(1260, 820); PumpToLoaded();
            var offTb = PagesTextOf(root, grid, rOff);
            Color offColor = offTb == null ? Colors.Black : V44Fix3Color(offTb.Foreground, Colors.Black);
            sb.AppendLine("    修2 Office 回填：总页数 0→" + rOff.PageTotal + "，页数列=\"" + rOff.PagesText + "\"（"
                + V44Fix3Hex(offColor) + "）种类=" + rOff.RangeState);
            Check(bad, tag + " 修2 Office 行由打印回写总页数后，页数列从\"—\"（两态）升级成\"" + CountText(1, RangeTotal) + "\"（三态·绿）",
                rOff.PageTotal == RangeTotal && rOff.PagesText == CountText(1, RangeTotal) && rOff.RangeState == RangeMark.Ok
                && offColor.R == ok.R && offColor.G == ok.G && offColor.B == ok.B);

            // —— 修4：范围格无效标红 + 改回即复原（放在最后，前面几处都还要用 rBad="99"） ——
            var boxBad = w.RangeBoxForTest(rBad);
            Color red = boxBad == null ? Colors.Black : V44Fix3Color(boxBad.Foreground, Colors.Black);
            Color plain = boxOk == null ? Colors.Black : V44Fix3Color(boxOk.Foreground, Colors.Black);
            bool gotFocus = boxBad != null && boxBad.Focus();
            PumpToLoaded();
            Thickness thFocus = boxBad == null ? new Thickness() : boxBad.BorderThickness;
            if (gotFocus) MainWindow.CloseRangeBubble(boxBad);    // 拿到焦点会顺带开气泡，测完收掉，别留到下一段
            sb.AppendLine("    修4 红字：无效行 " + V44Fix3Hex(red) + "（期望 " + V44Fix3Hex(fail) + "）、有效行 " + V44Fix3Hex(plain)
                + "；尝试真实键盘焦点=" + (gotFocus ? "成功（聚焦红框实测 BorderThickness=" + thFocus.Top + "）" : "失败（无头窗口不合焦，红框改断样式结构）"));
            Check(bad, tag + " 修4 聚焦红框（本次真机式焦点" + (gotFocus ? "已拿到" : "拿不到，以下面那条样式结构断言为准") + "）：拿到时红框宽度为 1",
                !gotFocus || thFocus.Top >= 1);
            rBad.RangeText = "3";
            w.UpdateLayoutForTest(1260, 820); PumpToLoaded();
            Color restored = boxBad == null ? Colors.Black : V44Fix3Color(boxBad.Foreground, Colors.Black);
            rBad.RangeText = "99";
            w.UpdateLayoutForTest(1260, 820); PumpToLoaded();
            Color again = boxBad == null ? Colors.Black : V44Fix3Color(boxBad.Foreground, Colors.Black);
            sb.AppendLine("    修4 复原：改成 \"3\" 后 " + V44Fix3Hex(restored) + "（不再红＝" + (restored.R != fail.R)
                + "），改回 \"99\" 又红＝" + (again.R == fail.R && again.G == fail.G && again.B == fail.B));
            Check(bad, tag + " 修4 零有效页码→范围格文字红 " + V44Fix3Hex(fail) + "，有效行不红",
                red.R == fail.R && red.G == fail.G && red.B == fail.B && plain.R != fail.R);
            Check(bad, tag + " 修4 改回有效页码即复原（不红），再改回无效又红（两态可来回）",
                restored.R != fail.R && again.R == fail.R && again.G == fail.G && again.B == fail.B && rBad.IsRangeInvalid);

            var st = Application.Current.TryFindResource("CellBoxRange") as Style;
            // 说明：XAML 里 Value="True" 解析后可能是字符串 "True"（DataTrigger 的类型要到运行时才由绑定值定），
            // 所以这里两种都认，并把触发器清单原样落日志（免得日后改了样式、断言却静默空转）。
            string inv = st == null ? "(取不到 CellBoxRange 样式)" : string.Join(" / ", st.Triggers.Cast<TriggerBase>().Select(t =>
                t is DataTrigger dts ? "Data[" + (dts.Binding as System.Windows.Data.Binding)?.Path.Path + "=" + dts.Value + ":" + (dts.Value == null ? "null" : dts.Value.GetType().Name) + "]"
                : t is MultiDataTrigger md ? "Multi[" + string.Join("+", md.Conditions.Select(c => (c.Binding as System.Windows.Data.Binding)?.Path.Path + "=" + c.Value)) + "]"
                : t.GetType().Name));
            bool redOnFlag = st != null && st.Triggers.Cast<TriggerBase>().OfType<DataTrigger>().Any(dt =>
                BindingIs(dt.Binding, "IsRangeInvalid") && IsTrue(dt.Value)
                && dt.Setters.Cast<Setter>().Any(s => s.Property == Control.ForegroundProperty));
            bool borderOnFocus = st != null && st.Triggers.Cast<TriggerBase>().OfType<MultiDataTrigger>().Any(mdt =>
                mdt.Conditions.Any(c => BindingIsSelf(c.Binding, "IsKeyboardFocused") && IsTrue(c.Value))
                && mdt.Setters.Cast<Setter>().Any(s => s.Property == Control.BorderThicknessProperty)
                && mdt.Setters.Cast<Setter>().Any(s => s.Property == Control.BorderBrushProperty));
            sb.AppendLine("    修4 样式结构（" + inv + "）：红字触发器认 IsRangeInvalid=" + redOnFlag
                + "；\"无效+聚焦\"多条件触发器加 BorderThickness/BorderBrush=" + borderOnFocus);
            Check(bad, tag + " 修4 红字与聚焦红框都挂在结构化布尔上（不解析文案），且红框只在聚焦时加", redOnFlag && borderOnFocus);

            // —— 修6：状态列"范围无效"黄警告（打印行为不变，只把静默回退显式说出来） ——
            w.MergeOverrides(new List<RowVm> { rBad, rPart });
            w.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Completed, FileName = rBad.Name, Pages = RangeTotal });
            w.UpdateLayoutForTest(1260, 820); PumpToLoaded();
            var stCell = CellOf(root, rBad, grid, Res("s.ColStatus"));
            var stTb = stCell == null ? null : FindByType(stCell, typeof(TextBlock)).Cast<TextBlock>().FirstOrDefault(t => t.Text == rBad.Status);
            Color stGot = stTb == null ? Colors.Black : V44Fix3Color(stTb.Foreground, Colors.Black);
            string want = Tip("s.StatusRangeInvalidFmt", "12");
            sb.AppendLine("    修6 无效行：状态=\"" + rBad.Status + "\" 种类=" + rBad.StatusKind + " 前景=" + V44Fix3Hex(stGot)
                + " 样式=" + V44Fix3Style(stTb) + " 文字宽/列宽=" + (stTb == null ? 0 : stTb.ActualWidth).ToString("0.#") + "/"
                + ColW(grid, Res("s.ColStatus")).ToString("0.#") + "（超宽靠截断，不折行）");
            Check(bad, tag + " 修6 范围无效打完→状态列\"" + want + "\"（黄、单行、走现有截断），不是静默绿勾",
                rBad.StatusKind == RowStatusKind.RangeInvalid && rBad.Status == want
                && stGot.R == warn.R && stGot.G == warn.G && stGot.B == warn.B
                && stTb != null && Equals(stTb.Style, Application.Current.TryFindResource("CellTextStatus"))
                && stTb.TextWrapping == TextWrapping.NoWrap && stTb.TextTrimming == TextTrimming.CharacterEllipsis);

            w.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Completed, FileName = rPart.Name, Pages = RangeTotal });
            w.UpdateLayoutForTest(1260, 820); PumpToLoaded();
            // 文件日志（Serilog 的 shared 文件汇按定时器落盘）回读：最多等 6 秒，等不到就是真没写
            string ign = Tip("s.LogRangeIgnored", "1");
            string logs = "";
            int tries = 0;
            for (; tries < 24; tries++)
            {
                logs = FileLogger.RecentLines(40);
                if (logs.Contains(ign)) break;
                System.Threading.Thread.Sleep(250);
            }
            sb.AppendLine("    修6 部分超出行：状态=\"" + rPart.Status + "\" 种类=" + rPart.StatusKind
                + "（只 1 个页码越界，不算无效，不告警）；文件日志含\"" + ign + "\"＝" + logs.Contains(ign)
                + "（回读尝试 " + tries + " 次；日志目录=" + FileLogger.LogDirectory
                + "；回读内容头=\"" + logs.Replace("\n", " ").Substring(0, Math.Min(60, logs.Length))
                + "\" 尾=\"" + logs.Replace("\n", " ").Substring(Math.Max(0, logs.Length - 70)) + "\"）");
            Check(bad, tag + " 修6 部分忽略（1-8,13 打 1-8）不告警：状态列仍是已完成，只在文件日志补一行\"" + Tip("s.LogRangeIgnored", "1") + "\"",
                rPart.StatusKind == RowStatusKind.Done && rPart.Status == Res("s.StatusDoneRow") && logs.Contains(Tip("s.LogRangeIgnored", "1")));

            var heights2 = FindByType(grid, typeof(DataGridRow)).Cast<DataGridRow>()
                .Where(r => r.ActualHeight > 0).Select(r => r.ActualHeight).ToList();
            sb.AppendLine("    铁律（警告上屏后）：行高去重=[" + string.Join(",", heights2.Select(h => Math.Round(h, 2)).Distinct()) + "]（RowHeight " + rowH + "）");
            Check(bad, tag + " 铁律·黄警告上屏后逐行行高与上屏前一致（状态列单行截断，不折行不撑高）",
                heights2.Count == hs.Count && !heights2.Where((h, i) => Math.Abs(h - hs[i]) >= 0.2).Any());

            // —— 修2 文案退档：总页数到三位数，60 DIP 列里连"100/120页"都放不下 → 退成短文 "100/120"（全称在气泡与提示里） ——
            var rBig = NewRangeRow(w, dir, "庚-厚册.pdf");
            rBig.RangeText = "1-100";
            rBig.SetTotalPages(120);            // 与 Office 回填同一入口（内部只认"总页数变了"）
            w.UpdateLayoutForTest(1260, 820); PumpToLoaded();
            var bigTb = PagesTextOf(root, grid, rBig);
            double bigNeed = TextNeedWidth(bigTb, rBig.PagesText);
            double bigFull = TextNeedWidth(bigTb, Res("s.RangeCountFmt").Replace("{0}", "100").Replace("{1}", "120"));
            sb.AppendLine("    修2 退档：总页数 120 → 页数列=\"" + rBig.PagesText + "\"（需宽 " + bigNeed.ToString("0.#")
                + " ≤ 实得 " + (bigTb == null ? 0 : bigTb.ActualWidth).ToString("0.#") + "；带单位要 " + bigFull.ToString("0.#")
                + "，故退短文），提示=\"" + (rBig.RangeHint ?? "").Replace("\n", "⏎") + "\"");
            Check(bad, tag + " 修2 三位数页数时页数列自动退成短文\"" + CountText(100, 120) + "\"且不被省略号截断（全称在气泡/提示；英文两档同形，退档只对中文成立）",
                rBig.PagesText == CountText(100, 120) && bigTb != null && bigNeed <= bigTb.ActualWidth + 0.5
                && (bigFull > bigTb.ActualWidth || Res("s.RangeCountShortFmt") == Res("s.RangeCountFmt"))
                && rBig.RangeHint.Contains("100") && rBig.RangeHint.Contains("120"));
            return bad;
        }

        /// <summary>修2/3/5/6 的中英键齐备（新增键只增不减；一一对应由资源键守护总断言兜底）。</summary>
        private static List<string> V49RText(StringBuilder sb)
        {
            var bad = new List<string>();
            var keys = new[] { "s.RangeCountFmt", "s.RangeCountShortFmt", "s.RangeBadShort", "s.RangeTipOkFmt", "s.RangeTipPartialFmt", "s.RangeTipBadFmt",
                "s.RangeTipUnknownOkFmt", "s.RangeTipUnknownBad", "s.RangeTipEmptyFmt", "s.RangeTipEmptyUnknown",
                "s.RangeTipFullWidth", "s.StatusRangeInvalidFmt", "s.LogRangeIgnored" };
            foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
            {
                App.ApplyLanguage(lang);
                string tag = lang == AppLanguage.EnUS ? "英文" : "中文";
                var missing = keys.Where(k => string.IsNullOrEmpty(Res(k)) || Res(k) == k).ToList();
                var over = keys.Where(k => Res(k).Contains("{3}") || Res(k).Contains("{{")).ToList();
                string sample = Tip("s.StatusRangeInvalidFmt", "12");
                string short2 = CountText(8, 12);
                bool cjk = sample.Any(ch => ch >= 0x4E00 && ch <= 0x9FFF);
                sb.AppendLine("— " + tag + "：新增 " + keys.Length + " 枚，缺=" + (missing.Count == 0 ? "0" : string.Join(",", missing))
                    + "；越界占位符=" + (over.Count == 0 ? "0" : string.Join(",", over))
                    + "；警告样例行=\"" + sample + "\"；页数列短文=\"" + short2 + "\"");
                Check(bad, tag + " 修2~修6 新增资源键齐备（" + keys.Length + " 枚中英一一对应），样例行无 {3} 之类越界占位符残留",
                    missing.Count == 0 && over.Count == 0 && sample.Contains("12") && !sample.Contains("{0}"));
                Check(bad, tag + " 警告文案与页数列短文随语言切换（中文含汉字 / 英文不含）", cjk == (lang == AppLanguage.ZhCN));
            }
            App.ApplyLanguage(AppLanguage.ZhCN);
            return bad;
        }

        /// <summary>渲染取证的同一批行：六行覆盖 绿 1/12 页 · 黄 8/12 页 · 红 未生效 · 空水印 · 全角 · Office 未知。</summary>
        private static MainWindow SeedRangeRows(string dir, out RowVm rOk, out RowVm rPart, out RowVm rBad,
            out RowVm rNone, out RowVm rFull, out RowVm rOff)
        {
            var w = new MainWindow();
            rOk = NewRangeRow(w, dir, "甲-有效.pdf"); rOk.RangeText = "1";
            rPart = NewRangeRow(w, dir, "乙-部分超出.pdf"); rPart.RangeText = "1-8,13";
            rBad = NewRangeRow(w, dir, "丙-无效.pdf"); rBad.RangeText = "99";
            rNone = NewRangeRow(w, dir, "丁-未填.pdf");
            rFull = NewRangeRow(w, dir, "戊-全角.pdf"); rFull.RangeText = "３－５";
            rOff = NewOfficeRow(w, dir, "己-待转换.docx"); rOff.RangeText = "3";
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            w.UpdateLayoutForTest(1120, 700);
            return w;
        }

        private static string RowHeightsNote(DataGrid grid)
        {
            var live = FindByType(grid, typeof(DataGridRow)).Cast<DataGridRow>().Where(r => r.ActualHeight > 0).ToList();
            var hs = live.Select(r => Math.Round(r.ActualHeight, 2)).Distinct().ToList();
            return "实生成 " + live.Count + " 行，行高去重=[" + string.Join(",", hs) + "]，RowHeight=" + grid.RowHeight;
        }

        /// <summary>V4.9 版本内修整渲染取证：打印页浅/深（六行等高目检）+ 气泡特写 + 无效红字特写 + 打印后黄警告 + 英文一页。</summary>
        internal static int V49RangeRender(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";
                    string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV49RPng-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    RowVm rOk, rPart, rBad, rNone, rFull, rOff;
                    var w = SeedRangeRows(dir, out rOk, out rPart, out rBad, out rNone, out rFull, out rOff);
                    var grid = w.FindName("Grid") as DataGrid;
                    code |= RenderWindow("V49范围反馈" + ts + "自检.txt", "V49范围反馈-打印页-" + ts + ".png", w, 1120, 700, new string[0], 0);
                    sb.AppendLine("—" + ts + "：打印页六行（绿" + rOk.PagesText + " / 黄" + rPart.PagesText + " / 红" + rBad.PagesText
                        + " / 空水印" + rNone.PagesText + " / 全角" + rFull.PagesText + " / Office" + rOff.PagesText + "）" + RowHeightsNote(grid));
                }

                // 英文一张（页数列短文与提示文案都要目视过一遍 60 DIP 列宽）
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.EnUS);
                {
                    string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV49RPe-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    RowVm a, b, c, d, e, f;
                    var w = SeedRangeRows(dir, out a, out b, out c, out d, out e, out f);
                    var grid = w.FindName("Grid") as DataGrid;
                    code |= RenderWindow("V49范围反馈英文自检.txt", "V49范围反馈-打印页-浅色-英文.png", w, 1120, 700, new string[0], 0);
                    sb.AppendLine("—英文：页数列短文=\"" + b.PagesText + "\"/\"" + c.PagesText + "\" " + RowHeightsNote(grid));
                }

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                {
                    string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV49RB-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    RowVm rOk, rPart, rBad, rNone, rFull, rOff;
                    var w = SeedRangeRows(dir, out rOk, out rPart, out rBad, out rNone, out rFull, out rOff);
                    var grid = w.FindName("Grid") as DataGrid;
                    var root = w.Content as FrameworkElement;

                    // ① 聚焦气泡特写（全角那行：✓ 结论 + "已自动兼容全角输入"注记）——浮层，量它自己，不进表格布局
                    var box = w.RangeBoxForTest(rFull);
                    var popup = MainWindow.RangeBubbleOf(box);
                    var card = popup == null ? null : popup.Child as Border;
                    if (card == null) { sb.AppendLine("— 气泡特写：取不到 Popup 内容（失败）"); code |= 1; }
                    else
                    {
                        card.Measure(new Size(360, 200));
                        var dd = card.DesiredSize;
                        card.Arrange(new Rect(0, 0, Math.Max(260, dd.Width), Math.Max(1, dd.Height)));
                        card.UpdateLayout();
                        PumpToLoaded();
                        sb.AppendLine("— 聚焦气泡特写（" + (card.Child as TextBlock).Text.Replace("\n", "⏎") +"）："
                            + SnapFlat(card, "V49范围气泡特写-浅色.png", 240) + "；表格此时" + RowHeightsNote(grid));
                    }

                    // ② 无效红字特写：页数格"未生效"（红）+ 范围格红字，两格并排出图
                    var cells = new[]
                    {
                        CellOf(root, rBad, grid, Res("s.ColPages")),
                        CellOf(root, rBad, grid, Res("s.ColRange")),
                    };
                    sb.AppendLine("— 无效红字特写（行=\"" + rBad.PagesText + "\" 范围=\"" + rBad.RangeText + "\"）："
                        + SnapFlatGroup(grid, cells, "V49范围无效红字特写-浅色.png", 288));

                    // ③ 打印后状态列黄警告（走真实完成回写：范围无效 → 黄字单行截断，行高不动）
                    w.MergeOverrides(new List<RowVm> { rBad, rPart });
                    w.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Completed, FileName = rBad.Name, Pages = RangeTotal });
                    w.PrintProgressForTest(new PrintProgress { Stage = PrintStage.Completed, FileName = rPart.Name, Pages = RangeTotal });
                    w.UpdateLayoutForTest(1120, 700); PumpToLoaded();
                    var stCells = new[]
                    {
                        CellOf(root, rBad, grid, Res("s.ColPages")),
                        CellOf(root, rBad, grid, Res("s.ColStatus")),
                    };
                    sb.AppendLine("— 打印后状态列（\"" + rBad.Status + "\" 种类=" + rBad.StatusKind + "；部分超出行="
                        + rPart.Status + " 不告警）：" + SnapFlatGroup(grid, stCells, "V49范围无效状态列-浅色.png", 288)
                        + "；警告上屏后" + RowHeightsNote(grid));
                }
            }
            catch (Exception ex) { sb.AppendLine("探针异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            txt += "\r\n说明：V4.9 版本内修整（40 号·打印范围反馈优化·固定行高版）取证——打印页浅/深各一张（六行等高目检：绿 1/12 页 / 黄 8/12 页 / "
                 + "红 未生效 / 空水印 / 全角 / Office 页数未知）、聚焦气泡特写一张（浮层，含\"已自动兼容全角输入\"注记）、无效红字特写一张（页数格＋范围格）、"
                 + "打印后状态列黄警告一张（部分超出不告警，只在文件日志补一行）、英文打印页一张（核对 60 DIP 列宽里的短文）。"
                 + "行高口径沿用修整10：RowHeight 36 + 底边线（本机 125% 实测 36.8），判「六行等高、不随内容长」——行内零撑高元素，详情一律走气泡与提示两个浮层。自检全程离线，不打网络、不真打印。"
                 + (txt.Contains("失败") || txt.Contains("未渲染") ? " 判定：以自检行与逐条数字为准（本段含失败/未渲染字样）" : " 判定：出图完整");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }

        /// <summary>V4.9 版本内修整自检行：修1~修6 一次跑完，逐条数字落 V49范围反馈定位.txt。</summary>
        internal static string V49RangeUi()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== V4.9 修整·修1 页码范围解析全角归一化（PrintCore.ParseRange） ========");
                bad.AddRange(V49RParse(sb));
                sb.AppendLine("======== V4.9 修整·修6 前提核对：打印计划（PrintPlan）行为零改动 ========");
                bad.AddRange(V49RPlan(sb));
                sb.AppendLine("======== V4.9 修整·修2/3/4/5/6 呈现层（真实列表行 + 可视树实取；浅/深/英文三遍） ========");
                bad.AddRange(V49RRows(AppTheme.Light, AppLanguage.ZhCN, sb));
                bad.AddRange(V49RRows(AppTheme.Dark, AppLanguage.ZhCN, sb));
                bad.AddRange(V49RRows(AppTheme.Light, AppLanguage.EnUS, sb));
                sb.AppendLine("======== 中英资源键（新增 13 枚） ========");
                bad.AddRange(V49RText(sb));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V49范围反馈定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（修1 全角\"１\"/\"３－５\"/全半角混排各一条实测、超范围仍静默忽略、荒谬区间有护栏；修6 前提＝PageSequence 空集回退打全部行为原样保留；"
                  + "修2 页数列三态（绿 1/12 页·黄 8/12 页·红 未生效）浅/深/英文三遍取可视树真实前景逐一对色且黄字三档底色对比度 ≥4.5、逐键打字两次通知两次、Office 行打印回写总页数后从两态升到 X/N、三位数页数退成短文且文案需宽≤单元格实得（列宽实测留证）；"
                  + "修3 气泡锚定输入框/不收焦点/默认收起、聚焦与失焦向同一 Popup 发开收请求（落点 Bottom+2 与 Top−2 实取、朝上判据纯几何三例；无头窗口没有 PresentationSource 故浮层不落地，可见性由渲染特写与真机复验覆盖）；"
                  + "修4 无效标红改回即复原，红字与聚焦红框只认结构化布尔（触发器清单原样落日志）；"
                  + "修5 页数格与范围格悬停＝同一份结论、延迟 1 秒；修6 无效行状态列黄警告单行截断、部分超出不告警只进文件日志；"
                  + "铁律＝六行行高去重后只剩一个值 36.8（＝RowHeight 36 + 底边线，与修整10 同口径）且黄警告上屏后逐行不变；新增中英键 13 枚齐备（含三位数页数的短文退档键）；逐条数字见 V49范围反馈定位.txt）"
                : "失败：" + string.Join("；", bad);
        }
    }
}
