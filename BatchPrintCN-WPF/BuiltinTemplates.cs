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
using System.Windows;

namespace BatchPrintCN
{
    /// <summary>
    /// 68 号 T1（需求八）：四个内置默认模板——「A4 黑白单面／A4 黑白双面／A4 彩色单面／A4 彩色双面」，
    /// 首启（模板库为空）自动预置，<b>不吃免费版 5 个自建名额</b>（库里 builtin=1，名额只数 builtin=0，见
    /// <see cref="SqliteStore.UserTemplateNames"/>）；可删，删了有「恢复默认模板」入口回填。
    /// 名字取当前语言的资源键（库里的名字是<b>预置那一刻</b>的语言，之后换语言不追溯改名——只补新、不改旧）。
    /// 除 TrialStart 之外的两枚跨启动标记（预置过没有、这台机器是不是全新安装）落 <c>app_meta</c> 表，不落 settings.json。
    /// </summary>
    internal static class BuiltinTemplates
    {
        /// <summary>库内标记：内置模板是否已预置过（预置只做一次，用户删光后不会在下次启动又自己长回来）。</summary>
        internal const string MetaSeeded = "builtin_tpl_seeded_v1";

        internal const int Count = 4;

        /// <summary>四枚内置模板的规格（名字资源键 + 参数）。除点名的纸型/颜色/双面三项外，其余全部走 PrintOptions 默认值。</summary>
        private static readonly KeyValuePair<string, Action<PrintOptions>>[] Specs =
        {
            new KeyValuePair<string, Action<PrintOptions>>("s.TplBuiltinA4MonoSimplex",
                o => { o.Paper = System.Drawing.Printing.PaperKind.A4; o.Color = ColorMode.BlackWhite; o.Duplex = DuplexMode.Simplex; }),
            new KeyValuePair<string, Action<PrintOptions>>("s.TplBuiltinA4MonoDuplex",
                o => { o.Paper = System.Drawing.Printing.PaperKind.A4; o.Color = ColorMode.BlackWhite; o.Duplex = DuplexMode.LongEdge; }),
            new KeyValuePair<string, Action<PrintOptions>>("s.TplBuiltinA4ColorSimplex",
                o => { o.Paper = System.Drawing.Printing.PaperKind.A4; o.Color = ColorMode.Color; o.Duplex = DuplexMode.Simplex; }),
            new KeyValuePair<string, Action<PrintOptions>>("s.TplBuiltinA4ColorDuplex",
                o => { o.Paper = System.Drawing.Printing.PaperKind.A4; o.Color = ColorMode.Color; o.Duplex = DuplexMode.LongEdge; }),
        };

        /// <summary>四枚内置模板的名字（按当前语言解析资源键；解析不到就退回键名，绝不静默少一枚）。</summary>
        internal static List<string> Names()
        {
            var list = new List<string>(Specs.Length);
            foreach (var s in Specs)
            {
                string n = Res(s.Key);
                if (!string.IsNullOrWhiteSpace(n)) list.Add(n.Trim());
            }
            return list;
        }

        /// <summary>按名字造出该内置模板的参数（名字不在四枚之列返回 null）。</summary>
        internal static PrintOptions OptionsFor(string name)
        {
            foreach (var s in Specs)
                if (string.Equals(Res(s.Key), (name ?? "").Trim(), StringComparison.Ordinal))
                {
                    var o = new PrintOptions();
                    o.PaperDisplayName = "A4";
                    s.Value(o);
                    return o;
                }
            return null;
        }

        /// <summary>
        /// 逐条补齐四枚内置模板（幂等：同名已存在——无论内置还是用户自建——都不覆盖、不重复插）。
        /// 返回<b>本次真正新增</b>的条数。首启预置与「恢复默认模板」共用这一句。
        /// </summary>
        internal static int Seed()
        {
            SqliteStore.EnsureDb();
            int added = 0;
            foreach (var s in Specs)
            {
                string name = (Res(s.Key) ?? "").Trim();
                if (name.Length == 0) continue;
                var o = new PrintOptions();
                o.PaperDisplayName = "A4";
                s.Value(o);
                if (SqliteStore.SaveBuiltinTemplate(name, o)) added++;
            }
            return added;
        }

        /// <summary>
        /// 首启预置：库里没做过预置标记时补一次四枚，并把标记写进 app_meta；
        /// 之后用户删光内置模板，下次启动<b>不会</b>自己长回来（要靠「恢复默认模板」入口）。
        /// </summary>
        internal static int EnsureSeeded()
        {
            SqliteStore.EnsureDb();
            if (!string.IsNullOrEmpty(SqliteStore.GetMeta(MetaSeeded))) return 0;
            int added = Seed();
            SqliteStore.SetMeta(MetaSeeded, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            FileLogger.Write("默认模板预置：本次新增 " + added + " 枚（内置标记 builtin=1，不吃免费版自建名额）。");
            return added;
        }

        private static string Res(string key)
        {
            try
            {
                object o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string;
            }
            catch { return null; }
        }
    }
}
