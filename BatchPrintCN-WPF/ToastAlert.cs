using System;
using System.Windows;
using Wpf.Ui.Controls;

namespace BatchPrintCN
{
    /// <summary>
    /// 三色提示（U2-2 起由 WPF-UI 的 Snackbar 呈现）：
    ///   蓝 = 新功能/提示；绿 = 打印成功；红 = 打印失败（含错误说明）。
    /// 非模态、右下角滑入、自动隐藏，不阻塞打印主流程。
    /// 宿主：所在窗口 XAML 里需有 x:Name="ToastHost" 的 SnackbarPresenter（主窗口已放）。
    /// </summary>
    public static class ToastAlert
    {
        public enum Kind { Info, Success, Error }

        public static void ShowInfo(string message, Window owner = null) { Show(message, Kind.Info, owner); }
        public static void ShowSuccess(string message, Window owner = null) { Show(message, Kind.Success, owner); }
        public static void ShowError(string message, Window owner = null) { Show(message, Kind.Error, owner); }

        public static void Show(string message, Kind kind, Window owner = null)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => Create(message, kind, owner)));
        }

        private static void Create(string message, Kind kind, Window owner)
        {
            var app = Application.Current;
            if (app == null) return;
            SnackbarPresenter host = FindHost(owner ?? app.MainWindow);
            if (host == null) return;

            SymbolRegular glyph;
            ControlAppearance appearance;
            switch (kind)
            {
                case Kind.Success:
                    appearance = ControlAppearance.Success;
                    glyph = SymbolRegular.CheckmarkCircle24;
                    break;
                case Kind.Error:
                    appearance = ControlAppearance.Danger;
                    glyph = SymbolRegular.ErrorCircle24;
                    break;
                default:
                    appearance = ControlAppearance.Info;
                    glyph = SymbolRegular.Info24;
                    break;
            }

            var snackbar = new Snackbar(host)
            {
                Content = message,
                Appearance = appearance,
                Icon = new SymbolIcon { Symbol = glyph, FontSize = 20 },
                Timeout = TimeSpan.FromMilliseconds(3500),
                IsCloseButtonEnabled = true
            };
            snackbar.Show();
        }

        /// <summary>从窗口的命名空间取 SnackbarPresenter（ToastHost）。</summary>
        private static SnackbarPresenter FindHost(Window window)
        {
            try { return window?.FindName("ToastHost") as SnackbarPresenter; }
            catch { return null; }
        }
    }
}
