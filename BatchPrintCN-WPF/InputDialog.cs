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
    /// <summary>轻量文本输入框（F2 模板命名等用），主题自适应。</summary>
    public class InputDialog : Window
    {
        private readonly TextBox _box;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            var app = Application.Current;
            var textBrush = (Brush)(app.TryFindResource("TextPrimaryBrush") ?? Brushes.Black);
            var textSecBrush = (Brush)(app.TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);
            var winBg = (Brush)(app.TryFindResource("WindowBgBrush") ?? Brushes.White);

            Title = title;
            Width = 420;
            Height = 180;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = winBg;

            var root = new Grid { Margin = new Thickness(16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock { Text = prompt, Foreground = textBrush, Margin = new Thickness(0, 0, 0, 10) });
            Grid.SetRow(root.Children[root.Children.Count - 1], 0);

            _box = new TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 14) };
            Grid.SetRow(_box, 1);
            root.Children.Add(_box);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var ok = new Button { Content = "确定", Width = 84, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancel = new Button { Content = "取消", Width = 84, Height = 30, IsCancel = true };
            ok.Style = (Style)app.TryFindResource("BtnPrimary");
            cancel.Style = (Style)app.TryFindResource("BtnOutline");
            ok.Click += (s, e) => DialogResult = true;
            cancel.Click += (s, e) => DialogResult = false;
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            Grid.SetRow(btns, 2);
            root.Children.Add(btns);

            Content = root;
            Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
        }

        public string Text { get { return _box.Text; } }

        /// <summary>弹出输入框，返回输入文本；取消返回 null。</summary>
        public static string Ask(Window owner, string title, string prompt, string defaultValue = "")
        {
            var dlg = new InputDialog(title, prompt, defaultValue) { Owner = owner };
            return dlg.ShowDialog() == true ? dlg.Text : null;
        }
    }
}
