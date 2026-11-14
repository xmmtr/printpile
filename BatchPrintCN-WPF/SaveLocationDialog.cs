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
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// v1.2.2 功能（2026-11-13 老板需求）：打印到「Microsoft Print to PDF」前的保存位置询问框（每批一次）。
    /// 形态对齐浏览器"下载前询问"：路径框＋浏览…＋「每次不询问」勾选＋确定/取消。
    /// 主题刷色沿用 InputDialog 的做法（代码建控件＋TryFindResource，深浅主题自适应）。
    /// </summary>
    public class SaveLocationDialog : Window
    {
        private readonly TextBox _box;
        private readonly CheckBox _dontAsk;

        /// <summary>用户选定的目录（点「确定」时生效）。</summary>
        public string SelectedPath { get; private set; }

        /// <summary>是否勾了「每次不询问」（调用方据此写回 SettingsService）。</summary>
        public bool DontAskAgain { get { return _dontAsk.IsChecked == true; } }

        public SaveLocationDialog(string title, string initialDir, string browseText, string dontAskText, string note)
        {
            var app = Application.Current;
            var textBrush = (Brush)(app.TryFindResource("TextPrimaryBrush") ?? Brushes.Black);
            var textSecBrush = (Brush)(app.TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);
            var winBg = (Brush)(app.TryFindResource("WindowBgBrush") ?? Brushes.White);

            SelectedPath = initialDir ?? "";
            Title = title ?? "保存文件至";
            Width = 520;
            Height = 240;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = winBg;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 说明
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 路径 + 浏览
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 每次不询问
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // 按钮

            // 说明行
            var prompt = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(note) ? "" : note,
                Foreground = textSecBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10),
            };
            Grid.SetRow(prompt, 0);
            root.Children.Add(prompt);

            // 路径行：文本框（可直接粘贴）+ 浏览…
            var pathRow = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _box = new TextBox { Text = SelectedPath, Margin = new Thickness(0, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            _box.Style = (Style)app.TryFindResource("InputGlowBox");   // v1.2.2（2026-11-14 拍板）：输入框统一方案 G 极光样式
            Grid.SetColumn(_box, 0);
            pathRow.Children.Add(_box);
            var browse = new Button { Content = browseText ?? "浏览…", Width = 96, Height = 30 };
            browse.Style = (Style)app.TryFindResource("BtnOutline");
            browse.Click += OnBrowse;
            Grid.SetColumn(browse, 1);
            pathRow.Children.Add(browse);
            Grid.SetRow(pathRow, 1);
            root.Children.Add(pathRow);

            // 每次不询问
            _dontAsk = new CheckBox
            {
                Content = dontAskText ?? "每次不询问",
                Foreground = textBrush,
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 14),
            };
            Grid.SetRow(_dontAsk, 2);
            root.Children.Add(_dontAsk);

            // 按钮行
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "确定", Width = 84, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 84, Height = 30, IsCancel = true };
            ok.Style = (Style)app.TryFindResource("BtnPrimary");
            cancel.Style = (Style)app.TryFindResource("BtnOutline");
            ok.Click += (s, e) =>
            {
                string dir = (_box.Text ?? "").Trim();
                if (dir.Length == 0)
                {
                    MessageBox.Show(this, "请选择一个保存文件夹。", "保存文件至", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                try { Directory.CreateDirectory(dir); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "该文件夹不可用：" + ex.Message, "保存文件至", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                SelectedPath = dir;
                DialogResult = true;
            };
            cancel.Click += (s, e) => DialogResult = false;
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            Grid.SetRow(btns, 3);
            root.Children.Add(btns);

            Content = root;
            Loaded += (s, e) => { try { _box.Focus(); _box.SelectAll(); } catch { } };
        }

        /// <summary>浏览…：.NET 8 WPF 的 OpenFolderDialog（无 WinForms 依赖）；初始目录无效时退回默认。</summary>
        private void OnBrowse(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "选择保存文件夹",
                    Multiselect = false,
                };
                string cur = (_box.Text ?? "").Trim();
                if (cur.Length > 0 && Directory.Exists(cur)) dlg.InitialDirectory = cur;
                if (dlg.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dlg.FolderName)) _box.Text = dlg.FolderName;
            }
            catch { /* 对话框异常不阻塞手输路径 */ }
        }
    }
}
