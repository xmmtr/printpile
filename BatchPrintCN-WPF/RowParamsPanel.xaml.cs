using System;
using System.Drawing.Printing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    /// <summary>
    /// 修整1（V4.4）：逐文件参数弹窗面板——主列表「修改」按钮打开，ContentDialog 承载（单实例按需打开，B-5）。
    /// 打开时把该行覆盖回填成"草稿"，编辑只改草稿；点「保存」才写回 RowVm.Override，取消/Esc 不写回。
    /// 收口：B-2（不含份数/页码，列表列为唯一入口）、B-3（纸盒无「自动」中间档）、B-6（缩放百分比只在模式=自定义时可填，
    /// 其他情况清空不留孤立值）、A-2（置灰在每次打开时按当时全局求值）、A-3（文案全部 XAML 声明式 DynamicResource）。
    /// 注：不 using Wpf.Ui.Controls（与 System.Windows.Controls 有 TextBlock 等同名类型），所需类型全限定。
    /// </summary>
    public partial class RowParamsPanel : UserControl
    {
        private static readonly string[] PaperNames = { "A4", "A3", "A5", "B5", "Letter", "Legal" };
        private static readonly PaperKind[] PaperKinds = { PaperKind.A4, PaperKind.A3, PaperKind.A5, PaperKind.B5, PaperKind.Letter, PaperKind.Legal };
        private static readonly int[] PagesValues = { 1, 2, 4, 6, 9, 16 };
        private static readonly PrintOptions FallbackGlobal = new PrintOptions();

        private readonly MainWindow _owner;
        private readonly RowVm _row;
        private bool _loading;

        /// <summary>「保存」已写回覆盖（宿主据此决定对话框结果）。</summary>
        public bool Saved { get; private set; }

        public event Action ConfirmRequested;
        public event Action CancelRequested;

        public RowParamsPanel(MainWindow owner, RowVm row)
        {
            InitializeComponent();
            _owner = owner;
            _row = row;
            Bind();
        }

        private PrintOptions Global
        {
            get { var h = _owner; return h != null ? h.ActiveOptions : FallbackGlobal; }
        }

        private string Printer
        {
            get { var h = _owner; return h != null ? h.ActivePrinterName : ""; }
        }

        /// <summary>打开时回填：该行覆盖 → 控件（_loading 期间控件事件不回写，防自激）。</summary>
        private void Bind()
        {
            _loading = true;
            try
            {
                FillDynamicItems();
                var o = _row != null ? _row.Override : null;
                RdDuplexBox.SelectedIndex = o != null && o.Duplex.HasValue ? (int)o.Duplex.Value + 1 : 0;
                RdColorBox.SelectedIndex = o != null && o.Color.HasValue ? (int)o.Color.Value + 1 : 0;
                RdPaperBox.SelectedIndex = IndexOf(o != null ? (PaperKind?)o.Paper : null, PaperKinds) + 1;
                RdScaleBox.SelectedIndex = o != null && o.Scale.HasValue ? (int)o.Scale.Value + 1 : 0;
                RdScalePctBox.Value = o != null && o.ScalePercent.HasValue ? o.ScalePercent.Value : (double?)null;
                RdPagesBox.SelectedIndex = IndexOf(o != null ? (int?)o.PagesPerSheet : null, PagesValues) + 1;
                RdBookletBox.SelectedIndex = o == null || !o.Booklet.HasValue ? 0 : (o.Booklet.Value ? 1 : 2);
                RdParityBox.SelectedIndex = o != null && o.PageParity.HasValue ? (int)o.PageParity.Value + 1 : 0;
                RdSourceBox.SelectedIndex = SourceIndex(o == null ? null : o.PaperSourceOverride);
                RefreshEnable();
            }
            finally { _loading = false; }
        }

        /// <summary>纸张 / 每页版数 / 纸盒三项清单：纸张与版数是固定表（版数文案走资源键），纸盒按当前打印机取（已缓存，B-5）。</summary>
        private void FillDynamicItems()
        {
            RdPaperBox.Items.Clear();
            RdPaperBox.Items.Add(L("s.FollowGlobal"));
            foreach (string n in PaperNames) RdPaperBox.Items.Add(n);

            RdPagesBox.Items.Clear();
            RdPagesBox.Items.Add(L("s.FollowGlobal"));
            foreach (int v in PagesValues) RdPagesBox.Items.Add(v == 1 ? L("s.PsPagesOne") : L("s.PsPagesN").Replace("{0}", v.ToString()));

            RdSourceBox.Items.Clear();
            RdSourceBox.Items.Add(L("s.FollowGlobal"));   // B-3：第一项就是"跟随全局=打印机默认纸盒"，不再有「自动」中间档
            foreach (string s in PrintSettingsPanel.PaperSourceNames(Printer)) RdSourceBox.Items.Add(s);
            string want = _row != null && _row.Override != null ? _row.Override.PaperSourceOverride : null;
            if (!string.IsNullOrEmpty(want) && RdSourceBox.Items.IndexOf(want) < 0) RdSourceBox.Items.Add(want);
        }

        private static int IndexOf<T>(T? value, T[] table) where T : struct
        {
            if (!value.HasValue) return -1;
            return Array.IndexOf(table, value.Value);
        }

        /// <summary>B-3：纸盒下拉 0=跟随全局（即自动），≥1=指定纸盒名；空串（历史遗留值）也当跟随。</summary>
        private int SourceIndex(string overrideValue)
        {
            if (string.IsNullOrEmpty(overrideValue)) return 0;
            int i = RdSourceBox.Items.IndexOf(overrideValue);
            return i >= 1 ? i : 0;
        }

        private static string SourceFrom(int selectedIndex, ComboBox box)
        {
            if (selectedIndex <= 0) return null;
            return box.Items[selectedIndex] as string;
        }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;     // 回填期间的控件事件不回算，防自激
            RefreshEnable();
        }

        /// <summary>草稿 → 覆盖对象（B-6：缩放模式不是"自定义百分比"时不带孤立百分比）。</summary>
        internal PerFileOverride Draft()
        {
            bool customPercent = RdScaleBox.SelectedIndex == (int)ScaleMode.Custom + 1;
            var o = new PerFileOverride
            {
                Duplex = RdDuplexBox.SelectedIndex >= 1 ? (DuplexMode)(RdDuplexBox.SelectedIndex - 1) : (DuplexMode?)null,
                Color = RdColorBox.SelectedIndex >= 1 ? (ColorMode)(RdColorBox.SelectedIndex - 1) : (ColorMode?)null,
                Paper = RdPaperBox.SelectedIndex >= 1 ? PaperKinds[RdPaperBox.SelectedIndex - 1] : (PaperKind?)null,
                PaperDisplayName = RdPaperBox.SelectedIndex >= 1 ? PaperNames[RdPaperBox.SelectedIndex - 1] : null,
                Scale = RdScaleBox.SelectedIndex >= 1 ? (ScaleMode)(RdScaleBox.SelectedIndex - 1) : (ScaleMode?)null,
                ScalePercent = customPercent ? PercentOf(RdScalePctBox.Value) : (int?)null,
                PagesPerSheet = RdPagesBox.SelectedIndex >= 1 ? PagesValues[RdPagesBox.SelectedIndex - 1] : (int?)null,
                Booklet = RdBookletBox.SelectedIndex == 1 ? true : RdBookletBox.SelectedIndex == 2 ? false : (bool?)null,
                PageParity = RdParityBox.SelectedIndex >= 1 ? (PageParityMode)(RdParityBox.SelectedIndex - 1) : (PageParityMode?)null,
                PaperSourceOverride = SourceFrom(RdSourceBox.SelectedIndex, RdSourceBox)
            };
            return o;
        }

        private static int? PercentOf(double? v)
        {
            if (!v.HasValue) return null;
            return Math.Max(1, Math.Min(400, (int)Math.Round(v.Value)));
        }

        /// <summary>联动可用性：小册子开启（本行选"开"，或跟随且全局已开）时版数/奇偶置灰；
        /// 百分比只在本行显式选了「自定义百分比」时可填，否则禁用并清空（B-6 不留隐形值）。</summary>
        private void RefreshEnable()
        {
            var g = Global;
            bool booklet = RdBookletBox.SelectedIndex == 1 || (RdBookletBox.SelectedIndex == 0 && g.Booklet);
            RdPagesBox.IsEnabled = !booklet;
            RdParityBox.IsEnabled = !booklet;

            bool customScale = RdScaleBox.SelectedIndex == (int)ScaleMode.Custom + 1;
            RdScalePctBox.IsEnabled = customScale;
            if (!customScale)
            {
                if (RdScalePctBox.Value.HasValue)
                {
                    _loading = true;
                    RdScalePctBox.Value = null;
                    _loading = false;
                }
                RdScalePctHint.Text = L("s.FollowGlobal");
            }
            RdScalePctHint.Visibility = RdScalePctBox.Value.HasValue ? Visibility.Collapsed : Visibility.Visible;
            CustomMark.Visibility = Draft().HasAny ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>「重置为跟随全局」：只清草稿，仍要点保存才写回（与"取消不写回"同一套语义）。</summary>
        private void OnReset(object sender, RoutedEventArgs e)
        {
            _loading = true;
            RdDuplexBox.SelectedIndex = 0;
            RdColorBox.SelectedIndex = 0;
            RdPaperBox.SelectedIndex = 0;
            RdScaleBox.SelectedIndex = 0;
            RdScalePctBox.Value = null;
            RdPagesBox.SelectedIndex = 0;
            RdBookletBox.SelectedIndex = 0;
            RdParityBox.SelectedIndex = 0;
            RdSourceBox.SelectedIndex = 0;
            _loading = false;
            RefreshEnable();
        }

        /// <summary>保存：草稿写回该行（全空则丢弃覆盖对象，行上回到"跟随全局"）。</summary>
        internal void DoSave()
        {
            if (_row != null)
            {
                var o = Draft();
                _row.Override = o.HasAny ? o : null;
                _row.NotifyOverrideChanged();
            }
            Saved = true;
            if (ConfirmRequested != null) ConfirmRequested();
        }

        internal void DoCancel() { if (CancelRequested != null) CancelRequested(); }

        private void OnSave(object sender, RoutedEventArgs e) { DoSave(); }
        private void OnCancel(object sender, RoutedEventArgs e) { DoCancel(); }

        private static string L(string key)
        {
            try
            {
                var o = System.Windows.Application.Current != null ? System.Windows.Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        /// <summary>
        /// 以 ContentDialog 呈现该行的逐文件参数（修整1）。返回 true = 用户点了保存。
        /// 单实例按需打开：列表行不再各自实例化一张卡片（B-5①）。
        /// </summary>
        public static Task<bool> ShowAsync(MainWindow owner, RowVm row)
        {
            return ShowAsync(owner, row, CancellationToken.None);
        }

        public static async Task<bool> ShowAsync(MainWindow owner, RowVm row, CancellationToken token)
        {
            var host = PrintSettingsPanel.ResolveHost();
            if (host == null) return false;

            var panel = new RowParamsPanel(owner, row);
            // 内容区 = DialogHeight − 标题区（约 104 DIP），补偿口径同 PrintSettingsPanel
            panel.Height = 560 - 110;

            var dlg = new Wpf.Ui.Controls.ContentDialog(host)
            {
                Title = L("s.RowParamsTitle") + " · " + (row != null ? row.Name : ""),
                Content = panel,
                DialogWidth = 780,
                DialogHeight = 560,
                DialogMargin = new Thickness(0),
                IsFooterVisible = false   // 页脚（重置 / 取消 / 保存）在面板内自带，关掉库模板的全宽 Close 条
            };
            panel.ConfirmRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.Primary);
            panel.CancelRequested += () => dlg.Hide(Wpf.Ui.Controls.ContentDialogResult.None);

            Wpf.Ui.Controls.ContentDialogResult result = await dlg.ShowAsync(token);
            return result == Wpf.Ui.Controls.ContentDialogResult.Primary && panel.Saved;
        }

        // ===== 自检用（同程序集 internal）=====
        internal FrameworkElement BoxForTest(string name) { return FindName(name) as FrameworkElement; }
        internal Wpf.Ui.Controls.NumberBox NumForTest(string name) { return FindName(name) as Wpf.Ui.Controls.NumberBox; }
        internal RowVm RowForTest { get { return _row; } }
        internal void RefreshForTest() { RefreshEnable(); }
    }
}
