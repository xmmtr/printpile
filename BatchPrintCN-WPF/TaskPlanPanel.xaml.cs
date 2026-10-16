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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// U2-6（V4.2.5）：任务规划内容面板——原 TaskPlanWindow 的界面与逻辑，
    /// 由 ContentDialog 宿主（ShowPlanAsync）装入主窗口 RootContentDialog。
    /// F3 任务规划确认：顶部显示当前打印设置摘要（含所用模板），下方「N 个文件 → M 个任务」及每任务参数与文件清单。
    /// 注：不 using Wpf.Ui.Controls（与 System.Windows.Controls 有 TextBlock 等同名类型），所需类型全限定。
    /// </summary>
    public partial class TaskPlanPanel : UserControl
    {
        /// <summary>确认打印（宿主 Hide(Primary)）。</summary>
        public event Action ConfirmRequested;
        /// <summary>取消（宿主 Hide(None)）。</summary>
        public event Action CancelRequested;
        /// <summary>修改设置（宿主 Hide(Secondary)，V4.2 修整三闭环）。</summary>
        public event Action ModifyRequested;

        public TaskPlanPanel(IReadOnlyList<TaskPlanGroup> groups, string summary, string settingsSummary, string templateName)
        {
            InitializeComponent();
            SummaryText.Text = summary;
            SettingsLine.Text = L("s.PlanSettings") + (settingsSummary ?? "");
            if (!string.IsNullOrEmpty(templateName))
            {
                TemplateLine.Text = L("s.TemplateLabel") + templateName;
                TemplateLine.Visibility = Visibility.Visible;
            }
            foreach (TaskPlanGroup g in groups)
            {
                var card = new Border
                {
                    Style = (Style)FindResource("CardBorder"),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    CornerRadius = new CornerRadius(8)
                };
                var panel = new StackPanel();
                panel.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = g.Header,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    Foreground = (Brush)FindResource("AccentBrush"),
                    Margin = new Thickness(0, 0, 0, 4)
                });
                foreach (string f in g.Files)
                    panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "  · " + f, FontSize = 12, TextWrapping = TextWrapping.Wrap });
                card.Child = panel;
                GroupPanel.Children.Add(card);
            }
        }

        private void OnOk(object sender, RoutedEventArgs e) { if (ConfirmRequested != null) ConfirmRequested(); }
        private void OnCancel(object sender, RoutedEventArgs e) { if (CancelRequested != null) CancelRequested(); }
        private void OnModify(object sender, RoutedEventArgs e) { if (ModifyRequested != null) ModifyRequested(); }

        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        /// <summary>
        /// 以 ContentDialog 呈现任务规划，返回用户选择（确认 / 取消 / 去改设置）。
        /// Primary=确认打印；Secondary=修改设置；None（取消/Esc）=取消。
        /// </summary>
        public static async Task<TaskPlanOutcome> ShowPlanAsync(Window owner, IReadOnlyList<TaskPlanGroup> groups, string summary,
            string settingsSummary, string templateName)
        {
            var host = PrintSettingsPanel.ResolveHost();
            if (host == null) return TaskPlanOutcome.Cancelled;

            var panel = new TaskPlanPanel(groups, summary, settingsSummary, templateName);
            // 同 PrintSettingsPanel：补偿 ContentDialog 标题区（约 104 DIP），让页脚三按钮落在对话框底部
            panel.Height = 560 - 110;

            var dlg = new Wpf.Ui.Controls.ContentDialog(host)
            {
                Title = L("s.TaskPlanTitle"),
                Content = panel,
                DialogWidth = 760,
                DialogHeight = 560,
                DialogMargin = new Thickness(0),
                IsFooterVisible = false   // 页脚（修改设置/取消/确认打印）在面板内自带，关掉库模板的全宽 Close 条
            };
            panel.ConfirmRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.Primary);
            panel.CancelRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.None);
            panel.ModifyRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.Secondary);

            Wpf.Ui.Controls.ContentDialogResult result = await dlg.ShowAsync(CancellationToken.None);
            if (result == Wpf.Ui.Controls.ContentDialogResult.Primary) return TaskPlanOutcome.Confirmed;
            if (result == Wpf.Ui.Controls.ContentDialogResult.Secondary) return TaskPlanOutcome.ModifySettings;
            return TaskPlanOutcome.Cancelled;
        }
    }

    /// <summary>规划窗用户选择。</summary>
    public enum TaskPlanOutcome { Cancelled, Confirmed, ModifySettings }

    /// <summary>任务规划分组（F3）：参数相同的文件合并为同一任务。</summary>
    public sealed class TaskPlanGroup
    {
        public string Key;                    // 合并键（份数|页码范围|打印设置摘要）
        public string Header;                 // 展示行：打印机 · 份数 · 双面 · 纸张 · 缩放…
        public List<string> Files = new List<string>();
        public List<PrintJob> Jobs = new List<PrintJob>();
    }
}
