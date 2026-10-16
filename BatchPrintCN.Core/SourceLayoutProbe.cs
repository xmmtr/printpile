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
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号·改1 的 A 口径：**源文档版面体检**（需求方拍板"两口径都做"里的主信号）。
    ///
    /// 为什么需要它：附7 那一案的超宽表格在 Word/WPS 转 PDF 时就被裁掉了，转换产物里根本没有"越界对象"
    /// （实测几何范围正好等于页面框，越界 0.0mm），所以"扫 PDF"只能报"内容贴到页边"这一级间接信号；
    /// 真正说得清病因的是源文档自己的版面参数：版心宽 = 页宽 − 左右页边距，表格宽 = tblGrid 各列合计。
    ///
    /// 覆盖范围如实限定：**只读 OOXML 的 .docx**（zip 里的 word/document.xml，纯解析、不启动 Word/WPS、不改文件）。
    /// .doc/.rtf/.txt/图片/xlsx/pptx 一律回"该格式暂不体检"，不猜；PDF 直入走 B 口径（PageOverflowScanner）。
    ///
    /// 口径边界（写清楚，免得被当成精确值）：`tblW type="auto"` 的表格 Word 排版时**可能**自行压缩到版心内，
    /// 这里按 XML 栅格合计报"超出多少毫米"，属"源文档按声明宽度排版会超出"的提示；附7 原件实测按此口径
    /// 报 5.2mm，与打印成品右侧被裁的表现一致。
    /// </summary>
    public sealed class SourceLayoutFinding
    {
        /// <summary>true = 检出"表格比版心宽"的问题。false 时看 Checked/Error 判断是"查过没问题"还是"没查"。</summary>
        public bool Found;
        /// <summary>是否真的查过（false = 该格式/该情况暂不体检，不能当成"没有超宽"）。</summary>
        public bool Checked;
        public string Error;

        public double PageWidthMm, PageHeightMm;
        public double MarginLeftMm, MarginRightMm;
        public double TextWidthMm;            // 版心宽
        public int TableCount;
        public int OffendingTable = -1;       // 最宽那张表的序号（1-based）
        public double WidestTableMm;
        public bool Landscape, MultipleSections;

        /// <summary>超出量（毫米）：最宽表格 − 版心。没查或没超为 0。</summary>
        public double OverMm { get { return Checked && Found ? Math.Round(WidestTableMm - TextWidthMm, 1) : 0; } }

        /// <summary>纸张口径（与 PDF 扫描同一措辞，便于日志并排看）。</summary>
        public string PageText
        {
            get
            {
                if (!Checked) return "未体检";
                string name = OverflowScan.CommonName(PageWidthMm, PageHeightMm) ?? "自定义";
                return name + (Landscape ? " 横版" : " 竖版") + "（" + OverflowScan.MmText(PageWidthMm) + "×" + OverflowScan.MmText(PageHeightMm) + "mm）";
            }
        }

        public static readonly SourceLayoutFinding NotApplicable = new SourceLayoutFinding { Checked = false, Error = "该格式暂不体检源文档版面" };
    }

    public static class SourceLayoutProbe
    {
        private const double TwipToMm = 25.4 / 1440.0;      // 1 英寸 = 1440 twip

        /// <summary>入口：目前只吃 .docx（OOXML）。其余格式如实返回"暂不体检"。</summary>
        public static SourceLayoutFinding Probe(string path, DocKind kind)
        {
            try
            {
                string ext = (Path.GetExtension(path ?? "") ?? "").ToLowerInvariant();
                if (kind != DocKind.Word || ext != ".docx") return SourceLayoutFinding.NotApplicable;
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return new SourceLayoutFinding { Checked = false, Error = "文件不存在" };
                string xml = ReadEntry(path, "word/document.xml");
                if (string.IsNullOrEmpty(xml)) return new SourceLayoutFinding { Checked = false, Error = "读不到 word/document.xml" };

                var r = new SourceLayoutFinding { Checked = true };
                ProbeDocument(xml, r);
                r.Found = r.TextWidthMm > 0 && r.WidestTableMm - r.TextWidthMm >= 0.5;
                if (!r.Found && r.TextWidthMm <= 0) { r.Checked = false; r.Error = "没取到页面/页边距参数"; }
                return r;
            }
            catch (Exception ex) { return new SourceLayoutFinding { Checked = false, Error = ex.Message }; }
        }

        /// <summary>一节（sectPr）的版面参数。</summary>
        private sealed class Section
        {
            public int At;                        // 在 XML 里的位置（用于"表格归属哪一节"）
            public double PageW, PageH, Ml, Mr, TextW;
            public bool Ok { get { return PageW > 0 && TextW > 0; } }
        }

        /// <summary>
        /// 按文档顺序体检：OOXML 里"某节的内容"由**它后面第一个** sectPr 决定（分节符写在段落的 pPr 里，管它前面的内容）。
        /// 只取最后一段 sectPr 会拿错页边距——附7 原件正是这种：第一节边距 12.7mm、末节 31.8mm，
        /// 按末节算会把"超出 5.2mm"报成"超出 43.3mm"。
        /// </summary>
        private static void ProbeDocument(string xml, SourceLayoutFinding r)
        {
            var sections = new List<Section>();
            foreach (Match m in Regex.Matches(xml, @"<w:sectPr[\s\S]*?</w:sectPr>")) sections.Add(ReadSection(m));
            if (sections.Count == 0) return;
            r.MultipleSections = sections.Count > 1;

            var tables = new List<KeyValuePair<int, double>>();       // 位置 → 宽（毫米）
            foreach (Match t in Regex.Matches(xml, @"<w:tbl[ >][\s\S]*?</w:tbl>"))
            {
                double mm = 0;
                var grid = Regex.Match(t.Value, @"<w:tblGrid[ >][\s\S]*?</w:tblGrid>");
                if (grid.Success)
                {
                    foreach (Match c in Regex.Matches(grid.Value, @"<w:gridCol[^>]*w:w=""(\d+)"""))
                    {
                        int tw;
                        if (int.TryParse(c.Groups[1].Value, out tw)) mm += tw * TwipToMm;
                    }
                }
                int? declared = AttrInt(t.Value, "w:tblW", "w:w");
                string type = AttrString(t.Value, "w:tblW", "w:type");
                if (string.Equals(type, "dxa", StringComparison.OrdinalIgnoreCase) && declared.HasValue && declared.Value * TwipToMm > mm)
                    mm = declared.Value * TwipToMm;                   // 明确定宽的表按声明宽
                tables.Add(new KeyValuePair<int, double>(t.Index, Math.Round(mm, 1)));
            }
            r.TableCount = tables.Count;

            Section first = null;
            double worst = double.MinValue;
            foreach (var kv in tables)
            {
                if (kv.Value <= 0) continue;
                Section own = null;
                foreach (Section s in sections) if (s.At >= kv.Key) { own = s; break; }
                if (own == null) own = sections[sections.Count - 1];
                if (first == null) first = own;
                if (!own.Ok) continue;
                if (kv.Value - own.TextW > worst)
                {
                    worst = kv.Value - own.TextW;
                    r.WidestTableMm = kv.Value;
                    r.OffendingTable = tables.FindIndex(x => x.Key == kv.Key) + 1;
                    r.PageWidthMm = own.PageW; r.PageHeightMm = own.PageH;
                    r.MarginLeftMm = own.Ml; r.MarginRightMm = own.Mr; r.TextWidthMm = own.TextW;
                    r.Landscape = own.PageW > own.PageH;
                }
            }
            if (r.TextWidthMm <= 0 && first != null)
            {
                r.PageWidthMm = first.PageW; r.PageHeightMm = first.PageH;
                r.MarginLeftMm = first.Ml; r.MarginRightMm = first.Mr; r.TextWidthMm = first.TextW;
                r.Landscape = first.PageW > first.PageH;
                if (tables.Count > 0) { r.WidestTableMm = tables[0].Value; r.OffendingTable = 1; }
            }
        }

        private static Section ReadSection(Match m)
        {
            string sec = m.Value;
            int? pgW = AttrInt(sec, "w:pgSz", "w:w");
            int? pgH = AttrInt(sec, "w:pgSz", "w:h");
            int? ml = AttrInt(sec, "w:pgMar", "w:left");
            int? mr = AttrInt(sec, "w:pgMar", "w:right");
            var s = new Section { At = m.Index };
            if (pgW.HasValue) s.PageW = pgW.Value * TwipToMm;
            if (pgH.HasValue) s.PageH = pgH.Value * TwipToMm;
            if (ml.HasValue) s.Ml = ml.Value * TwipToMm;
            if (mr.HasValue) s.Mr = mr.Value * TwipToMm;
            if (pgW.HasValue && ml.HasValue && mr.HasValue) s.TextW = (pgW.Value - ml.Value - mr.Value) * TwipToMm;
            return s;
        }

        private static int? AttrInt(string s, string element, string attr)
        {
            string v = AttrString(s, element, attr);
            int n;
            return v != null && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : (int?)null;
        }

        private static string AttrString(string s, string element, string attr)
        {
            var el = Regex.Match(s, "<" + element + "[^>]*>");
            if (!el.Success) return null;
            var a = Regex.Match(el.Value, attr + "\\s*=\\s*\"([^\"]*)\"");
            return a.Success ? a.Groups[1].Value : null;
        }

        /// <summary>从 docx（zip）里读一个条目的文本。</summary>
        private static string ReadEntry(string path, string entry)
        {
            using (var z = System.IO.Compression.ZipFile.OpenRead(path))
            {
                var e = z.GetEntry(entry.Replace('/', '\\')) ?? z.GetEntry(entry);
                if (e == null) return null;
                using (var s = e.Open())
                using (var sr = new StreamReader(s)) return sr.ReadToEnd();
            }
        }
    }
}
