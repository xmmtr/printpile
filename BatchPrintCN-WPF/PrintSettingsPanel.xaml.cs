using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Drawing.Printing;

namespace BatchPrintCN
{
    /// <summary>
    /// U2-4（V4.2.5）：打印设置内容面板——承载原 PrintSettingsWindow 的全部控件与逻辑，
    /// 由 ContentDialog 宿主（ShowAsync）装入主窗口 RootContentDialog，不再弹独立窗口。
    /// 确定语义：Primary 结果时把控件值写回 PrintOptions（ApplyTo）；取消/Escape 不写回。
    /// 注：不 using Wpf.Ui.Controls（与 System.Windows.Controls 有 TextBlock/MessageBox 等同名类型），所需类型全限定。
    /// </summary>
    public partial class PrintSettingsPanel : UserControl
    {
        private readonly PrintOptions _opt;
        private readonly string _printerName;   // F6：当前打印机（枚举 PaperSources 用，空 = 只显示"自动"）
        private static readonly string[] PaperNames = { "A4", "A3", "A5", "B5", "Letter", "Legal" };
        private static readonly PaperKind[] PaperKinds = { PaperKind.A4, PaperKind.A3, PaperKind.A5, PaperKind.B5, PaperKind.Letter, PaperKind.Legal };
        private static readonly int[] PagesValues = { 1, 2, 4, 6, 9, 16 };   // F6：扩到 9（3×3）/ 16（4×4）

        // V4.2 修整二：打印模板移入本弹窗（模板属于打印设置）
        private List<KeyValuePair<string, PrintOptions>> _templates = new List<KeyValuePair<string, PrintOptions>>();
        private bool _ready;                   // 构造期控件赋值不算"用户手动改动"
        private bool _applying;                // 套用模板期间的程序化赋值不回调重置
        private bool _suppressTemplateEvent;   // 代码清空/设置下拉时不触发 OnTemplateSelected

        /// <summary>用户点「确定」时触发（宿主负责 Hide(Primary) 并写回参数）。</summary>
        public event Action ConfirmRequested;
        /// <summary>用户点「取消」时触发（宿主负责 Hide(None)）。</summary>
        public event Action CancelRequested;

        /// <summary>本次弹窗里最后套用的模板名；用户随后手动改过任何控件则为 null（供主页规划窗显示）。</summary>
        public string AppliedTemplateName { get; private set; }

        public PrintSettingsPanel(PrintOptions options, string printerName)
        {
            InitializeComponent();
            _opt = options ?? new PrintOptions();
            _printerName = printerName;
            foreach (string n in PaperNames) PaperBox.Items.Add(n);
            foreach (int v in PagesValues) PagesBox.Items.Add(v == 1 ? L("s.PsPagesOne") : L("s.PsPagesN").Replace("{0}", v.ToString()));   // V4.2.3 B1-a：版数下拉文案资源化
            InitParityScaleAndSourceCombos();   // F6：奇偶页 / 缩放模式 / 纸盒
            LoadState();
            UpdatePreview();
            RefreshTemplateList(null);
            EffectiveText.Text = L("s.PsEffective") + _opt.Summary();
            _ready = true;
        }

        /// <summary>F6：奇偶页/缩放模式下拉项 + 纸盒下拉（自动 + 当前打印机 PaperSources）。</summary>
        private void InitParityScaleAndSourceCombos()
        {
            ParityBox.Items.Add(L("s.PsParityAll"));
            ParityBox.Items.Add(L("s.PsParityOdd"));
            ParityBox.Items.Add(L("s.PsParityEven"));
            ScaleModeBox.Items.Add(L("s.PsScaleFit"));
            ScaleModeBox.Items.Add(L("s.PsScaleNone"));
            ScaleModeBox.Items.Add(L("s.PsScaleShrink"));
            ScaleModeBox.Items.Add(L("s.PsScaleStretch"));
            ScaleModeBox.Items.Add(L("s.PsScaleCustom"));
            PaperSourceBox.Items.Add(L("s.PsPaperSourceAuto"));
            foreach (string name in PaperSourceNames(_printerName)) PaperSourceBox.Items.Add(name);
        }

        private static string _psPrinter;                    // B-5（V4.4 修整）：纸盒清单按打印机名缓存
        private static List<string> _psCache;
        internal static int PaperSourceEnumCalls;            // B-5 断言用：真实枚举（new PrinterSettings）次数

        /// <summary>F5（V4.4）：纸盒名清单抽成公用（打印设置弹窗与逐文件参数弹窗同口径）；打印机为空或取不到 → 空清单。
        /// 修整1/B-5 起按打印机名缓存：同一台打印机反复取清单不再枚举，换打印机才重取。</summary>
        internal static List<string> PaperSourceNames(string printerName)
        {
            string key = printerName ?? "";
            if (_psCache != null && string.Equals(_psPrinter, key, StringComparison.Ordinal)) return _psCache;
            PaperSourceEnumCalls++;
            var list = new List<string>();
            try
            {
                if (!string.IsNullOrWhiteSpace(printerName))
                {
                    var ps = new System.Drawing.Printing.PrinterSettings { PrinterName = printerName };
                    foreach (System.Drawing.Printing.PaperSource s in ps.PaperSources) list.Add(s.SourceName);
                }
            }
            catch { }
            _psPrinter = key;
            _psCache = list;
            return list;
        }

        private void LoadState()
        {
            SimplexRb.IsChecked = _opt.Duplex == DuplexMode.Simplex;
            LongEdgeRb.IsChecked = _opt.Duplex == DuplexMode.LongEdge;
            ShortEdgeRb.IsChecked = _opt.Duplex == DuplexMode.ShortEdge;
            ColorRb.IsChecked = _opt.Color == ColorMode.Color;
            BlackWhiteRb.IsChecked = _opt.Color == ColorMode.BlackWhite;
            AutoOrientRb.IsChecked = _opt.Orientation == OrientationMode.Auto;
            PortraitRb.IsChecked = _opt.Orientation == OrientationMode.Portrait;
            LandscapeRb.IsChecked = _opt.Orientation == OrientationMode.Landscape;
            int pi = Array.IndexOf(PaperKinds, _opt.Paper);
            PaperBox.SelectedIndex = pi >= 0 ? pi : 0;
            int pidx = Array.IndexOf(PagesValues, _opt.PagesPerSheet);
            PagesBox.SelectedIndex = pidx >= 0 ? pidx : 0;
            // F6：奇偶页 / 缩放模式 / 小册子 / 纸盒
            ParityBox.SelectedIndex = (int)_opt.PageParity;
            ScaleModeBox.SelectedIndex = (int)_opt.EffectiveScale;
            ScaleBox.IsEnabled = _opt.EffectiveScale == ScaleMode.Custom;
            ScaleBox.Value = _opt.ScalePercent;
            BookletBox.IsChecked = _opt.Booklet;
            ParityBox.IsEnabled = !_opt.Booklet;    // 小册子优先：置灰
            PagesBox.IsEnabled = !_opt.Booklet;
            SelectPaperSource(_opt.PaperSourceName);
        }

        /// <summary>F6：纸盒下拉按名称选择；不存在（模板换打印机）则动态加入保持可见以便再次保存。</summary>
        private void SelectPaperSource(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(sourceName)) { PaperSourceBox.SelectedIndex = 0; return; }
            int idx = PaperSourceBox.Items.IndexOf(sourceName);
            if (idx < 0) { PaperSourceBox.Items.Add(sourceName); idx = PaperSourceBox.Items.IndexOf(sourceName); }
            PaperSourceBox.SelectedIndex = idx;
        }

        private DuplexMode Duplex
        {
            get { return LongEdgeRb.IsChecked == true ? DuplexMode.LongEdge : ShortEdgeRb.IsChecked == true ? DuplexMode.ShortEdge : DuplexMode.Simplex; }
        }
        private ColorMode Color
        {
            get { return BlackWhiteRb.IsChecked == true ? ColorMode.BlackWhite : ColorMode.Color; }
        }
        private OrientationMode Orientation
        {
            get { return LandscapeRb.IsChecked == true ? OrientationMode.Landscape : PortraitRb.IsChecked == true ? OrientationMode.Portrait : OrientationMode.Auto; }
        }
        private int PaperIndex { get { return PaperBox.SelectedIndex < 0 ? 0 : PaperBox.SelectedIndex; } }
        private int PagesPerSheet { get { int i = PagesBox.SelectedIndex; return i >= 0 && i < PagesValues.Length ? PagesValues[i] : 1; } }
        private int ScalePercent
        {
            get
            {
                double? v = ScaleBox.Value;
                return v.HasValue ? Math.Max(1, Math.Min(400, (int)Math.Round(v.Value))) : 100;
            }
        }

        private string DuplexText() { return Duplex == DuplexMode.LongEdge ? "双面-长边" : Duplex == DuplexMode.ShortEdge ? "双面-短边" : "单面"; }
        private string ColorText() { return Color == ColorMode.BlackWhite ? "黑白" : "彩色"; }
        private string OrientationText() { return Orientation == OrientationMode.Portrait ? "纵向" : Orientation == OrientationMode.Landscape ? "横向" : "自动"; }

        public void UpdatePreview()
        {
            bool booklet = BookletBox.IsChecked == true;
            int pp = booklet ? 2 : PagesPerSheet;   // 小册子强制 2 版横排
            string scale = EffectiveScaleText();
            Preview.Update(Duplex, Orientation, Color, PaperNames[PaperIndex], PaperKinds[PaperIndex], scale, pp);
            string extra = (booklet ? " · 小册子" : "")
                + (ParityBox.SelectedIndex == 1 ? " · 仅奇数页" : ParityBox.SelectedIndex == 2 ? " · 仅偶数页" : "");
            SummaryText.Text = "示意 · " + OrientationText() + " · " + ColorText() + " · " + DuplexText() + " · " + PaperNames[PaperIndex]
                + " · " + scale + (pp > 1 ? " · 每页" + pp + "版" : "") + extra;
        }

        /// <summary>F6：缩放模式展示文案（与 Summary() 同口径）。</summary>
        private string EffectiveScaleText()
        {
            switch (ScaleModeBox.SelectedIndex)
            {
                case 1: return "不缩放";
                case 2: return "缩小适配";
                case 3: return "拉伸适配";
                case 4: return ScalePercent + "%";
                default: return "缩放到合适页面";
            }
        }

        private void OnAnyChecked(object sender, RoutedEventArgs e) { MarkManualEdit(); UpdatePreview(); }
        private void OnAnyChanged(object sender, SelectionChangedEventArgs e) { MarkManualEdit(); UpdatePreview(); }
        private void OnScaleTextChanged(object sender, TextChangedEventArgs e) { if (_ready && !_applying) { MarkManualEdit(); UpdatePreview(); } }

        /// <summary>F6：缩放模式切换 → 自定义百分比 NumberBox 仅在"自定义"时可填。</summary>
        private void OnScaleModeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_ready || ScaleModeBox == null) return;
            ScaleBox.IsEnabled = ScaleModeBox.SelectedIndex == 4;
            MarkManualEdit();
            UpdatePreview();
        }

        /// <summary>F6：小册子开关 → 每页版数/奇偶页置灰（小册子优先）。</summary>
        private void OnBookletChanged(object sender, RoutedEventArgs e)
        {
            if (!_ready || BookletBox == null) return;
            bool on = BookletBox.IsChecked == true;
            PagesBox.IsEnabled = !on;
            ParityBox.IsEnabled = !on;
            MarkManualEdit();
            UpdatePreview();
        }

        /// <summary>V4.2 修整二：用户手动改动任一设置控件 → 模板下拉回"未选择"，表示当前是改过的状态。</summary>
        private void MarkManualEdit()
        {
            if (!_ready || _applying || TemplateBox == null || TemplateBox.SelectedIndex < 0) return;
            AppliedTemplateName = null;
            SetTemplateSelection(-1);
        }

        // ===== V4.2 修整二：打印模板（SQLite，print_templates 表）=====

        /// <summary>V4.7 拆分2：模板上限 5 个。</summary>
        internal const int FreeMaxTemplates = 5;

        /// <summary>
        /// V4.7 拆分2：模板保存闸（纯函数，自检直取）。
        /// 未满 5 个 → 新增放行；同名覆盖是"编辑已有"、条目数不变 → 放行（哪怕库里已有 5 个）；
        /// 库里已超过 5 个（Pro 用户降级带来的旧库）→ 整体只读，新建与覆盖都拦下，但已有模板照常套用。
        /// </summary>
        internal static bool CanSaveTemplate(string name, List<string> existing)
        {
            if (existing == null) return true;
            if (existing.Count > FreeMaxTemplates) return false;
            if (existing.Contains(name)) return true;
            return existing.Count < FreeMaxTemplates;
        }

        private void RefreshTemplateList(string keepSelected)
        {
            try { _templates = SqliteStore.LoadTemplates()
                        .Select(t => new KeyValuePair<string, PrintOptions>(t.Name, t.Options))
                        .ToList(); }
            catch { _templates = new List<KeyValuePair<string, PrintOptions>>(); }

            _suppressTemplateEvent = true;
            try
            {
                TemplateBox.Items.Clear();
                foreach (var t in _templates) TemplateBox.Items.Add(t.Key);
                int idx = keepSelected == null ? -1 : IndexOfTemplate(keepSelected);
                TemplateBox.SelectedIndex = idx >= 0 ? idx : (AppliedTemplateName == null ? -1 : IndexOfTemplate(AppliedTemplateName));
            }
            finally { _suppressTemplateEvent = false; }
            UpdateTemplateHint();
        }

        private int IndexOfTemplate(string name)
        {
            for (int i = 0; i < _templates.Count; i++)
                if (string.Equals(_templates[i].Key, name, StringComparison.Ordinal)) return i;
            return -1;
        }

        private void SetTemplateSelection(int index)
        {
            _suppressTemplateEvent = true;
            try { TemplateBox.SelectedIndex = index; }
            finally { _suppressTemplateEvent = false; }
            UpdateTemplateHint();
        }

        private void UpdateTemplateHint()
        {
            if (TemplateHint == null || TemplateBox == null) return;
            TemplateHint.Visibility = TemplateBox.SelectedIndex >= 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnTemplateSelected(object sender, SelectionChangedEventArgs e)
        {
            UpdateTemplateHint();
            if (_suppressTemplateEvent || _applying) return;
            string name = TemplateBox.SelectedItem as string;
            if (string.IsNullOrEmpty(name)) { AppliedTemplateName = null; return; }
            var t = _templates.FirstOrDefault(x => x.Key == name);
            if (t.Value == null)
            {
                // V4.2.3 B1-c：旧版（V4.0~V4.2）存成 "{}" 的空模板，套用无意义 → 提示重存并退回未选
                System.Windows.MessageBox.Show(Window.GetWindow(this), L("s.MsgEmptyTemplate"), L("s.Hint"),
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                AppliedTemplateName = null;
                SetTemplateSelection(-1);
                return;
            }
            ApplyToControls(t.Value);
            AppliedTemplateName = name;
            FileLogger.Write(L("s.TemplateApplied") + name + "（" + Collect().Summary() + "）");
        }

        /// <summary>把模板参数套到弹窗控件（不直接写 _opt，确定时才生效），右侧预览同步刷新。</summary>
        private void ApplyToControls(PrintOptions src)
        {
            _applying = true;
            try
            {
                SimplexRb.IsChecked = src.Duplex == DuplexMode.Simplex;
                LongEdgeRb.IsChecked = src.Duplex == DuplexMode.LongEdge;
                ShortEdgeRb.IsChecked = src.Duplex == DuplexMode.ShortEdge;
                ColorRb.IsChecked = src.Color == ColorMode.Color;
                BlackWhiteRb.IsChecked = src.Color == ColorMode.BlackWhite;
                AutoOrientRb.IsChecked = src.Orientation == OrientationMode.Auto;
                PortraitRb.IsChecked = src.Orientation == OrientationMode.Portrait;
                LandscapeRb.IsChecked = src.Orientation == OrientationMode.Landscape;
                int pi = Array.IndexOf(PaperKinds, src.Paper);
                PaperBox.SelectedIndex = pi >= 0 ? pi : 0;
                int pidx = Array.IndexOf(PagesValues, src.PagesPerSheet);
                PagesBox.SelectedIndex = pidx >= 0 ? pidx : 0;
                ParityBox.SelectedIndex = (int)src.PageParity;
                SelectPaperSource(src.PaperSourceName);
                ScaleModeBox.SelectedIndex = (int)src.EffectiveScale;
                ScaleBox.IsEnabled = src.EffectiveScale == ScaleMode.Custom;
                ScaleBox.Value = Math.Max(1, Math.Min(400, src.ScalePercent));
                BookletBox.IsChecked = src.Booklet;
                ParityBox.IsEnabled = PagesBox.IsEnabled = !src.Booklet;
            }
            finally { _applying = false; }
            UpdatePreview();
        }

        private void OnSaveTemplate(object sender, RoutedEventArgs e)
        {
            string name = InputDialog.Ask(Window.GetWindow(this), L("s.SaveTemplate"), L("s.TemplatePrompt"), TemplateBox.SelectedItem as string);
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            // V4.7 拆分2：模板限 5 个（第 6 个新增被拦；已有模板不删、照常套用）
            if (!CanSaveTemplate(name, _templates.Select(t => t.Key).ToList()))
            {
                System.Windows.MessageBox.Show(Window.GetWindow(this), L("s.TemplateLimitHit"), L("s.Hint"),
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            SqliteStore.SaveTemplate(name, Collect());   // 以弹窗当前控件值保存（同名覆盖）
            AppliedTemplateName = name;
            RefreshTemplateList(name);
            FileLogger.Write(L("s.TemplateSaved") + name);
        }

        private void OnDeleteTemplate(object sender, RoutedEventArgs e)
        {
            string name = TemplateBox.SelectedItem as string;
            if (string.IsNullOrEmpty(name))
            {
                System.Windows.MessageBox.Show(Window.GetWindow(this), L("s.MsgNoTemplate"), L("s.Hint"),
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                return;
            }
            SqliteStore.DeleteTemplate(name);
            AppliedTemplateName = null;
            RefreshTemplateList(null);
            FileLogger.Write(L("s.TemplateDeleted") + name);
        }

        /// <summary>收集弹窗当前控件值（不改动 _opt）。</summary>
        private PrintOptions Collect()
        {
            var o = new PrintOptions();
            FillInto(o);
            return o;
        }

        private void FillInto(PrintOptions dst)
        {
            dst.Duplex = Duplex;
            dst.Color = Color;
            dst.Orientation = Orientation;
            dst.Paper = PaperKinds[PaperIndex];
            dst.PaperDisplayName = PaperNames[PaperIndex];
            // F6：缩放模式（FitPage 时 FitToPage 保持 true；Custom 时按百分比）+ 奇偶/纸盒/小册子
            var mode = ScaleModeBox.SelectedIndex == 1 ? ScaleMode.None
                : ScaleModeBox.SelectedIndex == 2 ? ScaleMode.ShrinkOnly
                : ScaleModeBox.SelectedIndex == 3 ? ScaleMode.StretchFill
                : ScaleModeBox.SelectedIndex == 4 ? ScaleMode.Custom : ScaleMode.FitPage;
            dst.Scale = mode;
            dst.FitToPage = mode == ScaleMode.FitPage;
            dst.ScalePercent = ScalePercent;
            dst.PagesPerSheet = PagesPerSheet;
            dst.PageParity = ParityBox.SelectedIndex == 1 ? PageParityMode.Odd
                : ParityBox.SelectedIndex == 2 ? PageParityMode.Even : PageParityMode.All;
            dst.PaperSourceName = PaperSourceBox.SelectedIndex > 0 ? (PaperSourceBox.SelectedItem as string) : null;
            dst.Booklet = BookletBox.IsChecked == true;
        }

        /// <summary>U2-4：确定（Primary）时把控件值写回调用方的 PrintOptions。</summary>
        public void ApplyTo(PrintOptions dst) { FillInto(dst); }

        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        private void OnCancel(object sender, RoutedEventArgs e) { CancelRequested?.Invoke(); }
        private void OnOk(object sender, RoutedEventArgs e) { ConfirmRequested?.Invoke(); }

        // ===== U2-4：ContentDialog 宿主 =====

        /// <summary>取主窗口里的 ContentDialog 宿主（RootContentDialog）。</summary>
        internal static Wpf.Ui.Controls.ContentDialogHost ResolveHost()
        {
            try { return (Application.Current?.MainWindow as Window)?.FindName("RootContentDialog") as Wpf.Ui.Controls.ContentDialogHost; }
            catch { return null; }
        }

        /// <summary>
        /// 以 ContentDialog 呈现打印设置。返回（是否确定生效, 最近套用模板名）。
        /// Primary=确定（已把控件值写回 options）；None（取消/Esc/关闭）=放弃改动。
        /// F6（V4.3）：printerName = 当前所选打印机（枚举纸盒用，可空）。
        /// </summary>
        public static async Task<(bool Applied, string TemplateName)> ShowAsync(PrintOptions options, string printerName)
        {
            var host = ResolveHost();
            var panel = new PrintSettingsPanel(options, printerName);
            if (host == null) return (false, null);   // 无宿主（理论不发生）：按取消处理，不丢配置

            // 实测（2026-09-19 真机）：ContentDialog 的内容区 = DialogHeight − 标题区（约 104 DIP），
            // 但内容排版却按整个 DialogHeight 给约束 → 底部（页脚）被裁。给面板显式高度补偿标题区，
            // 星号行自适应收缩，页脚落回可视区。若换 WPF-UI 版本后标题区高度变化，只需调这里的 110。
            panel.Height = 690 - 110;

            var dlg = new Wpf.Ui.Controls.ContentDialog(host)
            {
                Title = L("s.PrintSetup"),
                Content = panel,
                DialogWidth = 1020,
                DialogHeight = 690,
                DialogMargin = new Thickness(0),
                IsFooterVisible = false   // 页脚（当前生效 + 确定/取消）在面板内自带，关掉库模板的全宽 Close 条
            };
            panel.ConfirmRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.Primary);
            panel.CancelRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.None);

            Wpf.Ui.Controls.ContentDialogResult result = await dlg.ShowAsync(CancellationToken.None);
            if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
            {
                panel.ApplyTo(options);
                return (true, panel.AppliedTemplateName);
            }
            return (false, null);
        }

        // ===== 自检用（同程序集 internal）=====
        internal PrintOptions CollectedForTest { get { return Collect(); } }
        internal string AppliedTemplateForTest { get { return AppliedTemplateName; } }
    }
}
