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
using System.Windows.Controls.Primitives;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整第二批·修A：系统托盘图标。
    /// 图标本体用 WinForms 的 NotifyIcon（.NET 自带、零第三方依赖；发布目录本来就带着 System.Windows.Forms*.dll
    /// ——M2/M6 注释自陈"实测不省体积"，启用前后文件清单与字节完全一致，故 M6 断言改判为"不新增 WinForms 程序集"）；
    /// 右键菜单用 WPF 的 ContextMenu（跟随浅/深主题与双语资源，不用 WinForms 那套固定灰底菜单）。
    /// 左键单击＝恢复主窗口；右键＝「打开主窗口」/「退出」；悬停＝ToolTip 文字。
    /// 49 号·版2 改口径：原来"明确不做托盘气泡"，本令改成<b>只在自动检查发现新版本时</b>出一条气泡
    /// （<see cref="ShowBubble"/>）；仍不做气泡轮询、不做托盘图标闪烁动画。
    /// </summary>
    public static class TrayIcon
    {
        private static System.Windows.Forms.NotifyIcon _icon;
        private static Window _owner;

        /// <summary>托盘菜单里点「退出」→ 由主窗口接手既有退出清理流程。
        /// 单槽而不是事件：自检一轮要构造几十个窗口实例，+= 会越攒越多，触发时会给早已废弃的窗口发退出请求；
        /// 谁活着谁赋值（MainWindow 构造期覆盖、OnClosing 里清掉），语义正好是"当前那个主窗口"。</summary>
        public static Action ExitRequested;

        /// <summary>图标是否已建好并在位（自检与主窗口都看这个，不猜）。</summary>
        public static bool IsCreated { get { return _icon != null; } }
        public static bool IsVisible { get { return _icon != null && _icon.Visible; } }

        /// <summary>悬停文字（走资源键，中英都对；NotifyIcon.Text 有长度上限，键值须短）。</summary>
        public static string TipText { get { return L("s.TrayTip"); } }

        /// <summary>把窗口收进托盘：先建/显图标，再隐藏窗口——任务栏按钮随窗口一起消失（修A 口径：Hide 而非仅 Minimized）。</summary>
        public static void HideToTray(Window w)
        {
            if (w == null) return;
            _owner = w;
            Ensure();
            _icon.Text = TipText;                       // 语言可能已切换，每次进托盘刷新一次
            _icon.Visible = true;
            w.Hide();
        }

        /// <summary>
        /// 恢复主窗口：显示 + 退出最小化态 + 取焦点 + 置前（Topmost 打一下再放回是 WPF 里唯一稳的"带到最前"写法），
        /// 窗口回到任务栏可见后托盘图标即收回（不留两个入口）。
        /// </summary>
        public static void Restore()
        {
            Window w = _owner;
            if (w != null)
            {
                w.Show();
                if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
                w.ShowInTaskbar = true;
                w.Activate();
                w.Topmost = true; w.Topmost = false;
                try { w.Focus(); } catch { }
            }
            SetVisible(false);
        }

        /// <summary>退出/清理时释放图标（NotifyIcon 不 Dispose 会在托盘上留一个死图标，直到资源管理器重启）。</summary>
        public static void Dispose()
        {
            try { if (_icon != null) { _icon.Visible = false; _icon.Dispose(); } } catch { }
            _icon = null;
            _menu = null;
            _owner = null;
        }

        private static void SetVisible(bool v)
        {
            try { if (_icon != null) _icon.Visible = v; } catch { }
        }

        /// <summary>
        /// 49 号·版2：出一条托盘气泡（自动检查发现新版本时用，别的地方一律不调）。
        /// Windows 的气泡是挂在托盘图标上的——图标不在位就出不了气泡，所以这里临时把图标亮出来，
        /// 气泡时间到了再按"窗口是否可见"收回（主窗口开着＝收回，收在托盘里＝留着）。
        /// </summary>
        public static void ShowBubble(string title, string text, int seconds = 5)
        {
            try
            {
                bool wasVisible = IsVisible;
                Ensure();
                if (_icon == null) return;
                _icon.Text = TipText;
                if (!wasVisible) _icon.Visible = true;
                _icon.BalloonTipTitle = title ?? "";
                _icon.BalloonTipText = text ?? "";
                int ms = Math.Max(1, seconds) * 1000;
                _icon.ShowBalloonTip(ms);
                FileLogger.Write("托盘气泡：" + (title ?? "") + " —— " + (text ?? ""));
                if (!wasVisible) ScheduleHideAfter(seconds + 1);
            }
            catch (Exception ex) { FileLogger.Write("托盘气泡失败：" + ex.Message); }
        }

        /// <summary>临时亮出来的图标在气泡结束后收回（一次性定时器；Dispose 之后 tick 里全是空判断，不会炸）。</summary>
        private static void ScheduleHideAfter(int seconds)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(1, seconds)) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                try
                {
                    if (_icon == null) return;
                    Window w = _owner;
                    SetVisible(w != null && !w.IsVisible);      // 主窗口此刻仍收在托盘里就留着，否则收回
                }
                catch { }
            };
            t.Start();
        }

        /// <summary>自检取用：气泡正文与标题（直接读 NotifyIcon 上挂的那两条，断"内容对不对"）。</summary>
        internal static string BubbleTextForTest
        {
            get
            {
                Ensure();
                return _icon == null ? null : _icon.BalloonTipText;
            }
        }

        /// <summary>自检取用：气泡标题。</summary>
        internal static string BubbleTitleForTest
        {
            get
            {
                Ensure();
                return _icon == null ? null : _icon.BalloonTipTitle;
            }
        }

        // ===== 图标本体 =====

        private static void Ensure()
        {
            if (_icon != null) return;
            var ni = new System.Windows.Forms.NotifyIcon();
            try { ni.Icon = LoadIcon(); } catch { }
            ni.Text = TipText;
            ni.Visible = false;
            ni.MouseClick += (s, e) => HandleClick(e.Button == System.Windows.Forms.MouseButtons.Left);
            _icon = ni;
        }

        /// <summary>左键单击＝恢复；右键（或菜单键）＝弹托盘菜单。抽成方法体，自检直接调它（同一段代码）。</summary>
        private static void HandleClick(bool left)
        {
            if (left) Restore(); else ShowMenu();
        }

        /// <summary>自检用：直接触发焦点/图标点击的那段处理（不合成鼠标事件）。</summary>
        internal static void HandleClickForTest(bool left) { HandleClick(left); }

        private static System.Drawing.Icon LoadIcon()
        {
            // 复用应用图标 app.ico（随包复制到输出目录）；取不到再退回 exe 关联图标，两者都是同一枚图。
            try
            {
                string ico = Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (File.Exists(ico)) return new System.Drawing.Icon(ico);
            }
            catch { }
            return System.Drawing.Icon.ExtractAssociatedIcon(
                System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
        }

        // ===== 托盘菜单（WPF，随主题与语言） =====

        private static ContextMenu _menu;

        private static ContextMenu BuildMenu()
        {
            var m = new ContextMenu { Placement = PlacementMode.MousePoint };
            var open = new MenuItem { Header = L("s.TrayOpen") };
            open.Click += (s, e) => Restore();
            var exit = new MenuItem { Header = L("s.TrayExit") };
            exit.Click += (s, e) => { var h = ExitRequested; if (h != null) h(); };
            m.Items.Add(open);
            m.Items.Add(new Separator());
            m.Items.Add(exit);
            return m;
        }

        private static void ShowMenu()
        {
            if (_menu == null || !_menu.IsOpen) _menu = BuildMenu();     // 每次重建：语言/主题切换后文字与配色都是当前的
            _menu.Placement = PlacementMode.MousePoint;
            _menu.IsOpen = true;
        }

        /// <summary>自检/渲染取用：当前会弹出来的那份托盘菜单（含两项文字与分隔线）。</summary>
        internal static ContextMenu MenuForTest()
        {
            Ensure();
            _menu = BuildMenu();
            return _menu;
        }

        /// <summary>自检取用：图标句柄是否真的拿到了（app.ico 随包在位）。</summary>
        internal static bool IconHandleForTest
        {
            get
            {
                Ensure();
                return _icon != null && _icon.Icon != null && !_icon.Icon.Handle.Equals(IntPtr.Zero);
            }
        }

        /// <summary>自检取用：随包图标文件是否在位。</summary>
        internal static bool IconFileForTest { get { return File.Exists(Path.Combine(AppContext.BaseDirectory, "app.ico")); } }

        private static string L(string key)
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
