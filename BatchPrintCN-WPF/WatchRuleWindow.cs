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
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 72 号令（V1.1.0·F7）：规则编辑对话框——新建与编辑共用这一扇窗（纯代码建窗，与 ActivationDialog 同一模式：
    /// 无 XAML、主题自适应、自带自检取用口子，dialogtest 直接实例化渲染）。
    ///
    /// 七个字段按 72 号令的模型逐条落：规则名／文件夹（＋浏览＋含子文件夹）／文件类型／生效时段（全天或起止）／
    /// 目标打印机（默认跟随当前）／打印模板（默认不套）／打印后动作（默认保留原文件）。
    ///
    /// <b>两道校验</b>：① 目录保护（空／打不开／程序自身目录／与其他规则嵌套／路径过长）——五种拒绝理由都取资源键；
    /// ② 时段起止在"非全天"时必须是 HH:mm。校验不过就地红字说明，不关窗、不写回规则。
    /// 「打印后」两档是单选（保留／移动到已完成），<b>没有"删除"这一档</b>（72 号文件安全三律③）。
    /// </summary>
    public sealed class WatchRuleWindow : Window
    {
        private readonly WatchRule _rule;
        private readonly TextBox _name, _folder, _types, _start, _end;
        private readonly CheckBox _sub, _allDay, _keep, _move;
        private readonly ComboBox _printer, _template;
        private readonly TextBlock _error;
        private bool _saved;

        /// <summary>自检注入点：非 null 时代替真「选择文件夹」对话框（返回路径，null＝用户取消）。产品代码不赋值。</summary>
        internal static Func<string> BrowseOverride = null;

        internal WatchRuleWindow(WatchRule rule, IList<WatchRule> others, bool isNew)
        {
            _rule = rule ?? WatchRule.New("");
            var app = Application.Current;
            var textBrush = (Brush)(app != null ? app.TryFindResource("TextPrimaryBrush") : null) ?? Brushes.Black;
            var secBrush = (Brush)(app != null ? app.TryFindResource("TextSecondaryBrush") : null) ?? Brushes.Gray;
            var winBg = (Brush)(app != null ? app.TryFindResource("WindowBgBrush") : null) ?? Brushes.White;
            var badBrush = (Brush)(app != null ? app.TryFindResource("StatusFailTextBrush") : null) ?? Brushes.Firebrick;

            Title = L(isNew ? "s.WatchDlgNew" : "s.WatchDlgEdit");
            Width = 620;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Background = winBg;

            var root = new StackPanel { Margin = new Thickness(18) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;

            // ① 规则名
            AddLabel(grid, row, L("s.WatchDlgName"), secBrush);
            _name = new TextBox { Text = _rule.Name ?? "", Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            AddField(grid, row++, _name);

            // ② 文件夹＋浏览＋含子文件夹
            AddLabel(grid, row, L("s.WatchDlgFolder"), secBrush);
            var folderRow = new Grid();
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            folderRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _folder = new TextBox { Text = _rule.Folder ?? "", Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            Grid.SetColumn(_folder, 0); folderRow.Children.Add(_folder);
            var browse = new Button { Content = L("s.WatchDlgBrowse"), Height = 30, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(6, 0, 0, 0), FontSize = 12 };
            browse.Style = (Style)(app != null ? app.TryFindResource("BtnOutline") : null);
            browse.Click += (s, e) => PressBrowse();
            Grid.SetColumn(browse, 1); folderRow.Children.Add(browse);
            AddField(grid, row++, folderRow);

            _sub = new CheckBox { Content = L("s.WatchSub"), IsChecked = _rule.IncludeSubfolders, Margin = new Thickness(0, 8, 0, 0), FontSize = 12, Foreground = textBrush };
            AddField(grid, row++, _sub);

            // ③ 文件类型
            AddLabel(grid, row, L("s.WatchDlgTypes"), secBrush);
            _types = new TextBox { Text = _rule.Types ?? "", Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            AddField(grid, row++, _types);
            AddField(grid, row++, Hint(L("s.WatchDlgTypesHint")));

            // ④ 生效时段
            AddLabel(grid, row, L("s.WatchDlgWindow"), secBrush);
            _allDay = new CheckBox { Content = L("s.WatchDlgAllDay"), IsChecked = _rule.AllDay, FontSize = 12, VerticalContentAlignment = VerticalAlignment.Center, Foreground = textBrush };
            _allDay.Checked += (s, e) => SetTimeEnabled(false);
            _allDay.Unchecked += (s, e) => SetTimeEnabled(true);
            var windowRow = new StackPanel { Orientation = Orientation.Horizontal };
            windowRow.Children.Add(_allDay);
            _start = new TextBox { Text = string.IsNullOrWhiteSpace(_rule.StartTime) ? "08:00" : _rule.StartTime, Width = 74, Height = 30, Margin = new Thickness(12, 0, 0, 0), TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            _end = new TextBox { Text = string.IsNullOrWhiteSpace(_rule.EndTime) ? "18:00" : _rule.EndTime, Width = 74, Height = 30, Margin = new Thickness(6, 0, 0, 0), TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            windowRow.Children.Add(Labeled(L("s.WatchDlgFrom"), _start, secBrush));
            windowRow.Children.Add(Labeled(L("s.WatchDlgTo"), _end, secBrush));
            AddField(grid, row++, windowRow);
            AddField(grid, row++, Hint(L("s.WatchDlgTimeHint")));
            SetTimeEnabled(!_rule.AllDay);

            // ⑤ 目标打印机
            AddLabel(grid, row, L("s.WatchDlgPrinter"), secBrush);
            _printer = new ComboBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13, ItemTemplate = ComboText };
            _printer.Items.Add(L("s.WatchPrinterFollow"));
            foreach (string p in InstalledPrinters()) _printer.Items.Add(p);
            _printer.SelectedIndex = string.IsNullOrWhiteSpace(_rule.Printer) ? 0 : EnsureItem(_printer, _rule.Printer);
            AddField(grid, row++, _printer);

            // ⑥ 打印模板
            AddLabel(grid, row, L("s.WatchDlgTemplate"), secBrush);
            _template = new ComboBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center, FontSize = 13 };
            _template.Items.Add(L("s.WatchTplNone"));
            foreach (var t in TemplateNames()) _template.Items.Add(t);
            _template.SelectedIndex = string.IsNullOrWhiteSpace(_rule.TemplateName) ? 0 : EnsureItem(_template, _rule.TemplateName);
            AddField(grid, row++, _template);

            // ⑦ 打印后动作（单选，默认保留原文件；没有"删除"这一档）
            AddLabel(grid, row, L("s.WatchDlgAction"), secBrush);
            var actionRow = new StackPanel();
            _keep = new CheckBox { Content = L("s.WatchKeep"), IsChecked = !_rule.MoveAfterPrint, FontSize = 12, Foreground = textBrush };
            _move = new CheckBox { Content = L("s.WatchDlgMoveLabel"), IsChecked = _rule.MoveAfterPrint, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), Foreground = textBrush };
            _keep.Checked += (s, e) => { if (_keep.IsChecked == true) _move.IsChecked = false; else if (_move.IsChecked != true) _keep.IsChecked = true; };
            _move.Checked += (s, e) => { if (_move.IsChecked == true) _keep.IsChecked = false; else if (_keep.IsChecked != true) _keep.IsChecked = true; };
            actionRow.Children.Add(_keep);
            actionRow.Children.Add(_move);
            AddField(grid, row++, actionRow);
            AddField(grid, row++, Hint(L("s.WatchDlgActionHint")));

            root.Children.Add(grid);

            _error = new TextBlock
            {
                Foreground = badBrush,
                FontSize = 12,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            root.Children.Add(_error);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var ok = new Button { Content = L("s.Ok"), Width = 96, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            ok.Style = (Style)(app != null ? app.TryFindResource("BtnPrimary") : null);
            var cancel = new Button { Content = L("s.Cancel"), Width = 84, Height = 30, IsCancel = true };
            cancel.Style = (Style)(app != null ? app.TryFindResource("BtnOutline") : null);
            ok.Click += (s, e) => PressOk();
            cancel.Click += (s, e) => { try { DialogResult = false; } catch { try { Close(); } catch { } } };
            btns.Children.Add(ok);
            btns.Children.Add(cancel);
            root.Children.Add(btns);

            Content = root;
            Loaded += (s, e) => { _folder.Focus(); };

            // 目录校验要能看到"别的规则"，这一句把它接到本次编辑的规则集合上（编辑时排除自己）
            _others = others ?? new List<WatchRule>();
        }

        private readonly IList<WatchRule> _others;

        // ===== 建窗小工具 =====

        private static void AddLabel(Grid g, int row, string text, Brush sec)
        {
            var t = new TextBlock { Text = text, FontSize = 12, Foreground = sec, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(t, row); Grid.SetColumn(t, 0); g.Children.Add(t);
        }

        private static void AddField(Grid g, int row, UIElement e)
        {
            if (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(e, row); Grid.SetColumn(e, 1); g.Children.Add(e);
        }

        private static TextBlock Hint(string text)
        {
            return new TextBlock { Text = text, FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 4, 0, 8), TextWrapping = TextWrapping.Wrap };
        }

        private static StackPanel Labeled(string label, TextBox box, Brush sec)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = sec, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            sp.Children.Add(box);
            return sp;
        }

        private void SetTimeEnabled(bool on)
        {
            if (_start != null) _start.IsEnabled = on;
            if (_end != null) _end.IsEnabled = on;
        }

        private static List<string> InstalledPrinters()
        {
            var list = new List<string>();
            try
            {
                foreach (string p in PrinterSettings.InstalledPrinters.Cast<string>()) list.Add(p);
            }
            catch { }
            return list;
        }

        private static List<string> TemplateNames()
        {
            var list = new List<string>();
            try
            {
                foreach (var t in SqliteStore.LoadTemplates()) if (!string.IsNullOrWhiteSpace(t.Name)) list.Add(t.Name);
            }
            catch { }
            return list;
        }

        /// <summary>已有值不在候选里（打印机被删了/模板改名了）也要显示得出来，不然用户以为没设置。</summary>
        private static int EnsureItem(ComboBox box, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0;
            if (!box.Items.Contains(value)) box.Items.Add(value);
            return box.Items.IndexOf(value);
        }

        // 78 号令·B1 自检取用口：下拉框本体（断"最长下拉项文字零裁切"用，与真机同一控件）。
        internal ComboBox PrinterBoxForTest { get { return _printer; } }
        internal ComboBox TemplateBoxForTest { get { return _template; } }

        // ===== 校验与写回 =====

        /// <summary>
        /// 「确定」实体（自检直接调，与真机同一条路）：目录保护 → 时段格式 → 写回规则 → 关窗。
        /// 任何一步不过都只在这扇窗里说一句，不关窗、不写回。
        /// </summary>
        internal bool TryCommit()
        {
            _error.Visibility = Visibility.Collapsed;

            string folder = (_folder.Text ?? "").Trim();
            WatchRules.FolderProblem p = WatchRules.CheckFolder(folder, _others, _rule.Id);
            if (p != WatchRules.FolderProblem.None)
            {
                ShowError(L(WatchRules.ProblemKey(p)));
                return false;
            }

            bool allDay = _allDay.IsChecked == true;
            string start = (_start.Text ?? "").Trim(), end = (_end.Text ?? "").Trim();
            if (!allDay && (!WatchRules.IsValidTime(start) || !WatchRules.IsValidTime(end)))
            {
                ShowError(L("s.WatchDlgBadTime"));
                return false;
            }

            _rule.Name = (_name.Text ?? "").Trim();
            _rule.Folder = folder;
            _rule.IncludeSubfolders = _sub.IsChecked == true;
            _rule.Types = (_types.Text ?? "").Trim();
            _rule.AllDay = allDay;
            _rule.StartTime = allDay ? (_rule.StartTime ?? "08:00") : start;
            _rule.EndTime = allDay ? (_rule.EndTime ?? "18:00") : end;
            _rule.Printer = _printer.SelectedIndex <= 0 ? "" : (_printer.SelectedItem as string ?? "");
            _rule.TemplateName = _template.SelectedIndex <= 0 ? "" : (_template.SelectedItem as string ?? "");
            _rule.MoveAfterPrint = _move.IsChecked == true;
            return true;
        }

        /// <summary>点「确定」：校验通过才落 DialogResult（自检不 Show 时 DialogResult 置不得，所以先记 _saved）。</summary>
        internal void PressOk()
        {
            if (!TryCommit()) return;
            _saved = true;
            try { DialogResult = true; }
            catch { try { Close(); } catch { } }
        }

        private void PressBrowse()
        {
            string picked = null;
            var fake = BrowseOverride;
            if (fake != null) picked = fake();
            else
            {
                try
                {
                    var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L("s.WatchDlgBrowse") };
                    if (dlg.ShowDialog(this) == true) picked = dlg.FolderName;
                }
                catch (Exception ex) { ShowError(ex.Message); return; }
            }
            if (string.IsNullOrWhiteSpace(picked)) return;
            _folder.Text = picked;
            if (string.IsNullOrWhiteSpace(_name.Text)) _name.Text = PathName(picked);
            _error.Visibility = Visibility.Collapsed;
        }

        private static string PathName(string folder)
        {
            try { return System.IO.Path.GetFileName(folder.TrimEnd('\\', '/')); } catch { return ""; }
        }

        private void ShowError(string message)
        {
            if (_error == null) return;
            _error.Text = message;
            _error.Visibility = Visibility.Visible;
        }

        private static string L(string key) { return WatchRules.L(key); }

        // ===== 自检取用 =====

        internal bool SavedForTest { get { return _saved; } }
        internal string ErrorForTest { get { return _error == null || _error.Visibility != Visibility.Visible ? null : _error.Text; } }
        internal WatchRule RuleForTest { get { return _rule; } }
        internal void SetFolderForTest(string folder) { if (_folder != null) _folder.Text = folder; }
        internal void SetTypesForTest(string types) { if (_types != null) _types.Text = types; }
        internal void SetAllDayForTest(bool on) { if (_allDay != null) _allDay.IsChecked = on; SetTimeEnabled(!on); }
        internal void SetTimeForTest(string start, string end) { if (_start != null) _start.Text = start; if (_end != null) _end.Text = end; }
        internal void SetPrinterForTest(string printer) { if (_printer != null) { _printer.SelectedItem = printer ?? L("s.WatchPrinterFollow"); if (_printer.SelectedIndex < 0) _printer.SelectedIndex = 0; } }
        internal void SetTemplateForTest(string template) { if (_template != null) { _template.SelectedItem = template ?? L("s.WatchTplNone"); if (_template.SelectedIndex < 0) _template.SelectedIndex = 0; } }
        internal void SetMoveForTest(bool on) { if (_move != null) { _move.IsChecked = on; if (_keep != null) _keep.IsChecked = !on; } }
        internal string PrinterTextForTest { get { return _printer == null ? null : _printer.SelectedItem as string; } }
        internal string TemplateTextForTest { get { return _template == null ? null : _template.SelectedItem as string; } }

        /// <summary>弹出这一扇窗；返回 true＝用户点了「确定」且校验通过（调用方负责落盘）。</summary>
        public static bool Edit(Window owner, WatchRule rule, IList<WatchRule> others, bool isNew)
        {
            var dlg = new WatchRuleWindow(rule, others, isNew);
            if (owner != null) { try { dlg.Owner = owner; } catch { } }
            return dlg.ShowDialog() == true;
        }
    }
}
