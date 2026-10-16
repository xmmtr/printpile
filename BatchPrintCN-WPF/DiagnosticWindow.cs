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
    /// V3.0 崩溃诊断弹窗（P0-②）：未预期异常时展示诊断摘要（版本/系统/日志路径/最近日志/异常堆栈），
    /// 支持一键复制到剪贴板，用户可直接粘贴给排障方。主题自适应。
    /// </summary>
    public class DiagnosticWindow : Window
    {
        public bool ContinueRunning { get; private set; } = true;

        public DiagnosticWindow(string diagnostic)
        {
            var app = Application.Current;
            var textBrush = (Brush)(app.TryFindResource("TextPrimaryBrush") ?? Brushes.Black);
            var textSecBrush = (Brush)(app.TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);
            var bgBrush = (Brush)(app.TryFindResource("WindowBgBrush") ?? Brushes.White);
            var cardBrush = (Brush)(app.TryFindResource("CardBgBrush") ?? Brushes.White);

            Title = GetStr("s.Error");
            Width = 720;
            Height = 560;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ShowInTaskbar = false;
            Background = bgBrush;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            header.Children.Add(new TextBlock
            {
                Text = "程序遇到未预期错误，诊断信息如下。",
                Foreground = textBrush,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            });
            header.Children.Add(new TextBlock
            {
                Text = "点击「复制诊断」后可直接粘贴给排障方；日志已写入磁盘。",
                Foreground = textSecBrush,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0)
            });
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var box = new TextBox
            {
                IsReadOnly = true,
                Text = diagnostic,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.NoWrap,
                Background = cardBrush
            };
            Grid.SetRow(box, 1);
            root.Children.Add(box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var copy = new Button { Content = "复制诊断", Width = 100, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
            var cont = new Button { Content = "继续运行", Width = 100, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var quit = new Button { Content = "退出程序", Width = 100, Height = 32 };
            copy.Style = (Style)app.TryFindResource("BtnOutline");
            cont.Style = (Style)app.TryFindResource("BtnPrimary");
            quit.Style = (Style)app.TryFindResource("BtnDangerText");
            copy.Click += (s, e) =>
            {
                try { Clipboard.SetText(diagnostic); copy.Content = "已复制 ✓"; }
                catch { }
            };
            cont.Click += (s, e) => { ContinueRunning = true; DialogResult = true; };
            quit.Click += (s, e) => { ContinueRunning = false; DialogResult = true; };
            buttons.Children.Add(copy);
            buttons.Children.Add(cont);
            buttons.Children.Add(quit);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Content = root;
        }

        private static string GetStr(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }
    }
}
