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
    /// V4.9-1：点标题栏 ✕ 时的自定义提示窗（要放勾选框，系统 MessageBox 装不下；主题与双语口径沿用 InputDialog 的写法）。
    /// 两个选择：最小化（默认按钮）/ 退出程序（走既有退出清理）。
    /// V4.9 版本内修整第二批·修A：默认按钮文案由「保留在任务栏」改为「最小化」，行为同步升级为**最小化到系统托盘**
    /// （窗口 Hide、任务栏按钮消失、入口只剩托盘图标）；任务进行中的追加警示行不变。
    /// 44 号·缺2：修A 当时正文一字未改，仍是"留在任务栏"那套说法——与按钮和行为图文不符，正文已改口为「最小化到系统托盘继续运行」。
    /// 勾选「不再提示」后由 MainWindow 把本次选择写进 settings.json 的 CloseBehavior。
    /// 打印/转换进行中走同一扇窗、文案追加一行警示（不另开第二套弹窗）。
    /// 自检经 <see cref="Override"/> 注入选择，不弹真模态框（无窗口自检里弹模态会把整轮卡死）。
    /// </summary>
    public class ClosePromptWindow : Window
    {
        private readonly CheckBox _remember;
        private TextBlock _body, _warn;
        internal Button KeepButton;     // 自检取用：默认按钮＝最小化（修A 起＝最小化到托盘）
        internal Button ExitButton;     // 自检取用：退出程序
        internal string BodyLine { get { return _body == null ? null : _body.Text; } }        // 自检取用：正文行
        internal string WarnLine { get { return _warn == null || _warn.Visibility != Visibility.Visible ? "" : _warn.Text; } }
        public CloseBehavior Choice { get; private set; }

        public ClosePromptWindow(bool busy)
        {
            var app = Application.Current;
            var textBrush = (Brush)(app.TryFindResource("TextPrimaryBrush") ?? Brushes.Black);
            var textSecBrush = (Brush)(app.TryFindResource("TextSecondaryBrush") ?? Brushes.Gray);
            var winBg = (Brush)(app.TryFindResource("WindowBgBrush") ?? Brushes.White);

            Title = L("s.ClosePromptTitle");
            Width = 470;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = winBg;

            var root = new Grid { Margin = new Thickness(18, 16, 18, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(_body = new TextBlock
            {
                Text = L("s.ClosePromptBody"),
                Foreground = textBrush,
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            Grid.SetRow(_body, 0);

            // 进行中的任务：同一扇窗追加一行警示（深色下用次要字色，不新造配色）
            _warn = new TextBlock
            {
                Text = busy ? L("s.ClosePromptBusy") : "",
                Foreground = textSecBrush,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = busy ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetRow(_warn, 1);
            root.Children.Add(_warn);

            var btns = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            // 默认按钮＝最小化（回车即选中，修A 起进托盘）；「退出程序」不是默认按钮，防误触丢任务
            var keep = KeepButton = new Button { Content = L("s.CloseMinimizeBtn"), MinWidth = 118, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var exit = ExitButton = new Button { Content = L("s.CloseExitApp"), MinWidth = 96, Height = 30 };
            keep.Style = (Style)app.TryFindResource("BtnPrimary");
            exit.Style = (Style)app.TryFindResource("BtnOutline");
            keep.Click += (s, e) => { Choice = CloseBehavior.Minimize; DialogResult = true; };
            exit.Click += (s, e) => { Choice = CloseBehavior.Exit; DialogResult = true; };
            btns.Children.Add(keep);
            btns.Children.Add(exit);
            Grid.SetRow(btns, 2);
            root.Children.Add(btns);

            _remember = new CheckBox
            {
                Content = L("s.CloseRemember"),
                FontSize = 12.5,
                Foreground = textSecBrush,
                Margin = new Thickness(0, 14, 0, 0),
                IsChecked = false
            };
            Grid.SetRow(_remember, 3);
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(_remember);

            Content = root;
            Choice = CloseBehavior.Ask;
            Loaded += (s, e) => keep.Focus();
        }

        /// <summary>是否"不再提示，记住我的选择"。</summary>
        public bool Remember { get { return _remember != null && _remember.IsChecked == true; } }

        /// <summary>自检取用：勾选框本体（断言默认不勾）。</summary>
        internal CheckBox RememberBox { get { return _remember; } }

        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        /// <summary>正文（含任务进行中变体）——抽成纯函数，自检断言与窗口本体共用一份，不弹真窗。</summary>
        internal static string BodyText(bool busy)
        {
            return L("s.ClosePromptBody") + (busy ? "\n" + L("s.ClosePromptBusy") : "");
        }

        /// <summary>自检注入点：给一个"busy → 用户选择"的假实现（null＝当作取消）。</summary>
        internal static Func<bool, ClosePromptResult> Override;

        /// <summary>弹提示窗（或在自检里取注入结果）。</summary>
        public static ClosePromptResult Show(Window owner, bool busy)
        {
            var ov = Override;
            if (ov != null) return ov(busy) ?? ClosePromptResult.Cancel();
            var dlg = new ClosePromptWindow(busy);
            if (owner != null && owner.IsVisible) dlg.Owner = owner;   // Owner 必须是已显示的窗口，否则 ShowDialog 抛
            bool? r = dlg.ShowDialog();
            if (r != true || dlg.Choice == CloseBehavior.Ask) return ClosePromptResult.Cancel();
            return new ClosePromptResult { Choice = dlg.Choice, Remember = dlg.Remember };
        }
    }

    /// <summary>提示窗的一次结果：选了什么、要不要记住、还是干脆取消本次关闭。</summary>
    public sealed class ClosePromptResult
    {
        public CloseBehavior Choice;
        public bool Remember;
        public bool Cancelled;

        public static ClosePromptResult Cancel() { return new ClosePromptResult { Choice = CloseBehavior.Ask, Cancelled = true }; }
    }
}
