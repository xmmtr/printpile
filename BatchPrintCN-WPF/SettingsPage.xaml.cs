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
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Controls;

namespace BatchPrintCN
{
    /// <summary>
    /// U2-7（V4.2.5）：设置页逻辑——语言/主题用 ComboBox（SelectedIndex 0..n 对应枚举），开关走 Checked/Unchecked。
    /// V4.9 版本内修整第二批·修D：本页只剩 打印机 / 语言 / 外观 / 关闭行为 / 打印统计开关 五行，
    /// 转换引擎、验证落盘（三个目录 + 保留产物开关）、统计 CSV 目录、本机数据目录展示四块整体移除；
    /// 底层行为不变（引擎＝默认自动、各目录＝既有默认值、打印与转换流程零改动），
    /// settings.json 里那批旧键读取即忽略（见 SettingsService）。
    /// </summary>
    public partial class SettingsPage : UserControl
    {
        private bool _loading = true;
        private bool _printerSyncing;   // F15：打印机下拉同步期间屏蔽事件互递归

        public SettingsPage()
        {
            InitializeComponent();
            InitPrinterBox();   // F15：打印机分组（同一数据源 + settings 键双向同步）
            LangBox.SelectedIndex = SettingsService.Language == AppLanguage.EnUS ? 1 : 0;
            // 52 号令·步骤 4：外观下拉 → 主题色卡（ThemeCardHost 代码生成；ThemeBox 已随外观卡退役）
            BuildThemeCards();
            CloseBox.SelectedIndex = CloseIndexOf(SettingsService.CloseBehavior);   // V4.9-1：关闭行为三选
            ReportEnableBox.IsChecked = SettingsService.PrintReport;
            _loading = false;
        }

        // ===== 52 号令·步骤 4：主题色卡（内置 8 主题 + 用户导入；与原型主题系统原型-20261005.html 同形态）=====

        /// <summary>按当前主题列表重建色卡网格（启动、导入成功后各一次；选择态用边框高亮）。</summary>
        internal void BuildThemeCards()
        {
            if (ThemeCardHost == null) return;
            ThemeCardHost.Children.Clear();
            string current = ThemeService.Current != null ? ThemeService.Current.Name : SettingsService.ThemeName;
            foreach (var d in ThemeService.All())
            {
                ThemeCardHost.Children.Add(BuildThemeCard(d, d.Name == current));
            }
        }

        /// <summary>单枚色卡（三段缩略色块＋名称＋来源标签；选中态蓝框）。色块颜色直接取该主题令牌——真值预览。</summary>
        private Button BuildThemeCard(ThemeService.ThemeDef d, bool selected)
        {
            var btn = new Button
            {
                // 54 号令·B：色卡单排（需求方 2026-10-07 拍板"8 枚一排，别吊一枚在第二行"）——
                // 默认窗宽 1120 下主题卡可视宽约 832，8×(96+6)=816 放得下；改前 8×(104+8)=896 会折出第二排。
                // 窄窗口（MinWidth 960）仍按 WrapPanel 折行，不裁切。
                Width = 96,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(6),
                Style = (Style)Application.Current.TryFindResource("BtnOutline"),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            // 56 号令（需求方 2026-10-07 拍板"色卡下方灰色小字显示主题名"）：BtnBase 钉死 Height=32 会把
            // 内容裁成只剩色块——这里改回 Auto 让色块＋名称完整显示；名称用灰色小字（TextSecondaryBrush）。
            btn.Height = Double.NaN;
            var panel = new StackPanel();

            var swatch = new Grid { Height = 40 };
            var c1 = new System.Windows.Shapes.Rectangle { Fill = BrushOf(d, "bg.window"), RadiusX = 4, RadiusY = 4 };
            var c2 = new System.Windows.Shapes.Rectangle { Fill = BrushOf(d, "bg.card"), Margin = new Thickness(10, 4, 4, 4), RadiusX = 3, RadiusY = 3 };
            var c3 = new System.Windows.Shapes.Rectangle { Fill = BrushOf(d, "accent"), Width = 12, Margin = new Thickness(0, 4, 4, 4), HorizontalAlignment = HorizontalAlignment.Right, RadiusX = 3, RadiusY = 3 };
            swatch.Children.Add(c1);
            swatch.Children.Add(c2);
            swatch.Children.Add(c3);
            panel.Children.Add(swatch);
            // 56 号令：名称改为色卡下方灰色小字（原 TextPrimary/SemiBold 观感过重；来源标签按需求方口径撤下）
            panel.Children.Add(new TextBlock
            {
                Text = d.Name,
                FontSize = 11,
                Foreground = (Brush)Application.Current.TryFindResource("TextSecondaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            });
            btn.Content = panel;
            btn.BorderBrush = selected
                ? (Brush)Application.Current.TryFindResource("AccentBrush")
                : (Brush)Application.Current.TryFindResource("BorderBrush");
            btn.BorderThickness = new Thickness(selected ? 2 : 1);
            btn.Tag = d;
            btn.Click += OnThemeCardClick;
            return btn;
        }

        private static Brush BrushOf(ThemeService.ThemeDef d, string token)
        {
            var c = ThemeService.ToColor(d.Tokens.TryGetValue(token, out var v) ? v : null);
            if (!c.HasValue) return Brushes.Transparent;
            var b = new SolidColorBrush(c.Value);
            b.Freeze();
            return b;
        }

        /// <summary>点色卡＝应用主题（mode 联动 WPF-UI 基座＋令牌覆盖）并持久化；失败在本卡说明一句。</summary>
        private void OnThemeCardClick(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button b) || !(b.Tag is ThemeService.ThemeDef d)) return;
            var r = ThemeService.Apply(d);
            if (r == ThemeService.ApplyResult.Ok)
            {
                SettingsService.SetThemeName(d.Name);
                ShowThemeNote("");
                BuildThemeCards();   // 刷新选中态
            }
            else
            {
                ShowThemeNote(L("s.ThemeApplyFailed"));
            }
        }

        // 56 号令（需求方 2026-10-07 拍板）：「导入主题包」「打开主题文件夹」两按钮与处理器撤下，
        // 主题只从官网获取；ThemeService.Import／UserThemesDirectory 引擎能力保留（官网主题库上线时恢复入口）。

        /// <summary>「获取更多主题」＝占位提示（官网主题库不在本令范围，开工令已定）。</summary>
        private void OnMoreThemes(object sender, RoutedEventArgs e)
        {
            ToastAlert.ShowInfo(L("s.ThemeStoreComing"), Window.GetWindow(this));
        }

        private void ShowThemeNote(string text)
        {
            if (ThemeNoteText == null) return;
            ThemeNoteText.Text = text ?? "";
        }

        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        // ===== V4.9-1：关闭行为三选（每次询问 / 最小化到托盘 / 直接退出）——修A 把第二项语义升级为托盘 =====

        /// <summary>枚举 ⇄ 下拉序号（0 询问 / 1 最小化到托盘 / 2 退出）——纯函数，界面与自检共用一份映射。</summary>
        internal static int CloseIndexOf(CloseBehavior b)
        {
            return b == CloseBehavior.Minimize ? 1 : b == CloseBehavior.Exit ? 2 : 0;
        }

        internal static CloseBehavior CloseBehaviorAt(int index)
        {
            return index == 1 ? CloseBehavior.Minimize : index == 2 ? CloseBehavior.Exit : CloseBehavior.Ask;
        }

        private void OnCloseBehaviorChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || CloseBox == null) return;
            SettingsService.SetCloseBehavior(CloseBehaviorAt(CloseBox.SelectedIndex));   // 改动即存 settings.json
        }

        /// <summary>自检用：把当前设置回显到下拉（等同"别的入口改了设置后回到本页应看到新值"）。</summary>
        internal void RefreshCloseBoxForTest()
        {
            CloseBox.SelectedIndex = CloseIndexOf(SettingsService.CloseBehavior);
        }

        /// <summary>
        /// 修整19（V4.4 五批）：打印途中锁掉本页 F15 的两处打印参数入口（打印机下拉 + 打开打印机属性），
        /// 由主页 SetControlsBusy 统一调用——只堵主页不堵这里等于没堵（两处共用同一个 LastPrinter 键、双向同步）。
        /// 修D 之后本页其余四项（语言/外观/关闭行为/打印统计开关）本来就不在"打印参数"口径内，维持原状。
        /// </summary>
        internal void SetPrintBusy(bool enabled)
        {
            if (PrinterBox2 != null) PrinterBox2.IsEnabled = enabled;
            if (BtnOpenPrinterProps != null) BtnOpenPrinterProps.IsEnabled = enabled;
        }

        // ===== F15（V4.3）：打印机分组 =====

        /// <summary>填充打印机下拉（与主页 LoadPrinters 同一数据源：系统已安装打印机），并回显最近选择。</summary>
        private void InitPrinterBox()
        {
            _printerSyncing = true;
            try
            {
                PrinterBox2.Items.Clear();
                foreach (string p in System.Drawing.Printing.PrinterSettings.InstalledPrinters.Cast<string>())
                    PrinterBox2.Items.Add(p);
                string last = SettingsService.LastPrinter;
                if (!string.IsNullOrEmpty(last) && PrinterBox2.Items.Contains(last)) PrinterBox2.SelectedItem = last;
                else if (PrinterBox2.Items.Count > 0) PrinterBox2.SelectedIndex = 0;
                if (PrinterBox2.SelectedItem is string cur) SettingsService.SetLastPrinter(cur);   // 回写一次保持两处一致
            }
            finally { _printerSyncing = false; }
            SettingsService.LastPrinterChanged += OnLastPrinterChanged;   // 主页改 → 设置页跟随
        }

        private void OnPrinterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading || _printerSyncing || PrinterBox2 == null) return;
            if (PrinterBox2.SelectedItem is string p) SettingsService.SetLastPrinter(p);
        }

        /// <summary>主页改了打印机 → 设置页下拉跟随。</summary>
        private void OnLastPrinterChanged()
        {
            if (PrinterBox2 == null) return;
            string p = SettingsService.LastPrinter;
            string cur = PrinterBox2.SelectedItem as string;
            if (string.Equals(p, cur, StringComparison.Ordinal)) return;
            _printerSyncing = true;
            try
            {
                if (p != null && PrinterBox2.Items.Contains(p)) PrinterBox2.SelectedItem = p;
                else InitPrinterBox();
            }
            finally { _printerSyncing = false; }
        }

        /// <summary>打开 Windows 打印机属性（F15：自主页迁移；改的是打印机系统默认值）。</summary>
        private void OnOpenPrinterProps(object sender, RoutedEventArgs e)
        {
            string printer = (PrinterBox2.SelectedItem as string ?? "").Trim();
            if (printer.Length == 0)
            {
                MessageBox.Show(L("s.MsgSelectPrinter"), L("s.Hint"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // V4.9-3 加固：原来用字符串拼接构造 rundll32 参数，改 ProcessStartInfo.ArgumentList 逐参数添加
            //（与转换子进程同款防注入写法）。打印机名来自系统枚举本就受控，此改动是纵深防御与静态扫描降噪，行为零变化。
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("rundll32.exe") { UseShellExecute = true };
                psi.ArgumentList.Add("printui.dll,PrintUIEntry");
                psi.ArgumentList.Add("/e");
                psi.ArgumentList.Add("/n");
                psi.ArgumentList.Add(printer);
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex) { MessageBox.Show(L("s.MsgPrinterSetupError") + ex.Message, L("s.Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        // ===== 其余四行（语言 / 外观 / 关闭行为在上面 / 打印统计开关） =====

        private void OnLangChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var lang = LangBox.SelectedIndex == 1 ? AppLanguage.EnUS : AppLanguage.ZhCN;
            SettingsService.SetLanguage(lang);
            App.ApplyLanguage(lang);
        }

        // 52 号令·步骤 4：OnThemeChanged 已随外观下拉退役——主题选择走 OnThemeCardClick（ThemeService.Apply＋SetThemeName）。

        private void OnReportChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.SetPrintReport(ReportEnableBox.IsChecked == true);
        }

    }
}
