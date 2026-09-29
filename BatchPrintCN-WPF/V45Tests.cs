using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.5 四项（2026-09-25 需求方拍板）自检断言与渲染取证。
    /// 与 V44Tests.cs 同一个 partial（WpfTests），沿用既有量数口径：Check / Res / NewRow / FindByType /
    /// FindDerived / CellOf / SnapFlat / SnapFlatGroup / V44Fix3Contrast / PumpToLoaded / RenderWindow / RenderControl。
    /// 探针一律走真实方法本体（SetControlsBusyForTest、表头 Click 事件、AddRow/AddFiles 入口），
    /// 绝不在测试里复制一份逻辑——"测的与跑的不是同一段"是本项目反复吃过账的地方。
    /// </summary>
    public static partial class WpfTests
    {
        // —— 本批统一的主题键期望值（决1 拍板：浅色回旧观感深字；实底浅色深字、深色白字）——
        private static Color V45Want(string key, AppTheme th)
        {
            bool dark = th == AppTheme.Dark;
            switch (key)
            {
                case "BtnLightAreaTextBrush": return dark ? Color.FromRgb(0x60, 0x62, 0x66) : Color.FromRgb(0x20, 0x1F, 0x1E);
                case "BtnSolidTextBrush": return dark ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x20, 0x1F, 0x1E);
                case "BtnAccentTextBrush": return Color.FromRgb(0x40, 0x9E, 0xFF);
                default: return Colors.Transparent;
            }
        }

        private static Color V45BrushColor(string key)
        {
            return V44Fix3Color(Application.Current.TryFindResource(key) as Brush, Colors.Transparent);
        }

        /// <summary>取按钮里"人眼读到的那份文字"（与三批同口径：先按内容等于 Content 取，再排除私用区图标字形）。</summary>
        private static TextBlock V45BtnText(Button b)
        {
            if (b == null) return null;
            var tbs = FindByType(b, typeof(TextBlock)).Cast<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text)).ToList();
            return tbs.FirstOrDefault(t => b.Content is string && t.Text == (string)b.Content)
                ?? tbs.FirstOrDefault(t => !t.Text.Any(c => c >= 0xE000 && c <= 0xF8FF)) ?? tbs.FirstOrDefault();
        }

        /// <summary>单个按钮的字色事实 + 断言（两主题共用一份口径；浅底/实底分别对键，朴素红按拍板配对）。</summary>
        private static void V45Btn(Button b, string bn, AppTheme th, bool assert, StringBuilder sb, List<string> bad)
        {
            string tag = th == AppTheme.Dark ? "深色" : "浅色";
            if (b == null)
            {
                sb.AppendLine("    " + tag + " 按钮 " + bn + " 未找到");
                if (assert) bad.Add(tag + " 按钮 " + bn + " 未找到");
                return;
            }
            Color bg = V44Fix3Color(b.Background, Colors.White);
            Color sty = V44Fix3Color(b.Foreground, Colors.Black);
            var tb = V45BtnText(b);
            Color txt = V44Fix3Color(tb != null ? tb.Foreground : b.Foreground, Colors.Black);
            double cr = V44Fix3Contrast(bg, txt);
            bool lightBg = bg.A > 200 && 0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B > 200;
            sb.AppendLine("    " + tag + " " + bn + " 启用=" + b.IsEnabled + " 底=" + V44Fix3Hex(bg) + " 样式前景="
                + V44Fix3Hex(sty) + " 内容字色=" + V44Fix3Hex(txt) + " 对比度=" + cr.ToString("0.00")
                + (lightBg ? "（浅底）" : "（实底）"));
            if (!assert) return;
            Check(bad, tag + " " + bn + " 字色仍钉在内容元素上＝按钮样式前景（三批修复B 骨架不回退）",
                tb != null && txt.R == sty.R && txt.G == sty.G && txt.B == sty.B);
            if (!b.IsEnabled) return;   // 禁用态走 BtnBase 的 50% 透明（WCAG 对禁用控件豁免），只查色值来源
            if (bn == "BtnRemove")
            {
                Check(bad, tag + " BtnRemove 朴素红字色仍＝拍板 #F56C6C（白底红字配对，V4.5-1 明确不动）",
                    txt.R == 0xF5 && txt.G == 0x6C && txt.B == 0x6C);
                return;
            }
            string key = lightBg ? "BtnLightAreaTextBrush" : "BtnSolidTextBrush";
            Color want = V45Want(key, th);
            Check(bad, tag + " " + bn + " " + (lightBg ? "浅底" : "实底") + "字色＝" + key + "（期望 " + V44Fix3Hex(want)
                + " 实测 " + V44Fix3Hex(txt) + "）", txt.R == want.R && txt.G == want.G && txt.B == want.B);
            // 对比度口径（与三批一致，不新造做不到的门）：三批的 ≥4.5 只卡"浅底深字"按钮——实底按钮压的是
            // Element Plus 拍板底色（#409EFF/#F56C6C），深色白字实测 2.78/2.90（三批起就是这几个数，需求方已验收），
            // 底色属红线不动。V4.5-1 浅色实底改深字后 5.92/5.68 反而过线，所以浅色实底照样卡 ≥4.5，
            // 深色实底卡"＝三批白字"（=主题键值，已由上一条覆盖），两栏数字一律如实入取证文件。
            if (lightBg || th == AppTheme.Light)
                Check(bad, tag + " " + bn + " 对比度≥4.5（实测 " + cr.ToString("0.00") + "，字 " + V44Fix3Hex(txt)
                    + " 底 " + V44Fix3Hex(bg) + "）", cr >= 4.5);
            else
                Check(bad, tag + " " + bn + " 深色实底＝三批白字零回退（对比度 " + cr.ToString("0.00")
                    + " 与三批同值：白字压拍板底色，底色层本批未动）", txt.R >= 0xFA && txt.G >= 0xFA && txt.B >= 0xFA);
        }

        /// <summary>V4.5-1：三枚主题键在两字典的值 + 两主题下真按钮（主页八个 + 历史页两个）的字色与对比度。</summary>
        private static List<string> V45BtnColors(StringBuilder sb)
        {
            var bad = new List<string>();
            foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
            {
                string tag = th == AppTheme.Dark ? "深色" : "浅色";
                App.ApplyTheme(th);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("— 主题键（" + tag + "）");
                foreach (string k in new[] { "BtnLightAreaTextBrush", "BtnSolidTextBrush", "BtnAccentTextBrush" })
                {
                    Color got = V45BrushColor(k), want = V45Want(k, th);
                    sb.AppendLine("    键 " + k + " = " + V44Fix3Hex(got) + "（期望 " + V44Fix3Hex(want) + "）");
                    Check(bad, tag + " 主题键 " + k + " 值正确（" + V44Fix3Hex(got) + "）",
                        got.R == want.R && got.G == want.G && got.B == want.B);
                }
                var w = new MainWindow();
                w.UpdateLayoutForTest(1120, 700);
                sb.AppendLine("— 主页真按钮（" + tag + "）");
                foreach (string bn in new[] { "BtnAddFiles", "BtnAddFolder", "BtnRemove", "BtnClear",
                                              "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop" })
                    V45Btn(w.FindName(bn) as Button, bn, th, true, sb, bad);
                w.ShowPanelForTest("history");
                w.UpdateLayoutForTest(1120, 700);
                var hist = FindByType(w.Content as FrameworkElement, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                sb.AppendLine("— 历史页工具栏（" + tag + "）");
                foreach (string bn in new[] { "BtnRefresh", "BtnExport" })
                    V45Btn(hist == null ? null : hist.FindName(bn) as Button, "历史页 " + bn, th, true, sb, bad);

                // 悬停/按下态字色按主题各核一遍：无窗口环境点不出鼠标，核样式触发器里的值与浅蓝底配对的数字
                var outline = w.FindName("BtnAddFolder") as Button;
                string hover = "(未取到)";
                if (outline != null && outline.Style != null)
                    foreach (TriggerBase t in outline.Style.Triggers)
                    {
                        var tr = t as Trigger;
                        if (tr == null || tr.Property.Name != "IsMouseOver") continue;
                        var sv = tr.Setters.OfType<Setter>().FirstOrDefault(s2 => s2.Property.Name == "Foreground");
                        if (sv == null) break;
                        var sc = V45Resolve(sv.Value) as SolidColorBrush;
                        if (sc != null) hover = V44Fix3Hex(sc.Color);
                        break;
                    }
                Color acc = V45Want("BtnAccentTextBrush", th);
                sb.AppendLine("    " + tag + " 悬停字色（样式触发器解析值）=" + hover + "；键=" + V44Fix3Hex(acc)
                    + " 配浅蓝底 #ECF5FF 对比度=" + V44Fix3Contrast(acc, Color.FromRgb(0xEC, 0xF5, 0xFF)).ToString("0.00")
                    + "（Element Plus 拍板蓝字属底色/边框层，本批未动；数字随本批如实报需求方）");
                Check(bad, tag + " 悬停态解析出的字色＝键值（" + V44Fix3Hex(acc) + "，两主题同值）", hover == V44Fix3Hex(acc));
            }
            App.ApplyTheme(AppTheme.Light);
            return bad;
        }

        /// <summary>把 Setter 的值解析成可用对象（DynamicResource 值在已封存的样式里存的是 ResourceKey 扩展）。</summary>
        private static Brush V45Resolve(object v)
        {
            var b = v as Brush;
            if (b != null) return b;
            var ext = v as System.Windows.DynamicResourceExtension;
            string key = ext != null ? ext.ResourceKey as string : v as string;
            return key == null ? null : Application.Current.TryFindResource(key) as Brush;
        }

        private static List<DataGridCell> V45Cells(DataGridRow row)
        {
            return FindDerived(row, typeof(DataGridCell)).Cast<DataGridCell>().ToList();
        }

        private static FrameworkElement V45Root(MainWindow w, double wid = 1120, double hei = 700)
        {
            w.UpdateLayoutForTest(wid, hei);
            return w.Content as FrameworkElement;
        }

        private static ScrollViewer V45InnerScroll(DataGrid grid)
        {
            return FindDerived(grid, typeof(ScrollViewer)).Cast<ScrollViewer>().FirstOrDefault(sv => sv.ViewportHeight > 0);
        }

        private static List<CheckBox> V45RowBoxes(DataGrid grid)
        {
            return FindDerived(grid, typeof(DataGridRow)).Cast<DataGridRow>().Where(r => r.ActualHeight > 0)
                .SelectMany(r => FindDerived(r, typeof(CheckBox)).Cast<CheckBox>()).ToList();
        }

        /// <summary>只数"已生成的行"里的控件（列头/表格自身不算）——V4.5-3 的行内四类控件口径。</summary>
        private static List<T> V45RowCells<T>(DataGrid grid) where T : FrameworkElement
        {
            return FindDerived(grid, typeof(DataGridRow)).Cast<DataGridRow>().Where(r => r.ActualHeight > 0)
                .SelectMany(r => FindDerived(r, typeof(T)).Cast<T>()).ToList();
        }

        private static string V45Names<T>(List<T> els) where T : FrameworkElement
        {
            return string.Join(",", els.Select(e => string.IsNullOrEmpty(e.Name) ? e.GetType().Name : e.Name).Distinct());
        }

        /// <summary>表头全选框来一次"真点击"（走 OnHeaderCheckClicked 本体；三态框的翻转值本就不参与判定＝修整9 口径）。</summary>
        private static void V45ClickHeader(CheckBox hcb)
        {
            hcb.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        }

        /// <summary>V4.5-2：历史页「状态」列按当前语言出文案（中→成功/失败、英→Success/Failed），上色与主页同键。</summary>
        private static List<string> V45HistStatus(StringBuilder sb)
        {
            var bad = new List<string>();
            var w = new MainWindow();
            w.ShowPanelForTest("history");
            var root = V45Root(w);
            var hist = FindByType(root, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
            if (hist == null) { bad.Add("历史页未找到"); return bad; }
            var hgrid = hist.FindName("Grid") as DataGrid;
            foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
            {
                App.ApplyLanguage(lang);
                hist.SeedRowsForTest(12);
                hgrid.UpdateLayout();
                PumpToLoaded();
                hgrid.UpdateLayout();
                string tag = lang == AppLanguage.EnUS ? "英文" : "中文";
                var rows = FindDerived(hgrid, typeof(DataGridRow)).Cast<DataGridRow>().Where(r => r.ActualHeight > 0).ToList();
                sb.AppendLine("— 历史页状态列（" + tag + "）已呈现行 " + rows.Count
                    + "；资源键 ok=\"" + Res("s.HistStatusOk") + "\" fail=\"" + Res("s.HistStatusFail") + "\"");
                var seen = new List<string>();
                Color? okColor = null, failColor = null;
                int idx = 0;
                foreach (var r in rows)
                {
                    var cells = V45Cells(r);
                    if (cells.Count < 5) { idx++; continue; }
                    var tb = FindByType(cells[4], typeof(TextBlock)).Cast<TextBlock>().FirstOrDefault();
                    if (tb == null) { idx++; continue; }
                    seen.Add(tb.Text);
                    Color c = V44Fix3Color(tb.Foreground, Colors.Black);
                    if (idx % 5 != 0) okColor = c; else failColor = c;   // SeedRowsForTest 的 Success 口径＝i%5!=0
                    idx++;
                }
                string wantOk = lang == AppLanguage.EnUS ? "Success" : "成功";
                string wantFail = lang == AppLanguage.EnUS ? "Failed" : "失败";
                Check(bad, tag + " 状态列文案取自资源键（应为 \"" + wantOk + "\"/\"" + wantFail + "\"，实取样例 ["
                    + string.Join(",", seen.Distinct().Take(4)) + "]）",
                    seen.Count > 0 && seen.All(t => t == wantOk || t == wantFail));
                if (lang == AppLanguage.EnUS)
                    Check(bad, "英文界面状态列不再出现中文「成功/失败」", seen.All(t => !t.Contains("成功") && !t.Contains("失败")));
                Color wantOkC = V45BrushColor("StatusOkTextBrush"), wantFailC = V45BrushColor("StatusFailTextBrush");
                Check(bad, tag + " 状态列上色与主页修整16 同键（成功 " + (okColor == null ? "(无量)" : V44Fix3Hex(okColor.Value))
                    + " 期望 " + V44Fix3Hex(wantOkC) + "／失败 " + (failColor == null ? "(无量)" : V44Fix3Hex(failColor.Value))
                    + " 期望 " + V44Fix3Hex(wantFailC) + "）",
                    okColor != null && failColor != null && okColor.Value == wantOkC && failColor.Value == wantFailC);
            }
            App.ApplyLanguage(AppLanguage.ZhCN);
            sb.AppendLine("    CSV 导出口径不变：仍写同一字段（导出语言跟界面语言）");
            return bad;
        }

        /// <summary>V4.5-3：打印中"锁交互不锁滚动"——表格本体仍可用且真能滚，行内四类控件全灰，恢复后全放开。</summary>
        private static List<string> V45ScrollLock(StringBuilder sb)
        {
            var bad = new List<string>();
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV45S-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var main = new MainWindow();
            for (int i = 0; i < 40; i++) NewRow(main, dir, "可滚动取证" + (i + 1) + ".pdf");
            var root = V45Root(main, 1120, 620);
            var grid = main.FindName("Grid") as DataGrid;
            var sv = V45InnerScroll(grid);
            double canScroll = sv == null ? -1 : sv.ScrollableHeight;
            sb.AppendLine("— 取证环境：内层 ScrollViewer ScrollableHeight=" + canScroll.ToString("0.#")
                + "（无窗口渲染下虚拟化只按已生成行估 extent，行数灌 40 也只有这一截；判定看的是「能滚＋偏移真的变」，不是绝对高度）");
            Check(bad, "V4.5-3 取证环境：确实有可滚高度（ScrollableHeight " + canScroll.ToString("0.#") + " > 0）", canScroll > 0);

            Func<List<CheckBox>> rowBoxes = () => V45RowBoxes(grid);
            sb.AppendLine("— 空闲态：表格可用=" + grid.IsEnabled + " 行勾选框可用数=" + rowBoxes().Count(c => c.IsEnabled)
                + "/" + rowBoxes().Count);
            Check(bad, "V4.5-3 空闲态：表格可用 + 行内勾选框/输入框/参数按钮都可用",
                grid.IsEnabled && rowBoxes().Count > 0 && rowBoxes().All(c => c.IsEnabled)
                && PrintLock.Instance.Idle && !PrintLock.Instance.Busy);

            main.SetControlsBusyForTest(true);
            main.UpdateLayoutForTest(1120, 620);
            sv = V45InnerScroll(grid);
            var boxes = rowBoxes();
            int nBox = boxes.Count, offBox = boxes.Count(c => !c.IsEnabled);
            var allBoxesInGrid = V45RowCells<TextBox>(grid);
            int nTb = allBoxesInGrid.Count, offTb = allBoxesInGrid.Count(t => !t.IsEnabled);
            var allBtn = V45RowCells<Button>(grid);
            int nBtn = allBtn.Count, offBtn = allBtn.Count(b => !b.IsEnabled);
            var hdr = FindHeaderCheck(root);
            sb.AppendLine("— 打印态实取：DataGrid IsEnabled=" + grid.IsEnabled + "；行勾选框禁用 " + offBox + "/" + nBox
                + "；格内输入框禁用 " + offTb + "/" + nTb + "；参数/重试按钮禁用 " + offBtn + "/" + nBtn
                + "；表头全选框可用=" + (hdr == null ? "(无)" : hdr.IsEnabled.ToString())
                + "；输入框=" + V45Names(allBoxesInGrid) + " 按钮=" + V45Names(allBtn));
            Check(bad, "V4.5-3 打印态：DataGrid 本体保持 IsEnabled=true（滚动与选中不受影响）", grid.IsEnabled);
            Check(bad, "V4.5-3 打印态：PrintLock.Busy=true 且 Idle=false（行内控件绑的就是它）",
                PrintLock.Instance.Busy && !PrintLock.Instance.Idle);
            Check(bad, "V4.5-3 打印态：行内四类控件全部禁用（勾选框 " + offBox + "/" + nBox + "、输入框 " + offTb + "/" + nTb
                + "、按钮 " + offBtn + "/" + nBtn + "、表头全选框）",
                nBox > 0 && offBox == nBox && nTb > 0 && offTb == nTb && nBtn > 0 && offBtn == nBtn && hdr != null && !hdr.IsEnabled);

            double o0 = sv == null ? -1 : sv.VerticalOffset;
            if (sv != null) sv.ScrollToVerticalOffset(o0 + 180);
            grid.UpdateLayout();
            double o1 = sv == null ? -1 : sv.VerticalOffset;
            sb.AppendLine("— 打印态真实滚动：VerticalOffset " + o0.ToString("0.#") + " → " + o1.ToString("0.#"));
            Check(bad, "V4.5-3 打印态：滚动条仍可操作（ScrollToVerticalOffset 之后偏移真的变了 "
                + o0.ToString("0.#") + "→" + o1.ToString("0.#") + "）", o1 > o0);

            main.SetControlsBusyForTest(false);
            main.UpdateLayoutForTest(1120, 620);
            boxes = rowBoxes();
            var hdr2 = FindHeaderCheck(root);
            sb.AppendLine("— 恢复态：行勾选框可用 " + boxes.Count(c => c.IsEnabled) + "/" + boxes.Count
                + "，表头全选框可用=" + (hdr2 == null ? "(无)" : hdr2.IsEnabled.ToString()));
            Check(bad, "V4.5-3 恢复态：行内控件全部还原可用（不是只把表格放开）",
                boxes.Count > 0 && boxes.All(c => c.IsEnabled) && hdr2 != null && hdr2.IsEnabled
                && PrintLock.Instance.Idle && !PrintLock.Instance.Busy);
            string freshFile = Path.Combine(dir, "打印后新来的.pdf");
            BuildSamplePdf(freshFile);
            int n0 = main.RowTotalForTest;
            Check(bad, "V4.5-3 恢复后添加收口照常放行（不是把入口焊死）", main.AddFilesForTest(new[] { freshFile }) == n0 + 1);
            return bad;
        }

        /// <summary>V4.5-4：全选四情形 + 「全取消后行上 √ 立即消失」（可视树实取，不只看 VM 字段）。</summary>
        private static List<string> V45CheckAll(StringBuilder sb)
        {
            var bad = new List<string>();
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV45C-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var main = new MainWindow();
            var grid = main.FindName("Grid") as DataGrid;
            var root = V45Root(main);
            var hcb = FindHeaderCheck(root);
            Check(bad, "V4.5-4 取证环境：表头全选框已呈现", hcb != null);
            if (hcb == null) return bad;

            V45ClickHeader(hcb);
            main.UpdateLayoutForTest(1120, 700);
            Check(bad, "V4.5-4 情形① 空列表点表头：不崩、表头仍为未选中、没有凭空多出行",
                hcb.IsChecked == false && main.RowTotalForTest == 0);

            for (int i = 0; i < 8; i++) NewRow(main, dir, "全选清除" + (i + 1) + ".pdf");
            root = V45Root(main);
            hcb = FindHeaderCheck(root);
            var boxes = V45RowBoxes(grid);
            sb.AppendLine("— 导入后：已呈现行勾选框 " + boxes.Count + " 个，勾上 " + boxes.Count(b => b.IsChecked == true)
                + " 个；表头=" + hcb.IsChecked);
            Check(bad, "V4.5-4 前提：新导入的行默认全勾、表头为全选态",
                boxes.Count >= 5 && boxes.All(b => b.IsChecked == true) && hcb.IsChecked == true);

            V45ClickHeader(hcb);   // 全取消
            main.UpdateLayoutForTest(1120, 700);
            boxes = V45RowBoxes(grid);
            int still = boxes.Count(b => b.IsChecked != false);
            sb.AppendLine("— 全取消后可视树实取：" + boxes.Count + " 个行勾选框，未清除 " + still
                + " 个；VM Checked 全 false=" + main.RowsForTest.All(r => !r.Checked));
            Check(bad, "V4.5-4 情形② 全取消后可视树逐行实取 CheckBox.IsChecked==false（行上 √ 立即消失，未清除="
                + still + "）", boxes.Count > 0 && still == 0 && main.RowsForTest.All(r => !r.Checked));

            main.RowsForTest[0].Checked = true;
            main.UpdateStatusForTest();
            main.UpdateLayoutForTest(1120, 700);
            boxes = V45RowBoxes(grid);
            Check(bad, "V4.5-4 情形③a 行手改一个 → 表头变半选(null) 且那一行的 √ 出现在可视树里",
                hcb.IsChecked == null && boxes.Count > 0 && boxes[0].IsChecked == true);

            V45ClickHeader(hcb);   // 半选 → 全选
            main.UpdateLayoutForTest(1120, 700);
            boxes = V45RowBoxes(grid);
            Check(bad, "V4.5-4 情形③b 半选点表头 → 全部补齐（可视树实取 " + boxes.Count(b => b.IsChecked == true)
                + "/" + boxes.Count + "）", boxes.Count > 0 && boxes.All(b => b.IsChecked == true) && hcb.IsChecked == true);

            V45ClickHeader(hcb);   // 再一轮全取消（真机"点两轮"的自动化等价）
            main.UpdateLayoutForTest(1120, 700);
            boxes = V45RowBoxes(grid);
            Check(bad, "V4.5-4 情形④ 第二轮全取消仍然干净（未清除=" + boxes.Count(b => b.IsChecked != false) + "）",
                boxes.All(b => b.IsChecked == false));

            Check(bad, "V4.5-4 根因收口：RowVm.Checked 确实发 PropertyChanged（同值不通知，异值才通知）", V45CheckedNotifies());
            return bad;
        }

        private static bool V45CheckedNotifies()
        {
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV45T-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string f = Path.Combine(dir, "样本.pdf");
            BuildSamplePdf(f);
            var job = FileClassifier.CreateJob(f);
            if (job == null) return false;
            var r = new RowVm(job);
            int hits = 0;
            r.PropertyChanged += (s, e) => { if (e.PropertyName == "Checked") hits++; };
            r.Checked = false;
            r.Checked = false;    // 同值不再通知（虚拟化下不做全量刷）
            r.Checked = true;
            return hits == 2;
        }

        /// <summary>V4.5 自检行：保留的四项一次跑完，逐条数字落 V45一批字色滚动条定位.txt。</summary>
        internal static string V45Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== V4.5-1 按钮字色按主题分治（决1：浅色回旧观感） ========");
                bad.AddRange(V45BtnColors(sb));
                sb.AppendLine("======== V4.5-2 历史页状态列按语言出文案 + 状态上色（决2） ========");
                bad.AddRange(V45HistStatus(sb));
                sb.AppendLine("======== V4.5-3 打印中锁交互不锁滚动（决9a） ========");
                bad.AddRange(V45ScrollLock(sb));
                sb.AppendLine("======== V4.5-4 全选四情形 + 全取消后行上 √ 即时消失（决9b） ========");
                bad.AddRange(V45CheckAll(sb));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V45一批字色滚动条定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（浅色按钮字色回旧观感 #201F1E·实底也回深字（浅底 16.46、实底 5.92/5.68 均 ≥4.5），深色浅底 #606266 达 6.11、实底＝三批白字零回退（白字压拍板底色 2.78/2.90，与三批同值且底色层未动，数字如实报） / 历史页状态列中英切换正确且成功绿失败红与主页同键 / "
                  + "打印中 DataGrid 保持可用且真的能滚（offset 实取变化），行内勾选·份数页码输入·参数「修改」·重试全灰，停止后全还原 / "
                  + "全选四情形走可视树实取、全取消后行上 √ 立即消失；"
                  + "逐条数字见 V45一批字色滚动条定位.txt。V4.5-5 监视文件夹已移出，断言见 V4.7 行）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>
        /// V4.5 渲染取证（开工令 §4.2）：按钮字色 浅/深（主页工具栏 + 主页底栏 + 历史页工具栏，浅色与 v4.2.5 旧渲染目视对照）、
        /// 历史页状态列 中/英、打印中可滚动前后两张、全选四情形特写（V4.5-5 监视页渲染已移出）。
        /// </summary>
        internal static int V45Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV45R-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(dir);

                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";
                    var w = new MainWindow();
                    for (int i = 0; i < 40; i++) NewRow(w, dir, "五批可取证行" + (i + 1) + ".pdf");
                    w.UpdateLayoutForTest(1120, 700);
                    w.RequestStarFill();
                    PumpToLoaded();
                    var root = V44Fix3Relayout(w, 1120, 700);
                    var toolbar = new[] { "BtnAddFiles", "BtnAddFolder", "BtnRemove", "BtnClear" }
                        .Select(n => w.FindName(n) as FrameworkElement).Where(x => x != null).ToArray();
                    sb.AppendLine("工具栏特写（" + ts + "）：" + SnapFlatGroup(root, toolbar, "V45按钮字色-主页工具栏-" + ts + ".png", 360));
                    var bottom = new[] { "PrinterBox", "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop" }
                        .Select(n => w.FindName(n) as FrameworkElement).Where(x => x != null).ToArray();
                    sb.AppendLine("底栏特写（" + ts + "）：" + SnapFlatGroup(root, bottom, "V45按钮字色-主页底栏-" + ts + ".png", 360));

                    // 打印中可滚动：同一张表格，滚动前后各一张（证明条能拖、行内控件已灰）
                    var grid = w.FindName("Grid") as DataGrid;
                    w.SetControlsBusyForTest(true);
                    w.UpdateLayoutForTest(1120, 700);
                    grid = w.FindName("Grid") as DataGrid;
                    var sv = V45InnerScroll(grid);
                    double o0 = sv == null ? -1 : sv.VerticalOffset;
                    sb.AppendLine("打印态-滚动前：" + SnapFlat(grid, "V45打印中可滚动-前-" + ts + ".png", 120));
                    if (sv != null) sv.ScrollToVerticalOffset(o0 + 200);
                    grid.UpdateLayout();
                    double o1 = sv == null ? -1 : sv.VerticalOffset;
                    sb.AppendLine("打印态-滚动后（offset " + o0.ToString("0.#") + "→" + o1.ToString("0.#") + "）："
                        + SnapFlat(grid, "V45打印中可滚动-后-" + ts + ".png", 120));
                    w.SetControlsBusyForTest(false);
                }

                // 历史页工具栏字色（浅/深）
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";
                    var w = new MainWindow();
                    w.ShowPanelForTest("history");
                    var root0 = V45Root(w);
                    var hist = FindByType(root0, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    hist.SeedRowsForTest(12);
                    w.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    root0 = V44Fix3Relayout(w, 1120, 700);
                    var hb = new[] { "BtnRefresh", "BtnExport" }
                        .Select(n => hist.FindName(n) as FrameworkElement).Where(x => x != null).ToArray();
                    sb.AppendLine("历史页工具栏特写（" + ts + "）：" + SnapFlatGroup(root0, hb, "V45按钮字色-历史页工具栏-" + ts + ".png", 360));
                }

                // 历史页状态列（中/英）：表格特写一张 + 整窗一张（整窗渲染会摘走内容，故各起窗口）
                foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
                {
                    App.ApplyTheme(AppTheme.Light);
                    App.ApplyLanguage(lang);
                    string ls = lang == AppLanguage.EnUS ? "英文" : "中文";
                    var w1 = new MainWindow();
                    w1.ShowPanelForTest("history");
                    var r1 = V45Root(w1);
                    var h1 = FindByType(r1, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    h1.SeedRowsForTest(12);
                    w1.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    sb.AppendLine("历史页状态列（" + ls + "）：" + SnapFlat(h1.FindName("Grid") as DataGrid, "V45历史页状态列-" + ls + ".png", 240));
                    var w2 = new MainWindow();
                    w2.ShowPanelForTest("history");
                    var r2 = V45Root(w2);
                    var h2 = FindByType(r2, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    h2.SeedRowsForTest(12);
                    w2.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V45历史页" + ls + "自检.txt", "V45历史页状态列整窗-" + ls + ".png", w2, 1120, 700, new string[0], 0);
                }

                // 全选四情形：全取消后行上 √ 消失特写 / 再点全选后 √ 在位特写
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var mw = new MainWindow();
                for (int i = 0; i < 6; i++) NewRow(mw, dir, "全选特写" + (i + 1) + ".pdf");
                var mr = V45Root(mw);
                var mg = mw.FindName("Grid") as DataGrid;
                V45ClickHeader(FindHeaderCheck(mr));
                mw.UpdateLayoutForTest(1120, 700);
                mr = V44Fix3Relayout(mw, 1120, 700);
                int still = V45RowBoxes(mg).Count(b => b.IsChecked != false);
                sb.AppendLine("全取消后可视树未清除勾选框=" + still + "：" + SnapFlat(mg, "V45全选清除后-行上√消失.png", 240));
                V45ClickHeader(FindHeaderCheck(mr) ?? FindHeaderCheck(mw.Content as FrameworkElement));
                mw.UpdateLayoutForTest(1120, 700);
                sb.AppendLine("再点全选后：" + SnapFlat(mg, "V45全选后-行上√在位.png", 240));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { sb.AppendLine("探针异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            bool ok = !txt.Contains("失败") && code == 0;
            txt += "\r\n说明：按钮字色按主题分治（浅色回退 v4.2.5 旧观感深字、深色三批值零回退）——浅色那两张与"
                 + " work\\批量打印助手.v4.2.5 的同位置渲染目视对照；历史页状态列中英各一张（英文界面不再出现中文成功/失败）；"
                 + "打印中两张证明滚动条仍可用（offset 真的变了）而行内四类控件已灰；全选两张特写证明全取消后行上 √ 立即消失；"
                 + "V4.5-5 监视文件夹页（F7）与其端到端取证已移出，不再出该页图。"
                 + (ok ? " 判定：通过" : " 判定：以自检行与逐条数字为准（本段为出图记录）");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
