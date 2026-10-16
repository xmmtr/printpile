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
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BatchPrintCN
{
    public static partial class WpfTests
    {
    // V4.2.3 M4 拆分：便携目录与数据层断言（历史搜索过滤、打印模板存取与套用）。

        // V4.2 修整二·断言③：模板冒烟（存→下拉选→参数套到控件一致→手动改动回"未选择"→清理）
        // V4.3 扩展：src 携带 F6 新字段（奇偶/纸盒/缩放模式/9 版/小册子），证明"全部进模板与回收"
        private static string TemplateApplySmoke()
        {
            string name = "自检模板-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                var src = new PrintOptions
                {
                    Duplex = DuplexMode.ShortEdge,
                    Orientation = OrientationMode.Landscape,
                    Color = ColorMode.BlackWhite,
                    Paper = PaperKind.Legal,
                    PaperDisplayName = "Legal",
                    FitToPage = false,
                    ScalePercent = 150,
                    PagesPerSheet = 9,
                    PageParity = PageParityMode.Odd,
                    PaperSourceName = "手动送纸",
                    Booklet = true
                };
                src.Scale = ScaleMode.Custom;
                SqliteStore.SaveTemplate(name, src);
                if (!SqliteStore.LoadTemplates().Any(t => t.Name == name)) return "失败：存的模板读不回来";

                var dlg = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                var box = dlg.FindName("TemplateBox") as System.Windows.Controls.ComboBox;
                if (box == null) return "失败：弹窗无模板下拉";
                int idx = box.Items.IndexOf(name);
                if (idx < 0) return "失败：模板未列进下拉";
                box.SelectedIndex = idx;                       // 走真实选择链路（OnTemplateSelected → 套用）
                if (dlg.AppliedTemplateForTest != name) return "失败：选中后未记录模板名";

                var got = dlg.CollectedForTest;
                bool same = got.Duplex == src.Duplex && got.Orientation == src.Orientation && got.Color == src.Color
                    && got.Paper == src.Paper && got.PaperDisplayName == src.PaperDisplayName
                    && got.FitToPage == src.FitToPage && got.ScalePercent == src.ScalePercent && got.PagesPerSheet == src.PagesPerSheet
                    && got.PageParity == src.PageParity && got.PaperSourceName == src.PaperSourceName
                    && got.Booklet == src.Booklet && got.EffectiveScale == src.EffectiveScale;
                if (!same)
                    return "失败：套用后控件值不一致（" + got.Duplex + "/" + got.PaperDisplayName + "/" + got.ScalePercent + "/" + got.PagesPerSheet
                        + "/" + got.PageParity + "/" + got.PaperSourceName + "/" + got.Booklet + "/" + got.EffectiveScale + "）";

                var paper = dlg.FindName("PaperBox") as System.Windows.Controls.ComboBox;
                if (paper != null)
                {
                    paper.SelectedIndex = 0;                   // 用户手动改一项
                    if (dlg.AppliedTemplateForTest != null) return "失败：手动改动后模板未回未选";
                    if (box.SelectedIndex >= 0) return "失败：手动改动后下拉未清空";
                }

                SqliteStore.DeleteTemplate(name);
                bool cleaned = !SqliteStore.LoadTemplates().Any(t => t.Name == name);
                return cleaned ? "通过（含 F6 新字段 12 项参数套用一致 / 手动改动回未选 / 自检模板已清理）" : "失败：自检模板未删净";
            }
            catch (Exception ex) { try { SqliteStore.DeleteTemplate(name); } catch { } return "失败：" + ex.Message; }
        }



        // V4.2 断言②：搜索过滤冒烟——3 行数据，关键字命中 1 行（文件名与所在文件夹列均参与匹配）
        private static bool TestSearchFilter()
        {
            var rows = new System.Collections.Generic.List<SqliteStore.HistoryRow>
            {
                new SqliteStore.HistoryRow { Name = "季度报告.docx", Folder = @"C:\docs\2026" },
                new SqliteStore.HistoryRow { Name = "预算表.xlsx", Folder = @"C:\docs\财务" },
                new SqliteStore.HistoryRow { Name = "会议纪要.pdf", Folder = null }
            };
            int hitName = 0, hitFolder = 0, hitAll = 0;
            foreach (var r in rows)
            {
                if (HistoryPage.SearchMatch(r, "报告")) hitName++;
                if (HistoryPage.SearchMatch(r, "财务")) hitFolder++;
                if (HistoryPage.SearchMatch(r, "")) hitAll++;
            }
            return hitName == 1 && hitFolder == 1 && hitAll == 3;
        }
    }
}
