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
using System.Collections.Generic;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整（打印范围反馈）·修2/3/5/6：页码范围的结构化结论。
    /// 界面上色只认这个枚举、绝不认文案（修整16 的 G2 口径：改文案或切英文都不能让上色失效）。
    /// Unknown 两态＝总页数还未知（Office 文件要等打印时转换才出 PDF），此时只能判"写法能否解析出页码"，
    /// 打印完成回调把总页数回填后再升级成 Ok/Partial/Invalid。
    /// </summary>
    public enum RangeMark { Empty, Ok, Partial, Invalid, UnknownOk, UnknownBad }

    /// <summary>
    /// 一份解析结论喂三个呈现处（页数列 / 聚焦气泡 / 悬停提示）与状态列警告——
    /// 页码本身由 BatchPrinter.ParsedPages 给出，与打印引擎走的 ParseRange 同一段代码，
    /// 所以"屏上说的"与"实际打的"不会各算各的。纯计算、无界面依赖（自检可直接断言）。
    /// </summary>
    public sealed class RangeVerdict
    {
        public RangeMark Mark;
        public int Total;                 // 文档总页数（0 或负 = 未知）
        public List<int> Pages;           // 将实际参与打印的 1 基页码（升序去重）；总页数未知时＝解析出的全部页码
        public int Valid;                 // 有效页数
        public int Ignored;               // 超出总页数、被静默忽略的页码个数（修6 的文件日志）
        public bool FullWidth;            // 原输入含全角数字/全角减号（修1 归一化后才认出来）

        public bool TotalKnown { get { return Total > 0; } }

        public static RangeVerdict Build(string rangeText, int total)
        {
            var v = new RangeVerdict { Total = total, Pages = new List<int>(), FullWidth = BatchPrinter.UsesFullWidthInput(rangeText) };
            if (string.IsNullOrWhiteSpace(rangeText))
            {
                v.Mark = RangeMark.Empty;
                v.Valid = total > 0 ? total : 0;
                return v;
            }
            List<int> parsed = BatchPrinter.ParsedPages(rangeText);
            if (total <= 0)
            {
                v.Mark = parsed.Count > 0 ? RangeMark.UnknownOk : RangeMark.UnknownBad;
                v.Pages = parsed;
                v.Valid = parsed.Count;
                return v;
            }
            foreach (int n in parsed) if (n <= total) v.Pages.Add(n);
            v.Valid = v.Pages.Count;
            v.Ignored = parsed.Count - v.Valid;
            v.Mark = v.Valid == 0 ? RangeMark.Invalid : (v.Ignored > 0 ? RangeMark.Partial : RangeMark.Ok);
            return v;
        }

        /// <summary>连续页码压成区间（3,4,5 → "3-5"），最多列 max 段，超出以省略号收尾。</summary>
        public static string PageList(List<int> pages, string sep, int max)
        {
            var runs = new List<string>();
            for (int i = 0; i < pages.Count; )
            {
                int j = i;
                while (j + 1 < pages.Count && pages[j + 1] == pages[j] + 1) j++;
                runs.Add(j == i ? pages[i].ToString() : pages[i] + "-" + pages[j]);
                i = j + 1;
            }
            if (runs.Count <= max) return string.Join(sep, runs);
            return string.Join(sep, runs.GetRange(0, max)) + sep + "…";
        }
    }
}
