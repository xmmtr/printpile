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
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace BatchPrintCN
{
    /// <summary>
    /// F2/V4.1 历史页：SQLite 打印历史查询（打印机/时间筛选 + 显示档位），后台线程查询不卡界面。
    /// P0 修复：Refresh 带 _refreshing 重入屏蔽（防 IsVisibleChanged/SelectionChanged 无限互递归→栈溢出）。
    /// V4.2：工具栏搜索框（内存过滤不重复查库）、显示档位下拉移入本页、导出 CSV、「所在文件夹」列。
    /// </summary>
    public partial class HistoryPage : UserControl
    {
        private readonly ObservableCollection<SqliteStore.HistoryRow> _rows = new ObservableCollection<SqliteStore.HistoryRow>();
        private bool _refreshing;        // P0：重入屏蔽
        private int _queryVersion;       // 后台查询版本（丢弃过期结果）
        private ICollectionView _view;   // V4.2：搜索内存过滤视图（不触发查库）
        private System.Windows.Controls.Button _printerFilterButton;   // F16：打印机列头筛选漏斗（自检锚点）

        /// <summary>F16：打印机列头筛选按钮（自检断言用；XAML 名域 + 列头双通道查找）。</summary>
        internal System.Windows.Controls.Button PrinterFilterButtonForTest
        {
            get { return _printerFilterButton ?? (FindName("PrinterFilterButton") as System.Windows.Controls.Button); }
        }

        public HistoryPage()
        {
            InitializeComponent();
            Grid.ItemsSource = _rows;
            // 修整11/13（V4.4 二批）：与主列表同口径——文件名列吃掉剩余宽度（WPF-UI 模板把内层水平条写死 Auto，
            // 星号列不会自己铺满）；行数增减会让出/占回纵向滚动条，所以行集合变化后也补一次。
            // 修整18/21（五批）：与主页同一"同步一次 + 补排一拍 Loaded"口径（主页 RequestStarFill 同源），
            // 纵向滚动条要等排版那一拍才吃掉 ~16 DIP，只算一次就会顶出横向滚动条。
            Grid.SizeChanged += (s, e) => RequestStarFill();
            _rows.CollectionChanged += (s, e) => RequestStarFill();
            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = o => SearchMatch(o as SqliteStore.HistoryRow, SearchBox != null ? SearchBox.Text : null);
            IsVisibleChanged += (s, e) => { if (IsVisible) Refresh(); };
            _refreshing = true;          // 构造期填充控件不触发 Refresh
            InitFilterControls();
            _refreshing = false;
            _printerFilterButton = FindName("PrinterFilterButton") as System.Windows.Controls.Button;   // F16：列头漏斗引用
            // V4.5-2（决2）：状态列文案改走资源键之后，切换语言必须让已显示的行重取一次值
            // （StatusText 是绑定值、不是 DynamicResource，语言字典换了它自己不会动）。
            SettingsService.LanguageChanged += OnLanguageChangedForStatus;
            Unloaded += (s, e) => SettingsService.LanguageChanged -= OnLanguageChangedForStatus;
            Refresh();
        }

        /// <summary>自检取用：导出按钮本体（73 号令起本按钮不再有闸，常亮）。</summary>
        internal System.Windows.Controls.Button ExportButtonForTest { get { return BtnExport; } }

        /// <summary>V4.5-2：语言切换 → 重画单元格（Items.Refresh 会重新读绑定值）+ 重出底部计数文案。</summary>
        private void OnLanguageChangedForStatus()
        {
            try
            {
                UpdateCountText();
                Grid.Items.Refresh();
            }
            catch { }
        }

        private bool _starQueued;

        /// <summary>商用线 V1.0.0 自检取用：底部计数行整句（含 200 条升级引导尾巴）。</summary>
        internal string CountTextForTest { get { return CountText == null ? null : CountText.Text; } }

        /// <summary>商用线 V1.0.0 自检取用：档位下拉本体（企业第 5 项「无限制」的断言走这里）。</summary>
        internal System.Windows.Controls.ComboBox LimitBoxForTest { get { return LimitBox; } }

        /// <summary>修整21（V4.4 五批）自检用：灌合成行后冻结后台查库，避免布局量数被真实结果覆盖。</summary>
        private bool _freezeRefresh;
        internal void FreezeRefreshForTest() { _freezeRefresh = true; }

        /// <summary>修整21（V4.4 五批）自检用：直接灌入合成历史行（不查库），让纵向滚动条真实出现后再量横向布局。</summary>
        internal int SeedRowsForTest(int n)
        {
            _freezeRefresh = true;
            _queryVersion++;    // 构造期那次后台查库若已完成/后到，按"过期结果丢弃"口径再也覆盖不到合成行
            _rows.Clear();
            for (int i = 0; i < n; i++)
            {
                _rows.Add(new SqliteStore.HistoryRow
                {
                    Name = "历史页长文件名取证样本第" + (i + 1) + "条-2026秋季学期报名材料.pdf",
                    Ext = ".pdf",
                    Copies = 2,
                    Pages = i % 30,
                    Success = i % 5 != 0,
                    Time = new DateTime(2026, 9, 23, 12, 34, 0),
                    Printer = "校园公共打印池-3F-彩色",
                    Engine = "-",
                    Folder = @"D:\打印输出\教务处\2026 秋季学期报名材料归档目录"
                });
            }
            UpdateCountText();    // 否则取证图上的计数还停在"加载中…"
            return _rows.Count;
        }

        /// <summary>修整18/21（V4.4 五批）：星号列补宽——同步一次（观感不延迟）+ 补排一拍 Loaded（等纵向滚动条真正出现后再按真值算）。</summary>
        internal void RequestStarFill()
        {
            MainWindow.FillStarColumn(Grid, ColName);
            if (_starQueued || Dispatcher == null) return;
            _starQueued = true;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _starQueued = false;
                MainWindow.FillStarColumn(Grid, ColName);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void InitFilterControls()
        {
            FilterPrinter.Items.Add(L("s.HistoryAllPrinter"));
            foreach (string p in SqliteStore.QueryPrinters()) FilterPrinter.Items.Add(p);
            FilterPrinter.SelectedIndex = 0;

            FilterRange.Items.Add(L("s.HistoryRangeAll"));
            FilterRange.Items.Add(L("s.HistoryRangeToday"));
            FilterRange.Items.Add(L("s.HistoryRangeWeek"));
            FilterRange.SelectedIndex = 0;

            // V4.2：显示档位（从设置页移入历史页工具栏；settings.json 键沿用）
            // 73 号令：五档全给用户——200/100/最近十天/不显示/无限制，「无限制」不再是解锁项，档位就是纯粹的显示档位。
            RebuildLimitItems();
        }

        /// <summary>档位下拉固定五档，选中值始终对齐 SettingsService.HistoryLimit（老配置存过 unlimited 也照常恢复）。</summary>
        internal void RebuildLimitItems()
        {
            if (LimitBox == null) return;
            _refreshing = true;
            try
            {
                LimitBox.Items.Clear();
                LimitBox.Items.Add(L("s.HistoryLimit200"));
                LimitBox.Items.Add(L("s.HistoryLimit100"));
                LimitBox.Items.Add(L("s.HistoryLimit10d"));
                LimitBox.Items.Add(L("s.HistoryLimitHidden"));
                LimitBox.Items.Add(L("s.HistoryLimitUnlimited"));
                LimitBox.SelectedIndex = SettingsService.HistoryLimit switch
                {
                    HistoryLimit.R100 => 1,
                    HistoryLimit.TenDays => 2,
                    HistoryLimit.Hidden => 3,
                    HistoryLimit.Unlimited => 4,
                    _ => 0
                };
                if (LimitBox.SelectedIndex < 0) LimitBox.SelectedIndex = 0;
            }
            finally { _refreshing = false; }
        }

        // 供自检冒烟调用（internal，同程序集）
        internal void Refresh()
        {
            if (_refreshing || _freezeRefresh) return;   // P0：重入直接忽略（含 SelectionChanged 链）；修整21 自检冻结期同样忽略
            _refreshing = true;
            try
            {
                // 档位：200（默认上限）/ 100 / 十天 / 不显示（V4.2 起档位下拉在本页工具栏）
                HistoryLimit limit = SettingsService.HistoryLimit;
                if (limit == HistoryLimit.Hidden)
                {
                    _queryVersion++;
                    _rows.Clear();
                    EmptyText.Visibility = Visibility.Collapsed;
                    CountText.Text = L("s.HistoryHidden");
                    return;
                }

                EmptyText.Visibility = Visibility.Collapsed;
                CountText.Text = L("s.HistoryLoading");
                string printer = FilterPrinter.SelectedItem as string;
                string range = FilterRange.SelectedItem as string;
                string printerFilter = printer == L("s.HistoryAllPrinter") || string.IsNullOrEmpty(printer) ? null : printer;
                string rangeKey = range == L("s.HistoryRangeToday") ? "today"
                    : range == L("s.HistoryRangeWeek") ? "week" : "all";
                int limitN;
                if (limit == HistoryLimit.TenDays) rangeKey = "tendays";
                limitN = LimitRows(limit);   // 档位换算（73 号令起无封顶：无限制档就真是无限制）

                string p2 = printerFilter, r2 = rangeKey;
                int l2 = limitN;
                int ver = ++_queryVersion;

                // V4.1：后台线程查询，完成后回 UI 线程填充（过期结果丢弃）；自检/无同步上下文时回退默认调度器
                TaskScheduler uiScheduler;
                try { uiScheduler = TaskScheduler.FromCurrentSynchronizationContext(); }
                catch { uiScheduler = TaskScheduler.Default; }
                Task.Run(() => SqliteStore.QueryHistory(p2, r2, l2))
                    .ContinueWith(t =>
                    {
                        try
                        {
                            if (ver != _queryVersion) return;   // 已触发新查询，丢弃旧结果
                            _rows.Clear();
                            foreach (SqliteStore.HistoryRow r in t.Result) _rows.Add(r);
                            UpdateCountText();
                        }
                        catch { }
                    }, uiScheduler);
            }
            finally { _refreshing = false; }
        }

        private void OnFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing) return;
            Refresh();
        }

        /// <summary>
        /// 档位 → 查库行数上限（0＝不按条数截断，由时间范围决定）。
        /// V4.7 拆分3 定的档位兜底是 200 条；73 号令起「无限制」档对所有用户敞开（回补 Unlimited→0）。
        /// </summary>
        internal static int LimitRows(HistoryLimit limit)
        {
            if (limit == HistoryLimit.R100) return 100;
            if (limit == HistoryLimit.TenDays) return 0;
            if (limit == HistoryLimit.Unlimited) return 0;
            return 200;
        }

        // V4.2：显示档位变更 → 持久化并重新查库（与其他下拉同一重入门）
        // V4.7 拆分3：档位表里已无「无限制」，index 0 即 200 条（上限）。
        // 73 号令：档位下拉固定五项，index 4＝无限制（原来这一项要解锁才有，现在一视同仁；OnLimitChanged 的 index 映射照旧）。
        private void OnLimitChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_refreshing || LimitBox == null || LimitBox.SelectedIndex < 0) return;
            HistoryLimit v = LimitBox.SelectedIndex switch
            {
                1 => HistoryLimit.R100,
                2 => HistoryLimit.TenDays,
                3 => HistoryLimit.Hidden,
                4 => HistoryLimit.Unlimited,
                _ => HistoryLimit.R200
            };
            SettingsService.SetHistoryLimit(v);
            Refresh();
        }

        // V4.2：搜索框文本变化 → 仅内存过滤（CollectionView.Filter），不触发 Refresh 查库
        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            if (SearchHint != null)
                SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (_view != null) _view.Refresh();
            UpdateCountText();
        }

        private void OnRefresh(object sender, RoutedEventArgs e) { Refresh(); }

        // ===== F16（V4.3）：打印机列头下拉筛选（与顶部工具栏打印机下拉同一状态，两处入口） =====

        /// <summary>漏斗点击：弹出打印机清单（全部/每台，数据源=QueryPrinters 与工具栏一致），选中即过滤。</summary>
        private void OnPrinterFilterClick(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            string allLabel = L("s.HistoryAllPrinter");
            var all = new MenuItem { Header = allLabel, IsChecked = FilterPrinter.SelectedIndex <= 0 };
            all.Click += (s2, e2) => ApplyPrinterColumnFilter(null);
            menu.Items.Add(all);
            foreach (string p in SqliteStore.QueryPrinters())
            {
                string name = p;
                var mi = new MenuItem { Header = name, IsChecked = (FilterPrinter.SelectedItem as string) == name };
                mi.Click += (s2, e2) => ApplyPrinterColumnFilter(name);
                menu.Items.Add(mi);
            }
            menu.PlacementTarget = sender as UIElement;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        /// <summary>
        /// F16 断言入口（internal 供自检）：设置打印机列头筛选 = 顶部工具栏下拉选中对应项并刷新。
        /// printer=null → 全部。返回是否成功定位该选项。
        /// </summary>
        internal bool ApplyPrinterColumnFilter(string printer)
        {
            int idx = printer == null ? 0 : FilterPrinter.Items.IndexOf(printer);
            if (idx < 0) return false;
            if (FilterPrinter.SelectedIndex != idx)
            {
                FilterPrinter.SelectedIndex = idx;   // SelectionChanged → OnFilterChanged → Refresh
                Refresh();                            // 显式刷一次，行为确定（重入门挡重复）
            }
            return true;
        }

        /// <summary>
        /// V4.2：搜索匹配（断言②核心逻辑）。空关键字=全部命中；命中范围=文件名 + 所在文件夹（含 "—" 兜底列）。
        /// </summary>
        internal static bool SearchMatch(SqliteStore.HistoryRow row, string keyword)
        {
            if (row == null) return true;
            string k = (keyword ?? "").Trim();
            if (k.Length == 0) return true;
            return (row.Name ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0
                || (row.Folder ?? "").IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>V4.2：底部计数（含搜索过滤：显示"匹配 X / 共 Y 条"）。
        /// 73 号令：选了 200 档且真有 200 条以上时追加一句"更早记录还在、切『无限制』档可看全部"（纯文本、无弹窗，守"不做弹窗催办"）。</summary>
        private void UpdateCountText()
        {
            int total = _rows.Count;
            if (total == 0)
            {
                // 55 号：空态长文案移到表格区域居中（EmptyText），标题行只留短文本，防工具栏折排
                EmptyText.Text = L("s.HistoryEmpty");
                EmptyText.Visibility = Visibility.Visible;
                CountText.Text = "";
                return;
            }
            EmptyText.Visibility = Visibility.Collapsed;
            string kw = SearchBox != null ? SearchBox.Text : null;
            if (string.IsNullOrEmpty((kw ?? "").Trim()))
            {
                string text = L("s.HistoryCount").Replace("{0}", total.ToString());
                // 200 档看满时提醒一句"更早记录仍在、切『无限制』档可看全部"（73 号令起这是显示档位，不是收费墙）
                if (total >= 200 && SettingsService.HistoryLimit == HistoryLimit.R200)
                    text += L("s.HistoryCapHint");
                CountText.Text = text;
                return;
            }
            int shown = 0;
            foreach (SqliteStore.HistoryRow r in _rows) if (SearchMatch(r, kw)) shown++;
            CountText.Text = L("s.HistoryCountFiltered").Replace("{0}", shown.ToString()).Replace("{1}", total.ToString());
        }

        // V4.2：导出当前筛选+搜索结果为 CSV（UTF-8 BOM，默认 printout\，打印历史-yyyyMMdd-HHmm.csv）
        // 73 号令：导出 CSV 不再有任何闸（原来这里是企业功能判定，随解锁方案一起撤掉）。
        private void OnExport(object sender, RoutedEventArgs e)
        {
            try
            {
                string kw = SearchBox != null ? SearchBox.Text : null;
                var rows = new List<SqliteStore.HistoryRow>();
                foreach (SqliteStore.HistoryRow r in _rows) if (SearchMatch(r, kw)) rows.Add(r);
                if (rows.Count == 0)
                {
                    MessageBox.Show(L("s.HistoryExportEmpty"), L("s.HistoryTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var dlg = new SaveFileDialog
                {
                    Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*",
                    FileName = "打印历史-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + ".csv"
                };
                try
                {
                    string dir = AppPaths.PrintoutDir;
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dlg.InitialDirectory = dir;
                }
                catch { }

                if (dlg.ShowDialog() != true) return;
                WriteCsv(dlg.FileName, rows);
                FileLogger.Write("历史导出成功：" + dlg.FileName + "（" + rows.Count + " 条）");
                MessageBox.Show(L("s.HistoryExportDone").Replace("{0}", rows.Count.ToString()).Replace("{1}", dlg.FileName),
                    L("s.HistoryTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                FileLogger.Write("历史导出失败：" + ex.Message);
                MessageBox.Show(L("s.HistoryExportFail") + ex.Message, L("s.HistoryTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>V4.2：CSV 写出（UTF-8 带 BOM；列：文件名/所在文件夹/后缀/份数/页数/状态/时间/打印机/引擎）。</summary>
        private static void WriteCsv(string path, List<SqliteStore.HistoryRow> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("文件名,所在文件夹,后缀,份数,页数,状态,时间,打印机,引擎");
            foreach (SqliteStore.HistoryRow r in rows)
            {
                sb.AppendLine(string.Join(",",
                    CsvCell(r.Name),
                    CsvCell(r.FolderText),
                    CsvCell(r.Ext),
                    r.Copies.ToString(),
                    r.Pages.ToString(),
                    CsvCell(r.StatusText),
                    r.Time.ToString("yyyy-MM-dd HH:mm:ss"),
                    CsvCell(r.Printer),
                    CsvCell(r.Engine)));
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));   // UTF-8 BOM
        }

        /// <summary>CSV 单元格转义：含逗号/引号/换行时加引号包裹。</summary>
        private static string CsvCell(string v)
        {
            v = v ?? "";
            if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
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
