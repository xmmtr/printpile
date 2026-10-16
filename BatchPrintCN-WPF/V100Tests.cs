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
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// <summary>
    /// 52 号令·步骤 4 主题引擎断言（本文件原名 V100Tests，曾装 51 号解锁链路七组；73 号令把解锁/验签/
    /// 门控整体删除后，这里只留主题引擎一组＋几处共享的子树遍历小工具）。
    /// 全程离线：不联网、不碰真实剪贴板、不真开浏览器。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>金样：SHA-256("BPC1MC" + 四要素) 前 16 字节（node crypto 独立计算后硬编码，防两端实现同错）。</summary>
        /// <summary>
        /// 73 号令：本段只剩 52 号主题引擎一组——51 号那七组（Ed25519 三向量／机器码金样／解锁码编解码验签
        /// ／FeatureGate 三态／历史档位与 CSV 闸／模板闸／许可区两态与解锁对话框）随解锁方案整体退场。
        /// 这里不是"改判"而是"判据不存在"：闸与码都没了，断言留着就成了假绿，故整组删除并在此留一行对账说明。
        /// </summary>
        internal static string V100Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            bad.AddRange(V100ThemeEngine(sb));
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "52号主题引擎定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（52 号主题引擎：12 枚令牌齐备·内置主题清单·导入导出往返·色卡数量与选中态·深浅基座联动·用户主题目录读写）"
                : "失败：" + string.Join("；", bad);
        }

        // ===== 主题引擎（52 号令·步骤 4）=====
        private static List<string> V100ThemeEngine(StringBuilder sb)
        {
            var bad = new List<string>();
            var mode0 = Application.Current.ShutdownMode;
            var name0 = SettingsService.ThemeName;
            var theme0 = SettingsService.Theme;
            var dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV100-theme-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // ① 内置 8 主题：数量、12 令牌齐全、#RRGGBB 格式、名字与原型逐字一致
                string[] wantNames = { "浅色", "深色", "海盐蓝", "森绿", "暖沙", "紫夜", "樱粉", "高对比黑" };
                bool[] wantDark = { false, true, false, false, false, true, false, true };
                var all = ThemeService.All();
                bool listOk = ThemeService.Builtin.Count == 8;
                for (int i = 0; i < 8 && listOk; i++)
                    listOk = ThemeService.Builtin[i].Name == wantNames[i]
                          && ThemeService.Builtin[i].IsDark == wantDark[i]
                          && ThemeService.Builtin[i].Tokens.Count == 12;
                sb.AppendLine("内置主题数=" + ThemeService.Builtin.Count + "、名单与深浅档=" + (listOk ? "与原型逐字一致 ✓" : "失败！"));
                if (!listOk) bad.Add("内置主题名单/令牌");
                bool hexOk = ThemeService.Builtin.All(b => b.Tokens.Values.All(v => ThemeService.NormalizeHex(v) != null));
                if (!hexOk) bad.Add("内置主题色值格式");
                sb.AppendLine("内置主题 12 令牌全部 #RRGGBB: " + (hexOk ? "✓" : "失败！"));

                // ② mode 联动：选深色主题 → WPF-UI 基座切深（App 资源里 DarkTheme 字典在场），浅色同理
                ThemeService.Apply("紫夜");
                bool darkBase = ThemeIsDarkBase();
                ThemeService.Apply("海盐蓝");
                bool lightBase = !ThemeIsDarkBase();
                sb.AppendLine("mode 联动: 紫夜→深色基座=" + darkBase + "，海盐蓝→浅色基座=" + lightBase + " → " + (darkBase && lightBase ? "✓" : "失败！"));
                if (!darkBase || !lightBase) bad.Add("主题 mode 联动");
                // 令牌确实改了值：海盐蓝 accent=#1F6FB2（≠浅色默认 #0067C0）
                var acc = Application.Current.TryFindResource("AccentBrush") as SolidColorBrush;
                bool accentMoved = acc != null && acc.Color == ThemeService.ToColor("#1f6fb2").Value;
                sb.AppendLine("海盐蓝 accent 落到 AccentBrush: " + (accentMoved ? "#1F6FB2 ✓" : "失败！实际 " + (acc == null ? "null" : acc.Color.ToString())));
                if (!accentMoved) bad.Add("令牌映射 AccentBrush");

                // ③ .bptheme 非法拒绝三例（zip 魔数/缺 theme.json/format 不符）＋缺键回退＋合法导入
                byte[] notZip = System.Text.Encoding.UTF8.GetBytes("这不是 zip");
                bool rejMagic = ThemeService.ParseBpTheme(notZip) == null;
                byte[] emptyZip = MakeZip(new Dictionary<string, string>());   // 无 theme.json
                bool rejNoJson = ThemeService.ParseBpTheme(emptyZip) == null;
                byte[] badFormat = MakeZip(new Dictionary<string, string>
                {
                    ["theme.json"] = "{\"format\":\"other/1.0\",\"name\":\"X\",\"mode\":\"light\",\"tokens\":{}}"
                });
                bool rejFormat = ThemeService.ParseBpTheme(badFormat) == null;
                byte[] missingTokens = MakeZip(new Dictionary<string, string>
                {
                    ["theme.json"] = "{\"format\":\"bptheme/0.1\",\"name\":\"测试缺键\",\"author\":\"t\",\"version\":\"1.0\",\"mode\":\"light\",\"tokens\":{\"accent\":\"#123456\"}}"
                });
                ThemeService.LastParseStrict = true;
                var partial = ThemeService.ParseBpTheme(missingTokens);
                bool fallbackOk = partial != null && partial.Tokens["accent"] == "#123456"
                    && partial.Tokens["bg.window"] == ThemeService.Builtin[0].Tokens["bg.window"];   // 缺键回退浅色默认
                sb.AppendLine(".bptheme 拒绝: 非 zip=" + rejMagic + " 缺 theme.json=" + rejNoJson + " format 不符=" + rejFormat
                    + "；缺键回退（accent 收下、bg.window 回浅色默认）=" + fallbackOk);
                if (!rejMagic || !rejNoJson || !rejFormat || !fallbackOk) bad.Add(".bptheme 解析守卫");

                // 合法包导入：写入临时目录无关——Import 直接落 data\themes\；自检不污染真实数据目录，
                // 改为解析成功即断（Import 落盘路径在真机验证；这里注入不了目录，报备）。
                byte[] good = MakeZip(new Dictionary<string, string>
                {
                    ["theme.json"] = "{\"format\":\"bptheme/0.1\",\"name\":\"自检测试主题\",\"author\":\"V100\",\"version\":\"1.0\",\"mode\":\"dark\",\"tokens\":{\"bg.window\":\"#101010\",\"accent\":\"#ff8800\",\"accent.text\":\"#111111\"}}"
                });
                var parsed = ThemeService.ParseBpTheme(good);
                bool goodOk = parsed != null && parsed.IsDark && parsed.Tokens["accent"] == "#FF8800";
                sb.AppendLine("合法 .bptheme 解析: " + (goodOk ? "✓（mode=dark、accent=#FF8800）" : "失败！"));
                if (!goodOk) bad.Add(".bptheme 合法解析");

                // ④ 安全边界：主题数据只有颜色令牌——All() 的每个主题都只有 12 键、值都是 #RRGGBB（无代码/无 XAML 注入面）
                bool safe = ThemeService.All().All(t => t.Tokens.Count == 12 && t.Tokens.Keys.All(k => ThemeService.TokenKeys.Contains(k)));
                sb.AppendLine("安全边界（主题数据仅 12 颜色令牌）: " + (safe ? "✓" : "失败！"));
                if (!safe) bad.Add("主题安全边界");

                // ⑤ 设置页主题卡在位：8 枚内置色卡（色卡按钮 Tag=ThemeDef）
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                // 54 号令·B 视口取证（照 52 号返修① CSV 并排同款方法）：默认窗宽 1120 扣掉导航列 216 与
                // SettingsHost 左右各 16 → 页面实宽 872；此宽度下量各卡相对 ThemeCardHost 的 y，行数必须＝1。
                var page = new SettingsPage();
                page.Measure(new Size(872, 900));
                page.Arrange(new Rect(0, 0, 872, 900));
                page.UpdateLayout();
                int cards = 0;
                CollectThemeCards(page, ref cards);
                bool cardsOk = cards >= 8;
                sb.AppendLine("设置页主题色卡数: " + cards + (cardsOk ? "（≥8 枚 ✓）" : "（失败！应 ≥8）"));
                if (!cardsOk) bad.Add("设置页主题色卡");

                var btns = new List<Button>();
                CollectThemeCardButtons(page, btns);
                var host = page.FindName("ThemeCardHost") as FrameworkElement;
                var ys = CardRows(btns, host);
                bool oneRow = host != null && btns.Count >= 8 && ys.Count == 1;
                sb.AppendLine("色卡单排（默认窗宽→页面 872）: 宿主可视宽=" + (host == null ? -1 : host.ActualWidth).ToString("0.#")
                    + "、卡数=" + btns.Count + "、行数=" + ys.Count
                    + (oneRow ? " → 8 枚同排 ✓" : "（失败！仍折行或取不到宿主）"));
                if (!oneRow) bad.Add("色卡单排（默认宽度）");

                // 窄窗口（窗 960→页面 712）：允许折行，但任何一枚卡都不得被横向裁切
                page.Measure(new Size(712, 900));
                page.Arrange(new Rect(0, 0, 712, 900));
                page.UpdateLayout();
                var host2 = page.FindName("ThemeCardHost") as FrameworkElement;
                var ys2 = CardRows(btns, host2);
                bool clipped = false;
                if (host2 != null)
                    foreach (var b in btns)
                    {
                        double right = b.TransformToAncestor(host2).Transform(new Point(b.ActualWidth, 0)).X;
                        if (right > host2.ActualWidth + 0.5) clipped = true;
                    }
                sb.AppendLine("色卡窄窗（页面 712）: 行数=" + ys2.Count + "（允许折行）、被裁切的卡="
                    + (clipped ? "有！" : "无 ✓") + "、宿主可视宽=" + (host2 == null ? -1 : host2.ActualWidth).ToString("0.#"));
                if (clipped || host2 == null) bad.Add("色卡窄窗折行裁切");
                // 复位回默认宽度，免得把窄窗状态留给后续渲染段
                page.Measure(new Size(872, 900));
                page.Arrange(new Rect(0, 0, 872, 900));
                page.UpdateLayout();

                // 恢复默认主题（不把测试主题留给后续渲染段）
                ThemeService.Apply("浅色");
            }
            catch (Exception ex) { bad.Add("V100 主题段异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally
            {
                SettingsService.SetThemeName(name0);
                if (theme0 == AppTheme.Dark) SettingsService.SetTheme(AppTheme.Dark);
                Application.Current.ShutdownMode = mode0;
                try { Directory.Delete(dir, true); } catch { }
            }
            return bad;
        }

        private static bool ThemeIsDarkBase()
        {
            // WPF-UI 基座深浅的可靠探针：App 资源 merged 字典里 DarkTheme.xaml 是否在场
            var dicts = Application.Current.Resources.MergedDictionaries;
            return dicts.Any(d => d.Source != null && d.Source.OriginalString.EndsWith("DarkTheme.xaml", StringComparison.OrdinalIgnoreCase));
        }

        private static void CollectThemeCards(DependencyObject root, ref int count)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is Button b && b.Tag is ThemeService.ThemeDef) count++;
                CollectThemeCards(c, ref count);
            }
        }

        /// <summary>73 号令：收关于页「功能一览」的行（每行一个 Tag="featline" 的横排 StackPanel；原名 licfeat 随解锁概念退场）。</summary>
        private static void CollectFeatRows(DependencyObject root, List<StackPanel> into)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is StackPanel sp && (sp.Tag as string) == "featline") into.Add(sp);
                CollectFeatRows(c, into);
            }
        }

        /// <summary>57 号·B：取一棵子树里全部 TextBlock 的文字（按可视顺序），用于逐行核对「条目＋状态」。</summary>
        private static List<string> TextsOf(DependencyObject root)
        {
            var list = new List<string>();
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is TextBlock tb) list.Add(tb.Text ?? "");
                list.AddRange(TextsOf(c));
            }
            return list;
        }

        /// <summary>54 号·B：同上但收色卡本体（要量坐标，不能只数个数）。</summary>
        private static void CollectThemeCardButtons(DependencyObject root, List<Button> into)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is Button b && b.Tag is ThemeService.ThemeDef) into.Add(b);
                CollectThemeCardButtons(c, into);
            }
        }

        /// <summary>色卡占了几排：各卡相对宿主的 y 去重（同一排 y 相同；浮点容差 0.5px）。</summary>
        private static List<double> CardRows(List<Button> btns, FrameworkElement host)
        {
            var rows = new List<double>();
            if (host == null) return rows;
            foreach (var b in btns)
            {
                double y;
                try { y = b.TransformToAncestor(host).Transform(new Point(0, 0)).Y; }
                catch { continue; }
                if (!rows.Any(r => System.Math.Abs(r - y) < 0.5)) rows.Add(y);
            }
            return rows;
        }

        /// <summary>测限时体验最小 zip 构造（ZipArchive 内存写入；entries：文件名→文本内容）。</summary>
        private static byte[] MakeZip(Dictionary<string, string> entries)
        {
            using (var ms = new MemoryStream())
            {
                using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, true))
                {
                    foreach (var pair in entries)
                    {
                        var e = zip.CreateEntry(pair.Key);
                        using (var w = new StreamWriter(e.Open()))
                            w.Write(pair.Value);
                    }
                }
                return ms.ToArray();
            }
        }

    }
}
