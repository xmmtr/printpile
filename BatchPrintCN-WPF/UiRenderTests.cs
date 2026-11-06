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
    // V4.2.3 M4 拆分：UI 渲染与界面结构断言（主窗口/弹窗/规划窗/历史页工具栏/主题与双语渲染）。
        private static bool CheckInside(FrameworkElement root, FrameworkElement el)
        {
            if (el == null || root == null) return false;
            try
            {
                Point p = el.TransformToAncestor(root).Transform(new Point(0, 0));
                return el.RenderSize.Width > 0 && el.RenderSize.Height > 0
                    && p.X >= 0 && p.Y >= 0 && p.X + el.RenderSize.Width <= root.ActualWidth + 0.5 && p.Y + el.RenderSize.Height <= root.ActualHeight + 0.5;
            }
            catch { return el.RenderSize.Width > 0 && el.RenderSize.Height > 0; }
        }
        public static int RenderDialog()
        {
            BeginGate("dialogtest");
            Mark("弹窗构造+渲染");
            PrintSettingsPanel panel;
            try { panel = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF"); }
            catch (Exception ex) { return FailWith("弹窗自检.txt", ex); }
            // U2-4（V4.2.5）：打印设置改为 ContentDialog 内容面板；模板三控件在顶部行，页脚确定/取消也在面板内
            int code = RenderElement("弹窗自检.txt", "设置对话框预览.png", panel, 1020, 690,
                new[] { "BtnOk", "BtnCancel", "Preview", "TemplateBox", "BtnSaveTemplate", "BtnDeleteTemplate" });
            // 48 号修2：检查更新结果弹窗浅色态两张（失败＝新中文文案；另出「已是最新版」回归图）
            Mark("检查更新弹窗渲染(48号·浅色)");
            code |= V47UpdateNoticeRenders(false);
            // 73 号令：51 号那张解锁对话框与其三路径断言随对话框本体一并删除（dialogtest 只剩打印设置弹窗与任务规划弹窗）
            return code;
        }

        public static int RenderMain()
        {
            BeginGate("formtest");
            Mark("主窗口构造+渲染");
            MainWindow w;
            try { w = new MainWindow(); }
            catch (Exception ex) { return FailWith("主窗口自检.txt", ex); }
            return RenderWindow("主窗口自检.txt", "主窗口预览.png", w, 1260, 820,
                new[] { "PrinterBox", "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop", "Grid" });
        }

        private static int FailWith(string logName, Exception ex)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, logName), "构造失败\n" + ex, new UTF8Encoding(true)); }
            catch { }
            return 1;
        }

        /// <summary>主题渲染测试：深色主窗口 + 浅色主窗口 + 设置页/关于页，全部渲染通过返回 0。</summary>
        public static int RenderTheme()
        {
            BeginGate("themetest");
            int code = 0;

            // 深色主题主窗口
            App.ApplyTheme(AppTheme.Dark);
            MainWindow wDark;
            try { wDark = new MainWindow(); }
            catch (Exception ex) { code |= FailWith("深色主窗口自检.txt", ex); App.ApplyTheme(AppTheme.Light); return code | 1; }
            code |= RenderWindow("深色主窗口自检.txt", "深色主窗口预览.png", wDark, 1260, 820,
                new[] { "PrinterBox", "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop", "Grid" });

            // 浅色主题主窗口
            App.ApplyTheme(AppTheme.Light);
            MainWindow wLight;
            try { wLight = new MainWindow(); }
            catch (Exception ex) { code |= FailWith("浅色主窗口自检.txt", ex); App.ApplyTheme(AppTheme.Light); return code | 1; }
            code |= RenderWindow("浅色主窗口自检.txt", "浅色主窗口预览.png", wLight, 1260, 820,
                new[] { "PrinterBox", "BtnRefresh", "BtnPrintSetup", "BtnStart", "BtnStop", "Grid" });

            // V4.4 修整轮（2026-09-20）：主列表整体（「修改」按钮主色浅底/表格边框/份数列/页码水印）中英各一张
            Mark("主窗口深/浅两遍渲染");
            code |= PhaseCode("V4.4主列表", () => V44RowCardRender("主列表修整预览.png", "主列表修整自检.txt"));
            code |= PhaseCode("V4.4主列表", () => V44RowCardRender("主列表修整预览-英文.png", "主列表修整英文自检.txt", AppLanguage.EnUS));
            // 参数弹窗（浅/深/英）+ 页码水印特写（中英）；列头打印前后对比由 --selftest 的修整5 取证产出
            code |= PhaseCode("V4.4修整渲染", () => V44FixRender("V44修整渲染自检.txt"));
            // V4.4 二批（修整9~15）：主页宽/中/最小三档 + 历史页宽/最小 + 文件名截断与悬浮全名 + 边框特写浅/深 + 英文两档
            code |= PhaseCode("V4.4二批渲染", () => V44Fix2Render("V44修整渲染自检.txt"));
            code |= PhaseCode("V4.4三批渲染", () => V44Fix3Render("V44三批渲染自检.txt"));;   // 三批：深色带数据行主列表 + 边框特写（垫 WindowBgBrush）+ 按钮区 3 倍特写
            // 四批（修整16/17）：主页 浅/深 × 中英 × 默认宽 1120 / 最小宽 960 共 8 张 + 状态三态特写 + 份数格铺满特写（浅深中英，3 倍）
            code |= PhaseCode("V4.4四批渲染", () => V44Fix4Render("V44四批渲染自检.txt"));
            // 五批（修整18~23）：长列表 1120/960 + 打印中（浅/深）与进度计数特写 + 停止恢复态 + 历史页最小窗 960（浅/深）
            // + 设置页三目录行展开（浅/深）+ 关于页日志唯一入口
            code |= PhaseCode("V4.4五批渲染", () => V44Fix5Render("V44五批渲染自检.txt"));
            // V4.5 保留的四项：按钮字色浅/深对照 + 历史页状态列中英 + 打印中可滚动前后两张 + 全选四情形特写
            // （V4.5-5 监视文件夹已移出 → 该页渲染与端到端取证同步撤销）
            code |= PhaseCode("V4.5渲染", () => V45Render("V45一批渲染自检.txt"));
            // V4.7 拆分：导航四项 + 模板限 5 个提示 + 历史档位四项的断言取证与主页/历史/设置/关于浅深对照
            code |= PhaseCode("V4.7渲染", () => V47Render("V47渲染自检.txt"));
            code |= PhaseCode("V4.9渲染", () => V49Render("V49改进版渲染自检.txt"));;   // V4.9：关闭提示窗浅/深 + 反馈窗口浅/深 + 设置页关闭行为行 + 关于页反馈行
            code |= PhaseCode("40号范围渲染", () => V49RangeRender("V49范围反馈渲染自检.txt"));;   // V4.9 版本内修整（40 号）：打印页浅/深（六行等高）+ 气泡/红字/黄警告特写
            code |= PhaseCode("41号第二批渲染", () => V49bRender("V49b第二批渲染自检.txt"));   // 41 号：托盘图标与菜单浅/深 + 设置页/关于页精简浅/深 + 打印页回归
            code |= PhaseCode("45号三项渲染", () => V492Render("V492三项渲染自检.txt"));   // 45 号·改3：预览窗口浅/深 + 状态列超界黄警告
            code |= PhaseCode("46号预览交互渲染", () => V492cRender("V492c预览交互渲染自检.txt"));   // 46 号：缩放 200% 浅/深 + 末页边界态
            code |= PhaseCode("49号关于页渲染", () => V493Render("V493关于页渲染自检.txt"));   // 49 号：关于页改版浅/深/英文 + 内联"发现新版本"与"失败"两态特写
            code |= PhaseCode("66号更新按钮渲染", () => V66Render("66号更新按钮渲染自检.txt"));   // 66 号：主窗左下角橙色按钮 + 更新弹窗三态（确认/安装确认/便携版）
            code |= PhaseCode("68号四项改进渲染", () => V68Render("68号四项改进渲染自检.txt"));   // 68 号：限时体验弹窗两态 + 检查更新 found 态窄窗 + 模板行名额 + 关于页限时体验中
            code |= PhaseCode("72号监视文件夹渲染", () => V110Render("72号监视文件夹渲染自检.txt"));   // 72 号（V1.1.0·F7）：监视页浅/深＋规则编辑对话框（引导态那张已随门控在 73 号删除）
            code |= PhaseCode("73号开源版渲染", () => V120Render("73号开源版渲染自检.txt"));   // 73/78 号：关于页（只留开源声明＋仓库双按钮）与监视页（无引导态）
            code |= PhaseCode("80号开机自启动渲染", () => V121Render("80号开机自启动渲染自检.txt"));   // 80 号：设置页「关闭行为＋开机自启动」那一卡 浅/深/英文
            // 73 号令：V100Render（许可区/解锁窗/200 条引导出图）与 V70Render（导出置灰两态）随功能一并删除
            App.ApplyLanguage(AppLanguage.ZhCN);

            // 设置页（深色下渲染，验证资源切换无异常）
            App.ApplyTheme(AppTheme.Dark);
            var settings = new SettingsPage();
            code |= RenderControl("设置页自检.txt", "设置页预览.png", settings, 900, 600);
            App.ApplyTheme(AppTheme.Light);
            var settingsLight = new SettingsPage();
            code |= RenderControl("设置页浅色自检.txt", "设置页浅色预览.png", settingsLight, 900, 600);

            // 关于页（浅色）——49 号改版后页面变高（标题区 + 三按钮行 + 开关 + 渠道 + 立即检查卡），画布跟着加高，否则底部卡片被裁
            var about = new AboutPage();
            code |= RenderControl("关于页自检.txt", "关于页预览.png", about, 900, 1010);   // V1.0.0：解锁卡入页，画布 860→1010（防底部卡片被裁）

            // V4.7 拆分1：「监视文件夹」页（F7）已移除 → 不再出该页渲染图
            // （导航回到 打印任务 / 历史 / 设置 / 关于 四项）。

            // V3.0：崩溃诊断弹窗渲染（浅色主题，异常路径 UI 防崩溃）
            App.ApplyTheme(AppTheme.Light);
            var diag = new DiagnosticWindow("自检诊断测试\n版本: 3.0.0.0\n日志目录: " + FileLogger.LogDirectory + "\n最近日志: (自检无实际异常)");
            code |= RenderWindow("诊断弹窗自检.txt", "诊断弹窗预览.png", diag, 720, 560, new string[0]);

            // V4.2 修整二：打印设置弹窗底部模板行（U2-4 起为 ContentDialog 内容面板；深色主题渲染，浅色由 dialogtest 覆盖）
            App.ApplyTheme(AppTheme.Dark);
            code |= RenderElement("设置弹窗深色自检.txt", "设置弹窗预览-深色.png", new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF"), 1020, 690,
                new[] { "TemplateBox", "BtnSaveTemplate", "BtnDeleteTemplate", "BtnOk", "BtnCancel", "Preview" });

            // V4.2 修整一：历史页工具栏（高度/居中/档位不折行）深浅两主题渲染
            App.ApplyTheme(AppTheme.Light);
            code |= RenderControl("历史页浅色自检.txt", "历史页预览-浅色.png", new HistoryPage(), 1400, 620);
            App.ApplyTheme(AppTheme.Dark);
            code |= RenderControl("历史页深色自检.txt", "历史页预览-深色.png", new HistoryPage(), 1400, 620);

            // 61 号令（2026-10-09 需求方拍板）：历史页工具栏**固定单排**取证两档——
            // 默认窗宽 1120 一张；最窄 960 一张且把计数文本填上最长现实文案（计数不设上限时就是把右列挤折行的最坏变量）。
            // 空历史时同页还应显出「暂无打印记录…」（本令顺手修了它被不透明卡片底遮住的 z 序缺陷）。
            var hpRowWide = new HistoryPage();
            code |= RenderControl("历史页工具栏单排1120自检.txt", "历史页-工具栏单排-1120.png", hpRowWide, 1120, 620);
            var hpRowNarrow = new HistoryPage();
            hpRowNarrow.CountText.Text = "200 条 / 共 200 条";
            code |= RenderControl("历史页工具栏单排960自检.txt", "历史页-工具栏单排-960窄窗-最长计数.png", hpRowNarrow, 960, 620);

            // 61 号令·空态文案目视证据：自检环境里历史行异步加载不完成 → UpdateCountText 不触发，故手工把 EmptyText
            // 置成拍板文案＋可见，专测"它是否还被同格那张不透明卡片底遮住"（Panel.ZIndex 修复的看图证据）。
            var hpEmpty = new HistoryPage();
            hpEmpty.EmptyText.Text = "暂无打印记录。打印一次后这里就会显示历史。";
            hpEmpty.EmptyText.Visibility = Visibility.Visible;
            code |= RenderControl("历史页空态文案自检.txt", "历史页-空态文案-960.png", hpEmpty, 960, 620);

            App.ApplyTheme(AppTheme.Light);

            // V4.2 修整三：任务规划（U2-6 起为 ContentDialog 内容面板；设置摘要行 + 模板行 + 「修改设置」按钮，深浅两主题）
            var planGroups = new List<TaskPlanGroup>
            {
                new TaskPlanGroup { Key = "k1", Header = "打印机 · 份数 1 · 双面-长边 / A4", Files = { "自检文档A.pdf", "自检文档B.docx" } }
            };
            App.ApplyTheme(AppTheme.Light);
            var planLight = new TaskPlanPanel(planGroups, "2 个文件 → 1 个任务，请确认：", "双面-长边 / 自动 / 彩色 / A4 / 缩放到合适页面", "双面A4");
            code |= RenderElement("任务规划窗自检.txt", "任务规划窗预览-浅色.png", planLight, 760, 560,
                new[] { "SettingsLine", "TemplateLine", "SummaryText", "BtnModify", "BtnCancel", "BtnOk" });
            App.ApplyTheme(AppTheme.Dark);
            var planDark = new TaskPlanPanel(planGroups, "2 files → 1 task", "Long-edge / Auto / Color / A4 / Fit", "DuplexA4");
            code |= RenderElement("任务规划窗深色自检.txt", "任务规划窗预览-深色.png", planDark, 760, 560,
                new[] { "SettingsLine", "TemplateLine", "SummaryText", "BtnModify", "BtnCancel", "BtnOk" });

            // V4.2 修整：英文文案下三处界面同样不折行、不遮挡（模板行 / 规划窗 / 历史页工具栏）
            App.ApplyLanguage(AppLanguage.EnUS);
            code |= RenderElement("设置弹窗英文自检.txt", "设置弹窗预览-英文.png", new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF"), 1020, 690,
                new[] { "TemplateBox", "BtnSaveTemplate", "BtnDeleteTemplate", "BtnOk", "BtnCancel", "Preview" });
            code |= RenderElement("任务规划窗英文自检.txt", "任务规划窗预览-英文.png",
                new TaskPlanPanel(planGroups, "2 file(s) → 1 task(s), please confirm:", "LongEdge / Auto / Color / A4 / Fit to page", "DuplexA4"), 760, 560,
                new[] { "SettingsLine", "TemplateLine", "SummaryText", "BtnModify", "BtnCancel", "BtnOk" });
            code |= RenderControl("历史页英文自检.txt", "历史页预览-英文.png", new HistoryPage(), 1400, 620);
            App.ApplyLanguage(AppLanguage.ZhCN);

            App.ApplyTheme(AppTheme.Light);
            // 48 号修2：检查更新失败弹窗深色态（浅色与「已是最新」由 dialogtest 覆盖）
            Mark("检查更新弹窗渲染(48号·深色)");
            code |= V47UpdateNoticeRenders(true);
            App.ApplyTheme(AppTheme.Light);
            Mark("其余页面渲染(设置/关于/诊断/历史/规划/英文)");
            return code;
        }

        private static int RenderControl(string logName, string pngName, FrameworkElement root, double width, double height)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, logName);
            string pngPath = Path.Combine(AppContext.BaseDirectory, pngName);
            var sb = new StringBuilder();
            try
            {
                // 页面控件本身无背景（背景由所在窗口提供）：套一层当前主题底色，深色渲染才看得出对比度
                var host = new System.Windows.Controls.Border
                {
                    Background = (Brush)Application.Current.TryFindResource("WindowBgBrush"),
                    Child = root,
                    Padding = new Thickness(16)
                };
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                root = host;
                double rw = root.ActualWidth > 0 ? root.ActualWidth : width;
                double rh = root.ActualHeight > 0 ? root.ActualHeight : height;
                int pw = (int)Math.Ceiling(rw), ph = (int)Math.Ceiling(rh);
                var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(root);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(pngPath, FileMode.Create, FileAccess.Write)) enc.Save(fs);
                sb.AppendLine("内容: " + rw + "x" + rh);
                long colorful = 0, dark = 0, nonWhite = 0;
                var colors = new HashSet<int>();
                int stride = pw * 4;
                var pixels = new byte[stride * ph];
                rtb.CopyPixels(pixels, stride, 0);
                for (int x = 0; x < pw; x += 2)
                    for (int y = 0; y < ph; y += 2)
                    {
                        int i = y * stride + x * 4;
                        byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                        if (a < 10) continue;
                        colors.Add((r << 16) | (g << 8) | b);
                        if (r < 245 || g < 245 || b < 245) nonWhite++;
                        if (Math.Abs(r - g) > 12 || Math.Abs(g - b) > 12 || Math.Abs(r - b) > 12) colorful++;
                        if (r < 130 && g < 130 && b < 130) dark++;
                    }
                long size = new FileInfo(pngPath).Length;
                sb.AppendLine("PNG " + size + " 字节; 颜色 " + colors.Count + " 种; 非白 " + nonWhite + "; 彩色 " + colorful + "; 深色 " + dark);
                // 内容少的页面（如关于页）非白像素少，阈值放宽：有颜色种类 + 有可见内容即可
                bool ok = colors.Count > 20 && nonWhite > 1200 && size > 1500;
                sb.AppendLine(ok ? "判定：通过" : "判定：失败");
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染失败");
                sb.AppendLine(ex.ToString());
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return 1;
            }
        }

        /// <summary>U2（V4.2.5）：ContentDialog 内容面板渲染。面板不是 Window，宿主 Border 提供主题底色；
        /// 目标控件按面板自身名域（FindName）解析，先原尺寸、再缩小复查，防裁切，最后出图。</summary>
        private static int RenderElement(string logName, string pngName, FrameworkElement content, double width, double height, string[] targets)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, logName);
            string pngPath = Path.Combine(AppContext.BaseDirectory, pngName);
            var sb = new StringBuilder();
            try
            {
                var host = new System.Windows.Controls.Border
                {
                    Background = (Brush)Application.Current.TryFindResource("WindowBgBrush"),
                    Child = content
                };
                // 两拍布局：面板内含 ScrollViewer/*行/WPF-UI 卡片控件时，首排 Arrange 后子树尺寸可能尚为 0，
                // 复排一拍即稳定（与 TaskPlanSmoke 的直排一致口径）
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                host.Measure(new Size(width, height));
                host.Arrange(new Rect(0, 0, width, height));
                host.UpdateLayout();
                bool ok = true;
                sb.AppendLine("内容: " + width + "x" + height);
                foreach (string name in targets)
                {
                    var el = content.FindName(name) as FrameworkElement;
                    if (el == null) { sb.AppendLine("丢失(未找到): " + name); ok = false; continue; }
                    bool inside = false;
                    try
                    {
                        Point p = el.TransformToAncestor(host).Transform(new Point(0, 0));
                        inside = el.RenderSize.Width > 0 && el.RenderSize.Height > 0
                            && p.X >= 0 && p.Y >= 0 && p.X + el.RenderSize.Width <= host.ActualWidth + 0.5 && p.Y + el.RenderSize.Height <= host.ActualHeight + 0.5;
                    }
                    catch { inside = el.RenderSize.Width > 0 && el.RenderSize.Height > 0; }
                    sb.AppendLine((inside ? "可见 " : "丢失 ") + name + " 尺寸=" + el.RenderSize.Width + "x" + el.RenderSize.Height);
                    if (!inside) ok = false;
                }

                // 缩小复查（ContentDialog 不随窗口缩放，此处验证面板在更小空间内不丢关键控件）
                double sw = Math.Round(width * 0.88), sh = Math.Round(height * 0.9);
                host.Measure(new Size(sw, sh)); host.Arrange(new Rect(0, 0, sw, sh)); host.UpdateLayout();
                sb.AppendLine("缩小到 " + sw + "x" + sh + " 复查:");
                foreach (string name in targets)
                {
                    var el = content.FindName(name) as FrameworkElement;
                    bool insideSmall = el != null && CheckInside(host, el);
                    sb.AppendLine((insideSmall ? "可见 " : "丢失 ") + name);
                    if (!insideSmall) ok = false;
                }

                // 回原尺寸渲染出图
                host.Measure(new Size(width, height)); host.Arrange(new Rect(0, 0, width, height)); host.UpdateLayout();
                double rw = host.ActualWidth > 0 ? host.ActualWidth : width;
                double rh = host.ActualHeight > 0 ? host.ActualHeight : height;
                sb.AppendLine("面板期望: " + content.DesiredSize.Width + "x" + content.DesiredSize.Height + " / 宿主实排: " + rw + "x" + rh);
                int pw = (int)Math.Ceiling(rw), ph = (int)Math.Ceiling(rh);
                var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(host);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(pngPath, FileMode.Create, FileAccess.Write)) enc.Save(fs);

                long colorful = 0, dark = 0, nonWhite = 0;
                var colors = new HashSet<int>();
                int stride = pw * 4;
                var pixels = new byte[stride * ph];
                rtb.CopyPixels(pixels, stride, 0);
                for (int x = 0; x < pw; x += 2)
                    for (int y = 0; y < ph; y += 2)
                    {
                        int i = y * stride + x * 4;
                        byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                        if (a < 10) continue;
                        colors.Add((r << 16) | (g << 8) | b);
                        if (r < 245 || g < 245 || b < 245) nonWhite++;
                        if (Math.Abs(r - g) > 12 || Math.Abs(g - b) > 12 || Math.Abs(r - b) > 12) colorful++;
                        if (r < 130 && g < 130 && b < 130) dark++;
                    }
                long size = new FileInfo(pngPath).Length;
                sb.AppendLine("PNG " + size + " 字节; 颜色 " + colors.Count + " 种; 非白 " + nonWhite + "; 彩色 " + colorful + "; 深色文字 " + dark);
                bool renderOk = colorful > 150 && dark > 500 && colors.Count > 40;
                ok = ok && renderOk;
                sb.AppendLine(ok ? "判定：通过" : "判定：失败");
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染失败");
                sb.AppendLine(ex.ToString());
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return 1;
            }
        }

        private static int RenderWindow(string logName, string pngName, Window w, double width, double height, string[] targets, int minColorful = 150, Action afterLayout = null)
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, logName);
            string pngPath = Path.Combine(AppContext.BaseDirectory, pngName);
            var sb = new StringBuilder();
            try
            {
                w.Width = width;
                w.Height = height;
                var root0 = w.Content as FrameworkElement;
                if (root0 != null) { root0.Measure(new Size(width, height)); root0.Arrange(new Rect(0, 0, width, height)); }
                w.UpdateLayout();
                // U2 渲染盲区修正（同登记区 #3 机理）：FluentWindow 内容树里 TitleBar/NavigationView 等库控件
                // 自身无背景，无窗口渲染环境下透明 → PNG 显黑块；真机由窗口 Background=WindowBgBrush 兜底。
                // 渲染时垫一层同色底，让出图与真机观感一致。先摘出内容再托管（元素只能有一个可视化父级）。
                FrameworkElement root = root0;
                if (root0 != null)
                {
                    w.Content = null;
                    var bgHost = new System.Windows.Controls.Border
                    {
                        Background = (Brush)Application.Current.TryFindResource("WindowBgBrush"),
                        Child = root0
                    };
                    bgHost.Measure(new Size(width, height));
                    bgHost.Arrange(new Rect(0, 0, width, height));
                    bgHost.UpdateLayout();
                    root = bgHost;
                }
                // 46 号加：布局完成后的钩子（无头下 ScrollViewer 的 Extent 只有量过一遍才有值，滚动必须排在量之后、出图之前）
                if (afterLayout != null) { afterLayout(); if (root != null) root.UpdateLayout(); }
                double rw = root != null && root.ActualWidth > 0 ? root.ActualWidth : width;
                double rh = root != null && root.ActualHeight > 0 ? root.ActualHeight : height;
                int pw = (int)Math.Ceiling(rw), ph = (int)Math.Ceiling(rh);
                var rtb = new RenderTargetBitmap(pw, ph, 96, 96, PixelFormats.Pbgra32);
                if (root != null) rtb.Render(root); else rtb.Render(w);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using (var fs = new FileStream(pngPath, FileMode.Create, FileAccess.Write)) enc.Save(fs);

                bool ok = root != null;
                sb.AppendLine("窗口: " + width + "x" + height + " 内容: " + (root == null ? "null" : root.ActualWidth + "x" + root.ActualHeight));
                foreach (string name in targets)
                {
                    var el = w.FindName(name) as FrameworkElement;
                    if (el == null) { sb.AppendLine("丢失(未找到): " + name); ok = false; continue; }
                    bool inside = false;
                    if (root != null)
                    {
                        try
                        {
                            Point p = el.TransformToAncestor(root).Transform(new Point(0, 0));
                            inside = el.RenderSize.Width > 0 && el.RenderSize.Height > 0
                                && p.X >= 0 && p.Y >= 0 && p.X + el.RenderSize.Width <= root.ActualWidth + 0.5 && p.Y + el.RenderSize.Height <= root.ActualHeight + 0.5;
                        }
                        catch { inside = el.RenderSize.Width > 0 && el.RenderSize.Height > 0; }
                    }
                    sb.AppendLine((inside ? "可见 " : "丢失 ") + name + " 尺寸=" + el.RenderSize.Width + "x" + el.RenderSize.Height);
                    if (!inside) ok = false;
                }

                // 缩到最小尺寸再查一次（WPF 自动排版，按钮行不丢失）
                if (w.MinWidth > 0 && w.MinHeight > 0 && root != null)
                {
                    root.Measure(new Size(w.MinWidth, w.MinHeight));
                    root.Arrange(new Rect(0, 0, w.MinWidth, w.MinHeight));
                    w.UpdateLayout();
                    bool okSmall = true;
                    sb.AppendLine("缩小到 " + w.MinWidth + "x" + w.MinHeight + " 复查:");
                    foreach (string name in targets)
                    {
                        var el = w.FindName(name) as FrameworkElement;
                        bool insideSmall = el != null && CheckInside(root, el);
                        sb.AppendLine((insideSmall ? "可见 " : "丢失 ") + name);
                        if (!insideSmall) okSmall = false;
                    }
                    ok = ok && okSmall;
                }

                long colorful = 0, dark = 0, nonWhite = 0;
                var colors = new HashSet<int>();
                int stride = pw * 4;
                var pixels = new byte[stride * ph];
                rtb.CopyPixels(pixels, stride, 0);
                for (int x = 0; x < pw; x += 2)
                    for (int y = 0; y < ph; y += 2)
                    {
                        int i = y * stride + x * 4;
                        byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2], a = pixels[i + 3];
                        if (a < 10) continue;
                        colors.Add((r << 16) | (g << 8) | b);
                        if (r < 245 || g < 245 || b < 245) nonWhite++;
                        if (Math.Abs(r - g) > 12 || Math.Abs(g - b) > 12 || Math.Abs(r - b) > 12) colorful++;
                        if (r < 130 && g < 130 && b < 130) dark++;
                    }
                long size = new FileInfo(pngPath).Length;
                sb.AppendLine("PNG " + size + " 字节; 颜色 " + colors.Count + " 种; 非白 " + nonWhite + "; 彩色 " + colorful + "; 深色文字 " + dark);
                // U1 换皮（V4.2.4）阈值重标：Fluent 2 色板比旧版更中性（旧阈值 colorful>600/dark>800 是给
                // 米白+渐变蓝的老观感定的，纯灰底深字窗口如诊断弹窗实测 colorful≈320 会误判失败）。
                // 新基线：仍要求"有足够颜色种类 + 有实际内容 + 有彩色像素 + 有深色文字"，防空白渲染的能力不变。
                // minColorful 可由调用方下调：设置页两档实测彩色只有 8（本页没有强调色控件——开关默认关、下拉中性色），
                // 防空白仍由 颜色种类>40 + 深色文字>500 兜住（该页非白 8.9 万/20.9 万像素），出图见对话内贴图。
                bool renderOk = colorful > minColorful && dark > 500 && colors.Count > 40;
                ok = ok && renderOk;
                sb.AppendLine(ok ? "判定：通过" : "判定：失败");
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染失败");
                sb.AppendLine(ex.ToString());
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return 1;
            }
        }


        // V4.1 历史页交互冒烟：创建→显隐切换→变更筛选下拉→不死循环正常返回
        // V4.2 扩展（断言④）：搜索框输入（内存过滤路径）+ 档位下拉变更（OnLimitChanged→Refresh 重入门）不回归闪退
        private static string HistoryPageSmoke()
        {
            try
            {
                var page = new HistoryPage();
                page.Refresh();   // 等价 IsVisibleChanged → Refresh 路径
                var fp = page.FindName("FilterPrinter") as System.Windows.Controls.ComboBox;
                var fr = page.FindName("FilterRange") as System.Windows.Controls.ComboBox;
                if (fp != null && fp.Items.Count > 0) fp.SelectedIndex = 0;   // SelectionChanged → OnFilterChanged → Refresh（_refreshing 门）
                if (fr != null && fr.Items.Count > 0) fr.SelectedIndex = 1;
                if (fp != null && fp.Items.Count > 1) fp.SelectedIndex = 1;
                page.Refresh();

                // V4.2：搜索框输入 → TextChanged → 内存过滤（不查库）；再清空还原
                var searchBox = page.FindName("SearchBox") as System.Windows.Controls.TextBox;
                if (searchBox != null) searchBox.Text = "冒烟关键字";
                if (searchBox != null) searchBox.Text = "";

                // V4.2：档位下拉变更 → OnLimitChanged → Refresh（与其他下拉共用 _refreshing 门）
                var limitBox = page.FindName("LimitBox") as System.Windows.Controls.ComboBox;
                if (limitBox != null && limitBox.Items.Count > 2) limitBox.SelectedIndex = 2;
                if (limitBox != null && limitBox.Items.Count > 1) limitBox.SelectedIndex = 1;
                page.Refresh();
                return "通过（创建/Refresh/筛选/搜索/档位变更不死循环）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // V4.2 修整二·断言①：模板控件已从主页迁到打印设置弹窗（U2-4 起弹窗为 PrintSettingsPanel 内容面板）
        private static string TemplateControlsMoved()
        {
            try
            {
                var main = new MainWindow();
                string[] gone = { "TemplateBox", "BtnSaveTemplate", "BtnDeleteTemplate" };
                var stillInMain = gone.Where(n => main.FindName(n) != null).ToList();
                var dlg = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                var missingInDlg = gone.Where(n => dlg.FindName(n) == null).ToList();
                if (stillInMain.Count > 0) return "失败：主页仍含 " + string.Join(",", stillInMain);
                if (missingInDlg.Count > 0) return "失败：弹窗缺 " + string.Join(",", missingInDlg);
                return "通过（主页 0 个 / 弹窗 3 个齐备）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }


        // V4.2 修整三·断言④：规划窗顶部设置摘要行 + 模板行 + 「修改设置」按钮（U2-6 起为 TaskPlanPanel 内容面板）
        private static string TaskPlanSmoke()
        {
            try
            {
                var groups = new List<TaskPlanGroup>
                {
                    new TaskPlanGroup { Key = "k1", Header = "打印机 · 份数 1 · 双面-长边 / A4", Files = { "a.pdf" } }
                };
                var panel = new TaskPlanPanel(groups, "1 个文件 → 1 个任务", "双面-长边 / 自动 / 彩色 / A4 / 缩放到合适页面", "双面A4");
                panel.Measure(new Size(760, 560));
                panel.Arrange(new Rect(0, 0, 760, 560));
                panel.UpdateLayout();
                var line = panel.FindName("SettingsLine") as System.Windows.Controls.TextBlock;
                var tpl = panel.FindName("TemplateLine") as System.Windows.Controls.TextBlock;
                var modify = panel.FindName("BtnModify") as FrameworkElement;
                if (line == null || string.IsNullOrEmpty(line.Text) || line.Text.IndexOf("双面-长边", StringComparison.Ordinal) < 0)
                    return "失败：设置摘要行缺失或无内容";
                if (tpl == null || tpl.Visibility != Visibility.Visible || tpl.Text.IndexOf("双面A4", StringComparison.Ordinal) < 0)
                    return "失败：模板行未显示";
                if (modify == null || modify.RenderSize.Height <= 0) return "失败：修改设置按钮不可见";
                return "通过（摘要行 " + line.Text.Length + " 字 / 模板行可见 / 按钮高 " + modify.RenderSize.Height + "）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // V4.2.5 U2 换骨断言：外壳三件套（NavigationView / Snackbar 宿主 / ContentDialog 宿主）齐备，
        // 两个弹窗内容面板关键控件齐备，缩放已换 NumberBox，ToastAlert 已接 Snackbar 呈现。
        private static string U2ShellSmoke()
        {
            try
            {
                var main = new MainWindow();
                bool navOk = main.FindName("Nav") is Wpf.Ui.Controls.NavigationView;
                bool toastOk = main.FindName("ToastHost") is Wpf.Ui.Controls.SnackbarPresenter;
                bool dlgHostOk = main.FindName("RootContentDialog") is Wpf.Ui.Controls.ContentDialogHost;
                var settings = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                var plan = new TaskPlanPanel(new List<TaskPlanGroup>(), "1 个文件 → 1 个任务", "双面-长边 / A4", null);
                bool panelOk = settings.FindName("TemplateBox") != null && settings.FindName("Preview") != null
                    && settings.FindName("BtnOk") != null && settings.FindName("EffectiveText") != null
                    && plan.FindName("BtnModify") != null;
                bool numberOk = settings.FindName("ScaleBox") is Wpf.Ui.Controls.NumberBox;
                bool infoOk = main.FindName("PrintPanel") != null;
                if (!navOk || !toastOk || !dlgHostOk)
                    return "失败：外壳组件缺失 Nav=" + navOk + " Toast=" + toastOk + " DialogHost=" + dlgHostOk;
                if (!panelOk) return "失败：弹窗内容面板控件缺失";
                if (!numberOk) return "失败：缩放未换 NumberBox";
                if (!infoOk) return "失败：主面板缺失";
                return "通过（外壳三件套 + 两面板关键控件 + 缩放 NumberBox）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // V4.3 F15/F16 界面断言（EngineSelfTest 调用）：主页无打印机设置按钮 / 设置页打印机分组 /
        // 打印设置面板优先级灰字 / 历史页打印机列头漏斗 / 引擎列隐藏 / 排序列头提示
        private static string F15F16Smoke()
        {
            try
            {
                var main = new MainWindow();
                bool btnGone = main.FindName("BtnPrinterSetup") == null;
                var settings = new SettingsPage();
                bool group = settings.FindName("PrinterBox2") != null && settings.FindName("BtnOpenPrinterProps") != null;
                var panel = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                bool note = panel.FindName("PriorityNote") != null;
                var hist = new HistoryPage();
                bool funnel = hist.PrinterFilterButtonForTest != null;
                bool engineGone = !hist.Grid.Columns.OfType<System.Windows.Controls.DataGridTextColumn>()
                    .Any(c => (c.Binding as System.Windows.Data.Binding) != null && ((System.Windows.Data.Binding)c.Binding).Path.Path == "Engine");
                bool sortHints = hist.Grid.Columns.OfType<System.Windows.Controls.DataGridTextColumn>()
                    .All(c => c.Header is System.Windows.Controls.StackPanel);
                if (!btnGone) return "失败：主页仍有「打印机设置」按钮";
                if (!group) return "失败：设置页打印机分组缺失";
                if (!note) return "失败：打印设置面板缺「优先于打印机默认值」灰字";
                if (!funnel) return "失败：历史页打印机列头筛选缺失";
                if (!engineGone) return "失败：引擎列仍在界面";
                if (!sortHints) return "失败：排序列头提示缺失";
                return "通过（F15 主页无按钮/设置页分组在位/优先级灰字在位；F16 筛选漏斗/无引擎列/排序提示齐备）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // V4.2 修整一：历史页工具栏——控件高度统一 30、导出按钮文字单行且水平垂直居中、档位项不折行（中英两语言各测一遍）
        private static string HistoryToolbarSmoke()
        {
            string zh = HistoryToolbarCheck();
            App.ApplyLanguage(AppLanguage.EnUS);
            string en = HistoryToolbarCheck();
            App.ApplyLanguage(AppLanguage.ZhCN);
            if (!zh.StartsWith("通过")) return "中文：" + zh;
            if (!en.StartsWith("通过")) return "英文：" + en;
            return "通过（中英两语言各自：控件高度统一 30 / 导出文字单行居中 / 档位项不折行）";
        }

        private static string HistoryToolbarCheck()
        {
            try
            {
                var page = new HistoryPage();
                page.Measure(new Size(1600, 700));
                page.Arrange(new Rect(0, 0, 1600, 700));
                page.UpdateLayout();

                var export = page.FindName("BtnExport") as System.Windows.Controls.Button;
                var refresh = page.FindName("BtnRefresh") as System.Windows.Controls.Button;
                var limit = page.FindName("LimitBox") as System.Windows.Controls.ComboBox;
                var search = page.FindName("SearchBox") as System.Windows.Controls.TextBox;
                var range = page.FindName("FilterRange") as System.Windows.Controls.ComboBox;
                if (export == null || refresh == null || limit == null || search == null || range == null)
                    return "失败：工具栏控件缺失";

                bool heightOk = Math.Abs(export.ActualHeight - 30) < 0.6 && Math.Abs(refresh.ActualHeight - 30) < 0.6
                    && Math.Abs(limit.ActualHeight - 30) < 0.6 && Math.Abs(search.ActualHeight - 30) < 0.6
                    && Math.Abs(range.ActualHeight - 30) < 0.6;

                var tb = FindTextBlock(export);
                if (tb == null) return "失败：导出按钮无文字节点";
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                bool singleLine = tb.ActualHeight <= tb.FontSize * 1.6 + 0.5;
                Point p = tb.TransformToAncestor(export).Transform(new Point(0, 0));
                double padTop = p.Y, padBottom = export.ActualHeight - (p.Y + tb.ActualHeight);
                double padLeft = p.X, padRight = export.ActualWidth - (p.X + tb.ActualWidth);
                bool centerOk = Math.Abs(padTop - padBottom) <= 1.2 && Math.Abs(padLeft - padRight) <= 1.2;

                // 档位下拉：最宽项测量宽度须留在下拉框内（防"200 条（默认）"式折行）
                double usable = limit.ActualWidth - 34;
                double widest = 0; string widestText = "";
                foreach (object o in limit.Items)
                {
                    var probe = new System.Windows.Controls.TextBlock { Text = o as string ?? "", FontSize = limit.FontSize };
                    probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    if (probe.DesiredSize.Width > widest) { widest = probe.DesiredSize.Width; widestText = o as string ?? ""; }
                }
                bool itemOk = widest <= usable;
                if (!heightOk) return "失败：工具栏控件高度未统一（导出 " + export.ActualHeight + " / 刷新 " + refresh.ActualHeight
                    + " / 档位 " + limit.ActualHeight + " / 搜索 " + search.ActualHeight + "）";
                if (!singleLine) return "失败：导出按钮文字折行（高 " + tb.ActualHeight + "）";
                if (!centerOk) return "失败：导出按钮文字未居中（上 " + padTop.ToString("0.0") + " 下 " + padBottom.ToString("0.0")
                    + " 左 " + padLeft.ToString("0.0") + " 右 " + padRight.ToString("0.0") + "）";
                if (!itemOk) return "失败：档位项折行（最宽「" + widestText + "」" + widest.ToString("0.0") + " > 可用 " + usable.ToString("0.0") + "）";
                return "通过（高度统一 30 / 导出文字居中 上 " + padTop.ToString("0.0") + " 下 " + padBottom.ToString("0.0")
                    + " / 最宽档位 " + widest.ToString("0.0") + " ≤ 可用 " + usable.ToString("0.0") + "）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        private static System.Windows.Controls.TextBlock FindTextBlock(DependencyObject root)
        {
            if (root == null) return null;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                var tb = child as System.Windows.Controls.TextBlock;
                if (tb != null) return tb;
                var deeper = FindTextBlock(child);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
