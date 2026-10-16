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

namespace BatchPrintCN
{
    /// <summary>
    /// F1 引擎自动检测：探测本机是否安装 WPS Office / Microsoft Office（COM 引擎可用性）。
    /// 结果缓存（内存级），可手动重新探测。
    /// </summary>
    public static class EngineDetector
    {
        private static readonly string[] ComIds =
        {
            "Kwps.Application", "Word.Application", "Ket.Application",
            "Excel.Application", "Kwpp.Application", "PowerPoint.Application"
        };

        private static bool _checked;
        private static bool _hasCom;
        private static string _detectedId = "";

        /// <summary>是否已检测到本机 COM 引擎（WPS 或 Office）。</summary>
        public static bool HasCom { get { return _hasCom; } }
        public static string DetectedId { get { return _detectedId; } }
        public static bool Checked { get { return _checked; } }

        /// <summary>探测本机 COM 引擎（WPS/Office 任一存在即视为可用）。</summary>
        public static void Detect()
        {
            _hasCom = false;
            _detectedId = "";
            try
            {
                foreach (string id in ComIds)
                {
                    if (Type.GetTypeFromProgID(id) != null)
                    {
                        _hasCom = true;
                        _detectedId = id;
                        break;
                    }
                }
            }
            catch { }
            _checked = true;
        }

        /// <summary>探测结果展示文本：已检测到 WPS Office / Microsoft Office / 未检测到 Office/WPS。</summary>
        public static string Describe()
        {
            if (!_checked) return "";
            if (!_hasCom) return "未检测到 Office/WPS";
            if (_detectedId.StartsWith("Kw", StringComparison.OrdinalIgnoreCase)) return "已检测到：WPS Office（将用 COM）";
            return "已检测到：Microsoft Office（将用 COM）";
        }

        public static List<string> DetectedIds()
        {
            var list = new List<string>();
            foreach (string id in ComIds)
                try { if (Type.GetTypeFromProgID(id) != null) list.Add(id); } catch { }
            return list;
        }
    }
}
