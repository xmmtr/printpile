using System;
using System.Collections.Generic;
using System.Drawing.Printing;

namespace BatchPrintCN
{
    /// <summary>
    /// F5（V4.4）：逐文件参数覆盖——每个字段可空，null = 跟随全局打印设置。
    /// 两个约定：
    /// · PaperSourceOverride：null / 空串 = 跟随全局（即打印机默认纸盒，B-3 起不再有"显式自动"这一档）；非空 = 指定纸盒名。
    /// · PageRange：null/空白=跟随该行「页码范围」列（份数同理由 Copies 兜到该行「份数」列）。
    ///   V4.4 修整起，份数与页码范围的唯一录入入口是列表两列（B-2），逐文件弹窗不再提供这两项。
    /// 只承载单文件的差异化参数，不参与打印模板（模板=全局整套参数）。
    /// </summary>
    public sealed class PerFileOverride
    {
        public DuplexMode? Duplex;
        public ColorMode? Color;
        public PaperKind? Paper;
        public string PaperDisplayName;      // 与 Paper 同步写入（显示名，供 Summary 用）
        public ScaleMode? Scale;
        public int? ScalePercent;
        public int? PagesPerSheet;
        public bool? Booklet;
        public PageParityMode? PageParity;
        public string PaperSourceOverride;
        public int? Copies;
        public string PageRange;

        /// <summary>是否改过任何一项（全空 = 跟随全局，行上不留对象）。</summary>
        public bool HasAny
        {
            get
            {
                return Duplex.HasValue || Color.HasValue || Paper.HasValue || Scale.HasValue
                    || ScalePercent.HasValue || PagesPerSheet.HasValue || Booklet.HasValue
                    || PageParity.HasValue || !string.IsNullOrEmpty(PaperSourceOverride)
                    || Copies.HasValue || !string.IsNullOrWhiteSpace(PageRange);
            }
        }

        /// <summary>改过的字段显示名（按界面顺序），供任务规划窗标注「自定义」。</summary>
        public List<string> CustomFields(Func<string, string> localize)
        {
            var list = new List<string>();
            if (Duplex.HasValue) list.Add(localize("s.PsDuplex"));
            if (Color.HasValue) list.Add(localize("s.PsColorMode"));
            if (Paper.HasValue) list.Add(localize("s.PsPaper"));
            if (Scale.HasValue || ScalePercent.HasValue) list.Add(localize("s.PsScaleMode"));
            if (PagesPerSheet.HasValue) list.Add(localize("s.PsPagesPerSheet"));
            if (Booklet.HasValue) list.Add(localize("s.PsBooklet"));
            if (PageParity.HasValue) list.Add(localize("s.PsParity"));
            if (!string.IsNullOrEmpty(PaperSourceOverride)) list.Add(localize("s.PsPaperSource"));
            if (Copies.HasValue) list.Add(localize("s.ColCopies"));
            if (!string.IsNullOrWhiteSpace(PageRange)) list.Add(localize("s.ColRange"));
            return list;
        }
    }

    /// <summary>
    /// F5（V4.4）：把「全局打印设置 + 该文件覆盖」解析成这一份文件真正生效的参数。
    /// 无覆盖（null 或全空）时**直接返回全局实例本身**（引用相同、不克隆），
    /// 保证零覆盖场景与 V4.3 逐字节一致——A/B 对照断言依赖这一点。
    /// </summary>
    public static class EffectiveOptions
    {
        public static PrintOptions Resolve(PrintOptions global, PerFileOverride ov)
        {
            if (global == null) global = new PrintOptions();
            if (ov == null || !ov.HasAny) return global;

            var e = global.Clone();
            if (ov.Duplex.HasValue) e.Duplex = ov.Duplex.Value;
            if (ov.Color.HasValue) e.Color = ov.Color.Value;
            if (ov.Paper.HasValue)
            {
                e.Paper = ov.Paper.Value;
                e.PaperDisplayName = string.IsNullOrWhiteSpace(ov.PaperDisplayName) ? ov.Paper.Value.ToString() : ov.PaperDisplayName;
            }
            if (ov.Scale.HasValue)
            {
                e.Scale = ov.Scale;
                e.FitToPage = ov.Scale.Value == ScaleMode.FitPage;   // 与全局弹窗同一口径
            }
            if (ov.ScalePercent.HasValue) e.ScalePercent = ov.ScalePercent.Value;
            if (ov.PagesPerSheet.HasValue) e.PagesPerSheet = ov.PagesPerSheet.Value;
            if (ov.Booklet.HasValue) e.Booklet = ov.Booklet.Value;
            if (ov.PageParity.HasValue) e.PageParity = ov.PageParity.Value;
            // B-3（V4.4 修整）：只有具体纸盒名才算覆盖；null 与空串（历史遗留的"显式自动"）都等价于跟随全局=打印机默认纸盒
            if (!string.IsNullOrEmpty(ov.PaperSourceOverride)) e.PaperSourceName = ov.PaperSourceOverride;
            return e;
        }

        /// <summary>生效份数：覆盖值优先，否则用该行「份数」列的值。</summary>
        public static int Copies(PerFileOverride ov, int columnValue)
        {
            if (ov != null && ov.Copies.HasValue && ov.Copies.Value >= 1) return ov.Copies.Value;
            return columnValue;
        }

        /// <summary>生效页码范围：覆盖值优先，否则用该行「页码范围」列的值。</summary>
        public static string RangeText(PerFileOverride ov, string columnText)
        {
            if (ov != null && !string.IsNullOrWhiteSpace(ov.PageRange)) return ov.PageRange;
            return columnText;
        }
    }
}
