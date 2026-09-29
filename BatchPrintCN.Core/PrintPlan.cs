using System;
using System.Collections.Generic;
using System.Linq;

namespace BatchPrintCN
{
    /// <summary>
    /// F6（V4.3）：打印页序计划——纯计算无副作用，供 pdfium 打印通道与自检断言共用。
    /// 顺序：页码范围（ParseRange）→ 小册子骑马订重排（开启时优先，忽略奇偶）→ 奇偶页过滤。
    /// 输出序列里 -1 = 补位空白页（小册子把总页数补到 4 的倍数时用），打印时跳过绘制。
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
                if (pages.Length == 0) pages = Enumerable.Range(0, totalPages).ToArray();
            }
            else
            {
                pages = Enumerable.Range(0, totalPages).ToArray();
            }

            if (opt == null) return pages;
            if (opt.Booklet) return BookletSequence(pages, totalPages);   // 小册子优先：忽略奇偶页
            if (opt.PageParity == PageParityMode.Odd)
                pages = pages.Where(i => (i + 1) % 2 == 1).ToArray();     // 原 PDF 页码（1 基）为奇数
            else if (opt.PageParity == PageParityMode.Even)
                pages = pages.Where(i => (i + 1) % 2 == 0).ToArray();
            return pages;
        }

        /// <summary>
        /// 小册子骑马订页序：总页数补空白到 4 的倍数（N），输出 N/2 个"对"（每对拼一页 2 版横排）。
        /// 需求方拍板公式：k=0 对 (N,1)，k≥1 对 (2k, N+1-2k)。
        /// 例：8 页 → (8,1)(2,7)(4,5)(6,3)（0 基序列 7,0,1,6,3,4,5,2）；页号 &gt; 总页数 = 空白补位。
        /// </summary>
        public static int[] BookletSequence(int[] basePages, int totalPages)
        {
            int N = (totalPages + 3) / 4 * 4;
            var src = new HashSet<int>(basePages.Where(i => i >= 0));
            var list = new List<int>(N);
            for (int k = 0; k < N / 2; k++)
            {
                int left = k == 0 ? N : 2 * k;
                int right = k == 0 ? 1 : N + 1 - 2 * k;
                list.Add(src.Contains(left - 1) ? left - 1 : -1);
                list.Add(src.Contains(right - 1) ? right - 1 : -1);
            }
            return list.ToArray();
        }

        /// <summary>输出纸张数：小册子 = 补位后页数的一半（2 版）；其余 = 页数向上取整除每页版数。</summary>
        public static int OutputPageCount(int totalPages, PrintOptions opt)
        {
            if (opt != null && opt.Booklet)
            {
                int N = (totalPages + 3) / 4 * 4;
                return N / 2;
            }
            int n = Math.Max(1, opt != null ? opt.PagesPerSheet : 1);
            return (totalPages + n - 1) / n;
        }
    }
}
