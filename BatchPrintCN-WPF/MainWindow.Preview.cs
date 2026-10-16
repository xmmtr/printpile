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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号·改3：主列表双击开打印预览的入口与收尾。
    ///
    /// 铁律：主列表**行高、列宽、单元格内容零改动**——这里只挂一个 DataGrid.MouseDoubleClick 处理器，
    /// 不往格子里加任何控件；双击落在格子内的输入框/按钮/勾选框上时视为编辑动作，不开预览。
    /// </summary>
    public partial class MainWindow
    {
        private readonly List<PreviewWindow> _previews = new List<PreviewWindow>();

        /// <summary>当前开着的预览窗口（自检与渲染取用）。</summary>
        internal IReadOnlyList<PreviewWindow> Previews { get { return _previews; } }

        internal void PreviewOpened(PreviewWindow w) { if (w != null && !_previews.Contains(w)) _previews.Add(w); }
        internal void PreviewClosed(PreviewWindow w) { if (w != null) _previews.Remove(w); }

        /// <summary>「加入打印队列」的落点：只说一句日志（发送仍由主页「开始打印」掌握）。</summary>
        internal void PreviewQueued(string name)
        {
            AppendLog(L("s.PvQueuedFmt").Replace("{0}", name));
        }

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var src = e.OriginalSource as DependencyObject;
            if (InsideEditor(src)) return;
            RowVm vm = RowOf(src) ?? Grid.SelectedItem as RowVm;
            if (vm == null || vm.Job == null) return;
            try
            {
                var w = new PreviewWindow(this, vm) { Owner = this };
                PreviewOpened(w);
                w.Show();
            }
            catch (Exception ex)
            {
                AppendLog(L("s.Error") + ": " + ex.Message);
            }
        }

        /// <summary>双击目标是否落在可编辑控件里（输入框/按钮/勾选框/滚动条）——那些动作不该弹预览。</summary>
        private static bool InsideEditor(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBoxBase || d is ButtonBase || d is CheckBox || d is ComboBox || d is ScrollBar) return true;
                d = VisualTreeHelper.GetParent(d) ?? (d as FrameworkElement)?.Parent;
            }
            return false;
        }

        /// <summary>从命中元素向上找这一行的 RowVm（不依赖 SelectedItem，双击哪行就预览哪行）。</summary>
        private static RowVm RowOf(DependencyObject d)
        {
            while (d != null)
            {
                var fe = d as FrameworkElement;
                if (fe != null && fe.DataContext is RowVm) return (RowVm)fe.DataContext;
                d = VisualTreeHelper.GetParent(d) ?? (fe != null ? fe.Parent : null);
            }
            return null;
        }

        /// <summary>自检用：直接按行开预览（不合成鼠标事件，走同一段构造代码）。</summary>
        internal PreviewWindow OpenPreviewForTest(RowVm row)
        {
            var w = new PreviewWindow(this, row);
            try { if (IsVisible) w.Owner = this; } catch { }   // 自检里的 MainWindow 从未 Show，Owner 只能给显示过的窗口
            PreviewOpened(w);
            return w;
        }
    }
}
