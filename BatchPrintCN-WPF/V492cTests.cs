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
using System.Windows.Input;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 46 号（V4.9.2 预览交互增强）＋ 47 号（同一次交互的修订）断言：
    ///   增1 Ctrl+滚轮以指针为锚点缩放（47 号原样保留）；
    ///   修1 取消滚轮翻页——非 Ctrl 滚轮不再被接管（页内滚动交还 ScrollViewer）；
    ///   修2 信息条缩放组补「−」「＋」两枚按钮（与滚轮同档位、同一段代码）；
    ///   修3 翻页改「上一页／页码读数／下一页」按钮 + PgUp／PgDn，统一走 GoToPage。
    ///
    /// 无头环境的限制（按 40/41/45/46 号先例如实报备）：造不出真鼠标滚轮、真键盘消息与真实指针位置，
    /// 所以断言打到**交互请求与状态层**——直接调窗口里那几个处理方法的同一段代码
    /// （<see cref="PreviewWindow.WheelForTest"/> 走 PreviewMouseWheel 的分流、
    ///  <see cref="PreviewWindow.KeyForTest"/> 走 PreviewKeyDown 的同一静态判定、
    ///  <see cref="PreviewWindow.GoToPage"/> 与 <see cref="PreviewWindow.ZoomByStep"/> 就是按钮 Click 的实体），
    /// 档位钳制/锚点换算/重渲染 DPI/缩放读数/翻页与边界/按钮置灰/缩略图同步全部逐条核对；
    /// 真实滚轮手感、页内滚动的观感、按键是否真被 PreviewKeyDown 收到，进真机清单。
    /// 红线复核：打印引擎与 45 号三项成果（超界标注/警告条/信息条/双出口/范围联动/缓存）一行未动，
    /// 本文件只验交互层。
    /// </summary>
    public static partial class WpfTests
    {
        internal static string V492cUi()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            var mode0 = Application.Current.ShutdownMode;
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV492c-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                sb.AppendLine("======== 46 号·增1 缩放档位与锚点（纯函数逐档核对）========");
                bad.AddRange(V492cLevels(sb));
                sb.AppendLine("======== 46 号·增1 Ctrl+滚轮：以指针为锚点重渲染（DPI 随缩放走，不拉伸旧位图）========");
                bad.AddRange(V492cZoom(sb, dir));
                sb.AppendLine("======== 47 号·修2 信息条「−」「＋」缩放按钮（同档位、同一段代码、顶底钳制）========");
                bad.AddRange(V492cZoomButtons(sb, dir));
                sb.AppendLine("======== 47 号·修3 翻页按钮／PgUp·PgDn（统一走 GoToPage）＋ 修1 非 Ctrl 滚轮不再翻页 ========");
                bad.AddRange(V492cPaging(sb, dir));

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("V4.92c 异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally { Application.Current.ShutdownMode = mode0; }

            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V492c预览交互定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            try { Directory.Delete(dir, true); } catch { }
            return bad.Count == 0
                ? "通过（增1 原样保留：档位序列 25/50/75/100/150/200/300/400 与上下限钳制逐档走查、非档位值就近到滚动方向那一档、"
                  + "锚点换算按「文档比例不变」逐数核对、缩放后按 96×百分比 的 DPI 重新出图（重渲染计数与 DPI 都取到实证，页面 DIP 尺寸与位图一比一＝不拉伸旧图）、"
                  + "信息条读数跟着变、Ctrl+0 回适合页面 / Ctrl+1 回实际像素、缩略图不随缩放变；"
                  + "修1：非 Ctrl 滚轮后页码不变、缩放不变（滚动权已交还 ScrollViewer）；"
                  + "修2：「−」「＋」与滚轮同档位同一段代码，＋100→150／−150→100、400 点＋与 25 点−都钳住不动、读数同步、每点一次按新档重新出图；"
                  + "修3：GoToPage 上／下一页正确、首页与末页越界各一例 no-op、首页禁「上一页」末页禁「下一页」、页码读数「第 x / N 页」同步、"
                  + "翻页保留缩放、缩略图高亮同步，PgUp／PgDn 与按钮走同一段 HandleZoomKey，Ctrl+0／Ctrl+1 原样保留）"
                : "失败：" + string.Join("；", bad);
        }

        // ===== 增1 ①：档位与锚点的纯函数 =====
        private static List<string> V492cLevels(StringBuilder sb)
        {
            var bad = new List<string>();
            double up100 = PreviewWindow.NextZoom(100, 1);
            double dn100 = PreviewWindow.NextZoom(100, -1);
            double upTop = PreviewWindow.NextZoom(400, 1);
            double dnBottom = PreviewWindow.NextZoom(25, -1);
            double fromOddUp = PreviewWindow.NextZoom(120, 1);
            double fromOddDown = PreviewWindow.NextZoom(120, -1);
            double clampHi = PreviewWindow.ClampZoom(900);
            double clampLo = PreviewWindow.ClampZoom(3);
            sb.AppendLine("— 档位：100↑=" + up100 + " 100↓=" + dn100 + " 400↑=" + upTop + " 25↓=" + dnBottom
                + " 非档位值 120↑=" + fromOddUp + " 120↓=" + fromOddDown + " 钳制 900→" + clampHi + " 3→" + clampLo);
            Check(bad, "增1 档位序列照开工令（25/50/75/100/150/200/300/400）逐档走，上下限钳死不再外扩",
                up100 == 150 && dn100 == 75 && upTop == 400 && dnBottom == 25 && clampHi == 400 && clampLo == 25);
            Check(bad, "增1 当前值不在档位上时（如适合页面算出的 120%）就近到滚动方向的那一档，不跳档也不原地不动",
                fromOddUp == 150 && fromOddDown == 100);

            double a1 = PreviewWindow.AnchorScroll(100, 1000, 2000, 200);      // 指针下文档位置 300/1000 → 新内容里 600 − 200
            double a2 = PreviewWindow.AnchorScroll(0, 1000, 2000, 0);         // 指针在视口左上角 → 保持 0
            double a3 = PreviewWindow.AnchorScroll(500, 1000, 2000, 500);     // 视口右下角附近 → (500+500)/1000*2000-500
            double a4 = PreviewWindow.AnchorScroll(0, 0, 2000, 123);          // 旧内容还没量出来：原样返回，不瞎算
            sb.AppendLine("— 锚点换算：(100,1000→2000,指针200)=" + a1 + "　(0,1000→2000,指针0)=" + a2
                + "　(500,1000→2000,指针500)=" + a3 + "　(旧尺寸未知)=" + a4);
            Check(bad, "增1 锚点保持＝缩放前后「指针指着的那处文档位置」在屏幕原地不动（放大往外扩、缩小往里收）",
                Math.Abs(a1 - 400) < 0.01 && Math.Abs(a2) < 0.01 && Math.Abs(a3 - 1500) < 0.01 && Math.Abs(a4) < 0.01);

            double dpi100 = PreviewWindow.RenderDpi(100), dpi200 = PreviewWindow.RenderDpi(200);
            double dip = PreviewWindow.PageDip(595.3, 200);
            sb.AppendLine("— 出图口径：100%→DPI " + dpi100 + "　200%→DPI " + dpi200 + "　A4 宽 595.3pt 在 200% 下＝" + dip.ToString("0.0") + " DIP");
            Check(bad, "增1 缩放后按目标尺寸重新出图（出图 DPI＝96×百分比，与页面 DIP 一比一，不靠拉伸旧位图）",
                dpi100 == 96 && dpi200 == 192 && Math.Abs(dip - 595.3 * (96.0 / 72.0) * 2) < 0.01);
            return bad;
        }

        // ===== 增1 ②：窗口上的缩放与重渲染 =====
        /// <summary>量一次「按目标缩放重新出图」的实际耗时（同步跑在 UI 线程，报备用实测数字）。</summary>
        private static double RerenderMs(PreviewWindow pv, double percent)
        {
            int before = pv.RerenderCountForTest;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            pv.ZoomToForTest(percent);
            sw.Stop();
            return pv.RerenderCountForTest > before ? sw.Elapsed.TotalMilliseconds : -1;
        }

        private static List<string> V492cZoom(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            string pdf = Path.Combine(dir, "zoom.pdf");
            BuildInkPdf(pdf, 2, 10, 209.5);
            var w = new MainWindow();
            w.ShowPanelForTest("print");
            w.UpdateLayoutForTest(1120, 700);
            var job = new PrintJob { FilePath = pdf, Kind = DocKind.Pdf };
            var row = w.AddRowForTest(job);
            var pv = w.OpenPreviewForTest(row);
            pv.LoadForTest(pdf, PageOverflowScanner.Scan(pdf), null);
            PumpToLoaded();

            double p0 = pv.ZoomPercentForTest;
            int r0 = pv.RerenderCountForTest;
            double sheet0 = pv.SheetWidthForTest;
            pv.WheelForTest(120, true, 200, 150);            // Ctrl+滚轮向上＝放大
            PumpToLoaded();
            double p1 = pv.ZoomPercentForTest;
            int r1 = pv.RerenderCountForTest;
            double sheet1 = pv.SheetWidthForTest;
            double dpi1 = pv.LastRenderDpiForTest;
            sb.AppendLine("— Ctrl+滚轮向上：缩放 " + p0 + "%→" + p1 + "%　重渲染次数 " + r0 + "→" + r1
                + "　本次出图 DPI=" + dpi1 + "　页面宽 " + sheet0.ToString("0.0") + "→" + sheet1.ToString("0.0") + " DIP"
                + "　读数=\"" + pv.ZoomReadoutForTest + "\"");
            Check(bad, "增1 Ctrl+滚轮向上确实放大、并且**重新出图**（重渲染计数 +1、出图 DPI 随缩放走、页面 DIP 尺寸同步变大）",
                p1 > p0 && r1 == r0 + 1 && Math.Abs(dpi1 - PreviewWindow.RenderDpi(p1)) < 0.01 && sheet1 > sheet0
                && Math.Abs(sheet1 - PreviewWindow.PageDip(pv.PagePtWidthForTest, p1)) < 0.5);
            Check(bad, "增1 信息条读数跟着变（「视图缩放 150%」这一档取整显示）",
                pv.ZoomReadoutForTest != null && pv.ZoomReadoutForTest.Contains(((int)Math.Round(p1)).ToString() + "%"));

            pv.WheelForTest(-120, true, 200, 150);           // Ctrl+滚轮向下＝缩小一档
            PumpToLoaded();
            double p2 = pv.ZoomPercentForTest;
            sb.AppendLine("— Ctrl+滚轮向下：缩放 " + p1 + "%→" + p2 + "%　重渲染次数 " + pv.RerenderCountForTest);
            Check(bad, "增1 Ctrl+滚轮向下缩一档并再次重渲染（不是把旧图缩小画）",
                p2 < p1 && pv.RerenderCountForTest >= 3);

            pv.ZoomToForTest(400);
            double at400 = pv.ZoomPercentForTest;
            pv.WheelForTest(120, true, 10, 10);              // 顶格再向上＝钳在 400
            double afterTop = pv.ZoomPercentForTest;
            pv.ZoomToForTest(25);
            pv.WheelForTest(-120, true, 10, 10);             // 底格再向下＝钳在 25
            double afterBottom = pv.ZoomPercentForTest;
            sb.AppendLine("— 钳制：设 400 后向上滚=" + afterTop + "%　设 25 后向下滚=" + afterBottom + "%");
            Check(bad, "增1 缩放范围 25%~400% 钳死（顶/底继续滚不外扩、不报错）", afterTop == 400 && afterBottom == 25);

            // 同步重渲染的实际代价（报备用数字，不靠估）：档位越高出图像素越多，UI 线程同步跑
            double ms150 = RerenderMs(pv, 150), ms400 = RerenderMs(pv, 400);
            sb.AppendLine("— 同步重渲染耗时（当场量）：150% 一档 " + ms150.ToString("0") + " ms　400% 一档 " + ms400.ToString("0")
                + " ms　400% 出图宽 " + (int)Math.Round(pv.PagePtWidthForTest * PreviewWindow.RenderDpi(400) / 72.0) + " 像素（A4 短边）");

            // 缩略图不随缩放变（开工令：缩放只影响大页）
            double tw = ThumbImageWidth(pv);
            pv.ZoomToForTest(200);
            PumpToLoaded();
            double tw2 = ThumbImageWidth(pv);
            sb.AppendLine("— 缩略图宽：缩放 200% 前=" + tw + " 后=" + tw2 + "（大页此时宽=" + pv.SheetWidthForTest.ToString("0.0") + " DIP）");
            Check(bad, "增1 缩放只作用在大页，缩略图尺寸不动", tw > 0 && Math.Abs(tw2 - tw) < 0.01 && pv.SheetWidthForTest > tw);

            // Ctrl+0 / Ctrl+1 两个恢复口
            pv.ZoomToForTest(300);
            pv.ZoomToFit();
            bool fit = pv.FitModeForTest;
            double fitPct = pv.ZoomPercentForTest;
            pv.ZoomToActual();
            double actual = pv.ZoomPercentForTest;
            sb.AppendLine("— 恢复：Ctrl+0 后 适合页面模式=" + fit + "（算出 " + fitPct + "%）　Ctrl+1 后=" + actual + "% 读数=\"" + pv.ZoomReadoutForTest + "\"");
            Check(bad, "增1 Ctrl+0＝适合页面（回到按可视区算的比例）、Ctrl+1＝实际像素 100%",
                fit && fitPct > 0 && Math.Abs(actual - 100) < 0.01 && pv.ZoomReadoutForTest.Contains("100%"));

            // 英文态读数
            App.ApplyLanguage(AppLanguage.EnUS);
            pv.ZoomToForTest(150);
            string en = pv.ZoomReadoutForTest;
            App.ApplyLanguage(AppLanguage.ZhCN);
            sb.AppendLine("— 英文态读数：" + en);
            Check(bad, "增1 缩放读数双语成对（英文态＝Zoom 150%，不出现未解析键名）",
                en != null && en.Contains("Zoom 150%") && !en.StartsWith("s.Pv"));
            pv.Close();
            return bad;
        }

        // ===== 47 号·修2：信息条「−」「＋」缩放按钮 =====
        private static List<string> V492cZoomButtons(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            string pdf = Path.Combine(dir, "zoombtn.pdf");
            BuildInkPdf(pdf, 2, 10, 209.5);
            var w = new MainWindow();
            w.ShowPanelForTest("print");
            var row = w.AddRowForTest(new PrintJob { FilePath = pdf, Kind = DocKind.Pdf });
            var pv = w.OpenPreviewForTest(row);
            pv.LoadForTest(pdf, PageOverflowScanner.Scan(pdf), null);
            PumpToLoaded();

            pv.ZoomToForTest(100);
            PumpToLoaded();
            int r0 = pv.RerenderCountForTest;
            pv.ZoomStepForTest(1);                        // 「＋」＝放大一档（按钮 Click 走的就是这一句）
            PumpToLoaded();
            double up = pv.ZoomPercentForTest;
            int r1 = pv.RerenderCountForTest;
            string readUp = pv.ZoomReadoutForTest;
            double dpiUp = pv.LastRenderDpiForTest;
            pv.ZoomStepForTest(-1);                       // 「−」＝缩小一档
            PumpToLoaded();
            double down = pv.ZoomPercentForTest;
            int r2 = pv.RerenderCountForTest;
            sb.AppendLine("— 按钮：100 点＋=" + up + "%（读数「" + readUp + "」出图 DPI=" + dpiUp + "，重渲染 " + r0 + "→" + r1
                + "）　再点−=" + down + "%（重渲染 " + r1 + "→" + r2 + "）");
            Check(bad, "修2 「＋」100→150、「−」150→100，与滚轮同一档位序列（同一段 ZoomStep→NextZoom+ZoomTo，只是不传锚点）",
                up == 150 && down == 100);
            Check(bad, "修2 每点一次按新档重新出图（重渲染计数逐次 +1、出图 DPI＝96×百分比），不是拉伸旧位图",
                r1 == r0 + 1 && r2 == r1 + 1 && Math.Abs(dpiUp - PreviewWindow.RenderDpi(150)) < 0.01);
            Check(bad, "修2 缩放读数随按钮同步（「视图缩放 150%」这一档取整显示）",
                readUp != null && readUp.Contains("150%") && !readUp.StartsWith("s.Pv"));

            pv.ZoomToForTest(400);
            pv.ZoomStepForTest(1);                        // 顶格再点＋＝钳在 400
            double atTop = pv.ZoomPercentForTest;
            pv.ZoomToForTest(25);
            pv.ZoomStepForTest(-1);                       // 底格再点−＝钳在 25
            double atBottom = pv.ZoomPercentForTest;
            sb.AppendLine("— 钳制：400 点＋=" + atTop + "%　25 点−=" + atBottom + "%");
            Check(bad, "修2 到顶／到底再点＝钳制不动（不外扩、不报错；按开工令按钮不置灰，钳制即边界反馈）",
                atTop == 400 && atBottom == 25);
            pv.Close();
            return bad;
        }

        // ===== 47 号·修3 翻页（按钮／PgUp·PgDn 统一走 GoToPage）＋ 修1 非 Ctrl 滚轮不再翻页 =====
        private static List<string> V492cPaging(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            string pdf = Path.Combine(dir, "three.pdf");
            BuildInkPdf(pdf, 3, 10, 209.5);
            var w = new MainWindow();
            w.ShowPanelForTest("print");
            var row = w.AddRowForTest(new PrintJob { FilePath = pdf, Kind = DocKind.Pdf });
            var pv = w.OpenPreviewForTest(row);
            pv.LoadForTest(pdf, PageOverflowScanner.Scan(pdf), null);
            PumpToLoaded();
            int n = pv.Pages.Count;
            string read0 = pv.PageReadoutForTest;
            sb.AppendLine("— 页数：" + n + "（三页文档）　首页读数=\"" + read0 + "\"");

            pv.GoToPage(pv.ShownIndex + 1); int i1 = pv.ShownIndex;
            pv.GoToPage(pv.ShownIndex + 1); int i2 = pv.ShownIndex;
            pv.GoToPage(pv.ShownIndex - 1); int i3 = pv.ShownIndex;
            sb.AppendLine("— GoToPage：下=" + i1 + " 再下=" + i2 + " 上=" + i3 + "　读数=\"" + pv.PageReadoutForTest + "\"");
            Check(bad, "修3 GoToPage 前进／后退各走一页（「下一页」「上一页」按钮与 PgUp／PgDn 都调它），页码指示同步",
                i1 == 1 && i2 == 2 && i3 == 1);
            Check(bad, "修3 页码读数＝「第 x / N 页」随页更新，加载后总页数已填好（不是「第 x / 0 页」半成品、也不是未解析键名）",
                read0 != null && !read0.StartsWith("s.Pv") && read0 == "第 1 / 3 页" && pv.PageReadoutForTest == "第 2 / 3 页");

            pv.GoToPage(n - 1);
            int last = pv.ShownIndex;
            bool nextAtLast = pv.NextPageEnabledForTest, prevAtLast = pv.PrevPageEnabledForTest;
            pv.GoToPage(last + 1); int afterLast = pv.ShownIndex;
            pv.GoToPage(0); int first = pv.ShownIndex;
            bool prevAtFirst = pv.PrevPageEnabledForTest, nextAtFirst = pv.NextPageEnabledForTest;
            pv.GoToPage(-1); int afterFirst = pv.ShownIndex;
            sb.AppendLine("— 边界：末页=" + last + "（「下一页」可用=" + nextAtLast + " 「上一页」可用=" + prevAtLast + "）末页再进=" + afterLast
                + "　首页=" + first + "（「上一页」可用=" + prevAtFirst + " 「下一页」可用=" + nextAtFirst + "）首页再退=" + afterFirst);
            Check(bad, "修3 边界 no-op：末页向前、首页向后都原地不动（不环绕、不弹窗、不报错）",
                last == n - 1 && afterLast == n - 1 && first == 0 && afterFirst == 0);
            Check(bad, "修3 按钮置灰随边界：首页禁「上一页」、末页禁「下一页」，另一侧仍可用（与 GoToPage 的 no-op 双保险）",
                !nextAtLast && prevAtLast && !prevAtFirst && nextAtFirst);

            // 翻页保留缩放
            pv.ZoomToForTest(200);
            PumpToLoaded();
            double before = pv.ZoomPercentForTest;
            int thumbBefore = SelectedThumbIndex(pv);
            pv.GoToPage(pv.ShownIndex + 1);
            double after = pv.ZoomPercentForTest;
            PumpToLoaded();
            int sel = SelectedThumbIndex(pv);
            sb.AppendLine("— 缩放保留：翻页前=" + before + "% 翻页后=" + after + "%（页码 " + pv.ShownIndex + "、出图 DPI=" + pv.LastRenderDpiForTest
                + "）　缩略图高亮：翻页前=" + thumbBefore + " 翻页后=" + sel);
            Check(bad, "修3 翻页保留当前缩放值（新页按同一缩放重新出图，不回到适合页面）",
                Math.Abs(after - before) < 0.01 && after == 200 && pv.LastRenderDpiForTest == PreviewWindow.RenderDpi(200));
            Check(bad, "修3 翻页时缩略图选中态同步切换（高亮那一格就是正在显示的那一页）",
                sel == pv.ShownIndex && thumbBefore >= 0 && sel == thumbBefore + 1);

            // 修1：非 Ctrl 滚轮什么都不做（既不翻页也不缩放）
            int pageB = pv.ShownIndex; double zoomB = pv.ZoomPercentForTest; int rerB = pv.RerenderCountForTest;
            pv.WheelForTest(-120, false, 0, 0);
            pv.WheelForTest(120, false, 0, 0);
            int pageA = pv.ShownIndex; double zoomA = pv.ZoomPercentForTest; int rerA = pv.RerenderCountForTest;
            sb.AppendLine("— 修1 非 Ctrl 滚轮（上下各一轮）：前 页=" + pageB + " 缩放=" + zoomB + "% 重渲染=" + rerB
                + " → 后 页=" + pageA + " 缩放=" + zoomA + "% 重渲染=" + rerA);
            Check(bad, "修1 取消滚轮翻页：非 Ctrl 滚轮后页码不变、缩放不变、一次图都不重出（滚动权已交还 ScrollViewer 做页内滚动）",
                pageA == pageB && Math.Abs(zoomA - zoomB) < 0.01 && rerA == rerB);

            // Ctrl 滚轮仍只缩放不翻页
            pv.WheelForTest(120, true, 5, 5);
            int pageAfterCtrl = pv.ShownIndex; double zoomAfterCtrl = pv.ZoomPercentForTest;
            sb.AppendLine("— Ctrl 滚轮：页=" + pageAfterCtrl + "（滚前 " + pageA + "）缩放=" + zoomAfterCtrl + "%");
            Check(bad, "增1 与修1 合起来：按 Ctrl 的滚轮仍只缩放、不翻页", pageAfterCtrl == pageA && zoomAfterCtrl != zoomB);

            // 键盘：PgUp／PgDn 与按钮同一段；Ctrl+0／Ctrl+1 原样；无关键不吞
            bool h1 = pv.KeyForTest(false, Key.PageDown); int kDown = pv.ShownIndex;
            bool h2 = pv.KeyForTest(false, Key.PageUp); int kUp = pv.ShownIndex;
            bool h3 = pv.KeyForTest(false, Key.A);
            bool h4 = pv.KeyForTest(true, Key.D0); bool fit = pv.FitModeForTest;
            bool h5 = pv.KeyForTest(true, Key.NumPad1); double actual = pv.ZoomPercentForTest;
            sb.AppendLine("— 键盘：PgDn 已处理=" + h1 + " 页→" + kDown + "　PgUp 已处理=" + h2 + " 页→" + kUp
                + "　无关键 A 已处理=" + h3 + "　Ctrl+0 已处理=" + h4 + " 适合页面=" + fit
                + "　Ctrl+1(小键盘) 已处理=" + h5 + " 缩放=" + actual + "%");
            Check(bad, "修3 PgUp／PgDn 与按钮走同一段 GoToPage（键盘意图→下一页／上一页各一档）",
                h1 && kDown == pageAfterCtrl + 1 && h2 && kUp == kDown - 1);
            Check(bad, "修3 不吞无关按键：非 Ctrl 的普通键不置 Handled（否则文本框一类会收不到键）", !h3);
            Check(bad, "增1 两个恢复口原样保留：Ctrl+0＝适合页面、Ctrl+1（含小键盘 NumPad1）＝实际像素 100%",
                h4 && fit && h5 && Math.Abs(actual - 100) < 0.01);
            pv.Close();
            return bad;
        }

        /// <summary>第一格缩略图里位图的宽度（走逻辑树：无头环境里 ItemsControl 的可视树还没建起来）。</summary>
        private static double ThumbImageWidth(PreviewWindow pv)
        {
            var items = pv.Thumbs.Items;
            if (items.Count == 0) return 0;
            var sp = items[0] as StackPanel;
            var bd = sp == null ? null : sp.Children[0] as Border;
            var inner = bd == null ? null : bd.Child as DependencyObject;
            var g = inner as Grid;                      // 超界页会被包一层 ⚠ 角标
            var img = g != null ? g.Children.OfType<Image>().FirstOrDefault() : inner as Image;
            return img == null ? 0 : img.Width;
        }

        /// <summary>读缩略图列里当前高亮（2px 描边）那一格的下标。</summary>
        private static int SelectedThumbIndex(PreviewWindow pv)
        {
            var items = pv.Thumbs.Items;
            for (int k = 0; k < items.Count; k++)
            {
                var sp = items[k] as StackPanel;
                var bd = sp == null ? null : sp.Children[0] as Border;
                if (bd != null && bd.BorderThickness.Left >= 2) return k;
            }
            return -1;
        }

        // ===== 渲染取证 =====
        /// <summary>把大页视口横向滚到右缘，返回实际滚到的偏移（无头下必须先量一遍布局才拿得到 ExtentWidth）。</summary>
        private static double ScrollToSheetRight(PreviewWindow pv)
        {
            var sc = pv.PageScrollForTest;
            if (sc == null) return -1;
            sc.UpdateLayout();
            sc.ScrollToHorizontalOffset(sc.ExtentWidth);
            sc.UpdateLayout();
            return sc.HorizontalOffset;
        }

        internal static int V492cRender(string log)        {
            var sb = new StringBuilder();
            int code = 0;
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV492cR-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                string pdf = Path.Combine(dir, "near2.pdf");
                BuildInkPdf(pdf, 2, 10, 209.5);
                var scan = PageOverflowScanner.Scan(pdf);

                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";
                    var w = new MainWindow();
                    w.ShowPanelForTest("print");
                    var job = new PrintJob { FilePath = pdf, Kind = DocKind.Pdf };
                    var row = w.AddRowForTest(job);
                    var pv = w.OpenPreviewForTest(row);
                    pv.LoadForTest(pdf, scan, null);
                    pv.ZoomToForTest(200);
                    // 浅色停在左上（看放大后文字是否锐利＝重新出图而非拉伸旧位图）；深色把视口横向滚到右缘（看滚动条生效＋叠加层随页同步放大）
                    bool scrollRight = th == AppTheme.Dark;
                    double scrolledTo = -1;
                    PumpToLoaded();
                    code |= RenderWindow("V492c缩放200" + ts + "自检.txt", "V492c预览缩放200-" + ts + ".png", pv, 1080, 720, new string[0], 0,
                        scrollRight ? (Action)(() => { scrolledTo = ScrollToSheetRight(pv); }) : null);
                    sb.AppendLine("—" + ts + "：预览大页 200% 态，信息条读数「视图缩放 200%」，页面宽 " + pv.SheetWidthForTest.ToString("0.#") + " DIP＞可视区；"
                        + "信息条左侧翻页组读数「" + pv.PageReadoutForTest + "」（「上一页」此时可用=" + pv.PrevPageEnabledForTest + "），右侧缩放组＝−／读数／＋／适合页面／实际大小；"
                        + (scrollRight ? "视口已横向滚到右缘（横向偏移 " + scrolledTo.ToString("0.#") + " DIP）＝滚动条生效，且超界红斜纹与版心虚线框随页同步放大。出图"
                                       : "视口停在左上＝200% 下文字边缘锐利，是按 192 DPI 重新出图而非拉伸旧位图。出图"));
                    pv.Close();
                }

                // 末页边界态：GoToPage 到末页后再进一次＝原地不动，且「下一页」已置灰
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var mw = new MainWindow();
                mw.ShowPanelForTest("print");
                var mj = new PrintJob { FilePath = pdf, Kind = DocKind.Pdf };
                var mrow = mw.AddRowForTest(mj);
                var mpv = mw.OpenPreviewForTest(mrow);
                mpv.LoadForTest(pdf, scan, null);
                mpv.GoToPage(mpv.ShownIndex + 1);           // 下一页 → 末页
                mpv.GoToPage(mpv.ShownIndex + 1);           // 末页再进 → no-op
                PumpToLoaded();
                sb.AppendLine("—末页边界态：当前页下标=" + mpv.ShownIndex + "（共 " + mpv.Pages.Count + " 页）　页码读数=\""
                    + mpv.PageReadoutForTest + "\"　「上一页」可用=" + mpv.PrevPageEnabledForTest + "　「下一页」可用=" + mpv.NextPageEnabledForTest
                    + "　缩放读数=" + mpv.ZoomReadoutForTest);
                code |= RenderWindow("V492c末页边界自检.txt", "V492c预览末页边界-浅色.png", mpv, 1080, 720, new string[0], 0);
                sb.AppendLine("—浅色：末页（缩略图末格高亮、页码读数＝第 N / N 页、「下一页」置灰）＋再进一次不变化（边界 no-op）出图");
                mpv.Close();
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染异常：" + ex);
                code |= 1;
            }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V492c预览交互渲染自检.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            try { Directory.Delete(dir, true); } catch { }
            return code;
        }
    }
}
