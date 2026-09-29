using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    public partial class AboutPage : UserControl
    {
        private bool _checkingUpdate;

        public AboutPage()
        {
            InitializeComponent();
            VersionText.Text = AppVersion.Display;
            LogPathText.Text = FileLogger.LogDirectory;
        }

        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        private void OnOpenLog(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + FileLogger.LogDirectory + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开日志目录：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// V4.7 修整2：弹窗正文（按检查结果出"已是最新 / 发现新版本 / 检查更新失败"三种文案）。
        /// 与按钮点击共用这一个函数——自检直取它断言，不必弹真模态框（无窗口自检里弹窗会把整轮卡死）。
        /// </summary>
        internal static string NoticeFor(UpdateChecker.Result r, Version current)
        {
            if (r == null) return L("s.UpdateFailedFmt").Replace("{0}", "(null)");
            switch (r.Outcome)
            {
                case UpdateChecker.Outcome.Found:
                    string notes = string.IsNullOrWhiteSpace(r.Latest.Notes) ? "" : "\n" + L("s.UpdateNotesFmt").Replace("{0}", r.Latest.Notes);
                    return L("s.UpdateFoundFmt").Replace("{0}", r.Latest.Version).Replace("{1}", r.Latest.Date ?? "")
                        + notes + "\n\n" + L("s.UpdateAskDownload");
                case UpdateChecker.Outcome.Failed:
                    return L("s.UpdateFailedFmt").Replace("{0}", r.Error ?? "");
                default:   // UpToDate（含清单里没有任何可用 active 条目）
                    return L("s.UpdateLatestFmt").Replace("{0}", current == null ? AppVersion.Display : current.ToString(4));
            }
        }

        /// <summary>检查结果弹窗的标题（三种情形同一个标题）。</summary>
        internal static string NoticeTitle { get { return L("s.AboutUpdateTitle"); } }

        private async void OnCheckUpdate(object sender, RoutedEventArgs e)
        {
            if (_checkingUpdate) return;
            _checkingUpdate = true;
            BtnCheckUpdate.IsEnabled = false;
            try
            {
                Version cur = typeof(AboutPage).Assembly.GetName().Version;
                UpdateChecker.Result r = await UpdateChecker.CheckAsync(cur);
                var owner = Window.GetWindow(this);
                if (r.Outcome == UpdateChecker.Outcome.Found)
                {
                    if (MessageBox.Show(owner, NoticeFor(r, cur), NoticeTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes
                        && !UpdateChecker.OpenDownload(r.Latest))
                        MessageBox.Show(owner, L("s.UpdateOpenFailed"), NoticeTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(owner, NoticeFor(r, cur), NoticeTitle, MessageBoxButton.OK,
                        r.Outcome == UpdateChecker.Outcome.Failed ? MessageBoxImage.Warning : MessageBoxImage.Information);
                }
            }
            finally
            {
                _checkingUpdate = false;
                BtnCheckUpdate.IsEnabled = true;
            }
        }
    }
}
