using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BatchPrintCN
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AppPaths.Init();   // V4.1 便携化：必须在任何日志/设置/SQLite 使用前初始化目录

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

            string[] cmd = e.Args;
            // 自检模式：先按默认浅色主题渲染验证，再补跑深色主题渲染（RenderMain 内部处理）
            if (cmd.Any(a => a == "--dialogtest")) { Shutdown(WpfTests.RenderDialog()); return; }
            if (cmd.Any(a => a == "--formtest")) { Shutdown(WpfTests.RenderMain()); return; }
            if (cmd.Any(a => a == "--selftest")) { Shutdown(WpfTests.EngineSelfTest()); return; }
            if (cmd.Any(a => a == "--themetest")) { Shutdown(WpfTests.RenderTheme()); return; }

            // 正常启动：加载持久化设置（语言/主题/引擎），应用资源
            SettingsService.Load();
            ApplyTheme(SettingsService.Theme);
            ApplyLanguage(SettingsService.Language);

            CleanupTmp();   // V3.1 卫生项：启动清扫转换临时目录残留

            new MainWindow().Show();
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
            if (cmd.Any(a => a == "--selftest" || a == "--formtest" || a == "--dialogtest" || a == "--themetest"))
                return false;
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
