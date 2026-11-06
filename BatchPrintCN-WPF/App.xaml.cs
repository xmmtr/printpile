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
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BatchPrintCN
{
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// 49 号·版2：本进程是不是四项自检/对话框模式。自动检查更新的定时器靠它把自己挡在自检之外
        /// （自检一轮要构造几十个 MainWindow，起了定时器就会真发网络请求，门槛也就不可复现了）。
        /// </summary>
        public static bool GateMode { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            string[] cmd = e.Args;
            // 修B：探针模式只回答"有没有别人在跑"就退出（不读设置、不建窗口、不写日志），供自检做真跨进程断言
            if (HasFlag(cmd, "instance-probe")) { Environment.Exit(SingleInstance.RunProbe()); return; }

            // 缺1：四项自检的开关先归一化再认——带不带前导横线、大小写都算同一个开关。
            string gate = SelfTestGate(cmd);
            GateMode = gate != null;      // 49 号·版2：自动检查更新的定时器认这个旗标（对话框模式与四项自检一律不起表）
            if (gate == null)
            {
                // 修B：单实例守卫——已有实例在跑就把"还原主窗口"的信号发给它，本进程静默退出（不弹错误框）。
                // 四项自检不抢这把锁：自检要在一轮里反复构造窗口，被自己的守卫拦掉就成了假失败。
                string exe = SingleInstance.CurrentExePath;
                if (!SingleInstance.TryAcquire(exe))
                {
                    SingleInstance.NotifyExisting(exe);
                    Shutdown();
                    return;
                }
                SingleInstance.Watch(() => RestoreMainWindow());
            }

            AppPaths.Init();   // V4.1 便携化：必须在任何日志/设置/SQLite 使用前初始化目录

            // 78 号令·A1：一次性抹掉旧版（v1.0/v1.1）反滥用手段残留的 HKCU\Software\PrintPile（幂等、静默；
            // 开源免费后本产品对注册表零写入，此句是收尾动作，Inno 卸载项不在清理范围）。
            LegacyCleanup.Run();

            // M1：全局异常处理——未捕获异常不闪退：先落盘日志，再弹窗询问是否继续
            DispatcherUnhandledException += (s, args) =>
            {
                FileLogger.Write("DispatcherUnhandledException: " + args.Exception);
                args.Handled = ConfirmFatal(args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                FileLogger.Write("AppDomain.UnhandledException: " + args.ExceptionObject);
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                FileLogger.Write("UnobservedTaskException: " + args.Exception);
                args.SetObserved();
            };

            // 自检模式：跑完检查后由 FinishGate 统一收尾（释放托盘图标 → 把结论打到标准输出 → 交退出码）
            if (gate != null)
            {
                int code;
                if (gate == "dialogtest") code = WpfTests.RenderDialog();
                else if (gate == "formtest") code = WpfTests.RenderMain();
                else if (gate == "selftest") code = WpfTests.EngineSelfTest();
                else code = WpfTests.RenderTheme();
                WpfTests.FinishGate(gate, code);
                return;
            }

            // 正常启动：加载持久化设置（语言/主题/引擎），应用资源
            SettingsService.Load();
            ApplyLanguage(SettingsService.Language);

            // 80 号令（V1.2.1）：开机自启动按**意图**对齐 Run 键——意图为开而键缺失就补写、意图为关就确保无键
            //（用户在系统里手工删键属外部干预，下次启动按意图补回；程序换了目录也由这一步自动修正）。
            // 四项自检模式不写注册表（与 49 号"自检不起定时器"同一口径），自检要测的往返由 80 号自检段自己做完并清理。
            if (!GateMode) AutoStart.SyncOnStartup();

            // 52 号令·步骤 4：主题引擎恢复——扫描用户主题（data\themes\）→ 按保存的主题名应用（缺键按旧 Theme 键迁移）。
            // ThemeService.Apply 内部先按 mode 走 App.ApplyTheme（WPF-UI 深浅基座），再叠加 12 令牌色。
            ThemeService.ScanUserThemes();
            var themeDef = ThemeService.Find(SettingsService.ResolveThemeName(SettingsService.ThemeName, SettingsService.Theme))
                           ?? ThemeService.DefaultTheme;
            ThemeService.Apply(themeDef);

            // 73 号令：全功能免费开放，这里不再有许可/限时体验接线；只剩内置默认模板的首启预置
            //（库里 app_meta 记着"预置过"就不再自动补，删掉要靠「恢复默认模板」入口）。
            if (!GateMode) BuiltinTemplates.EnsureSeeded();

            CleanupTmp();   // V3.1 卫生项：启动清扫转换临时目录残留

            var main = new MainWindow();
            main.Show();
        }

        /// <summary>
        /// 缺1：命令行开关归一化后取那四个自检模式的名字（selftest/formtest/dialogtest/themetest），没有则 null。
        /// 原来只认 `--selftest` 这种带前导横线的写法：直接跑 `PrintPile.exe selftest` 会被当成"正常启动"，
        /// 于是不跑任何检查、弹出主窗口、进程永不退出、stdout 一个字节都没有——这正是 44 号令报的现象。
        /// </summary>
        internal static string SelfTestGate(string[] cmd)
        {
            foreach (string a in cmd)
            {
                string k = NormalizeFlag(a);
                if (k == "selftest" || k == "formtest" || k == "dialogtest" || k == "themetest") return k;
            }
            return null;
        }

        /// <summary>开关是否出现（同样先归一化，隐藏开关 --instance-probe 也走这套）。</summary>
        internal static bool HasFlag(string[] cmd, string name)
        {
            foreach (string a in cmd) if (NormalizeFlag(a) == name) return true;
            return false;
        }

        private static string NormalizeFlag(string a)
        {
            return (a ?? "").Trim().TrimStart('-', '/').ToLowerInvariant();
        }

        /// <summary>
        /// 修A/修B：把主窗口带回前台的**唯一**入口——托盘隐藏态（含修A 的 Hide）、最小化态、正常态三种都走这里。
        /// 触发点有两个：托盘图标左键单击（经 TrayIcon.Restore）、第二个实例启动时发来的还原信号（经 SingleInstance.Watch）。
        /// </summary>
        internal static bool RestoreMainWindow()
        {
            var w = Current == null ? null : Current.MainWindow;
            if (w == null) return false;
            if (TrayIcon.IsVisible || w.Visibility != Visibility.Visible) { TrayIcon.Restore(); return true; }
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            try
            {
                w.Activate();
                w.Topmost = true; w.Topmost = false;      // WPF 里唯一稳的"带到最前"写法
            }
            catch { }
            return true;
        }

        /// <summary>V3.1 卫生项：启动时清理转换临时目录残留（V4.1 指便携 tmp\）。</summary>
        private static void CleanupTmp()
        {
            try
            {
                if (System.IO.Directory.Exists(AppPaths.TmpDir))
                    foreach (string f in System.IO.Directory.GetFiles(AppPaths.TmpDir))
                    {
                        try { System.IO.File.Delete(f); } catch { }
                    }
            }
            catch { }
        }

        /// <summary>
        /// 运行时切换主题资源字典（浅色/深色），界面立即刷新。
        /// U1 换皮：① 原位替换（不再 Insert(0)），保证我们的主题字典优先级高于 WPF-UI 的同名键；
        ///          ② 同步驱动 WPF-UI 内部主题，背景材质固定 None（需求方拍板：Win10 环境不开云母）。
        /// </summary>
        public static void ApplyTheme(AppTheme theme)
        {
            if (Current == null) return;
            var dicts = Current.Resources.MergedDictionaries;
            int idx = -1;
            for (int i = 0; i < dicts.Count; i++)
            {
                var s = dicts[i].Source;
                if (s != null && (s.OriginalString.EndsWith("LightTheme.xaml", StringComparison.OrdinalIgnoreCase) ||
                                  s.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase)))
                { idx = i; break; }
            }
            string src = theme == AppTheme.Dark ? "Themes/DarkTheme.xaml" : "Themes/LightTheme.xaml";
            var nd = new ResourceDictionary { Source = new Uri(src, UriKind.Relative) };
            if (idx >= 0) { dicts.RemoveAt(idx); dicts.Insert(idx, nd); }
            else dicts.Add(nd);

            try
            {
                Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                    theme == AppTheme.Dark ? Wpf.Ui.Appearance.ApplicationTheme.Dark : Wpf.Ui.Appearance.ApplicationTheme.Light,
                    Wpf.Ui.Controls.WindowBackdropType.None, false);
            }
            catch { }   // 自检/无窗口环境下失败不影响我们自己的主题切换
        }

        /// <summary>运行时切换语言资源字典（中文/英文），界面立即刷新。</summary>
        public static void ApplyLanguage(AppLanguage lang)
        {
            if (Current == null) return;
            var dicts = Current.Resources.MergedDictionaries;
            var old = dicts.FirstOrDefault(d => d.Source != null &&
                (d.Source.OriginalString.EndsWith("zh-CN.xaml", StringComparison.OrdinalIgnoreCase) ||
                 d.Source.OriginalString.EndsWith("en-US.xaml", StringComparison.OrdinalIgnoreCase)));
            if (old != null) dicts.Remove(old);
            string src = lang == AppLanguage.EnUS ? "Localization/en-US.xaml" : "Localization/zh-CN.xaml";
            dicts.Add(new ResourceDictionary { Source = new Uri(src, UriKind.Relative) });
        }

        private static bool ConfirmFatal(Exception ex)
        {
            // 自检模式不弹窗：直接记日志并以失败退出（避免弹窗挂起自检进程）
            string[] cmd;
            try { cmd = Environment.GetCommandLineArgs(); }
            catch { cmd = new string[0]; }
            if (SelfTestGate(cmd) != null) return false;
            try
            {
                // V3.0：诊断弹窗（版本/系统/最近日志/异常堆栈 + 一键复制）
                var dlg = new DiagnosticWindow(BuildDiagnostic(ex));
                if (Current != null && Current.MainWindow != null)
                {
                    try { dlg.Owner = Current.MainWindow; } catch { }
                }
                dlg.ShowDialog();
                return dlg.ContinueRunning;
            }
            catch { return true; }
        }

        private static string BuildDiagnostic(Exception ex)
        {
            var sb = new System.Text.StringBuilder();
            try
            {
                sb.AppendLine("版本：" + AppVersion.Display + "（" + AppVersion.Full + "）");
                sb.AppendLine("系统：" + Environment.OSVersion + (Environment.Is64BitOperatingSystem ? " x64" : " x86"));
                sb.AppendLine(".NET：" + Environment.Version);
                sb.AppendLine("日志目录：" + FileLogger.LogDirectory);
                sb.AppendLine();
                sb.AppendLine("---- 最近日志 ----");
                sb.AppendLine(FileLogger.RecentLines(20));
                sb.AppendLine();
                sb.AppendLine("---- 异常 ----");
                sb.AppendLine(ex.ToString());
            }
            catch { sb.AppendLine(ex.ToString()); }
            return sb.ToString();
        }
    }
}
