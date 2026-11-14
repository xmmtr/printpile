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

namespace BatchPrintCN
{
    /// <summary>
    /// v1.2.2 修（2026-11-08 拍板）：页码范围文本非空、却一个有效页码都解析不出（全部超界/纯乱写）时由
    /// <see cref="PrintPlan.PageSequence"/> 抛出，取代旧的"静默回退打印全部页"危险默认——用户笔误一次不该等于整本打印。
    /// 上层无需专门接：BatchPrinter.Run 的逐文件 catch(Exception) 会按既有失败记账（记"失败"行、写日志、继续打后续文件）。
    /// </summary>
    public sealed class RangeInvalidException : Exception
    {
        /// <summary>解析失败的页码范围原文（仅用于失败日志与定位，不参与任何解析）。</summary>
        public string RangeText { get; }

        public RangeInvalidException(string rangeText)
            : base("页码范围无效：" + rangeText)
        {
            RangeText = rangeText;
        }
    }
}
