using System;
using System.Collections.Generic;
using System.Linq;

namespace BatchPrintCN
{
    /// <summary>
    /// F6（V4.3）：打印页序计划——纯计算无副作用，供 pdfium 打印通道与自检断言共用。
    /// 顺序：页码范围（ParseRange）→ 小册子骑马订重排（开启时优先，忽略奇偶）→ 奇偶页过滤。
    /// 输出序列里 -1 = 补位空白页（小册子把<b>选中页数</b>补到 4 的倍数时用；v1.2.2 修（2026-11-08 拍板）
    /// 前按全文总页数补），打印时跳过绘制。
    /// </summary>
    public static class PrintPlan
    {
        /// <summary>计算实际参与打印的 PDF 页序（0 基；-1=空白补位页）。</summary>
        public static int[] PageSequence(int totalPages, string rangeText, int[] pageIndexes, PrintOptions opt)
        {
            int[] pages;
            if (pageIndexes != null && pageIndexes.Length > 0)
                pages = pageIndexes.Where(i => i >= 0 && i < totalPages).Distinct().ToArray();
            else if (!string.IsNullOrWhiteSpace(rangeText))
            {
                pages = BatchPrinter.ParseRange(rangeText, totalPages);
                // v1.2.2 修（2026-11-08 拍板）：范围文本非空却解析不出任何有效页码（全部超界/纯乱写）＝用户笔误，
                // 不再静默回退打印全部页（笔误一次＝整本打印的危险默认），改抛 RangeInvalidException，
                // 由 BatchPrinter.Run 的逐文件失败记账接住（记"失败"行、写日志、继续打后续文件，不中断整批）。
                // 空白/null 文本仍＝全部页（本分支的 IsNullOrWhiteSpace 判定保留，不误伤"不填范围"的正常路径）；
                // pageIndexes 显式页序路径不受影响（上方分支自带越界滤除）。
                if (pages.Length == 0) throw new RangeInvalidException(rangeText);
            }
            else
            {
                pages = Enumerable.Range(0, totalPages).ToArray();
            }

            if (opt == null) return pages;
            if (opt.Booklet) return BookletSequence(pages);   // 小册子优先：忽略奇偶页
            if (opt.PageParity == PageParityMode.Odd)
                pages = pages.Where(i => (i + 1) % 2 == 1).ToArray();     // 原 PDF 页码（1 基）为奇数
            else if (opt.PageParity == PageParityMode.Even)
                pages = pages.Where(i => (i + 1) % 2 == 0).ToArray();
            return pages;
        }

        /// <summary>
        /// 小册子骑马订页序。v1.2.2 修（2026-11-08 拍板）：改按<b>选中页集</b>铺位——把选中的页升序去重后
        /// 视作"这本小册子自己的 1..m 页"，补空白到 4 的倍数（M），再套骑马订公式；不再按全文总页数 N 铺
        ///（旧行为：8 页文档范围只选 1 页也铺出 8 个位置＝4 面、3 面全白）。全选时与旧输出逐字一致。
        /// 需求方拍板公式：k=0 对 (M,1)，k≥1 对 (2k, M+1-2k)；每个位置放"小册子第 p 页"＝选中集里第 p 小的一页，
        /// p &gt; m ＝ 空白补位（-1，打印时跳过绘制）。
        /// 例：8 页全选 → (8,1)(2,7)(4,5)(6,3)（0 基 7,0,1,6,3,4,5,2）；只选第 1 页 → (—,1)(—,—)＝2 面（双面 1 张纸）。
        /// </summary>
        public static int[] BookletSequence(int[] basePages)
        {
            int[] sel = basePages.Where(i => i >= 0).Distinct().OrderBy(i => i).ToArray();
            int m = sel.Length;
            if (m == 0) return new int[0];
            int N = (m + 3) / 4 * 4;
            var list = new List<int>(N);
            for (int k = 0; k < N / 2; k++)
            {
                int left = k == 0 ? N : 2 * k;
                int right = k == 0 ? 1 : N + 1 - 2 * k;
                list.Add(left <= m ? sel[left - 1] : -1);
                list.Add(right <= m ? sel[right - 1] : -1);
            }
            return list.ToArray();
        }

        /// <summary>输出面数：小册子 = 选中页数补 4 的倍数后的一半（每面 2 版；双面打印即物理"张"数再减半）；
        /// 其余 = 页数向上取整除每页版数。v1.2.2 修（2026-11-08 拍板）：入参语义从"全文总页数"改为
        /// <b>选中页数</b>（PageSequence 的输出长度；与 BookletSequence 的选中页集口径一致），booklet 分支按它补 4；
        /// 全选时数值与旧版相同，范围/奇偶筛选后则按筛选结果算。</summary>
        public static int OutputPageCount(int selectedPageCount, PrintOptions opt)
        {
            if (opt != null && opt.Booklet)
            {
                int N = (selectedPageCount + 3) / 4 * 4;
                return N / 2;
            }
            int n = Math.Max(1, opt != null ? opt.PagesPerSheet : 1);
            return (selectedPageCount + n - 1) / n;
        }
    }
}
