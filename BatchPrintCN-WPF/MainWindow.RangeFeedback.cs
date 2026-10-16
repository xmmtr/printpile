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
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整（打印范围反馈）·修3：页码范围格的「聚焦气泡」。
    /// 铁律＝所有行高锁死不变，所以解析结论只能走浮层：气泡（聚焦时）与 ToolTip（悬停时），
    /// 两者的文本都取 RowVm.RangeHint——与页数列是同一次解析（RangeVerdict.Build）出来的，不会各说各话。
    /// 气泡不接收焦点、不占布局空间；锚点在输入框正下方，贴近屏幕下缘（含最后一行）时朝上翻转防出屏。
    /// </summary>
    public partial class MainWindow
    {
        private void OnRangeFocus(object sender, RoutedEventArgs e) { OpenRangeBubbleOnFocus(sender as TextBox); }

        private void OnRangeBlur(object sender, RoutedEventArgs e) { CloseRangeBubbleOnBlur(sender as TextBox); }

        /// <summary>两个焦点事件真正执行的方法体（自检直接调这两个＝走同一段代码，不复制逻辑）。
        /// 无头自检不合成键盘焦点：KeyboardFocusChangedEventArgs 没有公开构造函数，RaiseEvent 造不出这对事件，
        /// 而浮层本身要 PlacementTarget 有 PresentationSource 才能落地（对照实验写在自检里）。</summary>
        internal void OpenRangeBubbleOnFocus(TextBox box) { OpenRangeBubble(box, BubbleWouldOverflow(box)); }

        internal void CloseRangeBubbleOnBlur(TextBox box) { CloseRangeBubble(box); }

        /// <summary>开气泡。above 由调用方给（真机走 BubbleWouldOverflow 的屏幕几何，自检直接传两种取值覆盖朝上/朝下）。</summary>
        internal static void OpenRangeBubble(TextBox box, bool above)
        {
            Popup popup = RangeBubbleOf(box);
            if (popup == null) return;
            popup.Placement = above ? PlacementMode.Top : PlacementMode.Bottom;
            popup.VerticalOffset = above ? -2 : 2;
            popup.IsOpen = true;
        }

        internal static void CloseRangeBubble(TextBox box)
        {
            Popup popup = RangeBubbleOf(box);
            if (popup != null) popup.IsOpen = false;
        }

        /// <summary>气泡在范围格的单元模板里，是与输入框同级的兄弟节点（每个单元格一份自己的实例，不存在共享实例问题）。</summary>
        internal static Popup RangeBubbleOf(TextBox box)
        {
            var panel = box == null ? null : box.Parent as Panel;
            if (panel == null) return null;
            foreach (UIElement c in panel.Children)
            {
                var p = c as Popup;
                if (p != null) return p;
            }
            return null;
        }

        /// <summary>修3：朝上翻转的判据——输入框下沿到屏幕工作区底边放不下"气泡高 + 2 DIP 间距"（再留 8 DIP 余量）时朝上。
        /// 纯算术（设备像素），自检注入三组数覆盖"空间足/贴屏底/最后一行"，不依赖真实屏幕；
        /// 口径报备：判据是几何量出来的，不是"行号＝最后一行"——窗口很高、末行下方仍有空位时照旧朝下，这才是防出屏的本意。</summary>
        internal static bool PopupAbove(double anchorBottomPx, double workBottomPx, double needPx)
        {
            return anchorBottomPx + needPx > workBottomPx - 8;
        }

        /// <summary>真机侧的屏幕几何（无头自检里窗口没上屏，PointToScreen 取不到就按朝下处理，不影响其余断言）。</summary>
        private static bool BubbleWouldOverflow(TextBox box)
        {
            if (box == null) return false;
            try
            {
                Popup popup = RangeBubbleOf(box);
                double need = 40;                                   // 拿不到实测高时的保守值：一行半（DIP）
                var child = popup == null ? null : popup.Child as FrameworkElement;
                if (child != null)
                {
                    child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    need = Math.Max(20, child.DesiredSize.Height) + 2;
                }
                double dpi = VisualTreeHelper.GetDpi(box).DpiScaleY;
                Point below = box.PointToScreen(new Point(0, box.ActualHeight));
                double workBottomPx = SystemParameters.WorkArea.Bottom * dpi;
                return PopupAbove(below.Y, workBottomPx, need * dpi);
            }
            catch { return false; }
        }

        /// <summary>修3/修5 自检钩子：取某一行可视树里的范围输入框（单元模板里的 x:Name＝RangeBox）。</summary>
        internal TextBox RangeBoxForTest(RowVm row)
        {
            var grid = FindName("Grid") as DataGrid;
            if (grid == null || row == null) return null;
            return FindRangeBoxIn(grid.ItemContainerGenerator.ContainerFromItem(row) as DataGridRow);
        }

        private static TextBox FindRangeBoxIn(DependencyObject node)
        {
            if (node == null) return null;
            var box = node as TextBox;
            if (box != null && box.Name == "RangeBox") return box;
            int n = 0;
            try { n = VisualTreeHelper.GetChildrenCount(node); } catch { return null; }
            for (int i = 0; i < n; i++)
            {
                var found = FindRangeBoxIn(VisualTreeHelper.GetChild(node, i));
                if (found != null) return found;
            }
            return null;
        }
    }
}
