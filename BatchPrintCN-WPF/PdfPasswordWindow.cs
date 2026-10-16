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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>加密 PDF 密码输入窗口（M5 UI 引导，主题自适应）。</summary>
    public class PdfPasswordWindow : Window
    {
        private readonly PasswordBox _box;

        public PdfPasswordWindow(string fileName, string hint)
        {
            var app = Application.Current;
            var textBrush = (Brush)(app.TryFindResource("TextPrimaryBrush") ?? Brushes.Black);
            var textSecBrush = (Brush)(app.TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);
            var bgBrush = (Brush)(app.TryFindResource("CardBgBrush") ?? Brushes.White);
            var winBgBrush = (Brush)(app.TryFindResource("WindowBgBrush") ?? Brushes.White);

            Title = GetStr("s.PdfPasswordTitle");
            Width = 460;
            Height = 210;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = winBgBrush;

            var root = new Border { Background = bgBrush, CornerRadius = new CornerRadius(10), Margin = new Thickness(16), Padding = new Thickness(16, 10, 16, 12) };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tip = new TextBlock
            {
                Text = (string.IsNullOrEmpty(hint) ? GetStr("s.PdfPasswordHint") : hint) + "\n" + fileName,
                TextWrapping = TextWrapping.Wrap,
                Foreground = textBrush,
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid.SetRow(tip, 0);
            grid.Children.Add(tip);

            _box = new PasswordBox { Margin = new Thickness(0, 0, 0, 14) };
            Grid.SetRow(_box, 1);
            grid.Children.Add(_box);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = GetStr("s.Ok"), Width = 88, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = GetStr("s.Cancel"), Width = 88, Height = 32, IsCancel = true };
            ok.Style = (Style)app.TryFindResource("BtnPrimary");
            cancel.Style = (Style)app.TryFindResource("BtnOutline");
            ok.Click += (s, e) => DialogResult = true;
            cancel.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetRow(buttons, 2);
            grid.Children.Add(buttons);

            root.Child = grid;
            Content = root;
            Loaded += (s, e) => _box.Focus();
        }

        public string Password { get { return _box.Password; } }

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
