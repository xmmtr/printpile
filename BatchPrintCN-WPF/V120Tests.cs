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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 73 号令断言面（V1.2.0 全面 free & open）：解锁体系与功能上限整体拆除后的四组守护。
    ///
    /// 与"改判"不同，这里钉的是**结构性的不在场**：类型不在、控件不在、字样不在、限额不在——
    /// 目的是让"哪天又把码或上限悄悄加回来"这件事必须先把这几条断言改绿，改断言就得过一遍报备。
    /// 全程不打网络、不开浏览器、不碰真剪贴板；settings.json 探针写在临时目录副本上，跑完复原。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>
        /// 被禁字样（73 号令质量门槛原文那六个）——资源键文案里一个都不许再出现。
        /// 字面量故意拆开拼：否则本文件自己就成了 grep 的自污染源（V4.7 那条"旧域 0 残留"守卫同一手法）。
        /// </summary>
        private static readonly string[] BannedWords = { "商" + "业", "授" + "权", "激" + "活", "试" + "用", "购" + "买", "付" + "费" };

        internal static string V120Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            string oldCfg = null;
            var storePath0 = WatchRuleStore.PathOverride;
            string workDir = null;
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                // ===== ① 解锁体系的类型必须彻底不在场（两个程序集都查） =====
                string[] goneTypes = { "FeatureGate", "Ed25519", "LicenseStore", "TrialStore", "TrialDialog", "TrialFlow", "ActivationDialog" };
                var asms = new[] { typeof(WpfTests).Assembly, typeof(PrintOptions).Assembly };
                var stillThere = new List<string>();
                foreach (string tn in goneTypes)
                    foreach (var a in asms)
                        if (a.GetTypes().Any(t => string.Equals(t.Name, tn, StringComparison.Ordinal))) stillThere.Add(tn);
                int total = asms.Sum(a => { try { return a.GetTypes().Length; } catch { return 0; } });
                sb.AppendLine("— ① 类型清查：两程序集合计 " + total + " 个类型里，" + string.Join("/", goneTypes) + " 仍在场 " + stillThere.Count + " 个");
                Check(bad, "73 号 结构守卫 (a)：码制/验签/限时体验期/对话框那套类型在两个程序集里都已不存在（要加回来必须先改这条断言并报备）", stillThere.Count == 0);

                // 门控判定入口的调用点清查（FeatureGate.xxx 之类字面量）属构建期检查，交给发布脚本的 b 组门；
                // 自检不去假装扫源码——运行目录里没有源码，扫了也算不得数。

                // ===== ② 老用户遗留键容忍：读不报错、回写不再带这两枚、别的键一个不丢 =====
                string cfgPath = AppPaths.SettingsPath;
                try { if (File.Exists(cfgPath)) oldCfg = File.ReadAllText(cfgPath); } catch { }
                string legacy = "{\"Theme\":\"dark\",\"Language\":\"en\",\"LicenseCode\":\"OLD-CODE-1234\",\"TrialStart\":\"2026-10-01T00:00:00.0000000Z\",\"ApiBase\":\"https://legacy.example\",\"HistoryLimit\":\"unlimited\"}";
                bool loadOk = true;
                try
                {
                    File.WriteAllText(cfgPath, legacy);
                    SettingsService.Load();
                }
                catch (Exception ex) { loadOk = false; sb.AppendLine("— ② 读老配置异常：" + ex.Message); }
                string after = "";
                try
                {
                    SettingsService.Save();
                    after = File.ReadAllText(cfgPath);
                }
                catch (Exception ex) { sb.AppendLine("— ② 回写异常：" + ex.Message); }
                bool tolerant = loadOk
                    && !after.Contains("LicenseCode") && !after.Contains("TrialStart")     // 不回写、也不保留
                    && !after.Contains("ApiBase")                                            // 早先定的"不认识就不带"口径仍然成立
                    && after.Contains("\"dark\"") && after.Contains("en")                    // 认识的键一个不丢
                    && SettingsService.Language == AppLanguage.EnUS && SettingsService.HistoryLimit == HistoryLimit.Unlimited;
                sb.AppendLine("— ② 老 settings.json（含两枚解锁遗留键）读回不报错=" + loadOk
                    + "；回写后仍带遗留键=" + (after.Contains("LicenseCode") || after.Contains("TrialStart"))
                    + "；主题/语言/档位照旧=" + tolerant);
                Check(bad, "73 号 数据兼容：老用户 settings.json 里的解锁遗留键＝读取忽略、回写不再带、不报错也不崩（用户数据不主动清理，只是不再认识这两枚）", tolerant);
                try
                {
                    if (oldCfg != null) File.WriteAllText(cfgPath, oldCfg);
                    else if (File.Exists(cfgPath)) File.Delete(cfgPath);
                    SettingsService.Load();
                }
                catch { }

                // ===== ③ 全功能可用矩阵（不再有任何前置条件） =====
                var hp = new HistoryPage();
                bool csvOn = hp.ExportButtonForTest != null && hp.ExportButtonForTest.IsEnabled;
                var box = hp.FindName("LimitBox") as ComboBox;
                int tiers = box == null ? -1 : box.Items.Count;
                bool unlimitedTier = box != null && box.Items.Cast<object>().Select(o => o as string)
                    .Any(t => t == K("s.HistoryLimitUnlimited"));
                bool noCap = HistoryPage.LimitRows(HistoryLimit.Unlimited) == 0 && HistoryPage.LimitRows(HistoryLimit.R200) == 200;

                workDir = Path.Combine(Path.GetTempPath(), "pp-120-watch-" + Process.GetCurrentProcess().Id);
                try { Directory.CreateDirectory(workDir); } catch { }
                WatchRuleStore.PathOverride = () => Path.Combine(workDir, "watch-rules.json");
                var wr = WatchRule.New(workDir); wr.Name = "开源态"; wr.Enabled = true;
                WatchEngine.ResetForTest();
                WatchEngine.Apply(new List<WatchRule> { wr });
                int tracked = WatchEngine.TrackedRuleCount;
                var wp = new WatchPage();
                wp.Refresh();
                bool watchOpen = wp.AddEnabledForTest && tracked == 1;
                WatchEngine.ResetForTest(); WatchEngine.Stop();

                sb.AppendLine("— ③ 可用矩阵：导出 CSV 常亮=" + csvOn + "；历史档位 " + tiers + " 项含无限制=" + unlimitedTier
                    + "；无截断（Unlimited→" + HistoryPage.LimitRows(HistoryLimit.Unlimited) + "）=" + noCap
                    + "；监视页新建可用且规则起了观察者=" + watchOpen);
                Check(bad, "73 号 全功能可用 (a)：导出 CSV 不再受任何状态影响（按钮常亮，OnExport 里那道闸也删了）", csvOn);
                Check(bad, "73 号 全功能可用 (b)：历史档位五项齐（含「无限制」）且选它就真不截断", tiers == 5 && unlimitedTier && noCap);
                Check(bad, "73 号 全功能可用 (c)：监视页任何时候可新建规则，启用中的规则一律起观察者", watchOpen);

                // ===== ④ 关于页：解锁区与「功能一览」都不在、开源声明在位、仓库双按钮在位（中英各一份） =====
                var about = new AboutPage();
                bool goneArea = about.FindName("LicenseCard") == null && about.FindName("BtnActivate") == null
                    && about.FindName("BtnContact") == null && about.FindName("MachineCodeText") == null
                    && about.FindName("LicenseStatusText") == null;
                about.Measure(new Size(900, 1200)); about.Arrange(new Rect(0, 0, 900, 1200)); about.UpdateLayout();
                var rows = new List<StackPanel>();
                CollectFeatRows(about, rows);
                string lic = K("s.AboutLicenseLine");
                var gh = about.GitHubButtonForTest; var gt = about.GiteeButtonForTest;
                bool featGone = rows.Count == 0;
                bool licOk = !string.IsNullOrEmpty(lic) && lic.IndexOf("s.About", StringComparison.Ordinal) < 0;
                bool btnsOk = gh != null && gt != null && (gh.Content as string) == "GitHub" && (gt.Content as string) == "Gitee";
                sb.AppendLine("— ④ 关于页：解锁区控件全不在=" + goneArea + "；功能一览行数=" + rows.Count
                    + "（78 号令已删，应为 0）；开源声明=\"" + lic.Substring(0, Math.Min(28, lic.Length)) + "…\""
                    + "；仓库按钮 GitHub/Gitee 在位且文字对=" + btnsOk);
                Check(bad, "73 号 关于页 (a)：解锁区那五枚控件（卡片/输入码/下单/机器码/状态行）已从页面彻底消失", goneArea);
                Check(bad, "78 号 关于页 (b)：「功能一览」整块已删（featline 行=0）且一行 GPLv3 开源声明取到值", featGone && licOk);
                Check(bad, "78 号 关于页 (c)：GitHub／Gitee 仓库双按钮在位，文字＝品牌名原文、不占资源键", btnsOk);

                App.ApplyLanguage(AppLanguage.EnUS);
                var aboutEn = new AboutPage();
                aboutEn.Measure(new Size(900, 1200)); aboutEn.Arrange(new Rect(0, 0, 900, 1200)); aboutEn.UpdateLayout();
                var rowsEn = new List<StackPanel>();
                CollectFeatRows(aboutEn, rowsEn);
                bool enOk = rowsEn.Count == 0 && K("s.AboutLicenseLine").IndexOf("s.About", StringComparison.Ordinal) < 0;
                sb.AppendLine("— ④ 英文态：功能一览行=0 且开源声明取到英文值=" + enOk);
                Check(bad, "78 号 关于页 (d)：英文界面同样无功能一览行、开源声明无未解析键名", enOk);
                App.ApplyLanguage(AppLanguage.ZhCN);

                // ===== ⑤ 资源文件字样清查：两本字典里被禁六词 0 命中（源码侧由发布脚本 b 组门核） =====
                int hits = 0;
                foreach (string dict in new[] { "Localization/zh-CN.xaml", "Localization/en-US.xaml" })
                {
                    try
                    {
                        var rd = new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) };
                        foreach (object k in rd.Keys)
                        {
                            string v = rd[k] as string;
                            if (v == null) continue;
                            if (BannedWords.Any(w => v.Contains(w))) hits++;
                        }
                    }
                    catch (Exception ex) { sb.AppendLine("— ⑤ 读 " + dict + " 异常：" + ex.Message); hits++; }
                }
                sb.AppendLine("— ⑤ 资源文案里被禁字样命中数=" + hits + "（词表：" + string.Join("/", BannedWords) + "）");
                Check(bad, "73 号 字样门槛：中英两本文案里那六个被禁字样 0 命中（词表见 BannedWords，字面量拆开以免自污染）", hits == 0);

                // ===== ⑥ 版本：1.2.0.0（三位数口径 D-20，第二位进位） =====
                string fv = null, prod = null;
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName);
                    fv = vi.FileVersion; prod = vi.ProductVersion;
                }
                catch { }
                string av = AppVersion.Full, disp = AppVersion.Display;
                sb.AppendLine("— ⑥ 版本：FileVersion=" + fv + " ProductVersion=" + prod + " AssemblyVersion=" + av + " 界面＝" + disp);
                Check(bad, "73 号 版本：FileVersion／AssemblyVersion 均 1.2.0.0，界面显示 v1.2.0",
                    fv == "1.2.0.0" && av == "1.2.0.0" && disp == "v1.2.0");

                // ===== ⑦ 注册表退场（78 号·A1）：预置遗留键 → LegacyCleanup.Run() → 消失；连跑两次幂等不抛 =====
                bool regBefore = LegacyCleanup.LegacyKeyExists();
                try { using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(LegacyCleanup.LegacyKeyPath)) k.SetValue("TrialStart", "2026-01-01T00:00:00Z"); } catch { }
                bool preset = LegacyCleanup.LegacyKeyExists();
                bool cleaned = false, secondOk = true;
                try { LegacyCleanup.Run(); cleaned = !LegacyCleanup.LegacyKeyExists(); LegacyCleanup.Run(); }
                catch (Exception rex) { secondOk = false; sb.AppendLine("— ⑦ 二次 Run 抛异常：" + rex.Message); }
                sb.AppendLine("— ⑦ 注册表：清理前存在=" + regBefore + "；预置成功=" + preset + "；Run 后消失=" + cleaned + "；二次幂等不抛=" + secondOk);
                Check(bad, "78 号 注册表退场 (a)：预置 HKCU\\Software\\PrintPile 后 LegacyCleanup.Run() 删干净", preset && cleaned);
                Check(bad, "78 号 注册表退场 (b)：连跑两次幂等且不抛（键不存在＝无操作）", secondOk);

                // ===== ⑧ 界面三修（78 号·B）：B2 导航键改名＋B1 对话框最长下拉项零裁切 =====
                bool navZh = K("s.NavWatch") == "监视文件夹";
                App.ApplyLanguage(AppLanguage.EnUS);
                bool navEn = K("s.NavWatch") == "Watch Folders";
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("— ⑧ B2 导航键：zh「" + (navZh ? "监视文件夹" : K("s.NavWatch")) + "」=" + navZh
                    + "；en「" + (navEn ? "Watch Folders" : K("s.NavWatch")) + "」=" + navEn);
                Check(bad, "78 号 导航更名：s.NavWatch 中＝监视文件夹、英＝Watch Folders（改写不增删键）", navZh && navEn);

                // B1：监视规则对话框的两个下拉，塞进一条超长候选并选中，实测每个下拉项文字相对其元素零裁切
                string clipDetail;
                bool noClip = MeasureDialogComboClip(out clipDetail);
                sb.AppendLine("— ⑧ B1 对话框下拉零裁切=" + noClip + "（" + clipDetail + "）");
                Check(bad, "78 号 下拉修复：监视规则对话框打印机/模板下拉，最长项文字零裁切（浅/深各测）", noClip);

                sb.AppendLine("— ⑨ 本轮临时目录：" + workDir + "（自检自造自删）");
            }
            catch (Exception ex)
            {
                sb.AppendLine("探针异常：" + ex);
                bad.Add("异常 " + ex.Message);
            }
            finally
            {
                WatchRuleStore.PathOverride = storePath0;
                WatchEngine.ResetForTest();
                WatchEngine.Stop();
                try { if (workDir != null && Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
                try
                {
                    if (oldCfg != null) File.WriteAllText(AppPaths.SettingsPath, oldCfg);
                    SettingsService.Load();
                }
                catch { }
            }
            WriteDiag("73号开源改造定位.txt", sb.ToString());
            return bad.Count == 0
                ? "通过（73/78 号：①码制/验签/限时体验期/对话框那套类型在两个程序集里全部不在场 ②老 settings.json 的遗留键读取忽略、回写不再带、其余键零丢失、不崩 ③导出 CSV 常亮＋历史五档含无限制且不截断＋监视页随时可新建并起观察者 ④关于页解锁区五控件与功能一览整块消失、只留 GPLv3 声明＋GitHub/Gitee 双按钮（中英各一份）⑤中英本文案被禁六词 0 命中 ⑥FileVersion/AssemblyVersion=1.2.0.0 ⑦注册表遗留键预置后 Run 删干净且二次幂等 ⑧导航键改名＋对话框两下拉最长项零裁切；逐条数字见 73号开源改造定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>
        /// 78 号令·B1：实测监视规则对话框两个下拉的文字是否被裁切（浅/深各一遍），并出两张取证图。
        /// 两处都量：①下拉展开后每个 ComboBoxItem 里的文字；②收起状态下 ComboBox 自身显示选中值的那块文字
        /// （需求方反馈的"显示不全"可能落在选中值而非列表项，两处都钉住才敢说零裁切）。
        /// 做法＝RenderDialog 同款：把 Window 的 Content 摘下挂进 Border 离屏实例化→给打印机/模板下拉各塞
        /// 一条比真机最长还长的压力项并选中→用 FormattedText 算文字真实宽高与元素实宽高比对（超 0.75px 记裁切）。
        /// </summary>
        private static bool MeasureDialogComboClip(out string detail)
        {
            var worst = new List<string>();
            bool ok = true;
            foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
            {
                App.ApplyTheme(theme);
                var dlg = new WatchRuleWindow(WatchRule.New(@"C:\temp"), new List<WatchRule>(), true);
                string longPrinter = "超长打印机名-下拉裁切压力-ABCDEFGHIJKLMNOPQRSTUVWXYZ-0123456789-末尾";
                string longTpl = "超长模板名-合同黑白双面A4纵向缩放适应页边距-下拉裁切压力测试-末尾";
                dlg.PrinterBoxForTest.Items.Add(longPrinter); dlg.PrinterBoxForTest.SelectedItem = longPrinter;
                dlg.TemplateBoxForTest.Items.Add(longTpl); dlg.TemplateBoxForTest.SelectedItem = longTpl;

                var content = dlg.Content as FrameworkElement;
                dlg.Content = null;
                var host = new Border
                {
                    Background = (Brush)(Application.Current != null ? Application.Current.TryFindResource("WindowBgBrush") : null) ?? Brushes.White,
                    Child = content
                };
                host.Measure(new Size(640, 640)); host.Arrange(new Rect(0, 0, 640, 640)); host.UpdateLayout();
                PumpToLoaded();

                foreach (var box in new[] { dlg.PrinterBoxForTest, dlg.TemplateBoxForTest })
                {
                    string which = box == dlg.PrinterBoxForTest ? "打印机" : "模板";
                    // ① 收起态：下拉框自身显示选中值的那块文字（选中值被截断就是需求方说的"显示不全"）
                    var sel = FirstTextBlock(box);
                    if (sel != null && !string.IsNullOrEmpty(sel.Text))
                    {
                        double ov, oh; ClipOver(sel, out ov, out oh);
                        if (ov > 0.75 || oh > 0.75)
                        {
                            ok = false;
                            worst.Add(theme + "/" + which + "·选中值 文字高" + ov.ToString("0.0") + " 宽" + oh.ToString("0.0")
                                + " 超元素（元素 " + sel.ActualHeight.ToString("0.0") + "×" + sel.ActualWidth.ToString("0.0") + "）");
                        }
                        // 诊断：把 NoWrap＋省略号钉在这枚 TextBlock 上，看是否一行收得下（决定修法形态，验完删）
                        sel.TextWrapping = TextWrapping.NoWrap;
                        sel.TextTrimming = TextTrimming.CharacterEllipsis;
                        box.UpdateLayout(); PumpToLoaded();
                        double ov2, oh2; ClipOver(sel, out ov2, out oh2);
                        worst.Add("[诊断]" + theme + "/" + which + " 钉NoWrap后 元素" + sel.ActualHeight.ToString("0.0")
                            + "×" + sel.ActualWidth.ToString("0.0") + " 高差" + ov2.ToString("0.0") + " 宽差" + oh2.ToString("0.0"));
                    }
                    // ② 展开态：逐个数下拉项里的文字
                    box.IsDropDownOpen = true;
                    PumpToLoaded();
                    for (int i = 0; i < box.Items.Count; i++)
                    {
                        var ci = box.ItemContainerGenerator.ContainerFromIndex(i) as ComboBoxItem;
                        if (ci == null) continue;
                        var tb = FirstTextBlock(ci);
                        if (tb == null || string.IsNullOrEmpty(tb.Text)) continue;
                        double vOver, hOver; ClipOver(tb, out vOver, out hOver);
                        if (vOver > 0.75 || hOver > 0.75)
                        {
                            ok = false;
                            worst.Add(theme + "/" + which + "[" + i + "] 超元素 高" + vOver.ToString("0.0")
                                + " 宽" + hOver.ToString("0.0") + "（元素 " + tb.ActualHeight.ToString("0.0") + "×"
                                + tb.ActualWidth.ToString("0.0") + "）");
                        }
                    }
                    box.IsDropDownOpen = false;
                }
                // ③ 出图：同一份带超长选中值的对话框，浅/深各一张，供肉眼复核
                try { RenderControl(theme == AppTheme.Light ? "78号对话框超长值浅色.txt" : "78号对话框超长值深色.txt",
                        theme == AppTheme.Light ? "78号规则对话框-超长值-浅色.png" : "78号规则对话框-超长值-深色.png",
                        host, 640, 640); } catch { }
                host.Child = null;
            }
            App.ApplyTheme(AppTheme.Light);
            detail = worst.Count == 0 ? "浅/深两主题、两下拉的「选中值显示区」与「展开项」全部文字均在元素内" : string.Join("；", worst);
            return ok;
        }

        /// <summary>一枚 TextBlock 的文字真实宽高相对元素实宽高各超出多少（≤0 即放得下）。</summary>
        private static void ClipOver(TextBlock tb, out double vOver, out double hOver)
        {
            var ft = new FormattedText(tb.Text, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch),
                tb.FontSize, Brushes.Black, 96.0);
            vOver = ft.Height - tb.ActualHeight;
            hOver = ft.WidthIncludingTrailingWhitespace - tb.ActualWidth;
        }

        private static TextBlock FirstTextBlock(DependencyObject root)
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is TextBlock t) return t;
                var r = FirstTextBlock(c);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>73 号令渲染取证：关于页（功能一览＋开源声明）中英两张（原解锁区那两张已随控件消失）。</summary>
        internal static int V120Render(string log)
        {
            int code = 0;
            var sb = new StringBuilder();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var a = new AboutPage();
                code |= RenderControl("73号关于页中文.txt", "73号关于页-功能一览与开源声明.png", a, 900, 1010);
                sb.AppendLine("① 关于页·中文：解锁区与功能一览均已消失，只留一行 GNU GPLv3 开源声明＋GitHub/Gitee 仓库双按钮");

                App.ApplyLanguage(AppLanguage.EnUS);
                var b = new AboutPage();
                code |= RenderControl("73号关于页英文.txt", "73号关于页-英文-功能一览.png", b, 900, 1010);
                sb.AppendLine("② 关于页·英文：同一张卡的英文态（开源声明取英文值，双按钮文字为品牌名原文）");

                App.ApplyLanguage(AppLanguage.ZhCN);
                App.ApplyTheme(AppTheme.Light);
            }
            catch (Exception ex) { sb.AppendLine("渲染异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            txt += "\r\n说明：73 号令两张取证——关于页中文/英文（真控件出图）。监视页取证在 72 号那一段（V110Render），本令未新增界面。\r\n";
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
