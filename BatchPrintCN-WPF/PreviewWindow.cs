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
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号·改3：打印预览窗口（主列表双击任意行打开；交互原型＝work\原型-V4.9预览功能-20261002，需求方已认可）。
    ///
    /// 组成：标题（文件名）+ 信息条（类型与转换状态／纸张／缩放实际生效值／打印范围／黑白彩色份数）
    ///       + 顶部黄色警告条（与改1 日志同源）+ 左侧页缩略图列（点选切页）+ 中央大页渲染（叠版心虚线框与超界红斜纹）
    ///       + 底部「加入打印队列」「关闭」。
    ///
    /// 三条硬口径：
    /// ① 主列表**行高/列宽/单元格零改动**——入口只是 DataGrid 的一个 MouseDoubleClick 处理器，不往格子里塞任何东西；
    /// ② 渲染的就是将要打印的那份 PDF（PDF 直入＝原文件；Office＝同一条转换通道，产物进缓存，第二次预览不再转换）；
    /// ③ 只显示"将会打印"的页——页序取 <see cref="PrintPlan.PageSequence"/>，与打印同一份算法（范围/奇偶/小册子都算）。
    /// </summary>
    public sealed class PreviewWindow : Window
    {
        private readonly MainWindow _owner;
        private readonly RowVm _row;
        // 57 号令·B：预览窗从"只读展示"变成"第二个参数入口"，生效三件套随编辑重算，不再 readonly
        private PrintOptions _eff;
        private string _rangeText;
        private int _copies;
        private int _totalPages;            // 装载后记住总页数（重算页序不必每次重开 PDF）

        private TextBlock _title, _chips, _warnText, _hint, _legend;
        private Border _warnBar;
        private ItemsControl _thumbs;
        private Image _pageImage;
        private Canvas _overlay;
        private ScrollViewer _pageScroll;       // 46 号·增1：大页区改成可滚动容器（缩放后用滚动条平移；47 号：滚轮归还给它做页内滚动）
        private Grid _sheet;                    // 页面本体（尺寸＝按缩放算出的 DIP 大小）
        private Border _sheetBorder;
        private TextBlock _zoomText;            // 信息条右侧的当前缩放读数
        private TextBlock _pageText;            // 47 号·修3：信息条左侧的页码读数「第 x / N 页」
        private Button _prevBtn, _nextBtn;      // 47 号·修3：上一页 / 下一页（首页、末页各自置灰）
        private double _pageWpt, _pageHpt;        // 当前页的页面框尺寸（pt），算 DIP 与锚点用
        private double _zoomPercent = 100;      // 相对"实际像素"（1pt=1/72 英寸；96DPI 下 100% 即 96dpi 出图）
        private bool _fitMode = true;           // 打开时＝适合页面
        private int _rerenderCount;             // 自检取用：重渲染次数（证明缩放是按目标尺寸重画，不是拉伸旧位图）
        private double _lastRenderDpi;          // 自检取用：最近一次 pdfium 出图 DPI
        private Button _queueBtn, _closeBtn;
        private ScrollViewer _thumbScroll;

        // ===== 57 号令·B：信息条下方的参数编辑条（七项可调＋重置为跟随全局）=====
        // 数据流与主列表参数卡同源：改动写回 _row.Override（PerFileOverride）→ RowVm.NotifyOverrideChanged()
        // → 生效值一律经 EffectiveOptions.Resolve/RangeText/Copies 解析，本窗不第二条解析路。
        private ComboBox _pDuplex, _pColor, _pPaper, _pOrient, _pScale;
        private TextBox _pCopies, _pRange;
        private Button _pReset;
        private bool _loading;              // 回填期间的控件事件不回写（与 RowParamsPanel 同口径，防自激）
        /// <summary>自检取用：编辑条七枚控件（顺序＝双面/色彩/纸张/方向/缩放/份数/范围）。</summary>
        internal Control[] PvEditorsForTest
        { get { return new Control[] { _pDuplex, _pColor, _pPaper, _pOrient, _pScale, _pCopies, _pRange }; } }
        internal Button PvResetForTest { get { return _pReset; } }
        internal string PvResetText { get { return _pReset == null ? "" : (_pReset.Content ?? "").ToString(); } }
        /// <summary>自检取用：文本框提交（真机＝回车或离开焦点，两个入口调的都是这一个提交口）。</summary>
        internal void PvCommitTextForTest() { if (!_loading) ApplyParams(); }
        private static readonly string[] PaperNames = { "A4", "A3", "A5", "B5", "Letter", "Legal" };
        private static readonly System.Drawing.Printing.PaperKind[] PaperKinds =
            { System.Drawing.Printing.PaperKind.A4, System.Drawing.Printing.PaperKind.A3, System.Drawing.Printing.PaperKind.A5,
              System.Drawing.Printing.PaperKind.B5, System.Drawing.Printing.PaperKind.Letter, System.Drawing.Printing.PaperKind.Legal };

        private List<int> _pages = new List<int>();          // 0-based，按打印页序
        private OverflowScan _scan;
        private SourceLayoutFinding _src;
        private string _pdf;
        private bool _fromCache;
        private int _shown = -1;
        private bool _busy;

        /// <summary>自检取用：当前信息条文本。</summary>
        internal string ChipsText { get { return _chips == null ? null : _chips.Text; } }
        /// <summary>自检取用：警告条文本（空＝没警告）。</summary>
        internal string WarnText { get { return _warnText == null ? "" : _warnText.Text; } }
        /// <summary>自检取用：这次预览会显示哪几页（0-based，按打印页序）。</summary>
        internal IReadOnlyList<int> Pages { get { return _pages; } }
        /// <summary>自检取用：当前显示页在 _pages 里的下标。</summary>
        internal int ShownIndex { get { return _shown; } }
        /// <summary>自检取用：产物是否来自缓存。</summary>
        internal bool FromCache { get { return _fromCache; } }
        /// <summary>自检取用：底部主按钮（「加入打印队列」）。</summary>
        internal Button QueueButton { get { return _queueBtn; } }
        internal Button CloseButton { get { return _closeBtn; } }
        internal ItemsControl Thumbs { get { return _thumbs; } }
        internal bool Busy { get { return _busy; } }

        public PreviewWindow(MainWindow owner, RowVm row)
        {
            _owner = owner;
            _row = row;
            var app = Application.Current;
            _eff = EffectiveOptions.Resolve(owner == null ? new PrintOptions() : owner.ActiveOptions, row.Override);
            _rangeText = EffectiveOptions.RangeText(row.Override, row.Job.RangeText);
            _copies = EffectiveOptions.Copies(row.Override, row.Job.Copies);

            Title = L("s.PvTitleFmt").Replace("{0}", row.Job.Name);
            Width = 1080; Height = 720; MinWidth = 860; MinHeight = 560;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = Brush(app, "WindowBgBrush", Brushes.White);
            Foreground = Brush(app, "TextPrimaryBrush", Brushes.Black);
            FontSize = 13;

            var root = new Grid { Margin = new Thickness(16, 12, 16, 12) };
            // 0 标题 / 1 信息条（生效摘要＋翻页＋缩放读数）/ 2 参数编辑条（57 号·B）/ 3 警告条 / 4 正文（星号，唯一可伸缩行）/ 5 按钮行
            for (int i = 0; i < 4; i++) root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _title = new TextBlock
            {
                Text = Title,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brush(app, "TextPrimaryBrush", Brushes.Black),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 6)
            };
            Grid.SetRow(_title, 0);
            root.Children.Add(_title);

            _chips = new TextBlock
            {
                Text = BuildChips(),
                FontSize = 12.5,
                Foreground = Brush(app, "TextSecondaryBrush", Brushes.Gray),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            // 46 号·增1：信息条右侧加当前缩放读数 + 两枚小按钮（Ctrl+0 / Ctrl+1 的鼠标入口，快捷键同时保留）
            // 47 号·修2：读数两侧补「−」「＋」缩放按钮；修3：信息条左侧加「上一页／页码／下一页」翻页组
            var chipsRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };

            var zoomBox = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var outBtn = SmallText("−", "s.PvZoomOutTip");
            outBtn.Click += (s, e) => ZoomByStep(-1);
            var inBtn = SmallText("＋", "s.PvZoomInTip");
            inBtn.Click += (s, e) => ZoomByStep(1);
            zoomBox.Children.Add(outBtn);
            zoomBox.Children.Add(_zoomText = new TextBlock
            {
                Text = ViewZoomText(100),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush(app, "TextPrimaryBrush", Brushes.Black),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 8, 0)
            });
            zoomBox.Children.Add(inBtn);
            var fitBtn = SmallText(L("s.PvFitBtn"), null);
            var oneBtn = SmallText(L("s.PvActualBtn"), null);
            fitBtn.Click += (s, e) => ZoomToFit();
            oneBtn.Click += (s, e) => ZoomToActual();
            zoomBox.Children.Add(fitBtn);
            zoomBox.Children.Add(oneBtn);
            DockPanel.SetDock(zoomBox, Dock.Right);
            chipsRow.Children.Add(zoomBox);

            var pageBox = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 10, 0) };
            _prevBtn = SmallText(L("s.PvPrevBtn"), null);
            _nextBtn = SmallText(L("s.PvNextBtn"), null);
            _prevBtn.Click += (s, e) => GoToPage(_shown - 1);
            _nextBtn.Click += (s, e) => GoToPage(_shown + 1);
            _pageText = new TextBlock
            {
                Text = PageReadout(0, 0),
                FontSize = 12.5,
                Foreground = Brush(app, "TextSecondaryBrush", Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0)
            };
            pageBox.Children.Add(_prevBtn);
            pageBox.Children.Add(_pageText);
            pageBox.Children.Add(_nextBtn);
            DockPanel.SetDock(pageBox, Dock.Left);
            chipsRow.Children.Add(pageBox);

            chipsRow.Children.Add(_chips);
            Grid.SetRow(chipsRow, 1);
            root.Children.Add(chipsRow);

            // 57 号令·B：参数编辑条＝第二个参数入口（数据源与主列表参数卡同一个 RowVm.Override）
            var paramBar = BuildParamBar();
            Grid.SetRow(paramBar, 2);
            root.Children.Add(paramBar);
            BindParams();

            _warnBar = new Border
            {
                Background = Brush(app, "CardBgBrush", Brushes.White),
                BorderBrush = Brush(app, "StatusWarnTextBrush", Brushes.DarkOrange),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            _warnText = new TextBlock
            {
                Foreground = Brush(app, "StatusWarnTextBrush", Brushes.DarkOrange),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };
            _warnBar.Child = _warnText;
            Grid.SetRow(_warnBar, 3);
            root.Children.Add(_warnBar);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _thumbScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            _thumbs = new ItemsControl();
            _thumbScroll.Content = _thumbs;
            Grid.SetColumn(_thumbScroll, 0);
            body.Children.Add(_thumbScroll);

            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            // 46 号·增1：大页区从"Viewbox 等比塞满"改成 ScrollViewer + 定尺寸页面——
            // 缩放要能超出可视区（用滚动条平移），且必须按目标尺寸重新出图（拉伸旧位图会糊）。
            // 47 号·修1：滚轮只被 Ctrl 那一支接管（缩放），非 Ctrl 交还 ScrollViewer＝页内连续滚动；翻页走按钮与 PgUp/PgDn。
            _pageScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.Transparent
            };
            _pageImage = new Image { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
            _overlay = new Canvas { IsHitTestVisible = false, Background = Brushes.Transparent };
            _sheet = new Grid();
            _sheet.Children.Add(_pageImage);
            _sheet.Children.Add(_overlay);
            // 页面本体是白的，浅色主题下与窗口同色看不出边界——描一圈细边（不影响任何测量口径）
            _sheetBorder = new Border
            {
                Child = _sheet,
                BorderThickness = new Thickness(1),
                BorderBrush = Brush(app, "BorderStrongBrush", Brushes.LightGray),
                Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8)
            };
            _pageScroll.Content = _sheetBorder;
            _pageScroll.PreviewMouseWheel += OnPageWheel;
            Grid.SetRow(_pageScroll, 0);
            right.Children.Add(_pageScroll);
            _hint = new TextBlock
            {
                Text = "",
                FontSize = 12.5,
                Foreground = Brush(app, "TextSecondaryBrush", Brushes.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_hint, 1);
            right.Children.Add(_hint);
            _legend = new TextBlock
            {
                Text = L("s.PvOverflowLegend"),
                FontSize = 11.5,
                Foreground = Brush(app, "StatusWarnTextBrush", Brushes.DarkOrange),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_legend, 2);
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.Children.Add(_legend);

            Grid.SetColumn(right, 2);
            body.Children.Add(right);
            Grid.SetRow(body, 4);   // 星号行：正文拿满剩余高度，大页才按可用区缩放
            root.Children.Add(body);

            var foot = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            _queueBtn = new Button { Content = L("s.PvQueue"), MinWidth = 132, Height = 32, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            _closeBtn = new Button { Content = L("s.PvClose"), MinWidth = 96, Height = 32, IsCancel = true };
            _queueBtn.Style = (Style)app.TryFindResource("BtnPrimary");
            _closeBtn.Style = (Style)app.TryFindResource("BtnOutline");
            _queueBtn.Click += (s, e) => OnQueueClicked();
            _closeBtn.Click += (s, e) => Close();
            foot.Children.Add(_queueBtn);
            foot.Children.Add(_closeBtn);
            Grid.SetRow(foot, 5);
            root.Children.Add(foot);

            Content = root;
            SetBusy(true, L("s.PvConvertingHint"));
            PreviewKeyDown += OnKeyDownZoom;        // 46 号·增1：Ctrl+0＝适合页面、Ctrl+1＝实际像素
            Loaded += (s, e) => StartLoad();
            Closed += (s, e) => { if (_owner != null) _owner.PreviewClosed(this); };
        }

        // ===== 加载：转换（后台）→ 扫描 → 出缩略图与首页 =====
        private async void StartLoad()
        {
            object[] r = null;
            try
            {
                r = await System.Threading.Tasks.Task.Run(() =>
                {
                    bool fromCache;
                    string err;
                    string pdf = PreviewCache.EnsurePdf(_row.Job, null, out fromCache, out err);
                    OverflowScan scan = pdf == null ? null : PageOverflowScanner.Scan(pdf);
                    SourceLayoutFinding src = SourceLayoutProbe.Probe(_row.Job.FilePath, _row.Job.Kind);
                    return new object[] { pdf, fromCache, err, scan, src };
                });
            }
            catch (Exception ex) { SetBusy(false, L("s.PvRenderFailed") + "：" + ex.Message); return; }
            if (r == null) { SetBusy(false, L("s.PvRenderFailed")); return; }
            _pdf = (string)r[0];
            _fromCache = (bool)r[1];
            _scan = (OverflowScan)r[3];
            _src = (SourceLayoutFinding)r[4];
            if (_pdf == null) { SetBusy(false, (r[2] as string) ?? L("s.PvRenderFailed")); return; }
            int total = PdfRaster.PageCount(_pdf);
            _totalPages = total;
            _pages = PagesToShow(total, _rangeText, _eff);
            _chips.Text = BuildChips();
            ApplyWarning();
            BuildThumbs();
            SetBusy(false, _pages.Count == 0 ? L("s.PvNoPages") : "");
            if (_pages.Count > 0) ShowPage(0);
        }

        /// <summary>信息条里的小号文字按钮（47 号：缩放 −／＋ 与翻页 上一页／下一页 与 46 号的两个恢复口同款）。</summary>
        private Button SmallText(string content, string tipKey)
        {
            var b = new Button
            {
                Content = content,
                Style = (Style)Application.Current.TryFindResource("BtnText"),
                FontSize = 12,
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            if (tipKey != null) b.ToolTip = L(tipKey);
            return b;
        }

        // ===== 57 号令·B：参数编辑条（第二个参数入口，与主列表参数卡同一数据源）=====

        /// <summary>编辑条：七项可调（双面/色彩/纸张/方向/缩放/份数/页码范围）＋「重置为跟随全局」。
        /// 标签与选项一律复用既有资源键（s.Ps*／s.Col*／s.FollowGlobal／s.RowParamsReset），本令不新增键；
        /// 「类型」与「输出」两枚仍只读（煤计项，57 号拍板）。下拉＝改动即写回；文本框＝回车或离开焦点写回（与列表单元格同一提交口径）。</summary>
        private FrameworkElement BuildParamBar()
        {
            var app = Application.Current;
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };

            // 双面与缩放用「摘要行同款」的紧凑键（s.Pv*）——参数卡那套 s.Ps* 全称（如"双面 · 长边翻页（书籍式）"）
            // 在这条窄栏里会换行把整栏撑高；同一屏里下拉显示的字与生效摘要显示的字也保持一致。
            _pDuplex = ParamCombo(new[] { "s.PvDuplexSimplex", "s.PvDuplexLong", "s.PvDuplexShort" }, 118);
            _pColor = ParamCombo(new[] { "s.PsColor", "s.PsBlackWhite" }, 118);
            _pPaper = ParamCombo(null, 118);                     // 纸张＝固定表（与参数卡同一张 PaperNames）
            _pOrient = ParamCombo(new[] { "s.PvOrientAuto", "s.PsPortrait", "s.PsLandscape" }, 118);
            _pScale = ParamCombo(new[] { "s.PvScaleFit", "s.PvScaleNone", "s.PvScaleShrink", "s.PvScaleStretch", "s.PsScaleCustom" }, 118);
            _pCopies = ParamText(46);
            _pRange = ParamText(84);
            _pReset = new Button
            {
                Content = L("s.RowParamsReset"),
                Style = (Style)app.TryFindResource("BtnText"),
                FontSize = 12,
                Padding = new Thickness(6, 2, 6, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };
            _pReset.Click += (s, e) => OnResetParams();

            bar.Children.Add(ParamCell("s.PsDuplex", _pDuplex));
            bar.Children.Add(ParamCell("s.PsColorMode", _pColor));
            bar.Children.Add(ParamCell("s.PsPaper", _pPaper));
            bar.Children.Add(ParamCell("s.PsOrientation", _pOrient));
            bar.Children.Add(ParamCell("s.PsScaleMode", _pScale));
            bar.Children.Add(ParamCell("s.ColCopies", _pCopies));
            bar.Children.Add(ParamCell("s.ColRange", _pRange));
            bar.Children.Add(_pReset);
            return bar;
        }

        /// <summary>下拉：第一项恒为「跟随全局」（＝null 覆盖），其后依次对应枚举值（与 RowParamsPanel 同一索引口径）。
        /// 高度与字号一律交给主题样式（强制 Height=24 会让 Fluent 样式的选中文字压到边框上，2026-10-09 出图实测）。</summary>
        private ComboBox ParamCombo(string[] itemKeys, double width)
        {
            var box = new ComboBox
            {
                Width = width,
                VerticalAlignment = VerticalAlignment.Center,
                ItemTemplate = (DataTemplate)Application.Current.FindResource("ComboTextTemplate")
            };
            box.Items.Add(L("s.FollowGlobal"));
            if (itemKeys != null)
                foreach (string k in itemKeys) box.Items.Add(L(k));
            else
                foreach (string n in PaperNames) box.Items.Add(n);
            box.SelectionChanged += OnParamChanged;
            return box;
        }

        private TextBox ParamText(double width)
        {
            var tb = new TextBox
            {
                Width = width,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            tb.LostFocus += (s, e) => { if (!_loading) ApplyParams(); };
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter && !_loading) ApplyParams(); };
            return tb;
        }

        private StackPanel ParamCell(string labelKey, Control editor)
        {
            var sp = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 10, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            sp.Children.Add(new TextBlock
            {
                Text = L(labelKey),
                FontSize = 12,
                Foreground = Brush(Application.Current, "TextSecondaryBrush", Brushes.Gray),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            sp.Children.Add(editor);
            return sp;
        }

        /// <summary>打开时回填：该行覆盖 → 控件（_loading 期间不回写，防自激；与 RowParamsPanel 同口径）。</summary>
        private void BindParams()
        {
            if (_pDuplex == null) return;
            _loading = true;
            try
            {
                var o = _row != null ? _row.Override : null;
                _pDuplex.SelectedIndex = o != null && o.Duplex.HasValue ? (int)o.Duplex.Value + 1 : 0;
                _pColor.SelectedIndex = o != null && o.Color.HasValue ? (int)o.Color.Value + 1 : 0;
                _pPaper.SelectedIndex = o != null && o.Paper.HasValue ? Array.IndexOf(PaperKinds, o.Paper.Value) + 1 : 0;
                _pOrient.SelectedIndex = o != null && o.Orientation.HasValue ? (int)o.Orientation.Value + 1 : 0;
                _pScale.SelectedIndex = o != null && o.Scale.HasValue ? (int)o.Scale.Value + 1 : 0;
                _pCopies.Text = o != null && o.Copies.HasValue ? o.Copies.Value.ToString() : "";
                _pRange.Text = o != null && !string.IsNullOrWhiteSpace(o.PageRange) ? o.PageRange : "";
            }
            finally { _loading = false; }
        }

        private void OnParamChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            ApplyParams();
        }

        /// <summary>
        /// 编辑条 → 该行覆盖：只动这七项，预览窗不暴露的四项（版数/小册子/奇偶/纸盒）原样带过去，
        /// 绝不把参数卡里已设的值抹掉；七项全空＝丢弃覆盖对象，行上回到「跟随全局」。
        /// 缩放百分比沿用 B-6 口径：只有模式＝自定义百分比时保留百分比值，其余清空不留孤立值。
        /// </summary>
        private void ApplyParams()
        {
            if (_row == null) return;
            var prev = _row.Override;
            var o = new PerFileOverride
            {
                PagesPerSheet = prev != null ? prev.PagesPerSheet : (int?)null,
                Booklet = prev != null ? prev.Booklet : (bool?)null,
                PageParity = prev != null ? prev.PageParity : (PageParityMode?)null,
                PaperSourceOverride = prev != null ? prev.PaperSourceOverride : null,
                Duplex = _pDuplex.SelectedIndex >= 1 ? (DuplexMode)(_pDuplex.SelectedIndex - 1) : (DuplexMode?)null,
                Color = _pColor.SelectedIndex >= 1 ? (ColorMode)(_pColor.SelectedIndex - 1) : (ColorMode?)null,
                Orientation = _pOrient.SelectedIndex >= 1 ? (OrientationMode)(_pOrient.SelectedIndex - 1) : (OrientationMode?)null,
                Scale = _pScale.SelectedIndex >= 1 ? (ScaleMode)(_pScale.SelectedIndex - 1) : (ScaleMode?)null,
                PageRange = string.IsNullOrWhiteSpace(_pRange.Text) ? null : _pRange.Text.Trim()
            };
            if (_pPaper.SelectedIndex >= 1)
            {
                o.Paper = PaperKinds[_pPaper.SelectedIndex - 1];
                o.PaperDisplayName = PaperNames[_pPaper.SelectedIndex - 1];
            }
            bool customPercent = _pScale.SelectedIndex == (int)ScaleMode.Custom + 1;
            if (customPercent) o.ScalePercent = prev != null && prev.ScalePercent.HasValue ? prev.ScalePercent : (int?)null;

            int copies;
            if (int.TryParse((_pCopies.Text ?? "").Trim(), out copies) && copies >= 1)
                o.Copies = Math.Min(99, copies);

            _row.Override = o.HasAny ? o : null;
            _row.NotifyOverrideChanged();     // 主列表「参数」列与生效范围列跟着联动
            ReapplyAndRerender();
        }

        /// <summary>「重置为跟随全局」：只清预览窗这七项（其余四项不动），并立即写回（与参数卡"重置后仍需保存"不同——本窗没有保存键）。</summary>
        private void OnResetParams()
        {
            if (_pDuplex == null) return;
            _loading = true;
            try
            {
                _pDuplex.SelectedIndex = 0;
                _pColor.SelectedIndex = 0;
                _pPaper.SelectedIndex = 0;
                _pOrient.SelectedIndex = 0;
                _pScale.SelectedIndex = 0;
                _pCopies.Text = "";
                _pRange.Text = "";
            }
            finally { _loading = false; }
            ApplyParams();
        }

        /// <summary>覆盖变了 → 重算生效三件套 → 刷新摘要与页面（与装载走同一段呈现代码，不另开一条渲染路）。</summary>
        private void ReapplyAndRerender()
        {
            var ov = _row != null ? _row.Override : null;
            _eff = EffectiveOptions.Resolve(_owner == null ? new PrintOptions() : _owner.ActiveOptions, ov);
            _rangeText = EffectiveOptions.RangeText(ov, _row != null ? _row.Job.RangeText : "");
            _copies = EffectiveOptions.Copies(ov, _row != null ? _row.Job.Copies : 1);
            // 顺序有讲究：摘要行里的「全部（N 页）」取的是 _pages.Count，必须先重算页序再刷摘要，
            // 否则改完范围那一屏会显示上一次的页数（2026-10-09 自检抓到并修）。
            if (_pdf != null)
            {
                _pages = PagesToShow(_totalPages, _rangeText, _eff);
                ApplyWarning();
                BuildThumbs();
                SetBusy(false, _pages.Count == 0 ? L("s.PvNoPages") : "");
            }
            if (_chips != null) _chips.Text = BuildChips();
            if (_pdf == null || _pages.Count == 0) return;
            ShowPage(_shown >= 0 && _shown < _pages.Count ? _shown : 0);   // 范围收窄后页码不越界
        }

        /// <summary>页码读数（47 号·修3）：还没加载出页时不显示"第 1 / 0 页"这种半成品。</summary>
        private string PageReadout(int index, int total)
        {
            if (total <= 0) return "";
            return L("s.PvPageOfFmt").Replace("{0}", (index + 1).ToString()).Replace("{1}", total.ToString());
        }

        private void BuildThumbs()
        {
            _thumbs.Items.Clear();
            for (int i = 0; i < _pages.Count; i++)
            {
                int idx = i;
                var item = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                var border = new Border
                {
                    BorderThickness = new Thickness(i == 0 ? 2 : 1),
                    BorderBrush = i == 0 ? Brush(Application.Current, "AccentBrush", Brushes.DodgerBlue)
                                         : Brush(Application.Current, "BorderBrush", Brushes.LightGray),
                    Background = Brushes.White,
                    Padding = new Thickness(3),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = idx
                };
                var raster = PdfRaster.Rasterize(_pdf, _pages[i], 24);
                UIElement inner = raster == null
                    ? (UIElement)new TextBlock { Text = L("s.PvRenderFailed"), FontSize = 10.5, TextWrapping = TextWrapping.Wrap }
                    : (UIElement)new Image { Source = ToSource(raster), Width = 104, Stretch = Stretch.Uniform };
                // 超界的页在缩略图右下角加一枚 ⚠（元素还没认父，先包再交给 Border，避免"逻辑父已被占用"）
                if (PageProblem(_pages[i]) != null) inner = WrapWithMark(inner);
                border.Child = inner;
                var cap = new TextBlock
                {
                    Text = L("s.PvPageFmt").Replace("{0}", (i + 1).ToString()),
                    FontSize = 11.5,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                    Foreground = Brush(Application.Current, "TextSecondaryBrush", Brushes.Gray)
                };
                item.Children.Add(border);
                item.Children.Add(cap);
                border.MouseLeftButtonUp += (s, e) => ShowPage(idx);
                _thumbs.Items.Add(item);
            }
        }

        private static UIElement WrapWithMark(UIElement inner)
        {
            var g = new Grid();
            if (inner != null) g.Children.Add(inner);
            g.Children.Add(new TextBlock
            {
                Text = "⚠",
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 4, 2),
                Foreground = Brush(Application.Current, "StatusWarnTextBrush", Brushes.DarkOrange)
            });
            return g;
        }

        private void ShowPage(int i)
        {
            if (_pdf == null || i < 0 || i >= _pages.Count) return;
            _shown = i;
            // 先量这一页的页面框（不出图），"适合页面"才有依据；否则第一遍会按 100% 白画一次
            double pw0, ph0;
            if (PdfRaster.PageSize(_pdf, _pages[i], out pw0, out ph0)) { _pageWpt = pw0; _pageHpt = ph0; }
            double percent = EffectivePercent();
            double dpi = RenderDpi(percent);
            var raster = PdfRaster.Rasterize(_pdf, _pages[i], (float)dpi);
            if (raster == null)
            {
                _pageImage.Source = null;
                _overlay.Children.Clear();
                _hint.Text = L("s.PvRenderFailed");
                _hint.Visibility = Visibility.Visible;
                return;
            }
            _rerenderCount++; _lastRenderDpi = dpi;
            _pageWpt = raster.WidthPt; _pageHpt = raster.HeightPt;
            _hint.Visibility = Visibility.Collapsed;
            double dipW = PageDip(raster.WidthPt, percent), dipH = PageDip(raster.HeightPt, percent);
            _sheet.Width = dipW; _sheet.Height = dipH;
            _pageImage.Width = dipW; _pageImage.Height = dipH;
            _overlay.Width = dipW; _overlay.Height = dipH;
            _pageImage.Source = ToSource(raster);      // 新图算好才换 Source：这一刻之前屏上一直是旧图，不闪白
            DrawOverlay(raster, _pages[i], dipW, dipH);
            if (_zoomText != null) _zoomText.Text = ViewZoomText(percent);
            // 47 号·修3：页码读数 + 翻页按钮边界置灰（首页禁「上一页」、末页禁「下一页」，与 GoToPage 的 no-op 双保险）
            if (_pageText != null) _pageText.Text = PageReadout(i, _pages.Count);
            if (_prevBtn != null) _prevBtn.IsEnabled = i > 0;
            if (_nextBtn != null) _nextBtn.IsEnabled = i < _pages.Count - 1;
            for (int k = 0; k < _thumbs.Items.Count; k++)
            {
                var sp = _thumbs.Items[k] as StackPanel;
                if (sp == null) continue;
                var bd = sp.Children[0] as Border;
                if (bd == null) continue;
                bd.BorderThickness = new Thickness(k == i ? 2 : 1);
                bd.BorderBrush = k == i ? Brush(Application.Current, "AccentBrush", Brushes.DodgerBlue)
                                        : Brush(Application.Current, "BorderBrush", Brushes.LightGray);
            }
        }

        // ===== 46 号·增1/增2：缩放与翻页（交互层，打印与 45 号三项成果一行未动） =====

        /// <summary>缩放档位序列（开工令指定）；上下限＝首末两档。</summary>
        internal static readonly double[] ZoomLevels = { 25, 50, 75, 100, 150, 200, 300, 400 };

        /// <summary>一页 DIP 宽/高 ＝ 页面 pt 尺寸 × (96/72) × 缩放百分比/100（100%＝实际像素）。</summary>
        internal static double PageDip(double pt, double percent) { return pt * (96.0 / 72.0) * percent / 100.0; }

        /// <summary>出图 DPI＝96 × 百分比/100（这样位图像素与 DIP 一比一，缩放不靠拉伸）。</summary>
        internal static double RenderDpi(double percent) { return 96.0 * percent / 100.0; }

        /// <summary>
        /// 滚轮一格走一档：当前值不在档位上时，先就近到滚动方向的那一档（"跨档平滑插值到最近档"），
        /// 再按档位序列走；越界一律钳在 25%~400%。
        /// </summary>
        internal static double NextZoom(double current, int notches)
        {
            double v = current;
            for (int step = 0; step < Math.Abs(notches); step++)
            {
                bool up = notches > 0;
                if (up)
                {
                    double next = ZoomLevels.FirstOrDefault(x => x > v + 0.01);
                    v = next > 0 ? next : ZoomLevels[ZoomLevels.Length - 1];
                }
                else
                {
                    double prev = 0;
                    foreach (double x in ZoomLevels) if (x < v - 0.01) prev = x;
                    v = prev > 0 ? prev : ZoomLevels[0];
                }
            }
            return ClampZoom(v);
        }

        internal static double ClampZoom(double v)
        {
            return Math.Max(ZoomLevels[0], Math.Min(ZoomLevels[ZoomLevels.Length - 1], v));
        }

        /// <summary>
        /// 锚点保持：缩放前后让"鼠标指着的那个文档位置"停在屏幕原地。
        /// doc 比例＝(旧偏移+指针在视口内的位置)/旧内容尺寸，新偏移＝该比例×新内容尺寸−指针位置。
        /// </summary>
        internal static double AnchorScroll(double oldOffset, double oldSize, double newSize, double pointerInViewport)
        {
            if (oldSize <= 0) return oldOffset;
            double doc = oldOffset + pointerInViewport;
            double frac = doc / oldSize;
            return Math.Max(0, frac * newSize - pointerInViewport);
        }

        /// <summary>适合页面时的百分比（钳进档位区间）；可视区还没量出来时退回 100%。</summary>
        internal static double FitPercent(double viewportW, double viewportH, double pageWAt100, double pageHAt100)
        {
            if (viewportW <= 1 || viewportH <= 1 || pageWAt100 <= 0 || pageHAt100 <= 0) return 100;
            double k = Math.Min(viewportW / pageWAt100, viewportH / pageHAt100) * 100.0;
            return ClampZoom(k);
        }

        /// <summary>当前实际生效的缩放百分比（适合页面模式按可视区现算）。</summary>
        internal double EffectivePercent()
        {
            if (!_fitMode) return ClampZoom(_zoomPercent);
            double pageW100 = _pageWpt > 0 ? PageDip(_pageWpt, 100) : 0;
            double pageH100 = _pageHpt > 0 ? PageDip(_pageHpt, 100) : 0;
            return FitPercent(ViewportW, ViewportH, pageW100, pageH100);
        }

        /// <summary>可视区尺寸（无头自检里量不到时退回窗口客户区估算）。</summary>
        private double ViewportW
        {
            get
            {
                double v = _pageScroll == null ? 0 : _pageScroll.ViewportWidth;
                if (v > 1) return v;
                return Math.Max(0, Width - 200);
            }
        }

        private double ViewportH
        {
            get
            {
                double v = _pageScroll == null ? 0 : _pageScroll.ViewportHeight;
                if (v > 1) return v;
                return Math.Max(0, Height - 250);
            }
        }

        private string ViewZoomText(double percent)
        {
            return L("s.PvViewZoomFmt").Replace("{0}", ((int)Math.Round(percent)).ToString());
        }

        /// <summary>
        /// 按键意图 → 动作（46 号 Ctrl+0／Ctrl+1 ＋ 47 号·修3 PgUp／PgDn 翻页）。
        /// 单独抽出来是因为无头自检合成不了 KeyEventArgs（44/45/46 号同口径）：
        /// 事件处理器与自检都走这一段，断言即打到真代码。
        /// </summary>
        internal static bool HandleZoomKey(bool ctrl, Key key, PreviewWindow pv)
        {
            if (ctrl)
            {
                if (key == Key.D0 || key == Key.NumPad0) { pv.ZoomToFit(); return true; }
                if (key == Key.D1 || key == Key.NumPad1) { pv.ZoomToActual(); return true; }
                return false;
            }
            if (key == Key.PageUp) { pv.GoToPage(pv._shown - 1); return true; }
            if (key == Key.PageDown) { pv.GoToPage(pv._shown + 1); return true; }
            return false;
        }

        /// <summary>快捷键：Ctrl+0（含小键盘）＝适合页面，Ctrl+1＝实际像素；PgUp／PgDn＝上一页／下一页（47 号·修3）。</summary>
        private void OnKeyDownZoom(object sender, System.Windows.Input.KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (HandleZoomKey(ctrl, e.Key, this)) e.Handled = true;
        }

        /// <summary>
        /// 滚轮分流（47 号·修1）：按住 Ctrl＝以指针为锚点缩放；否则**不接管**——
        /// 滚动权还给 ScrollViewer，放大态滚轮＝页内连续滚动看细节，适页态整页已可见＝自然滚不动。
        /// 翻页走信息条按钮与 PgUp／PgDn（修3）。
        /// </summary>
        private void OnPageWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;     // 非 Ctrl：不置 Handled，让 ScrollViewer 自己滚
            ZoomByWheel(e.Delta, e.GetPosition(_pageScroll));
            e.Handled = true;
        }

        /// <summary>Ctrl+滚轮：算出新缩放，按目标尺寸重渲染，再把指针下的文档位置挪回原地。</summary>
        internal void ZoomByWheel(int delta, System.Windows.Point pointer)
        {
            int notches = delta == 0 ? 0 : (delta > 0 ? 1 : -1);
            ZoomStep(notches, pointer.X, pointer.Y);
        }

        /// <summary>47 号·修2：「−」「＋」按钮——与滚轮同一档位序列、同一段代码，只是不传锚点。</summary>
        internal void ZoomByStep(int notches) { ZoomStep(notches, null, null); }

        /// <summary>走一档缩放（pointerX/Y＝指针在视口内的位置，用于锚点保持；传 null＝不保持锚点）。</summary>
        private void ZoomStep(int notches, double? pointerX, double? pointerY)
        {
            if (notches == 0) return;
            ZoomTo(NextZoom(EffectivePercent(), notches), pointerX, pointerY);
        }

        /// <summary>设定缩放（pointerX/Y＝指针在视口内的位置，用于锚点保持；传 null＝不保持锚点）。</summary>
        private void ZoomTo(double percent, double? pointerX, double? pointerY)
        {
            percent = ClampZoom(percent);
            double oldW = _pageScroll == null ? 0 : _pageScroll.ExtentWidth;
            double oldH = _pageScroll == null ? 0 : _pageScroll.ExtentHeight;
            double offX = _pageScroll == null ? 0 : _pageScroll.HorizontalOffset;
            double offY = _pageScroll == null ? 0 : _pageScroll.VerticalOffset;
            _fitMode = false;
            _zoomPercent = percent;
            ShowPage(_shown);                                   // 按新 DPI 重渲染当前页
            _pageScroll.UpdateLayout();
            if (pointerX.HasValue && oldW > 1 && _pageScroll.ExtentWidth > 1)
                _pageScroll.ScrollToHorizontalOffset(AnchorScroll(offX, oldW, _pageScroll.ExtentWidth, pointerX.Value));
            if (pointerY.HasValue && oldH > 1 && _pageScroll.ExtentHeight > 1)
                _pageScroll.ScrollToVerticalOffset(AnchorScroll(offY, oldH, _pageScroll.ExtentHeight, pointerY.Value));
        }

        /// <summary>Ctrl+0＝适合页面。</summary>
        internal void ZoomToFit()
        {
            _fitMode = true;
            ShowPage(_shown);
            if (_pageScroll != null) { _pageScroll.ScrollToHorizontalOffset(0); _pageScroll.ScrollToVerticalOffset(0); }
        }

        /// <summary>Ctrl+1＝实际像素（100%）。</summary>
        internal void ZoomToActual()
        {
            _fitMode = false;
            ZoomTo(100, null, null);
        }

        /// <summary>
        /// 47 号·修3：翻页的唯一入口（信息条按钮、PgUp／PgDn、自检都走这里）。
        /// 越界＝原地不动（不环绕、不弹窗）；缩放值保留（EffectivePercent 不变，新页按同一缩放重新出图）。
        /// </summary>
        internal void GoToPage(int to)
        {
            if (_pages.Count == 0) return;
            if (to < 0 || to >= _pages.Count) return;      // 边界 no-op（按钮此时也已置灰，双保险）
            ShowPage(to);
        }

        /// <summary>自检用：走同一段滚轮分流（ctrl＝true 缩放；47 号起非 Ctrl 什么都不做），不合成鼠标输入。</summary>
        internal void WheelForTest(int delta, bool ctrl, double pointerX, double pointerY)
        {
            if (ctrl) ZoomByWheel(delta, new System.Windows.Point(pointerX, pointerY));
        }

        /// <summary>自检用：走与 PreviewKeyDown 同一段按键处理（无头环境合成不了 KeyEventArgs）。</summary>
        internal bool KeyForTest(bool ctrl, Key key) { return HandleZoomKey(ctrl, key, this); }

        /// <summary>自检/按钮用：直接设定缩放。</summary>
        internal void ZoomToForTest(double percent) { ZoomTo(percent, null, null); }
        internal void ZoomStepForTest(int notches) { ZoomByStep(notches); }
        internal double ZoomPercentForTest { get { return EffectivePercent(); } }
        internal bool FitModeForTest { get { return _fitMode; } }
        internal int RerenderCountForTest { get { return _rerenderCount; } }
        internal double LastRenderDpiForTest { get { return _lastRenderDpi; } }
        internal double SheetWidthForTest { get { return _sheet == null ? 0 : _sheet.Width; } }
        internal string ZoomReadoutForTest { get { return _zoomText == null ? null : _zoomText.Text; } }
        internal string PageReadoutForTest { get { return _pageText == null ? null : _pageText.Text; } }
        internal bool PrevPageEnabledForTest { get { return _prevBtn != null && _prevBtn.IsEnabled; } }
        internal bool NextPageEnabledForTest { get { return _nextBtn != null && _nextBtn.IsEnabled; } }
        internal ScrollViewer PageScrollForTest { get { return _pageScroll; } }
        internal double PagePtWidthForTest { get { return _pageWpt; } }

        // ===== 叠加层：版心虚线框 + 超界红斜纹（数值与改1 同源） =====
        private void DrawOverlay(PageRaster raster, int pageIndex, double sheetW, double sheetH)
        {
            _overlay.Children.Clear();
            double pageWmm = raster.WidthPt * 25.4 / 72.0;
            double pageHmm = raster.HeightPt * 25.4 / 72.0;
            if (pageWmm <= 0 || pageHmm <= 0) return;
            // 46 号·增1：叠加层跟着大页的 DIP 尺寸走（原来按位图像素算，缩放后就对不上了）
            double pxPerMmX = sheetW / pageWmm;
            double pxPerMmY = sheetH / pageHmm;
            var accent = Brush(Application.Current, "AccentBrush", Brushes.DodgerBlue);

            double band = OverflowScan.UnprintableBandMm;
            var safe = new Rectangle
            {
                Width = (pageWmm - 2 * band) * pxPerMmX,
                Height = (pageHmm - 2 * band) * pxPerMmY,
                Stroke = accent,
                StrokeThickness = 1.2,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                Fill = Brushes.Transparent
            };
            Canvas.SetLeft(safe, band * pxPerMmX);
            Canvas.SetTop(safe, band * pxPerMmY);
            _overlay.Children.Add(safe);
            var lab = new TextBlock
            {
                Text = L("s.PvSafeAreaFmt").Replace("{0}", (OverflowScan.CommonName(pageWmm, pageHmm) ?? "A4")),
                FontSize = 11,
                Foreground = accent
            };
            Canvas.SetLeft(lab, band * pxPerMmX + 2);
            Canvas.SetTop(lab, Math.Max(0, band * pxPerMmY - 16));
            _overlay.Children.Add(lab);

            var rep = PageProblem(pageIndex);
            var strips = rep == null ? null : HatchRects(rep, pageWmm, pageHmm, band);
            if (strips != null && strips.Count > 0)
            {
                foreach (RectMm s in strips)
                {
                    var r = new Rectangle
                    {
                        Width = Math.Max(2, s.Wmm * pxPerMmX),
                        Height = Math.Max(2, s.Hmm * pxPerMmY),
                        Fill = HatchBrush(),
                        Stroke = Brush(Application.Current, "StatusFailTextBrush", Brushes.Red),
                        StrokeThickness = 1
                    };
                    Canvas.SetLeft(r, s.Xmm * pxPerMmX);
                    Canvas.SetTop(r, s.Ymm * pxPerMmY);
                    _overlay.Children.Add(r);
                }
                _legend.Visibility = Visibility.Visible;
            }
            else _legend.Visibility = Visibility.Collapsed;
        }

        /// <summary>毫米矩形（以页面左上角为原点，与叠加层同一坐标）。</summary>
        internal struct RectMm { public double Xmm, Ymm, Wmm, Hmm; }

        /// <summary>
        /// 超界红斜纹的位置：哪几条边进了不可打印带（或越出页面框），就在那条边的带区画一条。
        /// 纯函数、按毫米算——断言直接核对数值与改1 的结论同源，不依赖渲染。
        /// </summary>
        internal static List<RectMm> HatchRects(PageBoundsReport p, double pageWmm, double pageHmm, double bandMm)
        {
            var list = new List<RectMm>();
            if (p == null) return list;
            double band = Math.Max(bandMm, p.MaxMm);
            if (p.IntoRightMm > 0 || p.OverRightMm > 0)
                list.Add(new RectMm { Xmm = pageWmm - band, Ymm = 0, Wmm = band, Hmm = pageHmm });
            if (p.IntoLeftMm > 0 || p.OverLeftMm > 0)
                list.Add(new RectMm { Xmm = 0, Ymm = 0, Wmm = band, Hmm = pageHmm });
            if (p.IntoTopMm > 0 || p.OverTopMm > 0)
                list.Add(new RectMm { Xmm = 0, Ymm = 0, Wmm = pageWmm, Hmm = band });
            if (p.IntoBottomMm > 0 || p.OverBottomMm > 0)
                list.Add(new RectMm { Xmm = 0, Ymm = pageHmm - band, Wmm = pageWmm, Hmm = band });
            return list;
        }

        private static DrawingBrush _hatch;
        private static Brush HatchBrush()
        {
            if (_hatch != null) return _hatch;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(0xE6, 0xD9, 0x53, 0x4F)), 2.2);
            var dg = new GeometryGroup();
            dg.Children.Add(new LineGeometry(new Point(0, 8), new Point(8, 0)));
            dg.Children.Add(new LineGeometry(new Point(-2, 2), new Point(2, -2)));
            dg.Children.Add(new LineGeometry(new Point(6, 10), new Point(10, 6)));
            var dd = new GeometryDrawing { Geometry = dg, Pen = pen };
            _hatch = new DrawingBrush(dd)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 8, 8),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None,
                Opacity = 0.34
            };
            _hatch.Freeze();
            return _hatch;
        }

        /// <summary>这一页（0-based 文档页序）的超界结论；没有则 null。</summary>
        private PageBoundsReport PageProblem(int documentPageIndex)
        {
            if (_scan == null || !_scan.Scanned) return null;
            foreach (PageBoundsReport p in _scan.Pages) if (p.Index == documentPageIndex) return p;
            return null;
        }

        private void ApplyWarning()
        {
            string w = WarningText(_scan, _src, L);
            if (string.IsNullOrEmpty(w)) { _warnBar.Visibility = Visibility.Collapsed; return; }
            _warnText.Text = w;
            _warnBar.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 顶部警告条文本（与改1 日志同源，只是换成界面双语口径）。纯函数，自检直接核对。
        /// </summary>
        internal static string WarningText(OverflowScan scan, SourceLayoutFinding src, Func<string, string> res)
        {
            var parts = new List<string>();
            if (scan != null && scan.Scanned && scan.HasOverflow)
            {
                if (scan.OverflowPageCount == 1)
                {
                    PageBoundsReport p = scan.Pages[0];
                    string key = p.Kind == PageOverflowKind.BeyondPage ? "s.PvOverflowOneFmt" : "s.PvNearEdgeOneFmt";
                    bool beyond = p.Kind == PageOverflowKind.BeyondPage;
                    parts.Add(res(key)
                        .Replace("{0}", p.Page.ToString())
                        .Replace("{1}", EdgeUi(p.WorstEdge, res))
                        // 真越界报"超出多少"；贴边报"距页边多少 + 进了多宽的不可打印带"（深度是两者之差，日志里另有逐边明细）
                        .Replace("{2}", OverflowScan.MmText(beyond ? p.MaxOverMm : Math.Max(0, GapOf(p))))
                        .Replace("{3}", OverflowScan.MmText(OverflowScan.UnprintableBandMm)));
                }
                else parts.Add(res("s.PvOverflowManyFmt").Replace("{0}", scan.OverflowPageCount.ToString()));
            }
            if (src != null && src.Checked && src.Found)
            {
                parts.Add(res("s.PvSourceWideFmt")
                    .Replace("{0}", src.OffendingTable.ToString())
                    .Replace("{1}", OverflowScan.MmText(src.WidestTableMm))
                    .Replace("{2}", src.PageText)
                    .Replace("{3}", OverflowScan.MmText(src.TextWidthMm))
                    .Replace("{4}", OverflowScan.MmText(src.OverMm)));
            }
            return string.Join("　", parts.ToArray());
        }

        private static double GapOf(PageBoundsReport p)
        {
            double g = p.GapRightMm;
            if (p.GapLeftMm < g) g = p.GapLeftMm;
            if (p.GapTopMm < g) g = p.GapTopMm;
            if (p.GapBottomMm < g) g = p.GapBottomMm;
            return g;
        }

        private static string EdgeUi(OverflowEdge e, Func<string, string> res)
        {
            switch (e)
            {
                case OverflowEdge.Right: return res("s.PvEdgeRight");
                case OverflowEdge.Left: return res("s.PvEdgeLeft");
                case OverflowEdge.Top: return res("s.PvEdgeTop");
                default: return res("s.PvEdgeBottom");
            }
        }

        /// <summary>
        /// 只显示"将会打印"的页：页序直接取打印那份算法（<see cref="PrintPlan.PageSequence"/>），
        /// 所以范围/奇偶/小册子在预览里同步生效；范围一个有效页码都没有时引擎按既有口径回退打全部，这里也照全部显示。
        /// </summary>
        internal static List<int> PagesToShow(int total, string rangeText, PrintOptions opt)
        {
            var list = new List<int>();
            if (total <= 0) return list;
            int[] seq = null;
            try { seq = PrintPlan.PageSequence(total, rangeText, null, opt); } catch { seq = null; }
            if (seq == null || seq.Length == 0)
            {
                for (int i = 0; i < total; i++) list.Add(i);
                return list;
            }
            foreach (int i in seq) if (i >= 0 && i < total && !list.Contains(i)) list.Add(i);
            list.Sort();
            return list;
        }

        /// <summary>信息条（类型与转换状态／纸张／缩放／范围／黑白彩色份数）。</summary>
        private string BuildChips()
        {
            return BuildChipsText(_row, _eff, _rangeText, _copies, _pages.Count, _fromCache, _pdf == null, L);
        }

        /// <summary>信息条文本（纯函数，自检逐字段核对）。</summary>
        internal static string BuildChipsText(RowVm row, PrintOptions opt, string rangeText, int copies, int shownPages, bool fromCache, bool loading, Func<string, string> res)
        {
            string kind = FileClassifier.KindText(row.Job.Kind);
            string typeText = row.Job.Kind == DocKind.Pdf
                ? res("s.PvKindPdfFmt").Replace("{0}", kind)
                : res("s.PvKindOfficeFmt").Replace("{0}", kind).Replace("{1}",
                    loading ? res("s.PvStateConverting") : (fromCache ? res("s.PvStateCached") : res("s.PvStateNone")));
            string paper = (string.IsNullOrWhiteSpace(opt.PaperDisplayName) ? "A4" : opt.PaperDisplayName) + " "
                + (opt.Orientation == OrientationMode.Landscape ? res("s.PvLandscape")
                   : opt.Orientation == OrientationMode.Portrait ? res("s.PvPortrait") : res("s.PvOrientAuto"));
            string scale = opt.EffectiveScale == ScaleMode.FitPage ? res("s.PvScaleFit")
                : opt.EffectiveScale == ScaleMode.None ? res("s.PvScaleNone")
                : opt.EffectiveScale == ScaleMode.ShrinkOnly ? res("s.PvScaleShrink")
                : opt.EffectiveScale == ScaleMode.StretchFill ? res("s.PvScaleStretch")
                : res("s.PvScalePercentFmt").Replace("{0}", opt.ScalePercent.ToString());
            string range = string.IsNullOrWhiteSpace(rangeText)
                ? res("s.PvRangeAllFmt").Replace("{0}", shownPages.ToString())
                : res("s.PvRangePickedFmt").Replace("{0}", shownPages.ToString());
            string output = (opt.Color == ColorMode.BlackWhite ? res("s.PvColorBw") : res("s.PvColorColor"))
                + " · " + (opt.Duplex == DuplexMode.LongEdge ? res("s.PvDuplexLong")
                    : opt.Duplex == DuplexMode.ShortEdge ? res("s.PvDuplexShort") : res("s.PvDuplexSimplex"))
                + " · " + res("s.PvCopiesFmt").Replace("{0}", Math.Max(1, copies).ToString());
            return res("s.PvLabelType") + " " + typeText + "　|　" + res("s.PvLabelPaper") + " " + paper
                + "　|　" + res("s.PvLabelScale") + " " + scale + "　|　" + res("s.PvLabelRange") + " " + range
                + "　|　" + res("s.PvLabelOutput") + " " + output;
        }

        private void OnQueueClicked()
        {
            // 拍板口径：这颗按钮＝确保该行被勾选 + 一行日志 + 关窗；真正发送仍由主页「开始打印」掌握
            _row.Checked = true;
            if (_owner != null) _owner.PreviewQueued(_row.Job.Name);
            Close();
        }

        private void SetBusy(bool busy, string hint)
        {
            _busy = busy;
            if (_queueBtn != null) _queueBtn.IsEnabled = !busy;
            if (_hint != null)
            {
                _hint.Text = hint ?? "";
                _hint.Visibility = string.IsNullOrEmpty(hint) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        /// <summary>
        /// 自检/渲染用：同步装载一份已经备好的 PDF 与体检结论（不走后台转换，也不依赖本机装了哪家 Office）。
        /// 走的是与真实加载同一段呈现代码（信息条/警告条/缩略图/叠加层/大页）。
        /// </summary>
        internal void LoadForTest(string pdf, OverflowScan scan, SourceLayoutFinding src)
        {
            _pdf = pdf;
            _scan = scan;
            _src = src;
            _fromCache = src != null && src.Checked;
            int total = PdfRaster.PageCount(pdf);
            _totalPages = total;
            _pages = PagesToShow(total, _rangeText, _eff);
            _chips.Text = BuildChips();
            ApplyWarning();
            BuildThumbs();
            SetBusy(false, _pages.Count == 0 ? L("s.PvNoPages") : "");
            if (_pages.Count > 0) ShowPage(0);
        }

        /// <summary>自检用：直接触发"加入打印队列"那段方法体（不合成鼠标事件）。</summary>
        internal void QueueForTest() { OnQueueClicked(); }
        /// <summary>自检用：直接切页。</summary>
        internal void ShowPageForTest(int i) { ShowPage(i); }

        private static BitmapSource ToSource(PageRaster r)
        {
            var src = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, r.Pixels, r.Stride);
            src.Freeze();
            return src;
        }

        private static Brush Brush(Application app, string key, Brush fallback)
        {
            try { return app != null ? (app.TryFindResource(key) as Brush) ?? fallback : fallback; }
            catch { return fallback; }
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
