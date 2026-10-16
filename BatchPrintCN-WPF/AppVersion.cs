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
    /// 统一版本号来源：所有界面显示版本号的地方都从这里取，
    /// 避免左下角 / 关于页 / 标题栏等各处硬编码不一致。
    /// 规则：显示「主.次.修订」三段（如 4.9.3）；修订与构建同为 0 时——商用线 V1.0.0（51 号·决策 D-13）改判为
    /// **保底三段**（1.0.0.0 → v1.0.0，需求方指定口径 V1.0.0；原为两段 vX.Y）。
    /// </summary>
    public static class AppVersion
    {
        public static string Display
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    if (v == null) return "";
                    if (v.Build <= 0 && v.Revision <= 0)
                        return "v" + v.Major + "." + v.Minor + "." + v.Build;   // D-13：保底三段（1.0.0.0 → v1.0.0）
                    if (v.Revision <= 0)
                        return "v" + v.Major + "." + v.Minor + "." + v.Build;
                    return "v" + v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
                }
                catch { return ""; }
            }
        }

        /// <summary>完整版本（含修订号），用于"关于"页等需要精确版本的地方。</summary>
        public static string Full
        {
            get
            {
                try
                {
                    var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                    return v == null ? "" : v.ToString();
                }
                catch { return ""; }
            }
        }
    }
}
