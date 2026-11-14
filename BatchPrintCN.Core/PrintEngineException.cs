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
    /// v1.2.2 修（2026-11-08 拍板）：打印引擎自身走不下去（找不到打印机、渲染/打开 PDF 失败、
    /// 格式不支持、页码范围解析不出等）时抛出，取代原先的泛型 Exception——失败原因与用户取消
    /// （<see cref="PrintCancelledException"/>）、范围笔误（<see cref="RangeInvalidException"/>）在类型上分开。
    /// 上层无需专门接：BatchPrinter.Run 的逐文件 catch(Exception) 会按既有失败记账（记"失败"行、写日志、
    /// 继续打后续文件），消息原文不变，只是类型更准确。
    /// </summary>
    public sealed class PrintEngineException : Exception
    {
        public PrintEngineException(string message) : base(message) { }
    }
}
