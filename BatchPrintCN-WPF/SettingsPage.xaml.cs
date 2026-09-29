using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    /// <summary>U2-7（V4.2.5）：设置页逻辑——布局改 WPF-UI 卡片行后，
    /// 语言/主题/引擎由 RadioButton 组改为 ComboBox（SelectedIndex 0..n 对应枚举），开关仍走 Checked/Unchecked。</summary>
    public partial class SettingsPage : UserControl
    {
        private bool _loading = true;
        private bool _printerSyncing;   // F15：打印机下拉同步期间屏蔽事件互递归

        public SettingsPage()
        {
            InitializeComponent();
            InitPrinterBox();   // F15：打印机分组（同一数据源 + settings 键双向同步）
            LangBox.SelectedIndex = SettingsService.Language == AppLanguage.EnUS ? 1 : 0;
            ThemeBox.SelectedIndex = SettingsService.Theme == AppTheme.Dark ? 1 : 0;
            EngineBox.SelectedIndex = SettingsService.ConverterEngine == ConverterEngine.Com ? 1
                : SettingsService.ConverterEngine == ConverterEngine.MiniPdf ? 2 : 0;
            ReportEnableBox.IsChecked = SettingsService.PrintReport;
            KeepConvertedBox.IsChecked = SettingsService.KeepConvertedPdf;
            // 修整23（V4.4 五批）：日志位置只在「关于」页显示（设置页这行删掉，去重）
            DataDirText.Text = AppPaths.Describe();
            RefreshDirRows();      // 修整22（V4.4 五批）：三个目录行加载即显当前生效路径
            Loaded += (s, e) => { RefreshDirRows(); DataDirText.Text = AppPaths.Describe(); };
            IsVisibleChanged += (s, e) => { if (IsVisible) RefreshDirRows(); };   // 别的入口改了设置，回本页就能看到新值
            RefreshEngineStatus();
            _loading = false;
        }

        /// <summary>
        /// 修整22（V4.4 五批）：设置页三个目录行显示**当前生效**的完整路径。
        /// 原来只在各「修改」回调里赋值（SettingsPage.xaml.cs:143/157/165），点开设置看到的是空白，
        /// 需求方想知道"输出到底去哪个文件夹"无从判断。SettingsService 的三个 getter 本就把"未自定义"
        /// 解析成默认便携目录（printout\ / 日志目录），所以直接取 getter 即与实际写入完全一致，禁止空白。
        /// 刷新时机：构造期、Loaded、每次重新可见、以及每次改目录之后（OnChangeXxx）。
        /// </summary>
        private void RefreshDirRows()
        {
            PrintoutDirText.Text = SettingsService.PrintoutDirectory;
            ConvertDirText.Text = SettingsService.ConvertOutputDirectory;
            ReportDirText.Text = SettingsService.ReportDirectory;
        }

        /// <summary>修整22 断言用：走真实刷新口（模拟"别的入口改了设置后回本页"）。</summary>
        internal void RefreshDirRowsForTest() { RefreshDirRows(); }
        internal string PrintoutDirForTest { get { return PrintoutDirText.Text; } }
        internal string ConvertDirForTest { get { return ConvertDirText.Text; } }
        internal string ReportDirForTest { get { return ReportDirText.Text; } }

        private void RefreshEngineStatus()
        {
            // F1：展示探测结果（未探测过则先探测）
            if (!EngineDetector.Checked) EngineDetector.Detect();
            EngineStatusText.Text = L("s.EngineDetectStatus") + EngineDetector.Describe();
        }

        /// <summary>
        /// 修整19（V4.4 五批）：打印途中锁掉本页 F15 的两处打印参数入口（打印机下拉 + 打开打印机属性），
        /// 由主页 SetControlsBusy 统一调用——只堵主页不堵这里等于没堵（两处共用同一个 LastPrinter 键、双向同步）。
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
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry /e /n \"" + printer + "\"") { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(L("s.MsgPrinterSetupError") + ex.Message, L("s.Error"), MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void OnReDetect(object sender, RoutedEventArgs e)
        {
            EngineDetector.Detect();
            RefreshEngineStatus();
        }

        private void OnLangChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var lang = LangBox.SelectedIndex == 1 ? AppLanguage.EnUS : AppLanguage.ZhCN;
            SettingsService.SetLanguage(lang);
            App.ApplyLanguage(lang);
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var theme = ThemeBox.SelectedIndex == 1 ? AppTheme.Dark : AppTheme.Light;
            SettingsService.SetTheme(theme);
            App.ApplyTheme(theme);
        }

        private void OnEngineChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var engine = EngineBox.SelectedIndex == 1 ? ConverterEngine.Com
                : EngineBox.SelectedIndex == 2 ? ConverterEngine.MiniPdf : ConverterEngine.Auto;
            SettingsService.SetConverterEngine(engine);
        }

        private void OnReportChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.SetPrintReport(ReportEnableBox.IsChecked == true);
        }

        /// <summary>V4.2.3 M2：选目录统一走 WPF 自带的 Microsoft.Win32.OpenFolderDialog（.NET 8+），
        /// 替代 WinForms 的 FolderBrowserDialog（去掉 UseWindowsForms 依赖）。</summary>
        private string PickFolder(string prompt, string initial)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = prompt, Multiselect = false };
            try { if (!string.IsNullOrEmpty(initial) && System.IO.Directory.Exists(initial)) dlg.InitialDirectory = initial; } catch { }
            return dlg.ShowDialog(Window.GetWindow(this)) == true ? dlg.FolderName : null;
        }

        private void OnChangeReportDir(object sender, RoutedEventArgs e)
        {
            string path = PickFolder(L("s.SettingsReportDir"), SettingsService.ReportDirectory);
            if (path == null) return;
            SettingsService.SetReportDirectory(path);
            RefreshDirRows();
        }

        private void OnKeepConvertedChecked(object sender, RoutedEventArgs e)
        {
            if (_loading) return;
            SettingsService.SetKeepConvertedPdf(KeepConvertedBox.IsChecked == true);
        }

        private void OnChangePrintoutDir(object sender, RoutedEventArgs e)
        {
            string path = PickFolder(L("s.SettingsPrintoutDir"), SettingsService.PrintoutDirectory);
            if (path == null) return;
            SettingsService.SetPrintoutDirectory(path);
            RefreshDirRows();
        }

        private void OnChangeConvertDir(object sender, RoutedEventArgs e)
        {
            string path = PickFolder(L("s.SettingsConvertDir"), SettingsService.ConvertOutputDirectory);
            if (path == null) return;
            SettingsService.SetConvertOutputDirectory(path);
            RefreshDirRows();
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
    }
}
