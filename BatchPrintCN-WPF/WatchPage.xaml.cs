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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    /// <summary>
    /// 72 号令（V1.1.0·F7）：「监视」页——规则列表＋新建/编辑/启停/删除。
    /// 73 号令（V1.2.0）起这是一页普通功能页：没有引导态、没有按钮置灰，任何时候都能用。
    ///
    /// 列表不整表重建：行对象留着，一秒只改「状态」列的文字——重建会把选中行清掉，「编辑/删除」就没了目标。
    /// 刷新时机＝构造、切语言、切回本页、CRUD 之后各一次（状态列另有页面可见时的一秒一拍）。
    /// </summary>
    public partial class WatchPage : UserControl
    {
        /// <summary>列表行（规则的一层皮：显示文字＋「启用」勾选框直接写回规则并落盘）。</summary>
        public sealed class RuleRow : INotifyPropertyChanged
        {
            public WatchRule Rule { get; private set; }

            public RuleRow(WatchRule rule) { Rule = rule; }

            public bool Enabled
            {
                get { return Rule.Enabled; }
                set { Rule.Enabled = value; }
            }

            public string Name { get { return WatchRules.DisplayNameOf(Rule); } }
            public string Folder { get { return Rule.Folder ?? ""; } }
            public string Params { get { return ParamsText(Rule); } }

            private string _state = "";
            public string State
            {
                get { return _state; }
                set { if (_state != value) { _state = value; OnChanged("State"); } }
            }

            private bool _problem;
            public bool Problem
            {
                get { return _problem; }
                set { if (_problem != value) { _problem = value; OnChanged("Problem"); } }
            }

            /// <summary>编辑规则后重取一遍显示文字（名称/文件夹/参数都会变）。</summary>
            public void Reload()
            {
                OnChanged("Enabled"); OnChanged("Name"); OnChanged("Folder"); OnChanged("Params");
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnChanged(string p) { var h = PropertyChanged; if (h != null) h(this, new PropertyChangedEventArgs(p)); }
        }

        private readonly ObservableCollection<RuleRow> _rows = new ObservableCollection<RuleRow>();
        private bool _loadedOnce;

        public WatchPage()
        {
            InitializeComponent();
            Grid.ItemsSource = _rows;
            Grid.SizeChanged += (s, e) => RequestStarFill();
            _rows.CollectionChanged += (s, e) => RequestStarFill();
            IsVisibleChanged += (s, e) => { if (IsVisible) Refresh(); };
            SettingsService.LanguageChanged += OnLanguageChanged;
            Unloaded += (s, e) =>
            {
                SettingsService.LanguageChanged -= OnLanguageChanged;
                StopTick();
            };
            Loaded += (s, e) =>
            {
                if (_loadedOnce) return;
                _loadedOnce = true;
                StartTick();      // 状态列要跟着引擎读数走（触发次数/最近文件），只在页面可见时一秒一拍
                Refresh();
            };
            Refresh();
        }

        private void OnLanguageChanged() { RebuildTexts(); }

        // ===== 刷新 =====

        /// <summary>刷新＝重读规则表（页面可见与 CRUD 之后都走这一句）。73 号令起本页没有任何解锁状态判断。</summary>
        internal void Refresh() { Rebuild(); }

        // ===== 列表 =====

        private void Rebuild()
        {
            List<WatchRule> rules = WatchRuleStore.Load();
            var ids = rules.Select(r => r.Id ?? "").ToList();
            var cur = _rows.Select(x => x.Rule.Id ?? "").ToList();
            bool same = ids.Count == cur.Count && !ids.Where((id, i) => id != cur[i]).Any();
            if (!same)
            {
                _rows.Clear();
                foreach (WatchRule r in rules) _rows.Add(new RuleRow(r));
            }
            else foreach (RuleRow row in _rows) row.Reload();
            UpdateStates();
            EmptyText.Visibility = _rows.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            CountText.Text = L("s.WatchCount").Replace("{0}", rules.Count.ToString()).Replace("{1}", rules.Count(r => r.Enabled).ToString());
            RequestStarFill();
        }

        /// <summary>一秒一拍只动「状态」列（目录有问题＝黄字提示；正常＝本轮已触发＋最近文件）。</summary>
        private void UpdateStates()
        {
            List<WatchRule> all = WatchRuleStore.Load();
            foreach (RuleRow row in _rows)
            {
                WatchRules.FolderProblem p = WatchRules.CheckFolder(row.Rule.Folder, all, row.Rule.Id);
                if (p != WatchRules.FolderProblem.None)
                {
                    row.Problem = true;
                    row.State = WatchRules.L(WatchRules.ProblemKey(p));
                    continue;
                }
                // 停用中的规则不给读数，免得看着像在跑
                if (!row.Rule.Enabled) { row.Problem = false; row.State = L("s.WatchOffState"); continue; }
                WatchEngine.RuleStats st = WatchEngine.StatsOf(row.Rule.Id);
                row.Problem = false;
                row.State = st.Fired <= 0 ? L("s.WatchNever")
                    : L("s.WatchFiredFmt").Replace("{0}", st.Fired.ToString()).Replace("{1}", string.IsNullOrWhiteSpace(st.LastFile) ? "—" : st.LastFile);
            }
        }

        // ===== CRUD =====

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            List<WatchRule> rules = WatchRuleStore.Load();
            var draft = WatchRule.New("");
            draft.AllDay = true;
            if (!WatchRuleWindow.Edit(Owner(), draft, rules, true)) return;
            rules.Add(draft);
            if (WatchRuleStore.Save(rules)) { Rebuild(); Reapply(); }
        }

        private void OnEdit(object sender, RoutedEventArgs e)
        {
            RuleRow row = Grid.SelectedItem as RuleRow;
            if (row == null) return;
            List<WatchRule> rules = WatchRuleStore.Load();
            WatchRule target = rules.FirstOrDefault(r => string.Equals(r.Id, row.Rule.Id, StringComparison.Ordinal)) ?? row.Rule;
            if (!WatchRuleWindow.Edit(Owner(), target, rules, false)) return;
            WatchRuleStore.Save(rules);
            Rebuild(); Reapply();
        }

        /// <summary>双击行＝直接进编辑（与「编辑」按钮同一条路）。</summary>
        private void OnRowDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) { OnEdit(sender, new RoutedEventArgs()); }

        /// <summary>
        /// 自检注入点：非 null 时代替"删除二次确认"那一下弹窗（返回 true＝确认删除）。
        /// 产品代码一行都不赋值——真机仍然是模态框，自检不弹模态（复利 M-20261011-01 的注入式口径）。
        /// </summary>
        internal static Func<string, bool> ConfirmOverride = null;

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            RuleRow row = Grid.SelectedItem as RuleRow;
            if (row == null) return;
            string name = WatchRules.DisplayNameOf(row.Rule);
            // 72 号红线：删除＝只删这条监视设置；二次确认里就把"不会移动或删除任何文件"说在前面
            bool yes;
            if (ConfirmOverride != null) yes = ConfirmOverride(name);
            else yes = MessageBox.Show(Owner(), L("s.WatchDeleteQ").Replace("{0}", name),
                    L("s.WatchTitle"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;
            if (!yes) return;
            List<WatchRule> rules = WatchRuleStore.Load();
            rules.RemoveAll(r => string.Equals(r.Id, row.Rule.Id, StringComparison.Ordinal));
            if (WatchRuleStore.Save(rules))
            {
                Rebuild(); Reapply();
                MainWindow mw = Owner() as MainWindow;
                if (mw != null) mw.AppendWatchLog(L("s.WatchDeleted").Replace("{0}", name));
            }
        }

        private void OnToggleEnabled(object sender, RoutedEventArgs e)
        {
            RuleRow row = Grid.SelectedItem as RuleRow ?? (sender as FrameworkElement)?.DataContext as RuleRow;
            if (row == null) return;
            row.Enabled = row.Rule.Enabled;      // 勾选框已写回 Rule.Enabled，这里只负责落盘与重建监视
            List<WatchRule> rules = WatchRuleStore.Load();
            WatchRule hit = rules.FirstOrDefault(r => string.Equals(r.Id, row.Rule.Id, StringComparison.Ordinal));
            if (hit != null) hit.Enabled = row.Rule.Enabled;
            WatchRuleStore.Save(rules);
            Reapply();
            UpdateStates();
        }

        private void Reapply()
        {
            var mw = Owner() as MainWindow;
            if (mw != null) mw.WatchReapply();
            else WatchEngine.Apply(WatchRuleStore.Load());
        }

        private Window Owner()
        {
            try
            {
                Window w = Window.GetWindow(this);
                if (w != null) return w;
                return Application.Current != null ? Application.Current.MainWindow : null;
            }
            catch { return null; }
        }

        // ===== 摘要文字 =====

        /// <summary>参数摘要：类型 · 时段(含子文件夹) · 打印机 · 模板 · 打印后（悬浮提示里给逐档全名）。</summary>
        internal static string ParamsText(WatchRule r)
        {
            if (r == null) return "";
            string types = string.IsNullOrWhiteSpace(r.Types) ? L("s.WatchAllTypes") : r.Types.Trim();
            string window = r.AllDay ? L("s.WatchAllDay")
                : (r.StartTime ?? "") + "–" + (r.EndTime ?? "");
            if (r.IncludeSubfolders) window += " · " + L("s.WatchSub");
            string printer = string.IsNullOrWhiteSpace(r.Printer) ? L("s.WatchPrinterFollow") : r.Printer.Trim();
            string tpl = string.IsNullOrWhiteSpace(r.TemplateName) ? L("s.WatchTplNone") : r.TemplateName.Trim();
            string action = r.MoveAfterPrint ? L("s.WatchMove") : L("s.WatchKeep");
            return string.Join(" · ", new[] { types, window, printer, tpl, action });
        }

        // ===== 星号列填满（与主页/历史页同一份 MainWindow.FillStarColumn） =====

        private bool _starQueued;

        internal void RequestStarFill()
        {
            MainWindow.FillStarColumn(Grid, ColParams);
            if (_starQueued || Dispatcher == null) return;
            _starQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _starQueued = false;
                MainWindow.FillStarColumn(Grid, ColParams);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ===== 状态列的一秒一拍 =====

        private System.Windows.Threading.DispatcherTimer _tick;

        private void StartTick()
        {
            if (_tick != null) return;
            _tick = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _tick.Tick += (s, e) => { if (IsVisible) UpdateStates(); };
            _tick.Start();
        }

        private void StopTick()
        {
            try { if (_tick != null) { _tick.Stop(); _tick = null; } } catch { }
        }

        /// <summary>编辑某条规则后重取显示文字（自检与对话框确定后共用）。</summary>
        internal void RebuildTexts()
        {
            foreach (RuleRow row in _rows) row.Reload();
            CountText.Text = L("s.WatchCount").Replace("{0}", _rows.Count.ToString()).Replace("{1}", _rows.Count(r => r.Rule.Enabled).ToString());
        }

        private static string L(string key) { return WatchRules.L(key); }

        // ===== 自检取用 =====

        internal ObservableCollection<RuleRow> RowsForTest { get { return _rows; } }
        internal bool AddEnabledForTest { get { return BtnAdd != null && BtnAdd.IsEnabled; } }
        internal string CountTextForTest { get { return CountText == null ? null : CountText.Text; } }
        internal string StateOfForTest(string ruleId)
        {
            RuleRow row = _rows.FirstOrDefault(r => string.Equals(r.Rule.Id, ruleId, StringComparison.Ordinal));
            return row == null ? null : row.State;
        }
        /// <summary>直接调删除那一条路（自检不真点按钮，走同一个实体：二次确认由调用方先答 Yes）。</summary>
        internal void PressAddForTest() { OnAdd(this, new RoutedEventArgs()); }
        internal void PressDeleteForTest(string ruleId)
        {
            RuleRow row = _rows.FirstOrDefault(r => string.Equals(r.Rule.Id, ruleId, StringComparison.Ordinal));
            if (row == null) return;
            Grid.SelectedItem = row;
            OnDelete(this, new RoutedEventArgs());
        }
        internal void PressToggleForTest(string ruleId, bool on)
        {
            RuleRow row = _rows.FirstOrDefault(r => string.Equals(r.Rule.Id, ruleId, StringComparison.Ordinal));
            if (row == null) return;
            row.Rule.Enabled = on;
            Grid.SelectedItem = row;
            OnToggleEnabled(this, new RoutedEventArgs());
        }
        internal RuleRow SelectedRowForTest { get { return Grid != null ? Grid.SelectedItem as RuleRow : null; } }
    }
}
