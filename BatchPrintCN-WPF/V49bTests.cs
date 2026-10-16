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
using System.Windows.Media.Imaging;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整第二批（41 号开工令）断言：
    /// 修A 最小化到系统托盘（图标/单击恢复/右键菜单/退出释放）·修B 单实例守卫（命名 Mutex + 事件唤醒 + 真跨进程探针）·
    /// 修D 设置页精简四块（控件消失、旧键读取即忽略、引擎与目录走默认）·修E 关于页精简（界面里不再出现应用名，版本只剩一处）。
    /// 铁律沿用 40 号：打印页行高锁死——本文件末尾对打印页做三态/气泡回归目检与行高复量。
    /// 全程离线：不真开浏览器、不真起第二实例的界面（探针子进程只回答"有没有别人在跑"就退出）。
    /// </summary>
    public static partial class WpfTests
    {
        // ===== 修A：托盘图标本体与两条恢复/退出路径 =====
        private static List<string> V49bTray(StringBuilder sb)
        {
            var bad = new List<string>();
            var mode = Application.Current.ShutdownMode;
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Window w = null;
            try
            {
                string tip = TrayIcon.TipText;
                sb.AppendLine("— 修A 图标资源：app.ico 随包在位=" + TrayIcon.IconFileForTest
                    + " 句柄取得到=" + TrayIcon.IconHandleForTest + "；悬停文字=\"" + tip + "\"（" + tip.Length + " 字）");
                // 64 号·B 就地改判（条目不删）：单文件形态不再随包 app.ico（图标内嵌进 exe，发布脚本 a3 还专门清掉散落的 ico）。
                // 托盘取图标本就有两条路＝①随包 app.ico ②从 exe 取关联图标句柄；任一成立即算"复用应用图标"，平铺形态仍两条都验。
                Check(bad, "修A→64 号·B 托盘图标复用应用图标（随包 app.ico 或 exe 内嵌句柄，二者取其一），悬停文字取资源键且不超过 NotifyIcon 的 63 字上限",
                    (TrayIcon.IconFileForTest || TrayIcon.IconHandleForTest) && tip == Res("s.TrayTip") && tip.Length > 0 && tip.Length <= 63);

                // 进托盘 / 单击恢复（用一个离屏透明窗口走真实方法体，不在使用方屏幕上闪窗口）
                // Style=null：绕开应用级隐式 Window 样式（WPF-UI 那份带 AllowsTransparency，
                // 裸窗口一进 Show 就抛"显示 Window 之后无法更改 AllowsTransparency"）；探针只要窗口对象本身
                w = new Window { Style = null, Width = 160, Height = 120, Left = -8000, Top = -8000, Opacity = 0, ShowInTaskbar = false, Title = "tray-probe" };
                w.Show(); PumpToLoaded();
                bool shownFirst = w.IsVisible;
                TrayIcon.HideToTray(w);
                PumpToLoaded();
                bool hidden = w.Visibility != Visibility.Visible && !w.IsVisible;
                bool iconUp = TrayIcon.IsVisible;
                TrayIcon.HandleClickForTest(true);          // 左键单击＝恢复（同一段方法体）
                PumpToLoaded();
                bool back = w.Visibility == Visibility.Visible && w.WindowState == WindowState.Normal;
                bool iconDown = !TrayIcon.IsVisible;
                sb.AppendLine("— 修A 进托盘/回前台：先 Show 成功=" + shownFirst + " → HideToTray 后窗口隐藏=" + hidden
                    + " 图标在场=" + iconUp + " → 左键单击后回到可见且非最小化=" + back + " 图标收回=" + iconDown);
                Check(bad, "修A 「最小化」＝Hide（窗口不可见→任务栏按钮随之消失）+ 托盘图标在场；左键单击＝Show+正常态+图标收回（两个入口都在同一段方法体里）",
                    shownFirst && hidden && iconUp && back && iconDown);

                // 右键菜单：两项 + 分隔线，文字取键；点「退出」触发主窗口那条清理路径
                var menu = TrayIcon.MenuForTest();
                var items = menu.Items.Cast<object>().ToList();
                var miOpen = items.OfType<MenuItem>().FirstOrDefault();
                var miExit = items.OfType<MenuItem>().Skip(1).FirstOrDefault();
                int exits = 0;
                TrayIcon.ExitRequested = () => exits++;
                bool hasSep = items.OfType<Separator>().Any();
                sb.AppendLine("— 修A 托盘菜单：项数=" + items.Count + "（含分隔线=" + hasSep + "）文字=\""
                    + (miOpen == null ? "" : miOpen.Header) + "\" / \"" + (miExit == null ? "" : miExit.Header)
                    + "\"，图标与菜单都用同一份双语资源键");
                Check(bad, "修A 右键菜单两项（打开主窗口 / 退出）+ 分隔线，文字全部来自资源键（切语言跟着变）",
                    items.Count == 3 && hasSep && miOpen != null && miExit != null
                    && (string)miOpen.Header == Res("s.TrayOpen") && (string)miExit.Header == Res("s.TrayExit"));
                if (miExit != null) miExit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Check(bad, "修A 菜单「退出」把请求交给主窗口（MainWindow.OnClosing 里那条既有清理路径），自检侧只断触发一次", exits == 1);

                // 41 号修A 当时"明确不做托盘气泡"；49 号·版2 把它改判成"只在自动检查发现新版本时出一条"。
                // 条目保留、口径就地改判（不物理删）：现在断的是 ShowBalloonTip 只存在于 ShowBubble 这一条路上，
                // 而"不做闪烁动画"这一半仍然成立。
                string tray = TraySource();
                bool readable = !string.IsNullOrEmpty(tray);
                Func<string, string, int> count = (src, needle) => (src.Length - src.Replace(needle, "").Length) / needle.Length;
                int balloon = count(tray, "ShowBalloonTip");
                int bubbleDefs = count(tray, "public static void ShowBubble");
                bool hasApi = typeof(TrayIcon).GetMethod("ShowBubble",
                    new[] { typeof(string), typeof(string), typeof(int) }) != null;
                bool onePath = !readable || (balloon == 1 && bubbleDefs == 1
                    && tray.IndexOf("public static void ShowBubble") < tray.IndexOf("ShowBalloonTip"));
                sb.AppendLine("— 修A 改判（49 号·版2）：源码文本可读=" + readable + "（发布目录里读不到源文件＝旧那条"
                    + "\"不含 ShowBalloonTip\"其实是空串上的假通过）　ShowBalloonTip 出现=" + balloon + " 次　ShowBubble 定义=" + bubbleDefs
                    + " 处　反射到 ShowBubble(string,string,int)=" + hasApi + "　仍不做闪烁动画=" + !tray.Contains("IconPulse"));
                Check(bad, "修A→49 号 气泡只有一条路：TrayIcon 上有 ShowBubble(string,string,int)（反射实证，发布目录也成立），"
                    + "且源码文本里 ShowBalloonTip 恰一次、位置在 ShowBubble 之内（不做轮询提示、不做闪烁动画）", hasApi && onePath);
            }
            finally
            {
                TrayIcon.Dispose();
                TrayIcon.ExitRequested = null;
                try { if (w != null) { w.Opacity = 0; w.Close(); } } catch { }
                PumpToLoaded();
                Application.Current.ShutdownMode = mode;
            }
            return bad;
        }

        /// <summary>读托盘源码本体做"没做什么"的负向核对（自检不引外部工具，直接读随包源码不可得时退回空串）。</summary>
        private static string TraySource()
        {
            foreach (string cand in new[]
            {
                Path.Combine(AppContext.BaseDirectory, "TrayIcon.cs"),
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TrayIcon.cs"),
            })
            {
                try { if (File.Exists(cand)) return File.ReadAllText(cand); } catch { }
            }
            return "";
        }

        // ===== 修B：单实例守卫（同进程语义 + 真跨进程探针 + 唤醒还原） =====
        private static List<string> V49bSingle(StringBuilder sb)
        {
            var bad = new List<string>();
            string exe = SingleInstance.CurrentExePath;
            string other = Path.Combine(Path.GetTempPath(), "另一份目录", "PrintPile.exe");
            sb.AppendLine("— 修B 命名口径：本份 exe=" + exe);
            sb.AppendLine("    锁名=" + SingleInstance.MutexNameFor(exe) + " 事件名=" + SingleInstance.EventNameFor(exe));
            Check(bad, "修B 名字不加 Global\\ 前缀（只在当前登录会话内互斥，不同用户各会话互不影响）",
                !SingleInstance.MutexNameFor(exe).StartsWith("Global\\", StringComparison.Ordinal)
                && !SingleInstance.EventNameFor(exe).StartsWith("Global\\", StringComparison.Ordinal));
            Check(bad, "修B 名字按可执行文件全路径派生 → 不同目录的同名副本（v4.8 目录 / U 盘拷贝）互不阻塞",
                SingleInstance.MutexNameFor(exe) != SingleInstance.MutexNameFor(other));

            // ① 同进程二次抢锁必须失败（＝"已有实例在跑"那条分支真的会走到）
            bool first = SingleInstance.TryAcquire(exe);
            bool second = SingleInstance.TryAcquire(exe);
            sb.AppendLine("— 修B 抢锁：第一次=" + first + " 第二次（同一把名字）=" + second + "（false＝走\"通知现有实例并退出\"分支）");
            Check(bad, "修B 抢锁语义：第一次成功、第二次失败（第二实例据此自行退出，不弹错误框）", first && !second);

            // ② 真跨进程：现有实例持锁时，子进程探针应报"已有实例"（3）；释放后应报"没人"（0）
            int codeHeld = RunProbeChild();
            SingleInstance.Release();
            int codeFree = RunProbeChild();
            sb.AppendLine("— 修B 跨进程探针（--instance-probe，只回答有无、立刻退出，不开界面不写日志）："
                + "父进程持锁时子进程退出码=" + codeHeld + "（期望 " + SingleInstance.ProbeCodeOwnedByOther
                + "）；父进程释放后=" + codeFree + "（期望 " + SingleInstance.ProbeCodeFree + "）");
            Check(bad, "修B 真跨进程实测：已有实例持锁时第二个进程报\"已存在\"并退出；锁释放后第二个进程能正常抢到",
                codeHeld == SingleInstance.ProbeCodeOwnedByOther && codeFree == SingleInstance.ProbeCodeFree);

            // ③ 唤醒还原：现有实例注册的回调要在 UI 线程上跑到
            SingleInstance.TryAcquire(exe);
            int woke = 0;
            SingleInstance.Watch(() => woke++);
            bool notified = SingleInstance.NotifyExisting(exe);
            for (int i = 0; i < 40 && woke == 0; i++) { PumpToLoaded(); System.Threading.Thread.Sleep(50); }
            PumpToLoaded();
            sb.AppendLine("— 修B 通知：NotifyExisting 返回=" + notified + "（事件已存在＝有人持锁）→ 现有实例回调触发次数=" + woke);
            Check(bad, "修B 第二个实例把\"还原主窗口\"的信号发给现有实例，现有实例的回调被唤醒并执行（经 Dispatcher 回 UI 线程）",
                notified && woke >= 1);
            SingleInstance.Release();

            // ④ 托盘态与最小化态都能被这一个入口带回前台（App.RestoreMainWindow 是两条信号共同的落点）
            var mode = Application.Current.ShutdownMode;
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var w = new Window { Style = null, Width = 160, Height = 120, Left = -8000, Top = -8000, Opacity = 0, ShowInTaskbar = false };
            try
            {
                w.Show(); PumpToLoaded();
                Application.Current.MainWindow = w;
                TrayIcon.HideToTray(w); PumpToLoaded();                 // 托盘态
                bool r1 = App.RestoreMainWindow(); PumpToLoaded();
                bool fromTray = r1 && w.Visibility == Visibility.Visible;
                w.WindowState = WindowState.Minimized; PumpToLoaded();   // 最小化态
                bool r2 = App.RestoreMainWindow(); PumpToLoaded();
                bool fromMin = r2 && w.WindowState == WindowState.Normal;
                sb.AppendLine("— 修B 还原入口 App.RestoreMainWindow()：托盘态→可见=" + fromTray + "；最小化态→正常态=" + fromMin);
                Check(bad, "修B/修A 还原走同一个入口，托盘隐藏态与最小化态两种都能带回前台", fromTray && fromMin);
                Application.Current.MainWindow = null;
            }
            finally
            {
                TrayIcon.Dispose();
                try { if (w != null) w.Close(); } catch { }
                PumpToLoaded();
                Application.Current.ShutdownMode = mode;
            }
            return bad;
        }

        /// <summary>起一个真子进程跑 --instance-probe 并取退出码（同 EngineTests 里 Worker 冒烟的做法）。</summary>
        private static int RunProbeChild()
        {
            try
            {
                string exe = SingleInstance.CurrentExePath;
                var psi = new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = false };
                psi.ArgumentList.Add("--instance-probe");
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p == null) return -1;
                    if (!p.WaitForExit(120000)) { try { p.Kill(); } catch { } return -2; }
                    return p.ExitCode;
                }
            }
            catch { return -3; }
        }

        // ===== 修D：设置页四块整体移除，旧键读取即忽略 =====
        private static List<string> V49bSettings(StringBuilder sb)
        {
            var bad = new List<string>();
            string old = SnapshotCfg();
            var w = new MainWindow();
            w.ShowPanelForTest("settings");
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            var sp = FindByType(w.Content as FrameworkElement, typeof(SettingsPage)).Cast<SettingsPage>().FirstOrDefault();
            if (sp == null) { bad.Add("修D 取不到设置页"); RestoreCfg(old); return bad; }

            var gone = new[] { "EngineBox", "EngineStatusText", "BtnReDetect",
                "PrintoutDirText", "BtnChangePrintoutDir", "KeepConvertedBox", "ConvertDirText", "BtnChangeConvertDir",
                "ReportDirText", "BtnChangeReportDir", "DataDirText" };
            var still = gone.Where(n => sp.FindName(n) != null).ToList();
            var kept = new[] { "PrinterBox2", "BtnOpenPrinterProps", "LangBox", "ThemeCardHost", "CloseBox", "ReportEnableBox" };   // 52 号令·步骤 4：ThemeBox 下拉退役，由 ThemeCardHost 色卡宿主接位
            var lost = kept.Where(n => sp.FindName(n) == null).ToList();
            sb.AppendLine("— 修D 控件：退役 " + gone.Length + " 个全部消失=" + (still.Count == 0)
                + "；保留 6 个都在位=" + (lost.Count == 0) + "（打印机/属性按钮/语言/外观/关闭行为/打印统计开关）");
            Check(bad, "修D 设置页只剩五行：转换引擎、验证落盘（三个目录+保留开关）、统计目录、数据目录展示共 11 个控件全部消失，保留项一个没丢",
                still.Count == 0 && lost.Count == 0);

            // 52 号令·步骤 4 就地改判：主题卡从 CardControl 换成 x:Name=ThemeCard 的 CardBorder Border（装色卡网格）——计数口径把它算进去
            var cards = FindByType(sp, typeof(Wpf.Ui.Controls.CardControl)).Cast<FrameworkElement>()
                .Concat(FindByType(sp, typeof(Wpf.Ui.Controls.CardExpander)).Cast<FrameworkElement>()).ToList();
            var themeBorder = sp.FindName("ThemeCard") as Border;
            if (themeBorder != null) cards.Add(themeBorder);
            // 52 号令·步骤 4：ThemeCard 是 Border（无 Header 属性），标题在自身可视树里——豁免空卡判定
            var empty = cards.Where(c => !(c is Border) && string.IsNullOrWhiteSpace(HeaderText(c))).ToList();
            double topGap = cards.OfType<FrameworkElement>().Any() ? 0 : 0;
            sb.AppendLine("— 修D 卡片数=" + cards.Count + "（应 5）空卡片=" + empty.Count + " 首张顶部间距="
                + (cards.FirstOrDefault() == null ? "-" : ((FrameworkElement)cards.First()).Margin.Top.ToString()));
            Check(bad, "V1.0.0·52 号改判·修D 布局收紧：恰好五张卡片（主题卡以 Border 形态接位）、无空卡片、首张不留顶部间距",
                cards.Count == 5 && empty.Count == 0 && (cards.First() as FrameworkElement).Margin.Top == 0);

            foreach (var dict in new[] { "Localization/zh-CN.xaml", "Localization/en-US.xaml" })
            {
                var prefixes = new[] { "s.SettingsEngine", "s.SettingsOutput", "s.SettingsPrintoutDir", "s.SettingsKeepConverted",
                    "s.SettingsConvertDir", "s.SettingsReportDir", "s.SettingsOther", "s.SettingsDataDir", "s.Engine", "s.DataDir" };
                var left = prefixes.Select(p => p + "=" + CountKeysWithPrefix(dict, p)).ToList();
                int keptReport = new[] { "s.SettingsReport", "s.SettingsReportDesc", "s.SettingsReportEnable" }
                    .Count(k => new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) }.Contains(k));
                sb.AppendLine("— 修D " + dict + "：退役前缀残留 " + string.Join(" ", left) + "；打印统计开关三枚仍在=" + keptReport);
                Check(bad, "修D 四组资源键中英成对清零（含 s.Engine* 与 s.DataDir），而保留的「打印统计」开关三枚仍在",
                    prefixes.All(p => CountKeysWithPrefix(dict, p) == 0) && keptReport == 3);
            }

            // 老配置里那五枚键：读取即忽略（不迁移不报错），下次保存自然消失
            WriteCfg("{\"Theme\":\"light\",\"Engine\":\"com\",\"ReportDirectory\":\"D:\\\\旧统计目录\","
                + "\"KeepConvertedPdf\":true,\"ConvertOutputDirectory\":\"D:\\\\旧转换目录\",\"PrintoutDirectory\":\"D:\\\\旧输出目录\"}");
            SettingsService.Load();
            bool defOk = SettingsService.ConverterEngine == ConverterEngine.Auto
                && SettingsService.ReportDirectory == FileLogger.LogDirectory
                && SettingsService.KeepConvertedPdf == false
                && SettingsService.PrintoutDirectory == SettingsService.DefaultPrintoutDirectory
                && SettingsService.ConvertOutputDirectory == SettingsService.DefaultPrintoutDirectory;
            SettingsService.Save();
            string cfg = "";
            try { cfg = File.ReadAllText(AppPaths.SettingsPath); } catch { }
            var leaked = new[] { "\"Engine\"", "\"ReportDirectory\"", "\"KeepConvertedPdf\"", "\"ConvertOutputDirectory\"", "\"PrintoutDirectory\"" }
                .Where(cfg.Contains).ToList();
            sb.AppendLine("— 修D 老配置五枚键（Engine=com / 三个目录 / KeepConvertedPdf=true）读取后：全部按默认=" + defOk
                + "（引擎=" + SettingsService.ConverterEngine + "，统计目录=" + SettingsService.ReportDirectory + "）；"
                + "再保存后文件里还残留=" + (leaked.Count == 0 ? "0 枚" : string.Join(",", leaked))
                + "；主题键仍在=" + cfg.Contains("\"Theme\""));
            Check(bad, "修D 底层行为不变：老 settings.json 那五枚键读取即忽略（引擎自动、目录走既有默认），且下次保存自然消失（不迁移不报错）",
                defOk && leaked.Count == 0 && cfg.Contains("\"Theme\""));

            // 引擎入参回归：默认「自动」在转换通道里解析成实际引擎（本机有无 Office/WPS 都要给出可用答案）
            string docx = Path.Combine(Path.GetTempPath(), "v49b-引擎回归.docx");
            string auto = OfficeComPrinter.ResolveEngine("auto", docx);
            string viaSvc = OfficeComPrinter.ResolveEngine(
                SettingsService.ConverterEngine == ConverterEngine.MiniPdf ? "minipdf"
                : SettingsService.ConverterEngine == ConverterEngine.Com ? "com" : "auto", docx);
            sb.AppendLine("— 修D 引擎入参：ResolveEngine(\"auto\",…)=" + auto + "；按设置换算后=" + viaSvc
                + "（两者一致＝设置页不再提供手选后，打印走的仍是同一条自动探测/兜底路径）");
            Check(bad, "修D 转换引擎走默认 auto 且与打印通道换算一致（界面退役不影响引擎选择）",
                !string.IsNullOrEmpty(auto) && auto == viaSvc);
            RestoreCfg(old);
            return bad;
        }

        private static string HeaderText(FrameworkElement card)
        {
            object hdr = null;
            try
            {
                var prop = card.GetType().GetProperty("Header");
                if (prop != null) hdr = prop.GetValue(card, null);
            }
            catch { }
            string s = hdr as string;
            if (!string.IsNullOrWhiteSpace(s)) return s;
            var el = hdr as FrameworkElement;
            if (el != null)
            {
                var tbs = FindByType(el, typeof(TextBlock)).Cast<TextBlock>().ToList();
                return string.Join(" ", tbs.Select(t => t.Text ?? "").ToArray());
            }
            return "";
        }

        // ===== 修E：关于页顶部标题卡移除，界面里不再出现应用名 =====
        private static List<string> V49bAbout(StringBuilder sb)
        {
            var bad = new List<string>();
            var w = new MainWindow();
            w.ShowPanelForTest("about");
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            var ap = FindByType(w.Content as FrameworkElement, typeof(AboutPage)).Cast<AboutPage>().FirstOrDefault();
            if (ap == null) { bad.Add("修E 取不到关于页"); return bad; }
            var texts = FindByType(ap, typeof(TextBlock)).Cast<TextBlock>()
                .Where(t => t.ActualHeight > 0 && !string.IsNullOrWhiteSpace(t.Text)).Select(t => t.Text.Trim()).ToList();
            // 62 号·B（2026-10-10 需求方拍板改名 PrintPile）就地改判，条目不删：原断言钉死旧品牌字面量，而本令要求
            // 源码树里该字面量 0 命中 → 改钉"品牌取自 s.AppTitle"（一处改值、断言不再跟着改名），
            // 另加清零守卫：旧自称（用「批量打印」+「助手」合成写出，正是为了源码里不留那枚字面量）在可见文字里 0 条。
            string brand = Res("s.AppTitle");
            string legacyBrand = "批量打印" + "助手";
            int appName = texts.Count(t => t.Contains(brand));
            int legacyOnScreen = texts.Count(t => t.Contains(legacyBrand));
            int versioned = texts.Count(t => t.Contains(AppVersion.Full));
            // 49 号·版1 把标题区加回来了（需求方指定的参照布局）。修E 那三条已验收断言就地改判，条目保留不删。
            sb.AppendLine("— 49 号·版1 改判后（62 号·B 起钉新品牌）：关于页可见文字 " + texts.Count + " 条；含品牌串「" + brand + "」=" + appName
                + " 条（标题区一处）；含旧自称 " + legacyOnScreen + " 条（应为 0）；含当前版本号 " + AppVersion.Full + " = " + versioned + " 条（构建行 + 「检查更新」说明行各一处）");
            Check(bad, "修E→49 号→62 号·B 标题区回归：品牌串恰一处（顶部标题行，取自 s.AppTitle），其余卡片标题仍不借用应用名", appName == 1);
            Check(bad, "62 号·B 旧品牌自称在关于页可见文字里 0 残留", legacyOnScreen == 0);
            // 62 号·B：主窗标题栏与关于页标题行同源（都取 s.AppTitle）→ 改名一处生效，两侧都不留旧自称
            string winTitle = w.Title ?? "";
            string barTitle = w.AppTitleBar != null ? (w.AppTitleBar.Title ?? "") : "";
            sb.AppendLine("— 62 号·B 标题栏同源：主窗 Title=\"" + winTitle + "\"、TitleBar 控件 Title=\"" + barTitle
                + "\"（两处都应等于新品牌 s.AppTitle）");
            Check(bad, "62 号·B 改名一处生效：主窗标题与 TitleBar 都取到新品牌（含 PrintPile）、旧自称 0 残留",
                winTitle.Contains("PrintPile") && barTitle.Contains("PrintPile")
                && !winTitle.Contains(legacyBrand) && !barTitle.Contains(legacyBrand));
            Check(bad, "修E→49 号 版本展示两处各司其职（构建行＝四段+哈希、检查更新说明行＝当前版本），不到处铺", versioned == 2);
            Check(bad, "修E→49 号 首屏第一块就是标题区（应用名 + 版本），往下第一张卡是「自动检查更新」",
                texts.Count > 0 && texts[0].Contains(Res("s.AppTitle")) && texts.Any(t => t.Contains(Res("s.AboutAutoTitle"))));

            foreach (var dict in new[] { "Localization/zh-CN.xaml", "Localization/en-US.xaml" })
            {
                var rd = new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) };
                bool engineGone = !rd.Contains("s.AboutEngine");
                bool newTitle = rd.Contains("s.AboutIndependenceTitle") && rd.Contains("s.AboutVersionNow");
                // 62 号·B：两本字典的 s.AppTitle 必须已是新品牌，且整本字典的值里旧自称 0 残留（"一处改值"的收口守卫）
                string appTitleVal = rd["s.AppTitle"] as string ?? "";
                int legacyVals = rd.Cast<System.Collections.DictionaryEntry>()
                    .Count(e => (e.Value as string ?? "").Contains(legacyBrand));
                sb.AppendLine("— 修E " + dict + "：s.AboutEngine 已删=" + engineGone + "；独立性卡标题键与\"当前版本\"键在位=" + newTitle
                    + "；62 号·B s.AppTitle=\"" + appTitleVal + "\"、旧自称残留值 " + legacyVals + " 枚");
                Check(bad, "修E 中英两侧同步：引擎说明键随设置页一起删除，新增的卡标题/版本前缀两枚键成对在位", engineGone && newTitle);
                Check(bad, "62 号·B 字典层收口（" + dict + "）：s.AppTitle 已取新品牌（含 PrintPile），旧自称整本 0 枚残留",
                    appTitleVal.Contains("PrintPile") && legacyVals == 0);
            }
            // 49 号·版1：「日志位置」expander 整块移除，由三按钮行那枚「打开日志」承接——入口没消失，只是从"展开再点"升成一步点。
            // 修E/修整23 的"日志入口不消失"这一半仍然成立，就地改判（条目保留不删）。
            var logText = ap.FindName("LogPathText") as TextBlock;
            var oldLogBtn = ap.FindName("BtnOpenLog") as Button;
            var newLogBtn = ap.FindName("BtnLog") as Button;
            sb.AppendLine("— 修E/修C→49 号 改判：LogPathText 与 BtnOpenLog 随 expander 退役（在位=" + (logText != null) + "/" + (oldLogBtn != null)
                + "），「打开日志」按钮承接同一职责（BtnLog 在位=" + (newLogBtn != null) + " 文字=\"" + (newLogBtn == null ? "" : newLogBtn.Content) + "\"）");
            Check(bad, "修E 的\"应用内到达日志文件夹的入口不消失\"这一半仍成立（49 号改判为三按钮行的「打开日志」承接，路径文字不再单列一行）",
                newLogBtn != null && ap.LogButtonForTest == newLogBtn);
            return bad;
        }

        /// <summary>41 号自检行：修A/修B/修D/修E 一次跑完，逐条数字落 V49第二批定位.txt。</summary>
        internal static string V49bUi()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== 41 号·修A 最小化到系统托盘（Hide + NotifyIcon + WPF 主题菜单） ========");
                bad.AddRange(V49bTray(sb));
                sb.AppendLine("======== 41 号·修B 单实例守卫（命名 Mutex + 事件唤醒 + 真跨进程探针） ========");
                bad.AddRange(V49bSingle(sb));
                sb.AppendLine("======== 41 号·修D 设置页精简（四块整体移除，旧键读取即忽略） ========");
                bad.AddRange(V49bSettings(sb));
                sb.AppendLine("======== 41 号·修E 关于页精简（顶部标题卡移除，界面不再出现应用名） ========");
                bad.AddRange(V49bAbout(sb));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V49第二批定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（修A 托盘：图标来源＝随包 app.ico 或 exe 内嵌句柄（64 号·B 单文件形态走后者，ico 已内嵌不外发）、悬停文字取键且不超 63 字、HideToTray 后窗口隐藏+图标在场、左键单击回可见正常态并收回图标、"
                  + "右键菜单两项+分隔线文字全取资源键、「退出」把请求交回主窗口清理路径、未调用 ShowBalloonTip；"
                  + "修B 单实例：锁名/事件名不带 Global\\ 且按 exe 全路径派生（不同目录副本互不阻塞）、同进程二次抢锁必失败、"
                  + "真子进程探针 --instance-probe 实测持锁时退出码 3／释放后 0、NotifyExisting 唤醒现有实例回调、"
                  + "App.RestoreMainWindow 对托盘态与最小化态都还原；"
                  + "修D 设置页：11 个退役控件全消失、保留 6 个一个没丢、恰好五张卡无空卡且首张无顶部间距、四组资源键中英各 0 枚残留、"
                  + "老配置五枚键读取即忽略（引擎自动＋目录默认）且再保存后自然消失、auto 引擎换算与打印通道一致；"
                  + "修E 关于页（62 号·B 起改钉新品牌）：可见文字里品牌串恰一处、旧自称 0 条、版本号只留两处（构建行＋检查更新说明）、首屏第一张卡即检查更新、"
                  + "s.AboutEngine 已删＋两枚新键成对、日志行保留（唯一入口，口径已报备）；逐条数字见 V49第二批定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        // ===== 渲染取证：托盘图标与右键菜单（浅/深）+ 设置页/关于页精简（浅/深）+ 打印页 40 号成果回归 =====
        internal static int V49bRender(string log)
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

                    // ① 托盘图标：把随包的同一枚 app.ico 按 16/24/32/48 四种逻辑尺寸排一条（真机托盘里就是它）
                    var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
                    try
                    {
                        var frame = BitmapFrame.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "app.ico"), UriKind.Absolute));
                        foreach (int px in new[] { 16, 24, 32, 48 })
                            strip.Children.Add(new Image { Source = frame, Width = px, Height = px, Margin = new Thickness(6, 0, 6, 0) });
                    }
                    catch (Exception ex) { strip.Children.Add(new TextBlock { Text = "图标读取失败：" + ex.Message }); }
                    var cap = new TextBlock { Text = TrayIcon.TipText, FontSize = 12, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    var holder = new Border();
                    var hp = new StackPanel();
                    hp.Children.Add(strip); hp.Children.Add(cap);
                    holder.Child = hp;
                    holder.Measure(new Size(600, 120));
                    var hd = holder.DesiredSize;
                    holder.Arrange(new Rect(0, 0, Math.Max(360, hd.Width), Math.Max(1, hd.Height) + 8));
                    holder.UpdateLayout(); PumpToLoaded();
                    sb.AppendLine("—" + ts + " 托盘图标（app.ico 16/24/32/48 + 悬停文字）：" + SnapFlat(holder, "V49b托盘图标-" + ts + ".png", 288));

                    // ② 托盘右键菜单：真开一次（放一个离屏透明窗当锚点，菜单才有 PresentationSource），拍它本体
                    var mode = Application.Current.ShutdownMode;
                    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var anchor = new Window { Style = null, Width = 8, Height = 8, Left = -8000, Top = -8000, Opacity = 0, ShowInTaskbar = false };
                    try
                    {
                        anchor.Show(); PumpToLoaded();
                        var menu = TrayIcon.MenuForTest();
                        menu.PlacementTarget = anchor;
                        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
                        menu.IsOpen = true;
                        PumpToLoaded();
                        bool opened = menu.IsOpen && menu.ActualWidth > 4;
                        menu.IsOpen = false;
                        // 无头环境拍不出真浮层：ContextMenu 开在独立 PopupRoot（自带 HwndSource）里，
                        // 实测拍本体只截到带偏移的半行、拍 PopupRoot 全空（对照实验已留档）。
                        // 这张取证图因此按"内容复刻"出：字体、字号、前景、底色、分隔线全部**从真菜单对象上读下来**
                        // （menu.Foreground / mi.FontFamily / mi.Padding …），只有外框几何是近似的；真实外观进真机复验清单。
                        var mi1 = menu.Items.OfType<MenuItem>().FirstOrDefault();
                        var mi2 = menu.Items.OfType<MenuItem>().Skip(1).FirstOrDefault();
                        var card = new Border
                        {
                            Background = menu.Background ?? Brushes.White,
                            BorderBrush = menu.BorderBrush ?? Brushes.Gray,
                            BorderThickness = menu.BorderThickness,
                            CornerRadius = new CornerRadius(3),
                            Padding = new Thickness(0, 3, 0, 3),
                            MinWidth = Math.Max(140, menu.ActualWidth)
                        };
                        var rows = new StackPanel();
                        foreach (var mi in new[] { mi1, mi2 })
                        {
                            if (mi == null) continue;
                            rows.Children.Add(new TextBlock
                            {
                                Text = (string)mi.Header,
                                Foreground = mi.Foreground ?? menu.Foreground ?? Brushes.Black,
                                FontFamily = mi.FontFamily,
                                FontSize = mi.FontSize,
                                Margin = new Thickness(11, 6, 18, 6)
                            });
                            if (mi != mi2) rows.Children.Add(new Separator { Margin = new Thickness(6, 2, 6, 2) });
                        }
                        card.Child = rows;
                        card.Measure(new Size(420, 200));
                        var cd = card.DesiredSize;
                        card.Arrange(new Rect(0, 0, Math.Max(180, cd.Width), Math.Max(1, cd.Height)));
                        card.UpdateLayout(); PumpToLoaded();
                        string r = SnapFlat(card, "V49b托盘菜单-" + ts + ".png", 240);
                        sb.AppendLine("—" + ts + " 托盘右键菜单（内容复刻：真菜单对象读出的字体/前景/底色 + 分隔线）：真菜单已开=" + opened
                            + " 真菜单实测尺寸 " + menu.ActualWidth.ToString("0.#") + "x" + menu.ActualHeight.ToString("0.#")
                            + " 项文字=\"" + (mi1 == null ? "" : mi1.Header) + "\" / \"" + (mi2 == null ? "" : mi2.Header)
                            + "\" → " + r);
                    }
                    finally
                    {
                        try { anchor.Close(); } catch { }
                        TrayIcon.Dispose();
                        Application.Current.ShutdownMode = mode;
                        PumpToLoaded();
                    }

                    // ③ 设置页精简后（修D）与关于页精简后（修E）整窗
                    var w = new MainWindow();
                    w.ShowPanelForTest("settings");
                    w.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V49b设置页" + ts + "自检.txt", "V49b设置页精简-" + ts + ".png", w, 1120, 700, new string[0], 0);
                    var wa = new MainWindow();
                    wa.ShowPanelForTest("about");
                    wa.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V49b关于页" + ts + "自检.txt", "V49b关于页精简-" + ts + ".png", wa, 1120, 700, new string[0], 0);
                    sb.AppendLine("—" + ts + "：设置页（五行·无四块）与关于页（无顶部标题卡）整窗出图");
                }

                // ④ 打印页回归（40 号成果目检：三态 + 气泡），并复量行高——铁律不因本批改动而破
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                {
                    string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV49bP-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    RowVm rOk, rPart, rBad, rNone, rFull, rOff;
                    var w = SeedRangeRows(dir, out rOk, out rPart, out rBad, out rNone, out rFull, out rOff);
                    var grid = w.FindName("Grid") as DataGrid;
                    code |= RenderWindow("V49b打印页回归自检.txt", "V49b打印页回归-浅色.png", w, 1120, 700, new string[0], 0);
                    var box = w.RangeBoxForTest(rFull);
                    var popup = MainWindow.RangeBubbleOf(box);
                    var card = popup == null ? null : popup.Child as Border;
                    if (card != null)
                    {
                        card.Measure(new Size(360, 200));
                        var dd = card.DesiredSize;
                        card.Arrange(new Rect(0, 0, Math.Max(260, dd.Width), Math.Max(1, dd.Height)));
                        card.UpdateLayout(); PumpToLoaded();
                        sb.AppendLine("—回归：聚焦气泡仍在（" + (card.Child as TextBlock).Text.Replace("\n", "⏎") + "）"
                            + SnapFlat(card, "V49b范围气泡回归-浅色.png", 240));
                    }
                    sb.AppendLine("—回归：" + RowHeightsNote(grid));
                }
            }
            catch (Exception ex) { sb.AppendLine("探针异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            txt += "\r\n说明：41 号（V4.9 版本内修整第二批）取证——托盘图标（随包 app.ico 四种尺寸 + 悬停文字）浅/深各一张、"
                 + "托盘右键菜单（打开主窗口／退出）浅/深各一张（无头环境若浮层落不了地会写明，改由真机复验看）、"
                 + "设置页精简后浅/深整窗（只剩打印机/语言/外观/关闭行为/打印统计五行，四块已移除且无空卡片）、"
                 + "关于页精简后浅/深整窗（顶部旧品牌标题卡曾被整块移除、49 号标题区回归、62 号·B 起品牌＝PrintPile · 批量打印，版本号并入检查更新行）、"
                 + "打印页 40 号成果回归一张（三态联动 + 聚焦气泡）并复量行高（铁律：六行等高不变）。"
                 + "自检全程离线：不真开浏览器、不真起第二实例界面、不打网络。"
                 + (txt.Contains("失败") || txt.Contains("未渲染") ? " 判定：以自检行与逐条数字为准（本段含失败/未渲染字样）" : " 判定：出图完整");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
