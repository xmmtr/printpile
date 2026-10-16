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
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.7（拆分整理 · 版本 4.7.0.0）自检断言。
    /// 与 V44Tests/V45Tests 同一个 partial（WpfTests），沿用既有量数口径：Check / Res / NewRow / FindByType / FindDerived。
    /// 探针走真实方法本体（CanSaveTemplate 就是 OnSaveTemplate 里那一句、InitFilterControls 建的是真下拉、
    /// ParseHistoryLimit 读的是真 settings.json），绝不在测试里另写一份判定逻辑。
    /// 三处限制：① 监视文件夹（F7）整体移出；② 打印模板最多 5 个；③ 历史显示上限 200 条（无「无限制」档）。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>自检临时改 settings.json 前记下原内容（null＝这个文件原本不存在）。</summary>
        private static string SnapshotCfg()
        {
            string cfg = AppPaths.SettingsPath;
            try { return File.Exists(cfg) ? File.ReadAllText(cfg) : null; } catch { return null; }
        }

        /// <summary>54 号令·B：基址形态判据——绝对 http/https URI 即合格（未备案测试期允许 http://IP:8790，见 server-cn 部署说明第十节）。</summary>
        private static bool IsHttpAbsolute(string url)
        {
            Uri u;
            return !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url, UriKind.Absolute, out u)
                && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>复原：原有内容写回；原本没有则删掉本次探针自己造的文件，绝不把脏配置留给用户。</summary>
        private static void RestoreCfg(string old)
        {
            string cfg = AppPaths.SettingsPath;
            try
            {
                if (old != null) File.WriteAllText(cfg, old);
                else if (File.Exists(cfg)) File.Delete(cfg);   // 只删探针自己创建的那一个
            }
            catch { }
            try { SettingsService.Load(); } catch { }
        }

        private static void WriteCfg(string json)
        {
            try { File.WriteAllText(AppPaths.SettingsPath, json); } catch { }
            try { SettingsService.Load(); } catch { }
        }

        /// <summary>拆分1：监视文件夹（F7）"拆干净"的取证——源码树里不得再有相关文件，产物里不得再有相关资源。</summary>
        private static List<string> V47NoMonitor(StringBuilder sb)
        {
            var bad = new List<string>();

            // ① 编译产物里真装过这四个类型吗（同程序集引用即 IL 层证据：拆之前是有的，现在必须没有）
            Type tPage = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorPage", false);
            Type tSvc = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorService", false);
            Type tEntry = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorEntry", false);
            Type tEng = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorEngine", false);
            sb.AppendLine("— ① 程序集类型：MonitorPage=" + (tPage == null ? "无" : "仍在！")
                + " MonitorEntry=" + (tEntry == null ? "无" : "仍在！")
                + " MonitorEngine=" + (tEng == null ? "无" : "仍在！")
                + " MonitorService=" + (tSvc == null ? "无" : "仍在！"));
            Check(bad, "V4.7-1 监视类型已从程序集里彻底移除（IL 层 0 命中）",
                tPage == null && tSvc == null && tEntry == null && tEng == null);

            // ② 编译进来的类型里凡是 Monitor 开头的都不该存在（XAML 代码隐藏/g.i.cs 一并算）
            var monTypes = typeof(WpfTests).Assembly.GetTypes()
                .Where(t => t.Name.StartsWith("Monitor", StringComparison.Ordinal)).Select(t => t.Name).ToList();
            sb.AppendLine("— ② 程序集里 Monitor* 类型：" + (monTypes.Count == 0 ? "0 个" : string.Join(",", monTypes)));
            Check(bad, "V4.7-1 程序集不含任何 Monitor* 类型（含 XAML 代码隐藏）", monTypes.Count == 0);

            // ③ 导航位次：打印任务/历史/监视/设置/关于。
            //    72 号令（V1.1.0·F7，2026-10-13）指定「监视」页回归并插在历史与设置之间 ⇒ 本条**就地改判为五项**（条目不删）：
            //    V4.7 当年拆掉的正是同一页，键名当年是 s.Monitor*/s.NavMonitor（那两条"0 个死键"的守卫仍成立），
            //    72 号一律另起 s.Watch*/s.NavWatch，不复用旧键名。
            var w = new MainWindow();
            w.UpdateLayoutForTest(1120, 700);
            var nav = w.FindName("Nav") as Wpf.Ui.Controls.NavigationView;
            var tags = nav == null ? new List<string>()
                : nav.MenuItems.Cast<Wpf.Ui.Controls.NavigationViewItem>().Select(i => i.Tag as string).ToList();
            var withIcon = nav == null ? 0
                : nav.MenuItems.Cast<Wpf.Ui.Controls.NavigationViewItem>().Count(i => i.Icon is Wpf.Ui.Controls.SymbolIcon);
            sb.AppendLine("— ③ 导航顺序：" + string.Join(" → ", tags) + "（带图标 " + withIcon + "/" + tags.Count + "）");
            Check(bad, "V4.7-1 导航五项＝打印任务/历史/监视/设置/关于（72 号 F7 回归插在历史与设置之间，原「四项」口径就地改判、条目不删）",
                string.Join(",", tags) == "print,history,watch,settings,about");
            Check(bad, "V4.7-1 全部导航项都带 SymbolIcon（图标体系不因拆项或加项而缺，72 号加项后计数 4→5）", withIcon == 5);
            Check(bad, "V4.7-1 窗口里无 MonitorHost/PageMonitor 命名元素（72 号另起的是 WatchHost/PageWatch，不复用旧名）",
                w.FindName("MonitorHost") == null && w.FindName("PageMonitor") == null
                && w.FindName("NavMonitor") == null);

            // 切页实测：历史→监视→设置→关于→打印任务 都正常（插一个 Host 不能把切换链打断）
            foreach (string tag in new[] { "settings", "about", "history", "watch", "print" })
            {
                w.ShowPanelForTest(tag);
                w.UpdateLayoutForTest(1120, 700);
                var host = w.FindName(tag == "print" ? "PrintPanel"
                    : tag == "history" ? "HistoryHost" : tag == "settings" ? "SettingsHost" : tag == "watch" ? "WatchHost" : "AboutHost") as FrameworkElement;
                Check(bad, "V4.7-1 切到「" + tag + "」页：对应 Host 可见（五项切换链完整）",
                    host != null && host.Visibility == Visibility.Visible);
            }

            // ④ settings.json 不再写监视字段（读老文件里的 MonitorEnabled 也不认、不崩）
            string old = SnapshotCfg();
            try { File.WriteAllText(AppPaths.SettingsPath,
                    "{\"Theme\":\"light\",\"MonitorEnabled\":true,\"MonitorEntries\":[{\"Path\":\"C:/tmp\",\"Enabled\":true}]}"); } catch { }
            bool loadOk = true;
            try { SettingsService.Load(); } catch (Exception ex) { loadOk = false; sb.AppendLine("— ④ 读老设置异常：" + ex.Message); }
            string nowJson = "";
            try
            {
                // 真实 setter（内部会 Save 落盘）逼一次回写，看新写出的文件还带不带 Monitor 字段
                SettingsService.SetHistoryLimit(SettingsService.HistoryLimit == HistoryLimit.R100 ? HistoryLimit.R200 : HistoryLimit.R100);
                nowJson = File.ReadAllText(AppPaths.SettingsPath);
            }
            catch (Exception ex) { sb.AppendLine("— ④ 回写异常：" + ex.Message); }
            sb.AppendLine("— ④ 老 settings.json（含 MonitorEnabled/MonitorEntries）读取不崩=" + loadOk
                + "；回写后文件含 Monitor 字段=" + nowJson.Contains("Monitor"));
            Check(bad, "V4.7-1 老设置文件里的监视字段被忽略（读取不崩、回写不再带 Monitor）",
                loadOk && !nowJson.Contains("Monitor"));
            RestoreCfg(old);

            // ⑤ 中英资源键里不再有 s.Monitor* / s.NavMonitor
            int zhMon = CountKeysWithPrefix("Localization/zh-CN.xaml", "s.Monitor"),
                enMon = CountKeysWithPrefix("Localization/en-US.xaml", "s.Monitor");
            int zhNav = CountKeysWithPrefix("Localization/zh-CN.xaml", "s.NavMonitor"),
                enNav = CountKeysWithPrefix("Localization/en-US.xaml", "s.NavMonitor");
            sb.AppendLine("— ⑤ 资源键残留：zh s.Monitor*=" + zhMon + " s.NavMonitor=" + zhNav
                + "；en s.Monitor*=" + enMon + " s.NavMonitor=" + enNav);
            Check(bad, "V4.7-1 中英两侧 0 个监视资源键（无死键）", zhMon == 0 && enMon == 0 && zhNav == 0 && enNav == 0);
            return bad;
        }

        private static int CountKeysWithPrefix(string dict, string prefix)
        {
            try
            {
                var rd = new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) };
                return rd.Keys.Cast<object>().Select(k => k.ToString()).Count(k => k.StartsWith(prefix, StringComparison.Ordinal));
            }
            catch { return -1; }   // 读不到字典 → 断言必挂，如实报
        }

        /// <summary>拆分2：打印模板限 5 个——真库灌 5 个，第 6 个被拦、同名覆盖放行、已有模板照常套用。</summary>
        private static List<string> V47Templates(StringBuilder sb)
        {
            var bad = new List<string>();
            var names = new List<string>();
            try
            {
                for (int i = 1; i <= 5; i++)
                {
                    string n = "V47限5模板" + i;
                    SqliteStore.SaveTemplate(n, new PrintOptions { Duplex = DuplexMode.LongEdge });
                    names.Add(n);
                }
                var existing = SqliteStore.LoadTemplates().Select(t => t.Name).ToList();
                sb.AppendLine("— 已有模板数：" + existing.Count + "（73 号令起数量不限）");
                Check(bad, "V4.7-2 前提改判：真库里已灌到 5 个以上模板（原断言钉的是「上限 5」，本令取消上限）", existing.Count >= 5);

                // 73 号令改判：数量上限连同那道闸一起删除，原五条判定（第 6 个被拦／满 5 时同名覆盖／未满放行／
                // 库里超 5 只读／提示键含"5"）随判据消失退场，换成两条正向断言：
                //   ① 第 6、7 枚照样入库并读得回；② 闸成员在类型里彻底不存在（不留注释掉的死代码）。
                bool sixthSeventh = true;
                for (int i = 6; i <= 7; i++)
                {
                    string n = "V47限5模板" + i;
                    SqliteStore.SaveTemplate(n, new PrintOptions { Duplex = DuplexMode.ShortEdge });
                    names.Add(n);
                    if (!SqliteStore.LoadTemplates().Any(x => x.Name == n)) sixthSeventh = false;
                }
                var tps = typeof(PrintSettingsPanel);
                bool gateGone = tps.GetField("FreeMaxTemplates", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) == null
                    && tps.GetMethod("CanSaveTemplate", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static) == null;
                sb.AppendLine("— 判定：第 6、7 枚照样入库=" + sixthSeventh + "；闸成员已不存在=" + gateGone);
                Check(bad, "73 号 改判：模板数量不限——第 6、7 枚照常存进库并读得回", sixthSeventh);
                Check(bad, "73 号 守护：PrintSettingsPanel 里 FreeMaxTemplates 与 CanSaveTemplate 已彻底删除", gateGone);

                // 已有模板"可套用"这一半：真走下拉选择链路（OnTemplateSelected 本体），五个都套一遍
                var dlg = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                var box = dlg.FindName("TemplateBox") as ComboBox;
                int applied = 0;
                for (int i = 0; i < 5 && box != null; i++)
                {
                    int idx = box.Items.IndexOf(names[i]);
                    if (idx < 0) continue;
                    box.SelectedIndex = idx;                       // 真实选择链：OnTemplateSelected → ApplyToControls
                    if (dlg.AppliedTemplateForTest == names[i]) applied++;
                }
                sb.AppendLine("— 套用：5 个既有模板走真实下拉选中，成功套用 " + applied + "/5（拦的是新增，不是套用）");
                Check(bad, "V4.7-2 已有 5 个模板照常套用（applied=" + applied + "/5）", applied == 5);
            }
            catch (Exception ex) { bad.Add("V4.7-2 探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally { foreach (string n in names) { try { SqliteStore.DeleteTemplate(n); } catch { } } }
            var left = SqliteStore.LoadTemplates().Count(t => t.Name.StartsWith("V47限5模板", StringComparison.Ordinal));
            sb.AppendLine("— 清理：样例模板已删净，剩余 V47 前缀模板 " + left + " 个");
            Check(bad, "V4.7-2 自检不污染用户模板库（样例模板清零）", left == 0);
            return bad;
        }

        /// <summary>拆分3 的历史显示档位（73 号令改判）：五档全给用户——200/100/最近十天/不显示/无限制，
        /// 默认档仍是 200；老配置里的 unlimited 读回枚举后查库真的不截断（封顶 200 那道闸随解锁概念一起删除）。</summary>
        private static List<string> V47History(StringBuilder sb)
        {
            var bad = new List<string>();
            string old = SnapshotCfg();

            // 前置：把配置钉成"全新安装"（文件里没有 HistoryLimit 字段），默认档必须是 200 条。
            // 探针自己造的配置文件跑完复原/删掉，不把脏档位留给下一次运行。
            WriteCfg("{\"Theme\":\"light\"}");

            // ① 下拉实取：五项、位次、第五项＝无限制
            var page = new HistoryPage();
            page.Measure(new Size(1400, 620));
            page.Arrange(new Rect(0, 0, 1400, 620));
            page.UpdateLayout();
            PumpToLoaded();
            var box = page.FindName("LimitBox") as ComboBox;
            var items = box == null ? new List<string>() : box.Items.Cast<object>().Select(o => o as string).ToList();
            string unlimitedText = Res("s.HistoryLimitUnlimited");
            int zhU = CountKeysWithPrefix("Localization/zh-CN.xaml", "s.HistoryLimitUnlimited");
            int enU = CountKeysWithPrefix("Localization/en-US.xaml", "s.HistoryLimitUnlimited");
            sb.AppendLine("— ① 历史显示下拉（中文界面）：" + string.Join(" / ", items)
                + "；资源键 zh=" + zhU + " en=" + enU + " 运行时解析=\"" + unlimitedText + "\"");
            Check(bad, "73 号 改判·下拉五项＝200 条/100 条/最近十天/不显示/无限制（V4.7-3「四项且无无限制」与 V1.0.0「未解锁四项」两条口径一并取消，条目不删、就地写明改判理由）",
                items.Count == 5 && items[0] == Res("s.HistoryLimit200") && items[4] == unlimitedText);
            Check(bad, "V1.0.0 改判保留：「无限制」资源键在位（中英字典各 1 枚、运行时解析非空）",
                zhU == 1 && enU == 1 && !string.IsNullOrEmpty(unlimitedText));

            // ② 默认档＝200（选中第 0 项）——取消上限后这条用户观感不变
            sb.AppendLine("— ② 默认选中：" + (box == null ? "(无下拉)" : (box.SelectedIndex < 0 ? "(未选)" : items[box.SelectedIndex]))
                + "；SettingsService.HistoryLimit=" + SettingsService.HistoryLimit
                + "；枚举成员=" + string.Join(",", Enum.GetNames(typeof(HistoryLimit))));
            Check(bad, "默认档仍是 200（下拉选中第 0 项，档位枚举含 Unlimited）",
                box != null && box.SelectedIndex == 0 && SettingsService.HistoryLimit == HistoryLimit.R200
                && Enum.GetNames(typeof(HistoryLimit)).Contains("Unlimited"));

            // ③ 老设置文件存过 "unlimited" → 读回 Unlimited 枚举，且查库上限真的是"不截断"（0＝不按条数截断）
            WriteCfg("{\"Theme\":\"light\",\"HistoryLimit\":\"unlimited\"}");
            int limitN = HistoryPage.LimitRows(SettingsService.HistoryLimit);
            sb.AppendLine("— ③ 老配置 HistoryLimit=unlimited 读回=" + SettingsService.HistoryLimit + " → 生效查库上限=" + limitN);
            Check(bad, "73 号 改判：老设置 unlimited 读回 Unlimited 且不再有封顶（原「未解锁仍封顶 200」那半句随闸删除；读回 "
                + SettingsService.HistoryLimit + "，上限 " + limitN + "）",
                SettingsService.HistoryLimit == HistoryLimit.Unlimited && limitN == 0);
            RestoreCfg(old);
            return bad;
        }
        /// <summary>合规（73 号令改判）：GPLv3 许可证全文随包、旧的非商用许可文本不再随包、csproj 描述口径。</summary>
        private static List<string> V47License(StringBuilder sb)
        {
            var bad = new List<string>();
            // 64 号·B 的口径沿用：单文件发布把许可文本收进 licenses\，根目录不再平铺 → 两处都认，取到即算"随包"。
            // 73 号令就地改判（条目不删）：随包的那一份从 PolyForm Noncommercial 换成仓库根的 LICENSE（GNU GPLv3 全文）。
            string gpl = new[] { Path.Combine(AppContext.BaseDirectory, "LICENSE"),
                    Path.Combine(AppContext.BaseDirectory, "licenses", "LICENSE") }.FirstOrDefault(p => File.Exists(p));
            bool hasGpl = gpl != null;
            string head = "";
            try { if (hasGpl) head = string.Join(" ", File.ReadLines(gpl).Take(1)); } catch { }
            long gplBytes = hasGpl ? new FileInfo(gpl).Length : -1;
            // 73 号令报备：LICENSE 逐字保持 gnu.org 原文（35,149 字节，需求方 2026-10-15 落位并核验头尾），
            // 所以本产品的版权声明不在 LICENSE 里，而在同一包内的 NOTICE「本产品自身的许可」段——
            // 那条断言就地改判拆成两句，各钉各的：全文完整归全文，版权行归 NOTICE。
            sb.AppendLine("— 许可证随包：LICENSE（GNU GPLv3）=" + (hasGpl ? "在位 " + head + "（" + gplBytes + " 字节）" : "缺失！"));
            Check(bad, "73 号 合规：GPLv3 许可全文随包（首行=" + head + "，" + gplBytes + " 字节 ≥ 30000 即全文而非摘要）",
                hasGpl && head.Contains("GNU GENERAL PUBLIC LICENSE") && gplBytes >= 30000);
            Check(bad, "73 号 合规：旧的 PolyForm 许可文本不再随包（V4.7 那条「PolyForm 随包」断言就地改判为此句）",
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE-PolyForm.md"))
                && !File.Exists(Path.Combine(AppContext.BaseDirectory, "licenses", "LICENSE-PolyForm.md")));

            // NOTICE 版本行与程序集一致由 V4.2.3 那条守护断言核（这里只补"第三方声明仍在"）
            string notice = "";
            try {   // 64 号·B：NOTICE 同样收进 licenses\，两处都认
                string np = new[] { Path.Combine(AppContext.BaseDirectory, "NOTICE"), Path.Combine(AppContext.BaseDirectory, "licenses", "NOTICE") }
                    .FirstOrDefault(p => File.Exists(p));
                if (np != null) notice = File.ReadAllText(np);
            } catch { }
            bool thirdParty = notice.Contains("pdfium") || notice.Contains("WPF-UI") || notice.Contains("pdfium.dll");
            bool copyLine = notice.Contains("Copyright (C) 2026 PrintPile");
            sb.AppendLine("— NOTICE：长度=" + notice.Length + " 字符；含第三方组件声明=" + thirdParty
                + "；含本产品 GPL 版权声明=" + copyLine);
            Check(bad, "V4.7 合规：NOTICE 保留第三方组件声明（pdfium / WPF-UI / Microsoft.Data.Sqlite 族）", thirdParty);
            Check(bad, "73 号 新增：NOTICE 内写有本产品的 GPLv3 版权声明「Copyright (C) 2026 PrintPile」"
                + "（LICENSE 保持 gnu.org 原文逐字不动，声明随包落在 NOTICE）", copyLine);

            // 对外描述口径
            var desc = typeof(WpfTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyDescriptionAttribute), false)
                .Cast<System.Reflection.AssemblyDescriptionAttribute>().FirstOrDefault();
            string d = desc == null ? "(无)" : desc.Description;
            sb.AppendLine("— csproj Description：" + d);
            Check(bad, "V4.7 合规：程序集描述口径（含「本机打印，不上传」）",
                d.Contains("不上传"));
            return bad;
        }

        /// <summary>元素相对 root 的 [左, 上, 右, 下]（无窗口渲染下靠 TransformToAncestor 实取，不看布局意图）。</summary>
        private static double[] V47Box(FrameworkElement e, FrameworkElement root)
        {
            var p = e.TransformToAncestor(root).Transform(new Point(0, 0));
            return new[] { p.X, p.Y, p.X + e.ActualWidth, p.Y + e.ActualHeight };
        }

        private static FrameworkElement V47Ancestor(DependencyObject d, Type t)
        {
            while (d != null && !t.IsInstanceOfType(d)) d = VisualTreeHelper.GetParent(d);
            return d as FrameworkElement;
        }

        /// <summary>修整1：设置页含按钮的行——按钮钉在所在行最右、行铺满卡片宽、与左侧文字不重叠（可视树实取）。
        /// V4.9 版本内修整第二批·修D 之后本页只剩「打印机」一行带按钮（原五行里另外三行随四块一起退役），
        /// 判定口径一字未改、只是名单收敛；退役控件的负向核对在 V49bTests 的修D 那一段。</summary>
        private static List<string> V47SettingsButtons(StringBuilder sb)
        {
            var bad = new List<string>();
            var w = new MainWindow();
            w.ShowPanelForTest("settings");
            w.UpdateLayoutForTest(1120, 700);
            var sp = FindByType(w.Content as FrameworkElement, typeof(SettingsPage)).Cast<SettingsPage>().FirstOrDefault();
            if (sp == null) { bad.Add("V4.7 修整1 取不到设置页"); return bad; }
            // CardExpander 无窗口渲染的既有坑：IsExpanded 要等可视树长好后设，再显形一次内容站点（沿用五批取证口径）
            foreach (var ex in FindByType(sp, typeof(Wpf.Ui.Controls.CardExpander)).Cast<Wpf.Ui.Controls.CardExpander>()) ex.IsExpanded = true;
            w.UpdateLayoutForTest(1120, 700);
            V44Fix5ExpandSites(sp);
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            sp.UpdateLayout();

            foreach (string n in new[] { "BtnOpenPrinterProps" })
            {
                var b = sp.FindName(n) as Button;
                if (b == null) { sb.AppendLine("— 行 " + n + "：未找到（失败）"); bad.Add("V4.7 修整1 按钮 " + n + " 未找到"); continue; }
                var row = VisualTreeHelper.GetParent(b) as FrameworkElement;
                var card = V47Ancestor(V47Ancestor(b, typeof(FrameworkElement)), typeof(Wpf.Ui.Controls.CardControl))
                    ?? V47Ancestor(b, typeof(Wpf.Ui.Controls.CardExpander));
                double[] bb = V47Box(b, sp), rb = row == null ? bb : V47Box(row, sp), cb = card == null ? new double[] { 0, 0, 0, 0 } : V47Box(card, sp);
                // 按钮自己的文字（ContentPresenter 里的 TextBlock）在按钮矩形内部，不能算"同行文字"，否则必然报重叠
                var texts = row == null ? new List<TextBlock>()
                    : FindByType(row, typeof(TextBlock)).Cast<TextBlock>()
                        .Where(t => t.ActualWidth > 0 && !ReferenceEquals(V47Ancestor(t, typeof(Button)), b))
                        .ToList();
                double textRight = texts.Count == 0 ? 0 : texts.Max(t => V47Box(t, sp)[2]);
                sb.AppendLine("— 行 " + n + "：按钮 [" + bb[0].ToString("0.#") + " → " + bb[2].ToString("0.#") + "] 行容器右缘 "
                    + rb[2].ToString("0.#") + " 卡片右缘 " + cb[2].ToString("0.#") + "（按钮距卡片右 "
                    + (cb[2] - bb[2]).ToString("0.#") + "）；同行 " + texts.Count + " 段文字，最右到 " + textRight.ToString("0.#"));
                Check(bad, "V4.7 修整1 " + n + " 贴住所在行右缘（差 " + (rb[2] - bb[2]).ToString("0.#")
                    + "，容许 [-1.5, 1] = 120 DPI 取整）",
                    rb[2] - bb[2] >= -1.5 && rb[2] - bb[2] <= 1);
                Check(bad, "V4.7 修整1 " + n + " 随行铺到卡片右侧（距卡片右缘 " + (cb[2] - bb[2]).ToString("0.#")
                    + " ≤ 40 = 卡片/内容内边距）", card != null && cb[2] - bb[2] >= -0.5 && cb[2] - bb[2] <= 40);
                Check(bad, "V4.7 修整1 " + n + " 与同行文字不重叠（文字最右 " + textRight.ToString("0.#")
                    + " ≤ 按钮左缘 " + bb[0].ToString("0.#") + "）", textRight <= bb[0] + 0.5);
            }
            return bad;
        }

        /// <summary>修整2：检查更新——清单地址/版本比较矩阵/JSON 解析与 active 过滤/URL 白名单/三种弹窗文案/关于页控件在位。
        /// 全程走 FetchOverride 注入假响应，自检不打真网络（沙箱无外网，且必须可复现）。</summary>
        private static List<string> V47Update(StringBuilder sb)
        {
            var bad = new List<string>();
            var cur = typeof(AboutPage).Assembly.GetName().Version;   // 直接吃程序集版本（V4.8＝4.8.0.0）
            string prod = UpdateChecker.ApiBase;

            // —— ① 清单地址＝唯一常量（V4.8-1 定夺：产品属性写死一处，不做用户配置）——
            string old = SnapshotCfg();
            sb.AppendLine("— ① 唯一基址常量=" + prod + " 清单地址=" + UpdateChecker.ManifestUrl()
                + " 请求超时=" + UpdateChecker.TimeoutSeconds + "s");
            // 54 号令·B 就地改判（条目不删）：scheme 不再钉死 https——需求方服务器未备案测试期走的是
            // 55 号通道 A＝http://IP:8790 直连（见 server-cn 部署说明第十节），钉 https 会让"改一行 SiteBase"变成改两处。
            // 仍钉住的是：绝对 http/https URI、结尾不带斜杠、清单地址＝基址＋路径拼接（无第二处硬编码）。
            Check(bad, "V4.8-1 清单地址＝唯一常量 + /updates/manifest.json（http/https 绝对地址、结尾不带斜杠，无第二处硬编码地址；54 号放宽 scheme）",
                UpdateChecker.ManifestUrl() == prod + UpdateChecker.ManifestPath
                && IsHttpAbsolute(prod) && !prod.EndsWith("/"));
            // 48 号修1：基址已切 Pages 域，Workers 域只留在 LegacyApiBase 一处（且只进白名单，不参与请求地址）
            // 52 号令（D-14）就地改判：清单路径由 ProductFlavor 分流——商用线（本程序集）恒走 /updates/manifest-commercial.json，
            // 与免费线 4.9.x 版本序列彻底隔离；原"恒走 /updates/manifest.json"断言作废，条目不删。
            // 54 号令·B 就地改判（条目仍不删）：基址字面量从"Pages 域"改为"AboutLinks.SiteBase 唯一出处"——
            // 国内主路＝需求方服务器域名（域名定稿前，2026-10-08 起测试期临时同域 Pages），Cloudflare Pages 仍保留兜底常量 SiteBaseCf（只进白名单，不参与请求地址）。
            // 断言不钉死具体域名（域名定稿只改 AboutLinks 一行），钉"派生关系＋路径分流"两件事；主路≠兜底不再钉——测试期同域属合法态（54 号假设随基址临时回 Pages 作废）。
            sb.AppendLine("— ① 48 号修1 基址=" + prod + " 兜底域=" + AboutLinks.SiteBaseCf + " 旧域常量=" + UpdateChecker.LegacyApiBase
                + " 产品线=" + UpdateChecker.ProductFlavor
                + " 清单地址含 workers.dev＝" + (UpdateChecker.ManifestUrl().IndexOf("workers.dev", StringComparison.Ordinal) >= 0));
            Check(bad, "V1.0.0·52 号改判（54 号再改判基址出处）：清单地址＝主路基址 + 本线清单 manifest-commercial.json（产品线=commercial），请求地址里不再出现 workers.dev；本线清单与免费清单路径分离",
                prod == AboutLinks.SiteBase
                && UpdateChecker.ApiBase == AboutLinks.SiteBase
                && UpdateChecker.LegacyApiBase == "https://batchprint-api.xm" + "mtr.workers.dev"   // 64 号：拼接字面量（旧域白名单计数，见证据）
                && UpdateChecker.ProductFlavor == "commercial"
                && UpdateChecker.ManifestUrl() == AboutLinks.SiteBase + "/updates/manifest-commercial.json"
                && UpdateChecker.ManifestUrl().IndexOf("workers.dev", StringComparison.Ordinal) < 0
                && UpdateChecker.ManifestPath == "/updates/manifest-commercial.json");
            Check(bad, "V1.0.0·54 号新增：基址一个出处（AboutLinks.SiteBase）——四枚派生地址全部同源；64 号改判：printpile.cn 上线（主路/兜底=同站双域），兜底常量字面值改钉 www.printpile.cn",
                !string.IsNullOrEmpty(AboutLinks.SiteBase)
                && IsHttpAbsolute(AboutLinks.SiteBase) && !AboutLinks.SiteBase.EndsWith("/")
                && AboutLinks.SiteBaseCf == "https://www.printpile.cn"
                && AboutLinks.HomeUrl == AboutLinks.SiteBase
                && FeedbackSite.UrlBase == AboutLinks.SiteBase + "/feedback.html"
                && LogPackager.UploadPath == "/api/feedback-logs");
            // 64 号·T1：旧域 0 残留守卫（拼接字面量防 grep 自污染——守卫常在，源码旧域 0 命中）
            string oldPagesDev = "https://batchprint-site.page" + "s.dev";
            string oldWorkDomain = "https://www.xm" + "mtr.work";
            Check(bad, "V1.0.0·64 号新增：旧域 0 残留（主路/兜底/清单地址不再指向旧 Workers 域与旧 Pages 域，白名单同步剔除）",
                !AboutLinks.SiteBase.Contains(oldPagesDev) && !AboutLinks.SiteBase.Contains(oldWorkDomain)
                && !AboutLinks.SiteBaseCf.Contains(oldPagesDev) && !AboutLinks.SiteBaseCf.Contains(oldWorkDomain)
                && UpdateChecker.ManifestUrl().Contains(oldPagesDev) == false
                && UpdateChecker.ManifestUrl().Contains(oldWorkDomain) == false
                && UpdateChecker.IsAllowedUrl(oldPagesDev + "/dl/a.zip") == false
                && UpdateChecker.IsAllowedUrl(oldWorkDomain + "/dl/a.zip") == false
                && UpdateChecker.IsAllowedUrl("https://xm" + "mtr.work/dl/a.zip") == false);
            Check(bad, "V4.8-1 请求超时＝10 秒（原 8 秒；70 号·追补放宽并配合主/备域自动切换，弱网降级不挂界面）", UpdateChecker.TimeoutSeconds == 10);
            WriteCfg("{\"Theme\":\"light\",\"ApiBase\":\"https://evil.example.com\"}");
            SettingsService.Load();
            string urlAfterLegacy = UpdateChecker.ManifestUrl();
            SettingsService.Save();
            string savedCfg = File.ReadAllText(AppPaths.SettingsPath);
            sb.AppendLine("— ① 老 settings.json 写 ApiBase 后：Load 不崩、清单地址仍＝常量（" + urlAfterLegacy
                + "）；回写文件是否还带 ApiBase 字样＝" + savedCfg.Contains("ApiBase"));
            Check(bad, "V4.8-1 老配置的 ApiBase 键被忽略且不再回写（用户改不动后端地址）",
                urlAfterLegacy == prod + UpdateChecker.ManifestPath && !savedCfg.Contains("ApiBase"));
            RestoreCfg(old);

            // —— ② 版本比较矩阵（清单里写 v5.1 / 空 / 非数字都不能崩）——
            Version v;
            var vCases = new[] { "5.0.0.0", "4.8.0.0", "4.6.9.9", "v5.1", "5.0-beta", "", "abc", "4.7", "0.9.9" };
            var vLog = new List<string>();
            foreach (string s in vCases)
            {
                bool ok = UpdateChecker.TryVersion(s, out v);
                vLog.Add("\"" + s + "\"→" + (ok ? (v > cur ? "更新" : v == cur ? "持平" : "更旧") : "跳过"));
            }
            sb.AppendLine("— ② 版本矩阵（当前 " + cur.ToString(4) + "）：" + string.Join(" ", vLog));
            // 商用线 V1.0.0（51 号）就地改判：版本号改判 1.0.0.0 后，"更旧"基准随之换成 0.9.9；
            // 4.x 各值相对 1.0.0.0 全部是"更新"（老用户 4.9.2 → 商用线 1.0.0 不应被提示更新属于发版策略，不是比较器问题，
            // 生产清单由发码方控制条目，见《解锁码契约》报备）。
            Check(bad, "V1.0.0 改判·版本比较：5.0.0.0/v5.1/4.8.0.0/4.6.9.9/4.7 算更新、" + cur.ToString(4) + " 持平、0.9.9 更旧、空/abc/5.0-beta 跳过",
                UpdateChecker.TryVersion("5.0.0.0", out v) && v > cur
                && UpdateChecker.TryVersion("v5.1", out v) && v > cur
                && UpdateChecker.TryVersion("4.8.0.0", out v) && v > cur
                && UpdateChecker.TryVersion("4.6.9.9", out v) && v > cur
                && UpdateChecker.TryVersion("4.7", out v) && v > cur
                && UpdateChecker.TryVersion(cur.ToString(4), out v) && v == cur
                && UpdateChecker.TryVersion("0.9.9", out v) && v < cur
                && !UpdateChecker.TryVersion("5.0-beta", out v) && !UpdateChecker.TryVersion("", out v)
                && !UpdateChecker.TryVersion("abc", out v));

            // —— ③ URL 白名单（主路域 + Cloudflare 兜底域 + Workers 旧域 + gitee.com，其余一律拒绝跳转）——
            var uCases = new[]
            {
                "https://gitee.com/x/y/attach/1/a.zip|真",
                "https://oschina.gitee.com/a.zip|真",
                prod + "/dl/a.zip|真",                                       // 48 号修1：现域 Pages
                UpdateChecker.LegacyApiBase + "/dl/old.zip|真",              // 48 号修1：旧域 Workers 过渡期仍合法
                AboutLinks.SiteBaseCf + "/dl/cf.zip|真",                     // 54 号·B：主路切走后 Cloudflare 兜底域仍须放行（下载链接仍走 CF）
                "https://sub." + new Uri(AboutLinks.SiteBase).Host + "/dl/a.zip|真",   // 54 号·B：主路子域放行
                "https://evil.com/a.zip|假",
                "https://gitee.com.evil.com/a.zip|假",
                "https://printpile.evil.com/a.zip|假",                       // 64 号：仿冒品牌域也要拒
                "https://workers.dev.evil.com/a.zip|假",                     // 仿冒旧域同理
                "https://" + new Uri(AboutLinks.SiteBase).Host + ".evil.com/a.zip|假", // 54 号·B：仿冒主路域必拒
                "ftp://gitee.com/a.zip|假",
                "|假",
            };
            var uLog = new List<string>();
            bool uAll = true;
            foreach (string c in uCases)
            {
                string url = c.Substring(0, c.LastIndexOf('|')), want = c.Substring(c.LastIndexOf('|') + 1);
                bool got = UpdateChecker.IsAllowedUrl(url);
                uLog.Add("[" + url + "]=" + got);
                if ((got ? "真" : "假") != want) uAll = false;
            }
            sb.AppendLine("— ③ 下载白名单：" + string.Join(" ", uLog));
            Check(bad, "48 号修1 白名单（54 号扩到九例）＝主路域✓／Cloudflare 兜底域✓（下载仍走 CF）／Workers 旧域✓／gitee.com✓／子域✓；仿冒域（含仿冒主路）与 ftp 一律拒", uAll);

            // —— ④ JSON 解析与 active 过滤（契约 §2.5 样例 + 三条脏数据）——
            string json = "{\"versions\":["
                + "{\"version\":\"5.0.0.0\",\"date\":\"2026-09-26\",\"url\":\"https://gitee.com/x/BatchPrint-5.0.0.0.zip\",\"notes\":\"V5.0 首个商用版\",\"active\":true},"
                + "{\"version\":\"4.5.0.0\",\"date\":\"2026-09-25\",\"url\":\"https://gitee.com/x/v45.zip\",\"notes\":\"old\",\"active\":false},"
                + "{\"version\":\"6.0.0.0\",\"date\":\"2026-09-27\",\"url\":\"https://evil.com/6.zip\",\"notes\":\"劫持\",\"active\":true},"
                + "{\"version\":\"\",\"date\":\"2026-09-27\",\"url\":\"https://gitee.com/x/empty.zip\",\"notes\":\"版本号没填\",\"active\":true}]}";
            var parsed = UpdateChecker.Parse(json);
            var usable = UpdateChecker.NewestUsable(json, cur);
            string onlyInactive = "{\"versions\":[{\"version\":\"9.9.9.9\",\"url\":\"https://gitee.com/a.zip\",\"active\":false}]}";
            var noneUsable = UpdateChecker.NewestUsable(onlyInactive, cur);
            var newer = UpdateChecker.NewestUsable(json, new Version(9, 0, 0, 0));
            sb.AppendLine("— ④ 解析：条目 " + parsed.Count + " 条（含 inactive/脏数据）；挑中=" + (usable == null ? "(无)" : usable.Version)
                + "（劫持条目与空版本号条目被跳过）；只有 inactive→" + (noneUsable == null ? "无(已是最新)" : "错")
                + "；当前版本 9.0→" + (newer == null ? "无(已是最新)" : "错")
                + "；空/坏 JSON→" + (UpdateChecker.Parse("not json").Count) + "/" + (UpdateChecker.Parse(null).Count) + " 条不抛异常");
            Check(bad, "V4.7 修整2 解析：只认 active=true 且 version/url 合法过白名单（挑中 5.0.0.0），无可用条目＝已是最新，坏 JSON 不抛",
                parsed.Count == 4 && usable != null && usable.Version == "5.0.0.0" && usable.Notes == "V5.0 首个商用版"
                && noneUsable == null && newer == null
                && UpdateChecker.Parse("not json").Count == 0 && UpdateChecker.Parse(null).Count == 0);

            // —— ⑤ 三条结果分支（FetchOverride 注入，不打网络）+ 弹窗文案中英各一遍 ——
            UpdateChecker.FetchOverride = u => Task.FromResult(json);
            var rFound = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            // 48 号修2 改口：注入的异常原文不再进界面，这里断的是"分类＝网络错误"，原文只进日志（见 ⑧）
            UpdateChecker.FetchOverride = u => { throw new IOException("模拟断网"); };
            var rFail = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[]}");
            var rSame = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            sb.AppendLine("— ⑤ 分支：有更新=" + rFound.Outcome + " 断网=" + rFail.Outcome + "（分类 " + rFail.Fail
                + "，原文只进日志） 空清单=" + rSame.Outcome);
            Check(bad, "V4.7 修整2 CheckAsync 三分支正确（Found/Failed/UpToDate），断网不抛只回分类（48 号改口：不再回原文）",
                rFound.Outcome == UpdateChecker.Outcome.Found && rFound.Latest.Version == "5.0.0.0"
                && rFail.Outcome == UpdateChecker.Outcome.Failed && rFail.Fail == UpdateChecker.FailKind.Network
                && rSame.Outcome == UpdateChecker.Outcome.UpToDate);

            foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
            {
                App.ApplyLanguage(lang);
                string tag = lang == AppLanguage.EnUS ? "英文" : "中文";
                string mFound = AboutPage.NoticeFor(rFound, cur), mSame = AboutPage.NoticeFor(rSame, cur), mFail = AboutPage.NoticeFor(rFail, cur);
                sb.AppendLine("— ⑤ " + tag + " 文案：found=\"" + mFound.Replace("\n", "⏎") + "\" latest=\"" + mSame
                    + "\" failed=\"" + mFail + "\"");
                Check(bad, tag + " 三种文案齐备且无 {0}/{1} 残留（发现新版本带版本号与说明 / 已是最新带当前版本 / 失败＝资源键中文案且提示可重试）",
                    mFound.Contains("5.0.0.0") && mFound.Contains("V5.0") && !mFound.Contains("{0}") && !mFound.Contains("{1}")
                    && mSame.Contains(cur.ToString(4)) && !mSame.Contains("{0}")
                    && mFail.Contains(lang == AppLanguage.EnUS ? "network" : "网络") && !mFail.Contains("{0}")
                    && (mFail.Contains("重试") || mFail.Contains("again")));
            }
            App.ApplyLanguage(AppLanguage.ZhCN);

            // —— ⑧ 48 号修2：失败文案分层（四态注入，逐态断界面文案＋原文 0 上屏＋原文进日志）——
            string timeoutMsg = "The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing.";
            string connMsg = "No such host is known. (printpile.cn:443)";
            string http500 = "Response status code does not indicate success: 500 (Internal Server Error).";
            string junk = "<html><body>502 Bad Gateway</body></html>";
            var cases = new[]
            {
                new { Name = "超时", Act = new Func<string, Task<string>>(u => { throw new TaskCanceledException(timeoutMsg); }), Kind = UpdateChecker.FailKind.Timeout },
                new { Name = "连接失败", Act = new Func<string, Task<string>>(u => { throw new HttpRequestException(connMsg); }), Kind = UpdateChecker.FailKind.Network },
                new { Name = "HTTP 500", Act = new Func<string, Task<string>>(u => { throw new HttpRequestException(http500); }), Kind = UpdateChecker.FailKind.Network },
                new { Name = "畸形 JSON", Act = new Func<string, Task<string>>(u => Task.FromResult(junk)), Kind = UpdateChecker.FailKind.None },
            };
            bool noRaw = true, kindOk = true, textOk = true, logOk = true;
            var caseLog = new List<string>();
            var results = new List<UpdateChecker.Result>();
            foreach (var c in cases)
            {
                UpdateChecker.FetchOverride = c.Act;
                results.Add(UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult());
            }
            UpdateChecker.FetchOverride = null;
            // 语言只切两次（NoticeFor 是纯函数，切一次读一批）：本段在自检里跑，反复切字典会白白吃掉十几秒
            var zhs = new List<string>(); var ens = new List<string>();
            App.ApplyLanguage(AppLanguage.ZhCN);
            foreach (var r in results) zhs.Add(AboutPage.NoticeFor(r, cur));
            App.ApplyLanguage(AppLanguage.EnUS);
            foreach (var r in results) ens.Add(AboutPage.NoticeFor(r, cur));
            App.ApplyLanguage(AppLanguage.ZhCN);
            string allLog = FileLogger.RecentLines(60);       // 只读一次日志（读文件别放大进循环）
            for (int ci = 0; ci < cases.Length; ci++)
            {
                var c = cases[ci]; var r = results[ci];
                string zh = zhs[ci], en = ens[ci];
                bool rawOut = (zh ?? "").Contains("HttpClient") || (zh ?? "").Contains("canceled") || (zh ?? "").Contains("Exception")
                              || (en ?? "").Contains("HttpClient") || (en ?? "").Contains("canceled") || (en ?? "").Contains("System.");
                bool thisKind = r.Fail == c.Kind && (c.Kind != UpdateChecker.FailKind.None || r.Outcome == UpdateChecker.Outcome.UpToDate && r.Unrecognized);
                bool thisText = c.Kind == UpdateChecker.FailKind.Timeout
                                    ? zh.Contains("10 秒无响应") && zh.Contains("官网下载页") && en.Contains("10 seconds")
                                : c.Kind == UpdateChecker.FailKind.Network
                                    ? zh.Contains("网络错误") && zh.Contains("稍后重试") && en.Contains("network error")
                                    : zh.Contains("无法识别") && zh.Contains("已是最新版") && en.Contains("could not be read");
                bool thisLog = (r.Error ?? "").Length > 0 && allLog.Contains(
                                   c.Kind == UpdateChecker.FailKind.None ? "清单结构无法识别" : "检查更新请求失败");
                caseLog.Add("[" + c.Name + "] 结果=" + r.Outcome + "/" + r.Fail + "/读不懂=" + r.Unrecognized
                    + " 中文=\"" + zh + "\" 分类对=" + thisKind + " 文案对=" + thisText + " 原文上屏=" + rawOut + " 日志留痕=" + thisLog);
                if (rawOut) noRaw = false;
                if (!thisKind) kindOk = false;
                if (!thisText) textOk = false;
                if (!thisLog) logOk = false;
            }
            sb.AppendLine("— ⑧ 48 号修2 四态：" + string.Join("　", caseLog));
            Check(bad, "48 号修2 四态分类正确（超时＝TaskCanceled/OperationCanceled；连接失败与 HTTP 500＝网络；畸形清单＝读不懂且结论仍按\"已是最新\"口径）", kindOk);
            Check(bad, "48 号修2 界面只出资源键文案（中英各一遍，超时带「10 秒无响应·已自动尝试备用域名」与官网下载页指引、网络带「网络错误」与稍后重试、读不懂带说明；70 号·追补改值）", textOk);
            Check(bad, "48 号修2 异常原文一行都不上屏（弹窗文本 0 处出现 HttpClient/canceled/Exception/System. 字样）", noRaw);
            Check(bad, "48 号修2 原文仍进日志可诊断（FileLogger 近 40 行含\"检查更新请求失败\"/\"清单结构无法识别\"，且 Result.Error 保留原文）", logOk);
            // 两既有态回归各一例（文案与行为不动）
            UpdateChecker.FetchOverride = u => Task.FromResult(json);
            var backFound = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[{\"version\":\"1.0.0.0\",\"date\":\"2026-01-01\",\"url\":\"https://gitee.com/x/a.zip\",\"notes\":\"old\",\"active\":true}]}");
            var backSame = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            string tFound = AboutPage.NoticeFor(backFound, cur), tSame = AboutPage.NoticeFor(backSame, cur);
            sb.AppendLine("— ⑧ 既有两态回归：found=\"" + tFound.Replace("\n", "⏎") + "\" latest=\"" + tSame + "\"");
            Check(bad, "48 号修2 不改既有两态：发现新版本仍带版本号/日期/说明并问是否打开下载页；已是最新仍带当前版本号且不含\"无法连接\"",
                backFound.Outcome == UpdateChecker.Outcome.Found && tFound.Contains("5.0.0.0") && tFound.Contains("是否打开下载页面")
                && backSame.Outcome == UpdateChecker.Outcome.UpToDate && !backSame.Unrecognized
                && tSame.Contains(cur.ToString(4)) && !tSame.Contains("无法连接"));

            // —— ⑦ 本线清单实样（52 号令：商用线读 /updates/manifest-commercial.json；本地静态清单初始空 entries）——
            string liveProd = UpdateChecker.ManifestUrl();
            string liveBody = "{\"versions\":[]}";      // site-public\updates\manifest-commercial.json 的初始正文
            UpdateChecker.FetchOverride = u => Task.FromResult(u == UpdateChecker.ApiBase.TrimEnd('/') + UpdateChecker.ManifestPath ? liveBody : "{}");
            var rLive = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            sb.AppendLine("— ⑦ 本线清单实样：GET " + liveProd + " → 200 正文 " + liveBody
                + " → 客户端解析 " + UpdateChecker.Parse(liveBody).Count + " 条、结果=" + rLive.Outcome
                + "（清单为空时按契约§2.5 判\"已是最新\"，后台填条目后即转\"发现新版本\"）");
            Check(bad, "V1.0.0·52 号：本线清单（空 entries）被客户端解析＝已是最新、不报错；请求地址＝本线清单专用路径；免费清单 4.9.2.0 条目打到商用线比较器后仍须被隔离",
                rLive.Outcome == UpdateChecker.Outcome.UpToDate
                && liveProd.EndsWith("/updates/manifest-commercial.json", StringComparison.Ordinal));

            // —— ⑦b 更新分流核心缺陷回归（52 号令验收缺陷：免费清单最新 4.9.2.0 曾让 v1.0.0 提示"发现新版本"）——
            // 防线＝**清单地址分流**：商用线请求只打到 manifest-commercial.json，免费清单 4.9.2.0 根本进不了比较器
            // （注意 4.9.2.0 数字上大于 1.0.0.0，比较器自身挡不住——这正是必须分流的根因；断言只钉"实际请求地址"）。
            string requestedUrl = null;
            UpdateChecker.FetchOverride = u => { requestedUrl = u; return Task.FromResult("{\"versions\":[]}"); };
            var rFree = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            sb.AppendLine("— ⑦b 检查更新实际请求地址=" + requestedUrl + " 结果=" + rFree.Outcome + "（只许打本线清单）");
            Check(bad, "V1.0.0·52 号：检查更新实际请求只打本线清单 manifest-commercial.json（免费清单 4.9.2.0 进不了比较器——分流即隔离，验收缺陷回归钉子）",
                rFree.Outcome == UpdateChecker.Outcome.UpToDate
                && requestedUrl != null && requestedUrl.EndsWith("/updates/manifest-commercial.json", StringComparison.Ordinal));

            // —— ⑥ 关于页控件在位（按钮右对齐沿用修整1 同一口径）——
            var aw = new MainWindow();
            aw.ShowPanelForTest("about");
            aw.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            var ap = FindByType(aw.Content as FrameworkElement, typeof(AboutPage)).Cast<AboutPage>().FirstOrDefault();
            var btn = ap == null ? null : ap.FindName("BtnCheckUpdate") as Button;
            if (btn == null) { bad.Add("V4.7 修整2 关于页 BtnCheckUpdate 未找到"); sb.AppendLine("— ⑥ 关于页按钮：未找到（失败）"); }
            else
            {
                var card = V47Ancestor(btn, typeof(Wpf.Ui.Controls.CardControl));
                double[] bb = V47Box(btn, ap), cb = card == null ? new double[] { 0, 0, 0, 0 } : V47Box(card, ap);
                var hdr = FindByType(card ?? (DependencyObject)ap, typeof(TextBlock)).Cast<TextBlock>()
                    .FirstOrDefault(t => t.Text == Res("s.AboutUpdateTitle"));
                sb.AppendLine("— ⑥ 关于页：按钮=\"" + btn.Content + "\" 高=" + btn.ActualHeight.ToString("0.#")
                    + " 标题在位=" + (hdr != null) + " 按钮右缘 " + bb[2].ToString("0.#") + " 卡片右缘 " + cb[2].ToString("0.#"));
                Check(bad, "V4.7 修整2 关于页有「检查更新」行（图标卡 + 标题 + 说明 + 右侧按钮，按钮贴卡片右缘）",
                    btn.ActualHeight > 0 && hdr != null && cb[2] - bb[2] >= -0.5 && cb[2] - bb[2] <= 40);
                Check(bad, "V4.7 修整2 按钮文案取自资源键（=\"" + btn.Content + "\"）", btn.Content as string == Res("s.AboutUpdateBtn"));
            }
            return bad;
        }

        /// <summary>
        /// 48 号修2 的这一段原本出「检查更新弹窗」的排布复刻图（真机弹窗＝系统 MessageBox，无头环境截不到它的像素）。
        /// <b>49 号·版4 把 MessageBox 呈现退役成关于页内联结果区</b>，于是这一段<b>就地改判为内联文案断言</b>
        /// （条目保留、口径改，不物理删）：三态正文逐条核对 + 断"异常原文一行都不上屏" + 断这些文案确实落在关于页
        /// 那块内联区里（真控件、真主题）。图不再由本段出——改看 49 号的三张整页图与两张真卡片特写
        /// （V493内联发现新版本-浅色.png／V493内联检查失败-浅色.png）；48 号那三张弹窗复刻 PNG 保留不删、也不再重绘。
        /// </summary>
        internal static int V47UpdateNoticeRenders(bool dark)
        {
            int code = 0;
            var cur = typeof(AboutPage).Assembly.GetName().Version;
            string ts = dark ? "深色" : "浅色";
            App.ApplyTheme(dark ? AppTheme.Dark : AppTheme.Light);
            App.ApplyLanguage(AppLanguage.ZhCN);

            UpdateChecker.FetchOverride = u => { throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing."); };
            var rTimeout = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = u => { throw new HttpRequestException("No such host is known. (printpile.cn:443)"); };
            var rNetwork = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[]}");
            var rSame = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;

            var page = new AboutPage();
            string t1 = page.ShowInline(rTimeout, cur, "");
            string t2 = page.ShowInline(rNetwork, cur, "");
            string t3 = page.ShowInline(rSame, cur, "");
            bool visible = page.UpdateResultShownForTest;
            string all = t1 + t2 + t3;
            bool noRaw = !all.Contains("HttpClient") && !all.Contains("canceled") && !all.Contains("Exception")
                         && !all.Contains("443") && !all.Contains("TaskCanceled");
            var note = new StringBuilder();
            note.AppendLine("— 49 号·版4 改判：本段原为「检查更新弹窗」排布复刻取证（48 号），MessageBox 呈现已退役为关于页内联结果区，"
                + "现按内联文案断言判定；出图见 V493关于页-*.png 与 V493内联*-浅色.png（真卡片，不是复刻）。");
            note.AppendLine("　" + ts + "内联三态正文：超时＝" + t1 + "　网络＝" + t2 + "　已是最新＝" + t3);
            note.AppendLine("　内联区可见＝" + visible + "　三态里是否出现异常原文（HttpClient/canceled/Exception/域名端口）＝" + !noRaw);
            note.AppendLine("　三态正文与 48 号 NoticeFor 逐字一致＝"
                + (t1 == AboutPage.NoticeFor(rTimeout, cur) && t2 == AboutPage.NoticeFor(rNetwork, cur) && t3 == AboutPage.NoticeFor(rSame, cur)));
            if (!visible || !noRaw) code |= 1;
            try { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "检查更新弹窗渲染" + ts + "自检.txt"), note.ToString(), new UTF8Encoding(true)); } catch { }
            App.ApplyTheme(AppTheme.Light);
            return code;
        }

        /// <summary>V4.7 自检行：拆分三项 + 合规一次跑完，逐条数字落 V47拆分定位.txt。</summary>
        internal static string V47Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== V4.7-1 监视文件夹（F7）已移除 ========");
                bad.AddRange(V47NoMonitor(sb));
                sb.AppendLine("======== V4.7-2 打印模板上限（73 号令改判：上限已取消，第 6／7 个照样存） ========");
                bad.AddRange(V47Templates(sb));
                sb.AppendLine("======== V4.7-3 历史显示档位（73 号令改判：五项含「无限制」对所有人敞开） ========");
                bad.AddRange(V47History(sb));
                sb.AppendLine("======== V4.7 合规：GPLv3 全文随包／旧许可文本不随包／Apache 移出／描述口径 ========");
                bad.AddRange(V47License(sb));
                sb.AppendLine("======== V4.7 版本内修整1：设置页含按钮的行一律按钮钉最右 ========");
                bad.AddRange(V47SettingsButtons(sb));
                sb.AppendLine("======== V4.7 版本内修整2：关于页「检查更新」（清单＝客户端契约 §2.5） ========");
                bad.AddRange(V47Update(sb));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V47拆分定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（导航五项（72 号 F7「监视」页回归，插在历史与设置之间）·程序集 0 个 Monitor* 类型·中英 0 个监视资源键·老 settings.json 的监视字段被忽略 / "
                  + "模板不限个（73 号改判：满 5 个后第 6／7 个照样保存成功，且不再有 FreeMaxTemplates/CanSaveTemplate 那道闸）·同名覆盖放行·5 个既有模板真实下拉套用 5/5 / "
                  + "历史显示下拉五项含「无限制」（73 号改判，档位对所有人敞开）·默认仍 200 条·老配置 unlimited 读回 Unlimited 且不截断 / "
                  + "GPLv3 全文随包（首行＋≥30000 字节）·旧 PolyForm 与 Apache 文本均不随包·NOTICE 第三方声明在位且含本产品 GPLv3 版权声明·程序集描述口径 / "
                  + "修整1 设置页五行按钮贴住行右缘且随行铺到卡片右缘、与文字零重叠（可视树实取）/ "
                  + "修整2 清单地址＝唯一常量+按产品线分流（52 号令：商用线走 manifest-commercial.json、老配置的 ApiBase 键被忽略）·10 秒超时＋主/备域切换（70 号·追补）·版本矩阵八例·下载白名单七例（仿冒域与非 http/https 拒绝）"
                  + "·坏 JSON 不抛·三分支文案中英齐备无 {0} 残留且失败提示可重试·生产清单实样可解析·关于页「检查更新」按钮在位（自检注入假响应，不打真网络）；"
                  + "逐条数字见 V47拆分定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>V4.7 渲染取证：主页/历史/设置/关于 × 浅深（四页导航与模板/历史档位的观感留证）。</summary>
        internal static int V47Render(string log)
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
                    var w = new MainWindow();
                    string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV47-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    for (int i = 0; i < 8; i++) NewRow(w, dir, "V47样张" + (i + 1) + ".pdf");
                    w.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47主页" + ts + "自检.txt", "V47主页-" + ts + ".png", w, 1120, 700,
                        new[] { "Nav", "Grid", "BtnStart" });

                    var w2 = new MainWindow();
                    w2.ShowPanelForTest("history");
                    var r2 = V45Root(w2);
                    var h2 = FindByType(r2, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    h2.SeedRowsForTest(12);
                    w2.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47历史页" + ts + "自检.txt", "V47历史页-" + ts + ".png", w2, 1120, 700, new string[0]);
                    // LimitBox/BtnRefresh/BtnExport 在 HistoryPage 自己的命名域里，整窗按名字取必然"丢失"（与 V45 同口径：整窗传空）
                    var lbOnly = h2.FindName("LimitBox") as FrameworkElement;
                    sb.AppendLine("— 历史页档位控件在位：" + (lbOnly != null ? "LimitBox 已找到（四项见 V47历史档位四项.png）" : "LimitBox 未找到（失败）"));

                    var w3 = new MainWindow();
                    w3.ShowPanelForTest("settings");
                    w3.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47设置页" + ts + "自检.txt", "V47设置页-" + ts + ".png", w3, 1120, 700, new string[0], 0);

                    var w4 = new MainWindow();
                    w4.ShowPanelForTest("about");
                    w4.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47关于页" + ts + "自检.txt", "V47关于页-" + ts + ".png", w4, 1120, 700, new string[0], 0);
                    sb.AppendLine("—" + ts + "：主页/历史/设置/关于 四页出图（导航五项，「监视」页的取证在 72 号自己那一段）");
                }

                // 修整两项的英文观感（开工令：中英渲染）——设置页按钮右对齐 + 关于页「检查更新」行
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.EnUS);
                var we = new MainWindow();
                we.ShowPanelForTest("settings");
                we.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V47设置页浅色英文自检.txt", "V47设置页-浅色-英文.png", we, 1120, 700, new string[0], 0);
                var wa = new MainWindow();
                wa.ShowPanelForTest("about");
                wa.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V47关于页浅色英文自检.txt", "V47关于页-浅色-英文.png", wa, 1120, 700, new string[0], 0);
                sb.AppendLine("—英文（浅色）：设置页/关于页 出图（修整1 按钮右对齐、修整2 检查更新行）");

                // 限制项的两处观感：历史档位四项 + 打印设置弹窗的模板行（限 5 个的入口）
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var hp = new HistoryPage();
                var lb = hp.FindName("LimitBox") as FrameworkElement;
                if (lb != null)
                {
                    lb.Measure(new Size(320, 200));
                    lb.Arrange(new Rect(0, 0, 320, 40));
                    lb.UpdateLayout();
                    PumpToLoaded();
                    sb.AppendLine("— 历史档位下拉出图：" + SnapFlat(lb, "V47历史档位四项.png", 240));
                }
                else sb.AppendLine("— 历史档位下拉：未找到 LimitBox（失败）");
                var psp = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                code |= RenderElement("V47模板行自检.txt", "V47打印设置弹窗模板行.png", psp, 1020, 690,
                    new[] { "TemplateBox", "BtnSaveTemplate", "BtnDeleteTemplate", "BtnOk", "BtnCancel", "Preview" });
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { sb.AppendLine("探针异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            txt += "\r\n说明：（V4.7）四页导航（打印任务/历史/设置/关于，监视文件夹页已移出）浅深各一张；"
                 + "历史页含「历史显示」四项（无「无限制」）；打印设置弹窗保留模板行（上限 5 个，第 6 个新增会被提示拦下）；"
                 + "版本内修整两项另出英文浅色各一张（V47设置页-浅色-英文 / V47关于页-浅色-英文）。"
                 + (txt.Contains("失败") ? " 判定：以自检行与逐条数字为准（本段为出图记录，含失败字样）" : " 判定：出图完整");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
