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
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

using System.Windows.Input;
using Microsoft.Win32;

namespace BatchPrintCN
{
    /// <summary>
    /// 修整16（V4.4 四批）：行状态的种类。只由结构化结果置位（PrintStage / 队列轮询 / 跳过分支），
    /// 界面按它上色——状态文案可以随时改，颜色映射不受影响（G2 口径：禁止解析中文文案）。
    /// Plain = 待打印/已跳过/队列阻塞原因等维持默认字色的状态；Printing 按需求方口径维持正常文字色
    /// （浅色深字、深色白字，浅色下纯白不可读）。
    /// RangeInvalid（V4.9 版本内修整·修6）＝打印成功、但页码范围一个有效页码都没有，引擎按既有口径回退打了全部；
    /// 黄色警示，只是把原来静默的回退显式说给用户，打印行为一行没改。
    /// ContentOverflow（45 号·改1）＝打印成功、但版面体检发现内容超出页面边缘，照原样打会被裁切；
    /// 与 RangeInvalid 同一支黄色、同一套结构化上色机制，同样只是告知——不自动缩放、不代改文档。
    /// </summary>
    public enum RowStatusKind { Plain, Printing, Done, Failed, RangeInvalid, ContentOverflow }

    public class RowVm : System.ComponentModel.INotifyPropertyChanged
    {
        public PrintJob Job { get; private set; }
        public string Name { get { return Job.Name; } }
        public string KindText { get; private set; }

        /// <summary>V4.9 修整·修2：页数列文案。未填范围时与修整前的现状文案逐字相同（"N 页"/"1 页"/"—"/"需密码"）；
        /// 填了范围就联动成 "X/N 页"（绿/黄）或"未生效"（红）——上色走 RangeState（Styles.xaml 的 CellTextPages），不认文案。</summary>
        public string PagesText
        {
            get { return _pagesText; }
            private set { if (_pagesText != value) { _pagesText = value; OnProp(nameof(PagesText)); } }
        }
        private string _pagesText;

        public string Copies
        {
            get { return _copies; }
            set { if (_copies != value) { _copies = value; OnProp(nameof(Copies)); } }
        }
        private string _copies = "1";

        /// <summary>修整7（V4.4）：页码范围列现在带灰字占位水印，值变化要能推给界面（用户边打字水印边收）。
        /// V4.9 修整·修2：同一次属性变化里顺带刷新范围反馈三处（页数列/气泡/提示），逐键即时。</summary>
        public string RangeText
        {
            get { return _rangeText; }
            set { if (_rangeText != value) { _rangeText = value; OnProp(nameof(RangeText)); RefreshRangeFeedback(); } }
        }
        private string _rangeText = "";

        // ===== V4.9 版本内修整·修2/3/4/5：范围反馈（页数列联动 + 聚焦气泡 + 悬停提示 + 无效标红）=====
        /// <summary>本行文件的总页数（0＝未知：Office 文件要到打印时转换才出 PDF；图片恒 1；加密未解 PDF 探不到）。
        /// 修2 的"转换完成后回填 X/N"就落在这里——打印完成回调拿到引擎回传的总页数后调 SetTotalPages。</summary>
        public int PageTotal { get; private set; }

        /// <summary>解析结论的结构化标记（上色只认它，与文案、与语言无关）。</summary>
        public RangeMark RangeState { get; private set; }

        /// <summary>修4：零有效页码 → 范围格文字标红、聚焦时再加红框；改回有效或清空即复原。</summary>
        public bool IsRangeInvalid { get { return RangeState == RangeMark.Invalid || RangeState == RangeMark.UnknownBad; } }

        /// <summary>修3/修5：聚焦气泡与悬停提示的完整结论（两处共用这一份，与页数列同一次解析算出）。</summary>
        public string RangeHint { get; private set; }

        /// <summary>范围文本或总页数变化后，一次算完三处呈现（同一事件内，界面靠 PropertyChanged 逐键更新）。</summary>
        private void RefreshRangeFeedback()
        {
            // 口径与打印一致：列里的文本 + 该行逐文件覆盖 → 生效范围（EffectiveOptions.RangeText 是唯一真相）
            RangeVerdict v = RangeVerdict.Build(EffectiveOptions.RangeText(Override, _rangeText), PageTotal);
            RangeState = v.Mark;
            PagesText = PagesTextFor(v);
            RangeHint = HintFor(v);
            OnProp(nameof(RangeState));
            OnProp(nameof(IsRangeInvalid));
            OnProp(nameof(RangeHint));
        }

        private string PagesTextFor(RangeVerdict v)
        {
            switch (v.Mark)
            {
                case RangeMark.Ok:
                case RangeMark.Partial:
                {
                    // 60 DIP 列宽实测（自检"列宽实测"那一行留证）：内容可用 47.2，"8/12页"=44.6 放得下、
                    // "8/12 页"=48.8 会被省略号截掉；三位数（"100/120页"≈67）连不带空格也放不下，
                    // 就按修2 的溢出口径退成短文 "X/N"（"100/120"≈45 仍在位），全称永远在气泡与悬停提示里。
                    string fmt = v.Total >= WideTotalDigits ? L("s.RangeCountShortFmt") : L("s.RangeCountFmt");
                    return fmt.Replace("{0}", v.Valid.ToString()).Replace("{1}", v.Total.ToString());
                }
                case RangeMark.Invalid:
                case RangeMark.UnknownBad:
                    return L("s.RangeBadShort");
                default:
                    return _pagePlain;      // 未填范围 / 总页数未知：文案维持现状，只有上色变
            }
        }

        /// <summary>修2 文案退档阈值：总页数到三位数就改用 "X/N" 短文（列宽实测见 V49范围反馈定位.txt）。</summary>
        internal const int WideTotalDigits = 100;

        private string HintFor(RangeVerdict v)
        {
            string sep = L("s.ListSep");
            string list = RangeVerdict.PageList(v.Pages, sep, BubbleListMax);
            string text;
            switch (v.Mark)
            {
                case RangeMark.Ok: text = L("s.RangeTipOkFmt").Replace("{0}", list).Replace("{1}", v.Valid.ToString()).Replace("{2}", v.Total.ToString()); break;
                case RangeMark.Partial: text = L("s.RangeTipPartialFmt").Replace("{0}", v.Valid.ToString()).Replace("{1}", list).Replace("{2}", v.Ignored.ToString()); break;
                case RangeMark.Invalid: text = L("s.RangeTipBadFmt").Replace("{0}", v.Total.ToString()); break;
                case RangeMark.UnknownOk: text = L("s.RangeTipUnknownOkFmt").Replace("{0}", list); break;
                case RangeMark.UnknownBad: text = L("s.RangeTipUnknownBad"); break;
                default: text = v.TotalKnown ? L("s.RangeTipEmptyFmt").Replace("{0}", v.Total.ToString()) : L("s.RangeTipEmptyUnknown"); break;
            }
            return v.FullWidth ? text + "\n" + L("s.RangeTipFullWidth") : text;
        }

        private const int BubbleListMax = 8;   // 气泡/提示里最多列 8 段连续区间，再多以省略号收尾（一行读完，不撑高度）

        /// <summary>修2/3/5/6：切语言时重算页数列与提示文案（页数数值与上色标记 RangeState 不受语言影响）。
        /// 修3 的全角注记"已自动兼容全角输入"也在这里随文案一起重建（判据 RangeVerdict.FullWidth，只在归一化真起作用时才加）。</summary>
        internal void RefreshTextsForLanguage() { RefreshRangeFeedback(); }

        /// <summary>F13：是否参与打印（默认勾选）。
        /// V4.5-4（决9b）：这里原来是自动属性 { get; set; } —— 表头「全取消」由代码批量改值，
        /// 没有 PropertyChanged 就没有人通知界面，TwoWay 绑定只推"界面→源"，于是行上的 √ 停在旧态
        /// （需求方真机反馈：按钮状态有反应、勾选框却不清掉）。改回通知属性，只通知变化的那一个属性
        /// （DataGrid 行虚拟化下不做全量刷）。</summary>
        public bool Checked
        {
            get { return _checked; }
            set { if (_checked != value) { _checked = value; OnProp(nameof(Checked)); } }
        }
        private bool _checked = true;

        // ===== F5（V4.4）：逐文件参数覆盖 =====
        /// <summary>该文件自己的参数覆盖；null = 全部跟随全局打印设置（不持久化，随行生灭）。</summary>
        public PerFileOverride Override { get; set; }

        /// <summary>修整1（V4.4）：该行的参数由「修改」按钮的弹窗编辑，行内不再有展开态（原 Expanded 随 RowDetailsTemplate 一起废弃）。</summary>

        /// <summary>行「参数」列按钮是否呈主色浅底（=该行有自定义参数）。</summary>
        public bool IsCustom { get { return Override != null && Override.HasAny; } }

        /// <summary>弹窗保存后调用：全空则丢弃覆盖对象，并刷新行上的自定义态。</summary>
        public void NotifyOverrideChanged()
        {
            if (Override != null && !Override.HasAny) Override = null;
            OnProp(nameof(IsCustom));
            RefreshRangeFeedback();      // V4.9 修整·修2：生效范围随逐文件覆盖变化，页数列联动跟着重算
        }

        /// <summary>「重置为跟随全局」：清空该行全部覆盖。</summary>
        public void ClearOverride()
        {
            Override = null;
            OnProp(nameof(IsCustom));
            RefreshRangeFeedback();      // V4.9 修整·修2：同上
        }

        private string _status = "";                // F13/F4：行状态（已跳过/排队中/打印中/已完成/失败…）
        public string Status
        {
            get { return _status; }
            private set { if (_status != value) { _status = value; OnProp(nameof(Status)); } }
        }

        /// <summary>F4：失败行才显示"重试"。V4.2.3 G2 起由打印结果显式置位，不再判断状态文字前缀。</summary>
        public bool CanRetry { get; private set; }

        /// <summary>修整16（V4.4 四批）：本行状态的种类（供界面按类别上色，与文案无关）。</summary>
        public RowStatusKind StatusKind { get; private set; }

        public void SetStatus(string text, bool retry, RowStatusKind kind = RowStatusKind.Plain)
        {
            if (CanRetry != retry) { CanRetry = retry; OnProp(nameof(CanRetry)); }
            if (StatusKind != kind) { StatusKind = kind; OnProp(nameof(StatusKind)); }
            Status = text;
        }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        private void OnProp(string n) { PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(n)); }

        public RowVm(PrintJob job)
        {
            Job = job;
            KindText = FileClassifier.KindText(job.Kind);
            int total;
            _pagePlain = ProbePages(job, out total);    // V4.9 修整·修2：文案照现状，顺手把总页数留下来
            PageTotal = total;
            RefreshRangeFeedback();
        }

        /// <summary>修2：未填范围时页数列显示的现状文案（"N 页"/"1 页"/"—"/"需密码"）——
        /// 有范围时它被 "X/N 页" 或"未生效"顶替，范围清空后又换回来，四种取值与修整前逐字相同。</summary>
        private string _pagePlain;

        /// <summary>修2：打印完成回调把引擎回传的总页数填进来（Office 文件正是"转换完成后回填 X/N"那一句的落点），
        /// 页数列与气泡/提示同一次重算。总页数未知（0）或没变化则不动。</summary>
        internal void SetTotalPages(int n)
        {
            if (n <= 0 || n == PageTotal) return;
            PageTotal = n;
            _pagePlain = n + " 页";      // 与 PDF 行探得页数时的现状文案同形
            RefreshRangeFeedback();
        }

        /// <summary>探页数：返回"未填范围时的页数列文案"，out 参给总页数（0＝未知）。逻辑与修整前的 PageCountText 一致。</summary>
        private static string ProbePages(PrintJob job, out int total)
        {
            total = 0;
            try
            {
                if (job.Kind == DocKind.Pdf)
                {
                    using (PdfiumFile pdf = PdfiumFile.Open(job.FilePath)) { total = pdf.PageCount; return total + " 页"; }
                }
                if (job.Kind == DocKind.Image) { total = 1; return "1 页"; }
            }
            catch (PdfPasswordRequiredException) { return "需密码"; }
            catch { }
            return "—";
        }

        /// <summary>取当前语言资源字符串（与本页各窗口同一段口径：键不存在回退键名，不让呈现层抛）。</summary>
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

    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private readonly ObservableCollection<RowVm> _rows = new ObservableCollection<RowVm>();
        private readonly PrintOptions _options = new PrintOptions();
        private BatchPrinter _activePrinter;
        private Thread _worker;
        private string _lastError;   // 最近一条打印失败原因（供 Toast 展示）
        private List<RowVm> _remaining;   // R2：停止后剩余待打印行（修整1/B-1：存行不存 job 快照，续打时按行当前参数重新合并）
        private List<PrintRecord> _records;  // R3：本轮打印统计
        private string _lastPrinter;         // 当前打印任务使用的打印机名
        private int _progressTotal;          // 修整6（V4.4）：进度条分母 = 本次"开始打印"时勾选的文件总数
        private int _progressBase;           // 修整6：已完成数（续打时从停止处的进度起算，不归零）

        public MainWindow()
        {
            InitializeComponent();
            Title = L("s.AppTitle");
            AppTitleBar.Title = L("s.AppTitle");
            VersionLabel.Text = AppVersion.Display;
            // 66 号令 T1：左下角橙色更新按钮——可见性**只由 UpdateState 派生**（自动检查与关于页手动检查两条入口都汇到那里），
            // 本窗不自建第二份"有没有更新"的状态，避免按钮与弹窗看到的世界不一致。
            UpdateState.Changed += OnUpdateStateChanged;
            OnUpdateStateChanged();
            SettingsService.LanguageChanged += OnLanguageChanged;
            SettingsService.LastPrinterChanged += OnLastPrinterChanged;   // F15：设置页改打印机 → 主页跟随
            TrayIcon.ExitRequested = RequestExitFromTray;    // V4.9 修整第二批·修A：托盘右键菜单「退出」接回同一条清理路径（单槽：谁活着谁占）
            EngineDetector.Detect();   // F1：启动时探测本机 Office/WPS（COM 可用性）
            SqliteStore.EnsureDb();    // F2：确保本地库存在
            Grid.ItemsSource = _rows;
            // 修整11/13：文件名列吃掉剩余宽度（排到 Loaded 优先级，等内层视口宽稳定后再算）
            // 修整18（五批）：行变化/尺寸变化统一走 RequestStarFill（同步一次 + 补排一拍），
            // 否则"刚导入一批文件"那一拍还没排版就按无滚动条的视口算宽 → 纵向滚动条随后吃掉 16 DIP 就顶出横向条。
            Grid.SizeChanged += (s, e) => RequestStarFill();
            SelectInitialNavItem();   // 构造期先选一次（自检渲染不走 Loaded，靠这里出选中态）
            Loaded += (s, e) => { LocateHeaderCheck(); SelectInitialNavItem(); };   // F13：表头全选框定位；U2-1：导航初始选中
            // 49 号·版2：自动检查更新的定时器只挂在主窗口（关于页之类非主窗口不起表）；四项自检/对话框模式由
            // App.GateMode 挡住，Hide→Show 让 Loaded 再响一次时也不会叠第二个定时器（AutoUpdate 内部幂等）。
            Loaded += (s, e) => AutoUpdate.Start();
            // 72 号令（V1.1.0·F7）：启动时恢复"启用中"的监视规则（复测点⑤）。两个探针交给引擎——
            // "主窗此刻能不能接"与"把一个稳定文件交回主窗入队"，本窗口不持有引擎状态；GateMode 在引擎里挡住，自检直接调 Apply。
            Loaded += (s, e) => StartWatchFromMain();
            LoadPrinters();
            UpdateStatus();
            AppendLog(L("s.LogStartHint"));
        }

        /// <summary>66 号令 T2：更新对话框（非模态、同一会话只开一个，重复点按钮＝把已有那只唤到前台）。</summary>
        private UpdateDialog _updateDialog;

        /// <summary>66 号令 T1：状态变了就刷新按钮（下载中置灰，避免重复起下载）。</summary>
        private void OnUpdateStateChanged()
        {
            if (BtnUpdate == null) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnUpdateStateChanged)); return; }
            BtnUpdate.Visibility = UpdateState.ButtonVisible ? Visibility.Visible : Visibility.Collapsed;
            BtnUpdate.IsEnabled = !UpdateState.Downloading;
        }

        private void OnUpdateButtonClick(object sender, RoutedEventArgs e)
        {
            var entry = UpdateState.Pending;
            if (entry == null) return;
            if (_updateDialog != null && _updateDialog.IsLoaded) { _updateDialog.Activate(); return; }
            _updateDialog = new UpdateDialog(entry, AutoUpdate.CurrentVersion(), UpdateInstaller.LooksInstalled()) { Owner = this };
            _updateDialog.Closed += (s, ev) => _updateDialog = null;
            _updateDialog.Show();
        }

        private void OnLanguageChanged()
        {
            Title = L("s.AppTitle");
            AppTitleBar.Title = L("s.AppTitle");
            UpdateStatus();
            UpdateProgressText();   // 修整20：进度文本按新语言重建（计数不变，措辞与单位换）
            foreach (RowVm r in _rows) r.RefreshTextsForLanguage();   // V4.9 修整·修2/3/5：页数列联动与气泡/提示文案跟着换语言（上色标记不变）
        }

        /// <summary>取当前语言资源字符串（键不存在则回退键名）。</summary>
        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        /// <summary>U2-1：NavigationView 只做导航条，四个页面仍由本窗口的 Host 切换（不引入 Frame 懒加载，
        /// 保证 HistoryPage/SettingsPage 在启动时即实例化——自检与 F4 队列轮询都依赖这一点）。
        /// V4.2.5 修整（需求方真机反馈）：库在没有 Frame/导航服务时，单击导航项不会改选中态、
        /// SelectionChanged 不触发（真机复现"历史/设置/关于点上去没反应"）→ 页面切换直接挂每项 Click
        /// （OnNavItemClick），本事件仅作库内部真的改变选中时的兜底（item 为空时不动，防误跳回打印页）。</summary>
        private void OnNavSelectionChanged(Wpf.Ui.Controls.NavigationView sender, RoutedEventArgs e)
        {
            if (PrintPanel == null || SettingsHost == null || AboutHost == null || HistoryHost == null || WatchHost == null) return;
            var item = sender.SelectedItem as Wpf.Ui.Controls.NavigationViewItem;
            if (item == null) return;
            SetNavActive(item);
            ShowPanel(item.Tag as string ?? "print");
        }

        /// <summary>V4.2.5 修整：导航项是真按钮（ButtonBase），Click 直接驱动页面切换与选中态（IsActive）。</summary>
        private void OnNavItemClick(object sender, RoutedEventArgs e)
        {
            if (PrintPanel == null || SettingsHost == null || AboutHost == null || HistoryHost == null || WatchHost == null) return;
            if (sender is Wpf.Ui.Controls.NavigationViewItem item)
            {
                SetNavActive(item);
                ShowPanel(item.Tag as string ?? "print");
            }
        }

        /// <summary>选中高亮随点击移动（库在无导航服务时不自己维护 IsActive）。</summary>
        private void SetNavActive(Wpf.Ui.Controls.NavigationViewItem active)
        {
            if (NavPrint == null) return;
            NavPrint.IsActive = active == NavPrint;
            NavHistory.IsActive = active == NavHistory;
            NavWatch.IsActive = active == NavWatch;
            NavSettings.IsActive = active == NavSettings;
            NavAbout.IsActive = active == NavAbout;
        }

        /// <summary>初始选中：打印任务页。V4.2.5 起直接置 IsActive（原来的 RaiseEvent 模拟点击
        /// 只碰巧给了首项高亮，其余项依旧点不动——根因见 OnNavItemClick 注释）。</summary>
        private void SelectInitialNavItem()
        {
            if (NavPrint == null) return;
            NavPrint.IsActive = true;
        }

        private void ShowPanel(string tag)
        {
            PrintPanel.Visibility = tag == "print" ? Visibility.Visible : Visibility.Collapsed;
            SettingsHost.Visibility = tag == "settings" ? Visibility.Visible : Visibility.Collapsed;
            AboutHost.Visibility = tag == "about" ? Visibility.Visible : Visibility.Collapsed;
            HistoryHost.Visibility = tag == "history" ? Visibility.Visible : Visibility.Collapsed;
            WatchHost.Visibility = tag == "watch" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LoadPrinters()
        {
            string current = PrinterBox.Text;
            if (string.IsNullOrWhiteSpace(current)) current = SettingsService.LastPrinter;   // F15：优先恢复上次选择
            PrinterBox.Items.Clear();
            foreach (string p in PrinterSettings.InstalledPrinters.Cast<string>()) PrinterBox.Items.Add(p);
            if (PrinterBox.Items.Count == 0) PrinterBox.Items.Add(L("s.MsgNoPrinter"));
            if (!string.IsNullOrEmpty(current) && PrinterBox.Items.Contains(current)) { PrinterBox.SelectedItem = current; return; }
            try
            {
                string def = new PrinterSettings().PrinterName;
                if (!string.IsNullOrEmpty(def) && PrinterBox.Items.Contains(def)) PrinterBox.SelectedItem = def;
                else PrinterBox.SelectedIndex = 0;
            }
            catch { PrinterBox.SelectedIndex = 0; }
        }

        private void UpdateStatus()
        {
            int total = _rows.Count;
            int chk = _rows.Count(r => r.Checked);
            StatusText.Text = L("s.CheckedCount").Replace("{0}", chk.ToString()).Replace("{1}", total.ToString());
            UpdateHeaderCheckState();
            RequestStarFill();   // 修整18（五批）：行数变化会让出/占回纵向滚动条，视口宽跟着变（同步一次 + 补排一拍）
        }

        private bool _starQueued;

        /// <summary>
        /// 修整18（V4.4 五批）：星号列补宽的"晚一拍"口径。
        /// 刚导入一批文件时，UpdateStatus 那一拍还没排版，内层 ScrollViewer 的 ViewportWidth 仍是"无纵向滚动条"的宽；
        /// 按它算完文件名列，下一拍纵向滚动条出现吃掉 ~16 DIP → 列合计超出 → 横向滚动条被顶出来（需求方真机反馈）。
        /// 故同步算一次（观感不延迟）+ 补排一次 Loaded 优先级（排版之后、视口宽已是真值）；同一次请求只排一拍，防排队堆积。
        /// </summary>
        internal void RequestStarFill()
        {
            FillStarColumn(Grid, ColName);
            if (_starQueued || Dispatcher == null) return;
            _starQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _starQueued = false;
                FillStarColumn(Grid, ColName);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // F13：勾选事件 → 刷新统计与全选三态
        private void OnRowCheckClicked(object sender, RoutedEventArgs e) { UpdateStatus(); }

        /// <summary>
        /// 修整9（V4.4 二批）：点表头勾选框＝"要么全勾、要么全取消"，判定只看行的当前实际状态，
        /// 绝不读 Click 之后的 IsChecked——三态框翻转顺序是 false→null→true，读翻转值会让"全不选"
        /// 状态下第一次点击落到 null（一个都勾不上），要点第二下才全选（真机反馈"点一下没反应"）。
        /// </summary>
        private void OnHeaderCheckClicked(object sender, RoutedEventArgs e)
        {
            if (_headerCheck == null) _headerCheck = sender as CheckBox;   // 自检/无 Loaded 路径下补定位
            int total = _rows.Count;
            int chk = _rows.Count(r => r.Checked);
            bool selectAll = chk < total;      // 已全选 → 本次全取消；全不选/半选 → 本次全选
            foreach (RowVm r in _rows) r.Checked = selectAll;
            UpdateStatus();
        }

        private void UpdateHeaderCheckState()
        {
            if (_headerCheck == null) return;
            int total = _rows.Count;
            int chk = _rows.Count(r => r.Checked);
            _headerCheck.IsChecked = total == 0 ? false : chk == total ? true : chk == 0 ? false : (bool?)null;
        }

        private CheckBox _headerCheck;

        // ===== F2/V4.2：最近套用的打印模板名（模板控件在「打印设置」弹窗内，主页仅追踪供 F3 显示）=====
        private string _templateName;

        private void LocateHeaderCheck()
        {
            try
            {
                _headerCheck = FindDescendantCheckBox(Grid, "HeaderCheck");
            }
            catch { _headerCheck = null; }
        }

        private static CheckBox FindDescendantCheckBox(DependencyObject root, string name)
        {
            if (root is CheckBox cb && cb.Name == name) return cb;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var found = FindDescendantCheckBox(System.Windows.Media.VisualTreeHelper.GetChild(root, i), name);
                if (found != null) return found;
            }
            return null;
        }

        private void AppendLog(string line)
        {
            FileLogger.Write(line);   // M2：日志落盘（界面不再展示日志框；V4.2.3 G2 起 UI 状态不再解析此文本）
        }

        // ===== F5（V4.4）：逐文件参数卡片与主页的接口 =====

        /// <summary>行内参数卡片读全局设置（判"跟随全局"到底是什么、小册子联动置灰）。</summary>
        internal PrintOptions ActiveOptions { get { return _options; } }

        /// <summary>行内参数卡片按当前所选打印机枚举纸盒（与打印设置弹窗同口径）。</summary>
        internal string ActivePrinterName { get { return (PrinterBox == null ? "" : (PrinterBox.Text ?? "")).Trim(); } }

        /// <summary>修整1（V4.4）：「参数」列的「修改」按钮 → 打开该文件的逐文件参数弹窗（单实例按需打开，B-5①）。</summary>
        private async void OnEditParams(object sender, RoutedEventArgs e)
        {
            var row = (sender as FrameworkElement)?.Tag as RowVm;
            if (row == null) return;
            await RowParamsPanel.ShowAsync(this, row);
        }

        /// <summary>自检用（同程序集 internal）：往列表加一行并返回其 RowVm。</summary>
        internal RowVm AddRowForTest(PrintJob job)
        {
            var r = new RowVm(job);
            _rows.Add(r);
            UpdateStatus();
            return r;
        }

        /// <summary>修整19/20（五批）断言用：行数读真实集合。</summary>
        internal int RowTotalForTest { get { return _rows.Count; } }

        // 自检用（修整1/B-1、修整6）：续打队列与进度基线的读/写口子
        internal int ProgressTotalForTest { get { return _progressTotal; } set { _progressTotal = value; } }
        internal int ProgressBaseForTest { get { return _progressBase; } }
        internal void SetRemainingForTest(List<RowVm> rows) { _remaining = rows; }
        internal List<RowVm> RowsForTest { get { return _rows.ToList(); } }

        /// <summary>自检用：无窗口环境下给内容根排版——可视树里的行容器（参数列按钮、单元格编辑器、列头）全靠这一步生成。</summary>
        internal void UpdateLayoutForTest() { UpdateLayoutForTest(1260, 900); }

        /// <summary>修整13（二批）自检用：按指定视口尺寸排版（模拟宽/中/最小三档窗口）。</summary>
        internal void UpdateLayoutForTest(double width, double height)
        {
            var size = new Size(width, height);
            Measure(size);
            var content = Content as FrameworkElement;
            if (content != null)
            {
                content.Measure(size);
                content.Arrange(new Rect(0, 0, size.Width, size.Height));
                content.UpdateLayout();
            }
            var grid = FindName("Grid") as DataGrid;
            if (grid != null) grid.UpdateLayout();
        }

        /// <summary>修整11/13（V4.4 二批）：让"文件名列"真正吃掉剩余宽度（两页共用这一个口子）。
        /// 为什么要用代码补：WPF-UI 的 DataGrid 模板把内层 ScrollViewer（PassiveScrollViewer）的水平条
        /// **写死成 Auto**（本地值，外层附加属性压不住），列呈现器于是按"无限宽"排版，星号列永远停在
        /// MinWidth、右边留一条空白 filler——一批就是这个现象（删掉「大小」列让出的宽度也没落到文件名列上）。
        /// 这里按内层可视宽把该列宽度直接算出来：宽窗口铺满；窄窗口回落到 MinWidth，
        /// 列总宽超出可视区后由横向滚动条兜底（修整13a 的"挤压变滚动"）。</summary>
        internal static void FillStarColumn(DataGrid grid, DataGridColumn star)
        {
            if (grid == null || star == null || grid.ActualWidth <= 0) return;
            double avail = InnerScroller(grid)?.ViewportWidth ?? (grid.ActualWidth - 2);
            if (avail < 80) avail = grid.ActualWidth - 2;
            double others = 0;
            foreach (DataGridColumn c in grid.Columns)
                if (!ReferenceEquals(c, star)) others += c.ActualWidth > 0 ? c.ActualWidth : Math.Max(c.MinWidth, 20);
            double w = Math.Max(star.MinWidth, Math.Round(avail - others, 1));
            if (!star.Width.IsAbsolute || Math.Abs(star.Width.Value - w) > 0.6) star.Width = new DataGridLength(w);
        }

        private static ScrollViewer InnerScroller(DataGrid grid)
        {
            return FindInnerScrollViewer(grid);
        }

        private static ScrollViewer FindInnerScrollViewer(System.Windows.DependencyObject node)
        {
            if (node == null) return null;
            int n = 0;
            try { n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); } catch { return null; }
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(node, i);
                var sv = child as ScrollViewer;
                if (sv == null) sv = FindInnerScrollViewer(child);
                if (sv != null && sv.ViewportWidth > 0) return sv;
            }
            return null;
        }

        /// <summary>二批自检用（修整9）：直接改 RowVm.Checked 后刷新表头三态与统计（真机由行勾选框的 Click 驱动）。</summary>
        internal void UpdateStatusForTest() { UpdateStatus(); }

        /// <summary>修整13（二批）自检用：切到指定页（历史页在窗口内的窄屏表现要在真实外壳里量，独立渲染会少掉导航栏占的宽度）。</summary>
        internal void ShowPanelForTest(string tag) { ShowPanel(tag); }

        private RowVm _currentRow;    // F4：当前正在打印的行（队列轮询用）

        /// <summary>
        /// V4.2.3 G2：结构化打印进度 → 行状态 / 重试按钮 / 失败原因。
        /// 取代原先"解析中文日志前缀"的做法（改文案即静默失效、英文界面下失效的隐患一并消除）。
        /// </summary>
        private void OnPrintProgress(PrintProgress p)
        {
            if (p == null) return;
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(() => OnPrintProgress(p))); return; }

            RowVm row = _rows.FirstOrDefault(r => r.Name == p.FileName);
            if (p.Stage == PrintStage.Started)
            {
                _currentRow = row;
                _curFileStart = System.DateTime.UtcNow;   // 修整20：单文件耗时样本的起点（UI 侧计时，不碰引擎）
                if (row != null) row.SetStatus(L("s.StatusPrintingRow"), false, RowStatusKind.Printing);
            }
            else if (p.Stage == PrintStage.Completed)
            {
                // V4.9 版本内修整·修2/修6：引擎回传的 p.Pages 是这个文件的**总页数**（PDF 直接探得、Office 是转换出的
                // 临时 PDF 的页数），所以"打印时才知道页数"的 Office 行在这里回填，页数列当场从两态升到 "X/N 页"；
                // 同一份解析结论顺带判"范围是否一个有效页码都没有"——引擎的回退行为一字未改，这里只把结果显式标出来。
                if (row != null)
                {
                    row.SetTotalPages(p.Pages);
                    RangeVerdict v = RangeVerdict.Build(row.Job.RangeText, row.PageTotal);
                    if (v.Ignored > 0) AppendLog(L("s.LogRangeIgnored").Replace("{0}", v.Ignored.ToString()));   // 修6：部分忽略只进日志，不占状态列
                    if (v.Mark == RangeMark.Invalid && v.TotalKnown)
                        row.SetStatus(L("s.StatusRangeInvalidFmt").Replace("{0}", v.Total.ToString()), false, RowStatusKind.RangeInvalid);
                    else if (OverflowStatusText(row.Job, out string over))
                        row.SetStatus(over, false, RowStatusKind.ContentOverflow);   // 45 号·改1：版面体检发现超界（只告知，不代改）
                    else
                        row.SetStatus(L("s.StatusDoneRow"), false, RowStatusKind.Done);
                }
                _currentRow = null;
                CloseFileSample();                          // 修整20
            }
            else
            {
                _currentRow = null;
                _progFailTotal++;                           // 修整20：累计失败只增不减（含重试前）
                string reason = p.Reason ?? "";
                _lastError = L("s.StatusFailedRow") + "：" + p.FileName + (reason.Length > 0 ? " → " + reason : "");
                if (row != null) row.SetStatus(L("s.StatusFailedRow") + (reason.Length > 0 ? "：" + reason : ""), true, RowStatusKind.Failed);
                CloseFileSample();                          // 修整20：失败也消耗时间，同样进样本
            }
            UpdateProgressText();   // 修整20：计数/预估随结构化进度事件即时刷新（Started 那一拍也要动）
        }

        /// <summary>
        /// 45 号·改1：把"版面体检"的结论换成状态列的一行文案（单行，由 CellText 的省略号截断，行高与列宽都不动）。
        /// 两级信号各说各话：B 口径（PDF 墨迹）有一页页码就报第几页、多页只报数量并指向日志（状态列宽 76，塞不下逐页明细）；
        /// A 口径（源文档表格比版心宽）没有"第几页"可报，直接报超出多少毫米。
        /// 优先级：范围无效（"到底打了什么"）排在超界（"版面会不会被裁"）前面，见 OnPrintProgress。
        /// </summary>
        internal static bool OverflowStatusText(PrintJob job, out string text)
        {
            text = null;
            var d = job == null ? null : job.Diag;
            if (d == null) return false;
            var scan = d.Overflow;
            if (scan != null && scan.HasOverflow)
            {
                // 真越出页面框与"贴到页边"分开说：前者一定被裁，后者取决于打印机机械边距，不混为一谈
                bool beyond = scan.BeyondPageCount > 0;
                if (scan.OverflowPageCount == 1)
                    text = L(beyond ? "s.StatusOverflowFmt" : "s.StatusNearEdgeFmt").Replace("{0}", scan.Pages[0].Page.ToString());
                else
                    text = L("s.StatusOverflowManyFmt").Replace("{0}", scan.OverflowPageCount.ToString());
            }
            else if (d.SourceLayout != null && d.SourceLayout.Found)
            {
                text = L("s.StatusSourceWideFmt").Replace("{0}", OverflowScan.MmText(d.SourceLayout.OverMm));
            }
            return text != null && text.Length > 0 && !text.StartsWith("s.Status");
        }

        /// <summary>V4.2.3 G2：队列阻塞原因出文案（走资源键，中英双语）。</summary>
        private string QueueReasonText(QueueReason r)
        {
            switch (r)
            {
                case QueueReason.PaperOut: return L("s.QueuePaperOut");
                case QueueReason.Offline: return L("s.QueueOffline");
                case QueueReason.Deleting: return L("s.QueueDeleting");
                default: return L("s.QueueBlocked");
            }
        }

    }
}
