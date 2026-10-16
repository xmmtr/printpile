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
    /// <summary>V4.2.3 M4（G2 补强）：打印阶段（结构化，替代 UI 解析中文日志前缀）。</summary>
    public enum PrintStage { Started, Completed, Failed }

    /// <summary>
    /// 单个文件的结构化进度事件：UI 按 Stage 分支更新行状态/重试按钮/失败提示，
    /// 不再依赖 "正在打印：" / "完成：" / "失败：" 这类日志字符串（改文案即静默失效的隐患消除）。
    /// 日志文本仍由 BatchPrinter 照原样写入文件，供排障使用（V3.1 拍板：引擎日志保持中文）。
    /// </summary>
    public sealed class PrintProgress
    {
        public PrintStage Stage;
        public string FileName;     // 与 UI 行显示名一致（DisplayName 优先）
        public string Engine;       // 实际引擎（com / minipdf / com(自动) …；非 Office 文件为 "-"）
        public string Reason;       // 失败原因（仅 Failed 有值）
        public int Copies;
        public int Pages;           // 已打印页数（仅 Completed 有值）
    }
}
