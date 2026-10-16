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
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 68 号令断言面现剩两项：四个内置默认模板（T1）与检查更新 found 态布局（T3）。
    /// T2（机器码可选中）与 T4（首启 15 天限时体验）两组随 73 号令功能整体退场——不是改判，是判据已不存在
    /// （没有码、也没有限时体验期），故整组删除并在这里留下一行对账说明。
    /// 铁律沿袭：<b>全程离线、不碰真浏览器、不碰真剪贴板、不依赖真实时间流逝</b>；
    /// 布局断言只钉几何与派生状态（列宽／单行／换行），不钉颜色与措辞字面量。
    /// </summary>
    public static partial class WpfTests
    {
        // ===== 探针小工具 =====

        /// <summary>把元素在指定宽度下排版一遍并回读几何（无窗口渲染口径：Measure→Arrange→UpdateLayout）。</summary>
        private static void V68Layout(FrameworkElement el, double width)
        {
            el.Measure(new Size(width, 1400));
            el.Arrange(new Rect(0, 0, width, Math.Max(1, el.DesiredSize.Height)));
            el.UpdateLayout();
        }

        /// <summary>四枚内置模板名（当前语言）。</summary>
        private static readonly string[] V68TplKeys =
            { "s.TplBuiltinA4MonoSimplex", "s.TplBuiltinA4MonoDuplex", "s.TplBuiltinA4ColorSimplex", "s.TplBuiltinA4ColorDuplex" };

        /// <summary>68 号新增文案键（zh/en 各 13 枚）。</summary>
        private static readonly string[] V68Keys =
        {
            "s.TplBuiltinA4MonoSimplex", "s.TplBuiltinA4MonoDuplex", "s.TplBuiltinA4ColorSimplex", "s.TplBuiltinA4ColorDuplex",
            "s.TplRestoreBuiltin", "s.TplRestoredFmt", "s.TplBuiltinHint",
        };

        internal static string V68Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();

            var lang0 = SettingsService.Language;
            var madeOwn = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                // ===== ⑤ T1 迁移与预置：builtin 列＋app_meta 表、四枚在位、参数逐项对 =====
                string schema = SqliteStore.BuiltinSchemaCheck();
                sb.AppendLine("— 68 号 库结构与标记落库：" + schema);
                Check(bad, "68 号 库迁移在位：print_templates.builtin 列存在、app_meta 可写读（内置标记与限时体验标记都不占设置键账）",
                    schema.StartsWith("通过", StringComparison.Ordinal));
                SqliteStore.EnsureDb();
                foreach (string n in BuiltinTemplates.Names()) { try { SqliteStore.DeleteTemplate(n); } catch { } }
                int seeded = BuiltinTemplates.Seed();
                var all = SqliteStore.LoadTemplates();
                var names = all.Select(t => t.Name).ToList();
                var builtins = BuiltinTemplates.Names();
                bool four = seeded == 4 && builtins.Count == 4 && builtins.All(n => names.Contains(n));
                bool flagOn = builtins.All(n => SqliteStore.IsBuiltinTemplate(n));
                var mine0 = SqliteStore.UserTemplateNames();
                bool notCounted = !builtins.Any(n => mine0.Contains(n));
                var optMap = all.ToDictionary(t => t.Name, t => t.Options);
                bool p1 = Opt(optMap, "s.TplBuiltinA4MonoSimplex", DuplexMode.Simplex, ColorMode.BlackWhite);
                bool p2 = Opt(optMap, "s.TplBuiltinA4MonoDuplex", DuplexMode.LongEdge, ColorMode.BlackWhite);
                bool p3 = Opt(optMap, "s.TplBuiltinA4ColorSimplex", DuplexMode.Simplex, ColorMode.Color);
                bool p4 = Opt(optMap, "s.TplBuiltinA4ColorDuplex", DuplexMode.LongEdge, ColorMode.Color);
                bool restDefault = builtins.All(n => { var o = optMap.ContainsKey(n) ? optMap[n] : null; return o != null && o.Paper == System.Drawing.Printing.PaperKind.A4 && o.Orientation == OrientationMode.Auto && o.PagesPerSheet == 1 && !o.Booklet && o.PageParity == PageParityMode.All && string.IsNullOrEmpty(o.PaperSourceName) && o.Scale == ScaleMode.FitPage; });
                sb.AppendLine("— 68 号 首启预置：本次新增 " + seeded + " 枚；库里共 " + all.Count + " 枚、其中自建 " + mine0.Count
                    + " 枚；双面长边=" + p2 + " 彩色双面=" + p4 + "；其余项走默认=" + restDefault);
                Check(bad, "68 号 模板库为空时预置四枚默认模板（黑白/彩色 × 单面/双面-长边，纸型 A4，其余默认），并带 builtin 标记",
                    four && flagOn && p1 && p2 && p3 && p4 && restDefault);
                Check(bad, "68 号 内置模板不吃自建名额：UserTemplateNames 只列 builtin=0 那几枚（四枚内置都不在其中）", notCounted);
                int again = BuiltinTemplates.Seed();
                Check(bad, "68 号 预置幂等：再点一次「恢复默认模板」不多插、不覆盖（库里名字与条数都不变）",
                    again == 0 && SqliteStore.LoadTemplates().Count == all.Count);
                string metaBefore = SqliteStore.GetMeta(BuiltinTemplates.MetaSeeded);
                int ensureSecond = BuiltinTemplates.EnsureSeeded();
                Check(bad, "68 号 EnsureSeeded 只做一次（app_meta 记着预置时刻：" + (string.IsNullOrEmpty(metaBefore) ? "本段先补的" : "已有") + "），第二次直接返回 0", ensureSecond == 0);

                // ===== ⑦ T1 恢复入口：删光内置→点「恢复默认模板」回填、自建一个没动 =====
                foreach (string n in builtins) { try { SqliteStore.DeleteTemplate(n); } catch { } }
                int ownBefore = SqliteStore.UserTemplateNames().Count;
                var panel = new PrintSettingsPanel(new PrintOptions(), "");
                bool btnInPlace = panel.FindName("BtnRestoreTemplates") is Button;
                int restored = panel.PressRestoreTemplates();
                string note = panel.ShowRestoreNote(restored);      // 点击链路的后半句（Click 处理器＝这两句，自检逐句走）
                var namesAfter = SqliteStore.LoadTemplates().Select(t => t.Name).ToList();
                int ownAfter = SqliteStore.UserTemplateNames().Count;
                bool tipShown = panel.RestoreNoteShownForTest;
                sb.AppendLine("— 68 号 恢复入口：按钮在位=" + btnInPlace + " 补回数=" + restored + " 提示=\"" + note + "\" 可见=" + tipShown
                    + "；回填后四枚都在=" + builtins.All(n => namesAfter.Contains(n)) + " 自建数 " + ownBefore + "→" + ownAfter);
                Check(bad, "68 号 默认模板可删、删光后「恢复默认模板」一次补齐四枚，且用户自建模板一个没动（数与名都不变）",
                    btnInPlace && restored == 4 && builtins.All(n => namesAfter.Contains(n)) && ownAfter == ownBefore && tipShown);
                Check(bad, "68 号 恢复提示文案取资源键 s.TplRestoredFmt（把补齐枚数写进句子里，不弹模态）",
                    !string.IsNullOrEmpty(note) && note == K("s.TplRestoredFmt").Replace("{0}", restored.ToString()));

                // ===== ⑨ T3 检查更新 found 态布局（三档窗宽 × 中英双语：标题不竖排、说明在卡片内换行） =====
                var cur = typeof(AboutPage).Assembly.GetName().Version;
                var longEntry = new UpdateChecker.Entry
                {
                    Version = "1.0.1.0",
                    Url = UpdateChecker.ApiBase + "/dl/PrintPile-Setup-v1.0.1.0.exe",
                    Active = true,
                    Date = "2026-10-11",
                    Notes = "1) 修复批量打印后托盘图标偶发残留的问题；2) 历史记录支持按打印机名筛选与导出；3) 新增左下角橙色更新提示与弹窗内直接下载；4) 转换引擎在部分 WPS 版本下不再重复弹窗；5) 预览窗口翻页更跟手"
                };
                var found = UpdateChecker.Result.Found(longEntry);
                foreach (var cfg in new[] {
                    Tuple.Create("中文", AppLanguage.ZhCN, new[] { 960, 1120, 1400 }),
                    Tuple.Create("英文", AppLanguage.EnUS, new[] { 960, 1120, 1400 }) })
                {
                    App.ApplyLanguage(cfg.Item2);
                    foreach (int w in cfg.Item3)
                    {
                        var mw = new MainWindow();
                        mw.ShowPanelForTest("about");
                        mw.UpdateLayoutForTest(w, 700);
                        PumpToLoaded();
                        var ap = FindByType(mw.Content as FrameworkElement, typeof(AboutPage)).Cast<AboutPage>().FirstOrDefault() ?? new AboutPage();
                        ap.ShowInline(found, cur, "");
                        mw.UpdateLayoutForTest(w, 700);
                        PumpToLoaded();
                        var hdr = ap.UpdateHeaderPanelForTest as FrameworkElement;
                        var title = ap.UpdateTitleBlockForTest as FrameworkElement;
                        var result = ap.UpdateResultBlockForTest as FrameworkElement;
                        var card = ap.UpdateCardForTest as FrameworkElement;
                        if (title != null) { title.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); }
                        double wantTitle = title == null ? 0 : title.DesiredSize.Width;
                        double hdrW = hdr == null ? 0 : hdr.ActualWidth;
                        double titleW = title == null ? 0 : title.ActualWidth;
                        double titleH = title == null ? 0 : title.ActualHeight;
                        double resW = result == null ? 0 : result.ActualWidth;
                        double resH = result == null ? 0 : result.ActualHeight;
                        double cardW = card == null ? 0 : card.ActualWidth;
                        double cardRight = card == null ? 0 : card.ActualWidth;
                        double resRight = result == null ? 0 : V47Box(result, card)[2];
                        bool oneLine = titleH > 0 && titleH <= 26;
                        bool headWide = hdrW >= 230 - 0.5;
                        bool titleFits = titleW + 0.5 >= wantTitle;
                        bool wrapped = resW >= 60 && resH > 26;
                        bool inside = resRight <= cardRight + 0.5 && resW <= 300 + 0.5;
                        sb.AppendLine("— 68 号 布局 " + cfg.Item1 + " 窗宽 " + w + "：标题列实得=" + hdrW.ToString("0.#")
                            + " 标题单行宽/实得=" + wantTitle.ToString("0.#") + "/" + titleW.ToString("0.#") + " 标题高=" + titleH.ToString("0.#")
                            + " 说明块宽/高=" + resW.ToString("0.#") + "/" + resH.ToString("0.#") + " 卡片宽=" + cardW.ToString("0.#")
                            + " 说明右缘−卡片右缘=" + (cardRight - resRight).ToString("0.#")
                            + " → 不竖排=" + (oneLine && titleFits) + " 换行不越界=" + (wrapped && inside));
                        Check(bad, "68 号 T3 布局 " + cfg.Item1 + " 窗宽 " + w + "：标题「检查更新」保持单行不竖排（高 " + titleH.ToString("0.#")
                            + "）、标题列不被挤（" + hdrW.ToString("0.#") + "≥230）、说明在卡片内换行（宽 " + resW.ToString("0.#") + "）",
                            oneLine && headWide && titleFits && wrapped && inside);
                    }
                }
                App.ApplyLanguage(AppLanguage.ZhCN);

                // ===== ⑬ 剩余 7 枚模板文案键在当前语言全部取到值（73 号令：68 号那 13 枚里限时体验相关的 6 枚已成对删除） =====
                var missing = V68Keys.Where(k => { var v = K(k); return string.IsNullOrEmpty(v) || v == k; }).ToList();
                sb.AppendLine("— 68 号 在位文案键：" + V68Keys.Length + " 枚，取不到值的=" + missing.Count + (missing.Count == 0 ? "" : "：" + string.Join(",", missing)));
                Check(bad, "68 号 剩余 7 枚模板文案键在当前语言全部取到值（中英成对由资源键守护那条核统一管）", missing.Count == 0);
            }
            catch (Exception ex)
            {
                sb.AppendLine("探针异常：" + ex);
                bad.Add("异常 " + ex.Message);
            }
            finally
            {
                foreach (string n in madeOwn) { try { SqliteStore.DeleteTemplate(n); } catch { } }
                try { foreach (string n in BuiltinTemplates.Names()) SqliteStore.SaveBuiltinTemplate(n, BuiltinTemplates.OptionsFor(n)); } catch { }
                App.ApplyLanguage(lang0);
                SqliteStore.SetMeta(BuiltinTemplates.MetaSeeded, "");   // 自检不替产品把"预置过"这件事钉死（首启仍会自己补）
            }
            WriteDiag("68号四项改进定位.txt", sb.ToString());
            return bad.Count == 0
                ? "通过（68 号剩余两组：T1 库迁移在位（builtin 列＋app_meta）、空库预置四枚默认模板且参数逐项核对、删光后「恢复默认模板」一次补齐四枚且自建零变化、提示文案取键；"
                  + "T3 检查更新 found 态三档窗宽×中英双语各测：标题单行不竖排、标题列≥230、说明在卡片内换行不越界；"
                  + "7 枚在位文案键全取到值；全程离线不碰真剪贴板与真浏览器。"
                  + "73 号令退场项不再断言：限时体验判定与叠加层真值表、开始限时体验广播、机器码只读可选中、许可区三档态与弹窗通路（判据已随功能不存在）；逐条数字见 68号四项改进定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>取某一枚内置模板的参数并按"双面方式＋颜色"核对（纸型与其余项在另一条断言里统一核）。</summary>
        private static bool Opt(Dictionary<string, PrintOptions> map, string key, DuplexMode duplex, ColorMode color)
        {
            string name = K(key);
            PrintOptions o;
            if (!map.TryGetValue(name, out o) || o == null) return false;
            return o.Duplex == duplex && o.Color == color;
        }

        /// <summary>68 号令渲染取证（73 号令改判：删掉限时体验弹窗两张与关于页许可区那张，剩检查更新 found 态与打印设置模板行两张）。</summary>
        internal static int V68Render(string log)
        {
            int code = 0;
            var sb = new StringBuilder();
            var lang0 = SettingsService.Language;
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                var cur = typeof(AboutPage).Assembly.GetName().Version;
                var entry = new UpdateChecker.Entry
                {
                    Version = "1.0.1.0",
                    Url = UpdateChecker.ApiBase + "/dl/PrintPile-Setup-v1.0.1.0.exe",
                    Active = true,
                    Date = "2026-10-11",
                    Notes = "1) 修复批量打印后托盘图标偶发残留的问题；2) 历史记录支持按打印机名筛选与导出；3) 新增左下角橙色更新提示与弹窗内直接下载"
                };
                var page = new AboutPage();
                page.ShowInlineForTest(AboutPage.InlineFor(UpdateChecker.Result.Found(entry), cur), true);
                code |= RenderControl("68号更新卡found态.txt", "68号检查更新-found态.png", page, 700, 560);
                sb.AppendLine("① 检查更新 found 态（窄窗 700 出图：标题「检查更新」单行不竖排、内联说明在卡片内换行）");

                var panelHost = new PrintSettingsPanel(new PrintOptions(), "");
                SqliteStore.EnsureDb();
                var ownNames = new List<string>();
                for (int i = 1; i <= 5; i++) ownNames.Add("V68模板样本" + i);
                int before = SqliteStore.UserTemplateNames().Count;
                foreach (string n in ownNames) SqliteStore.SaveTemplate(n, new PrintOptions { Duplex = DuplexMode.LongEdge });
                panelHost.PressRestoreTemplates();
                V68Layout(panelHost, 860);
                code |= RenderPadded(panelHost, "68号模板行态.txt", "68号打印设置-模板行.png", 860, 620);
                sb.AppendLine("② 打印设置模板行：四枚默认模板在位＋五枚自建、「恢复默认模板」入口在位（73 号令起模板数量不限）");
                foreach (string n in ownNames) { try { SqliteStore.DeleteTemplate(n); } catch { } }
                sb.AppendLine("　　（渲染期临时补的 5 枚自建模板已删干净：删前自建 " + before + " 枚，删后 " + SqliteStore.UserTemplateNames().Count + " 枚）");


                App.ApplyLanguage(lang0);
            }
            catch (Exception ex) { sb.AppendLine("渲染异常：" + ex); code |= 1; }
            finally
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            string txt = sb.ToString();
            txt += "\r\n说明：68 号令两张取证（73 号令删掉限时体验弹窗两张与关于页许可区那张）——检查更新 found 态窄窗不竖排、打印设置模板行（内置四枚＋自建五枚同列）。\r\n";
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }

        /// <summary>把一只没装进窗口的控件垫一层主题底色出图（同 66 号 RenderDialog 口径，用于 UserControl 整体）。</summary>
        private static int RenderPadded(FrameworkElement content, string logName, string pngName, double width, double height)
        {
            var parent = content.Parent as Panel;
            if (parent != null) parent.Children.Remove(content);
            var host = new Border
            {
                Background = (Brush)(Application.Current != null ? Application.Current.TryFindResource("WindowBgBrush") : null) ?? Brushes.White,
                Child = content
            };
            return RenderControl(logName, pngName, host, width, height);
        }
    }
}
