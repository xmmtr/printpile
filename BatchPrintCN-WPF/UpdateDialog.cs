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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 66 号令 T2/T3：更新对话框——**确认 → 下载 → 安装** 三段同一个窗（非模态、可关），与 InputDialog/ActivationDialog
    /// 同一模式＝纯代码建窗、主题自适应、自检走注入点不碰真网络与真进程。
    /// 取消语义（需求方拍板）：确认段点「取消」只关本窗，左下角橙色按钮保留；下载段点「取消」删掉半截临时文件后退回确认段。
    /// 安装段分叉：安装版（判据见 <see cref="UpdateInstaller.LooksInstalled"/>）先给一次「即将关闭并打开安装向导」的确认，
    /// 用户点确认才拉起 Setup（/SILENT）并退出本程序；便携版给「打开所在文件夹」＋"解压覆盖即完成更新"的提示，v1 不做自动覆盖。
    /// </summary>
    public class UpdateDialog : Window
    {
        public enum Phase { Confirm, Downloading, Failed, ReadyInstalled, ReadyPortable, Launching }

        private readonly UpdateChecker.Entry _entry;
        private readonly Version _current;
        private readonly bool _installed;
        private System.Threading.CancellationTokenSource _cts;
        private string _path;

        private readonly TextBlock _notes;
        private readonly TextBlock _size;
        private readonly ProgressBar _bar;
        private readonly TextBlock _progressText;
        private readonly TextBlock _hint;
        private readonly Button _primary;
        private readonly Button _secondary;

        /// <summary>自检注入点：非 null 时代替"关掉本程序"（真机＝Application.Shutdown）。</summary>
        internal static Action ExitOverride;

        public UpdateDialog(UpdateChecker.Entry entry, Version current, bool installed)
        {
            _entry = entry;
            _current = current ?? AutoUpdate.CurrentVersion();
            _installed = installed;

            var app = Application.Current;
            Title = L("s.UpTitle");
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = (Brush)(app != null ? app.TryFindResource("WindowBgBrush") : null) ?? Brushes.White;
            SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");

            var root = new StackPanel { Margin = new Thickness(20, 16, 20, 16) };
            root.Children.Add(new TextBlock
            {
                Text = L("s.UpFoundFmt").Replace("{0}", _entry == null ? "" : _entry.Version),
                FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(_notes = new TextBlock
            {
                Text = _entry == null || string.IsNullOrWhiteSpace(_entry.Notes) ? "" : _entry.Notes,
                FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(new TextBlock
            {
                Text = L("s.UpCurrentFmt").Replace("{0}", _current == null ? "" : _current.ToString()),
                FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            root.Children.Add(_size = new TextBlock
            {
                Text = L("s.UpSizeFmt").Replace("{0}", "…"),
                FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            _bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = 0, Height = 10, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            root.Children.Add(_bar);
            _progressText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
            root.Children.Add(_progressText);
            _hint = new TextBlock { FontSize = 12, Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap };
            _hint.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondaryBrush");
            root.Children.Add(_hint);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            _primary = new Button { Content = L("s.UpBtnDownload"), MinWidth = 104, Height = 30, Margin = new Thickness(0, 0, 8, 0), Style = StyleOf("BtnPrimary"), IsDefault = true };
            _secondary = new Button { Content = L("s.Cancel"), MinWidth = 76, Height = 30, Style = StyleOf("BtnOutline"), IsCancel = true };
            _primary.Click += (s, e) => OnPrimary();
            _secondary.Click += (s, e) => OnSecondary();
            btns.Children.Add(_primary);
            btns.Children.Add(_secondary);
            root.Children.Add(btns);

            Content = root;
            GoPhase(Phase.Confirm);
            Closed += (s, e) => { try { if (_cts != null) _cts.Cancel(); } catch { } };
        }

        private static Style StyleOf(string key)
        {
            var app = Application.Current;
            return app == null ? null : app.TryFindResource(key) as Style;
        }

        private static string L(string key)
        {
            var app = Application.Current;
            var o = app == null ? null : app.TryFindResource(key);
            return o as string ?? key;
        }

        /// <summary>当前阶段（自检取用）。</summary>
        internal Phase CurrentPhaseForTest { get; private set; }

        /// <summary>进度读数（自检逐数核对：百分比与"已下载／总大小"文本）。</summary>
        internal double PercentForTest { get { return _bar == null ? -1 : _bar.Value; } }
        internal string ProgressTextForTest { get { return _progressText == null ? null : _progressText.Text; } }
        internal string PrimaryTextForTest { get { return _primary == null ? null : _primary.Content as string; } }
        internal string HintTextForTest { get { return _hint == null ? null : _hint.Text; } }
        internal string DownloadedPathForTest { get { return _path; } }
        /// <summary>自检用：直接推进下载（等价于点主按钮）。</summary>
        internal void PressPrimaryForTest() { OnPrimary(); }
        internal void PressSecondaryForTest() { OnSecondary(); }

        private void GoPhase(Phase p)
        {
            CurrentPhaseForTest = p;
            bool downloading = p == Phase.Downloading;
            _bar.Visibility = downloading ? Visibility.Visible : Visibility.Collapsed;
            _progressText.Visibility = _bar.Visibility;
            _notes.Visibility = p == Phase.Confirm || p == Phase.Downloading || p == Phase.Failed ? Visibility.Visible : Visibility.Collapsed;
            _size.Visibility = p == Phase.Confirm || p == Phase.Downloading ? Visibility.Visible : Visibility.Collapsed;
            switch (p)
            {
                case Phase.Confirm: Set(L("s.UpBtnDownload"), L("s.Cancel"), null); break;
                case Phase.Downloading: Set(L("s.UpDownloading"), L("s.Cancel"), null); break;
                case Phase.Failed: Set(L("s.Retry"), L("s.Cancel"), L("s.UpFailed")); break;
                case Phase.ReadyInstalled: Set(L("s.UpBtnInstall"), L("s.UpBtnLater"), L("s.UpExitConfirm")); break;
                case Phase.ReadyPortable: Set(L("s.UpBtnFolder"), L("s.PvClose"), L("s.UpPortableHint")); break;
                case Phase.Launching: Set(L("s.UpOpening"), L("s.PvClose"), null); break;
            }
        }

        private void Set(string primary, string secondary, string hint)
        {
            if (primary != null) _primary.Content = primary;
            _secondary.Content = secondary;
            _hint.Text = hint ?? "";
            _hint.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnPrimary()
        {
            switch (CurrentPhaseForTest)
            {
                case Phase.Confirm:
                case Phase.Failed:
                    StartDownload(); break;
                case Phase.ReadyInstalled:
                    InstallNow(); break;
                case Phase.ReadyPortable:
                    if (UpdateInstaller.OpenFolderSelecting(_path)) Close();
                    else Set(null, L("s.PvClose"), L("s.UpFailed"));
                    break;
                case Phase.Launching:
                    Close(); break;
            }
        }

        private void OnSecondary()
        {
            if (CurrentPhaseForTest == Phase.Downloading)
            {
                try { if (_cts != null) _cts.Cancel(); } catch { }
                return;   // 取消下载 → 退回确认段（临时文件由下载器删；左下角按钮仍在）
            }
            Close();      // 其余段：只关本窗，橙色按钮保留（需求方拍板）
        }

        private async void StartDownload()
        {
            _cts = new System.Threading.CancellationTokenSource();
            var token = _cts.Token;
            UpdateState.SetDownloading(true);
            GoPhase(Phase.Downloading);
            _path = UpdateDownloader.TempPathFor(_entry);
            var progress = new Progress<UpdateDownloader.Progress>(p =>
            {
                _bar.Value = p.Percent;
                _progressText.Text = p.Percent.ToString("0") + "%　" + UpdateDownloader.SizeText(p.Received)
                    + (p.Total > 0 ? "／" + UpdateDownloader.SizeText(p.Total) : "");
                if (p.Total > 0) _size.Text = L("s.UpSizeFmt").Replace("{0}", UpdateDownloader.SizeText(p.Total));
            });
            UpdateDownloader.Outcome o;
            try { o = await UpdateDownloader.RunAsync(_entry, progress, token); }
            finally { UpdateState.SetDownloading(false); }

            if (token.IsCancellationRequested)
            {
                _bar.Value = 0;
                _progressText.Text = "";
                GoPhase(Phase.Confirm);
                return;
            }
            if (o == null || !o.Ok)
            {
                FileLogger.Write("更新下载未完成：" + (o == null ? "(无结果)" : (o.Error ?? o.Fail.ToString())));
                _bar.Value = 0;
                GoPhase(Phase.Failed);
                return;
            }
            _path = o.Path;
            _bar.Value = 100;
            GoPhase(_installed ? Phase.ReadyInstalled : Phase.ReadyPortable);
        }

        private void InstallNow()
        {
            GoPhase(Phase.Launching);
            if (!UpdateInstaller.LaunchSetup(_path))
            {
                Set(L("s.UpBtnInstall"), L("s.PvClose"), L("s.UpFailed"));
                return;
            }
            UpdateState.ClearForInstall();
            FileLogger.Write("更新：已交棒安装程序，本程序退出");
            var exit = ExitOverride;
            if (exit != null) { exit(); return; }
            try { Application.Current.Shutdown(); } catch (Exception ex) { FileLogger.Write("更新：退出失败：" + ex.Message); }
        }
    }
}
