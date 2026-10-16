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
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    /// <summary>
    /// 49 号（V4.9.3 关于页改版）断言，四块一一对应开工令：
    ///   版1 标题区 + 三按钮行（官网 / 打开数据目录 / 打开日志）；
    ///   版2 自动检查更新（三枚设置新键 + 注入时钟的 24 小时判定 + 托盘气泡 + 失败静默 + 自检不起定时器）；
    ///   版3 更新渠道（stable / preview 的预发布解析矩阵）；
    ///   版4 立即检查（内联结果六态、白名单拒绝分支、检查中防连点）。
    ///
    /// 无头环境的限制（按 40/41/44/45/46 号先例如实报备）：
    ///   ① 自检进程 <see cref="App.GateMode"/> 为真，自动检查的定时器本来就不该起——这既是限制也是本令要断的一条；
    ///      定时器"到点就查"因此改断 <see cref="AutoUpdate.Due"/> 与 <see cref="AutoUpdate.RunOnceAsync"/> 这两段实体（tick 调的就是它们）。
    ///   ② 不真开浏览器、不真开资源管理器（注入 AboutLinks 两个 Override），但"目录不存在先创建"是真的（在 %TEMP% 里建出来再验）。
    ///   ③ 不打真网络（UpdateChecker.FetchOverride）、不真弹气泡（AutoUpdate.BubbleOverride）。
    ///      真机才验：浏览器真的起来、资源管理器真的开到那两个目录、气泡真的弹出、启动 30 秒后真的自己查了一次。
    /// 红线复核：打印管线与 40~47 号成果零改动；48 号的 UpdateChecker 判定/分类/白名单与 NoticeFor 文案原样复用
    /// （只加了带渠道的 overload，两参版逐字同行为）。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>自检用的假时钟（注入 <see cref="AutoUpdate.Clock"/>）。</summary>
        private sealed class FixedClock : IClock
        {
            public DateTime Moment;
            public DateTime UtcNow { get { return Moment; } }
        }

        /// <summary>取资源键当前值（断言里用它比对，中英两边自动跟着 App.ApplyLanguage 走）。</summary>
        private static string K(string key)
        {
            var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
            return o as string ?? key;
        }

        internal static string V493Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            var mode0 = Application.Current.ShutdownMode;
            var clock0 = AutoUpdate.Clock;
            bool auto0 = SettingsService.AutoCheckUpdate;
            var chan0 = SettingsService.UpdateChannel;
            string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV493-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(dir);
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                sb.AppendLine("======== 49 号·版1 标题区与构建行（纯函数逐数核对）+ 域名常量清单 ========");
                bad.AddRange(V493Pure(sb));
                sb.AppendLine("======== 49 号·版1 关于页窗口：三枚按钮在位、注入启动器断 URL、目录两条分支 ========");
                bad.AddRange(V493Page(sb, dir));
                sb.AppendLine("======== 49 号·版2/版3 设置三枚新键：默认值、真文件往返、老配置无键 ========");
                bad.AddRange(V493Settings(sb));
                sb.AppendLine("======== 49 号·版2 自动检查：注入时钟三例、写回时刻、失败静默、气泡内容、自检不起表 ========");
                bad.AddRange(V493Auto(sb));
                sb.AppendLine("======== 49 号·版3 更新渠道：预发布解析矩阵（stable 跳过／preview 参与／畸形不崩）========");
                bad.AddRange(V493Channel(sb));
                sb.AppendLine("======== 49 号·版4 立即检查：内联六态中英各一遍、白名单拒绝、检查中防连点 ========");
                bad.AddRange(V493Inline(sb));

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("V4.93 异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally
            {
                AutoUpdate.Clock = clock0;
                UpdateChecker.FetchOverride = null;
                AboutLinks.OpenUrlOverride = null;
                AboutLinks.OpenFolderOverride = null;
                AutoUpdate.BubbleOverride = null;
                SettingsService.SetAutoCheckUpdate(auto0);
                SettingsService.SetUpdateChannel(chan0);
                Application.Current.ShutdownMode = mode0;
                try { Directory.Delete(dir, true); } catch { }
            }

            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V493关于页定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（版1 标题区与构建行逐数核对（哈希只取 8 位、无日期不留空括号、读不到就不显示构建段）＋四枚按钮对象实取与逻辑树实数、两枚网址按钮断到具体 URL、两枚目录按钮断到\"不存在先创建\"与\"断路径不弹框\"两条分支；"
                  + "版2 默认开、关掉存 false 重启读回 false、老配置无键按 true、注入时钟三例（超 24h 触发并写回 LastAutoCheckUtc／24h 内不触发／禁用不触发）、"
                  + "失败只写日志界面零动作、气泡文案含版本号、四项自检模式不起定时器；"
                  + "版3 stable 跳过预发布、preview 按主数字参与且展示保留原文、不比 48 号更激进（主数字不大于当前仍判已是最新）、畸形串两渠道都跳过不崩、默认 stable 与切 preview 真文件往返；"
                  + "版4 内联六态中英各一遍（失败两态与读不懂态零异常原文上屏）、只有发现新版本才出「打开下载页」、白名单拒绝分支不打开只提示、检查中按钮禁点且第二次调用不重入；"
                  + "48 号 UpdateChecker 判定/分类/白名单与 NoticeFor 文案原样复用，打印与 40~47 号成果零改动）"
                : "失败：" + string.Join("；", bad);
        }

        // ===== 版1 ①：标题区与构建行（纯函数） =====
        private static List<string> V493Pure(StringBuilder sb)
        {
            var bad = new List<string>();
            string normal = AboutPage.FormatBuildLine("4.9.3+af8a606a678b16c657da9d94913d55200ebb3ba3", "4.9.3.0", "2026-10-03");
            string nohash = AboutPage.FormatBuildLine("4.9.3", "4.9.3.0", "2026-10-03");
            string nodate = AboutPage.FormatBuildLine("4.9.3+abc123def", "4.9.3.0", "");
            string none = AboutPage.FormatBuildLine(null, "4.9.3.0", "2026-10-03");
            string blank = AboutPage.FormatBuildLine("   ", "4.9.3.0", "2026-10-03");
            string live = AboutPage.BuildInfoText();
            sb.AppendLine("— 构建行：正常=\"" + normal + "\"　无哈希=\"" + nohash + "\"　无日期=\"" + nodate
                + "\"　读不到＝\"" + none + "\"　空白＝\"" + blank + "\"");
            sb.AppendLine("— 本机构建行（真读程序集特性）：\"" + live + "\"　标题行=\"" + AboutPage.AppTitleLine() + "\"");
            Check(bad, "版1 构建行＝「构建 四段号+哈希前 8 位（日期）」（40 位全哈希不上屏）",
                normal.Contains("4.9.3.0+af8a606a") && !normal.Contains("af8a606a678b") && normal.Contains("2026-10-03"));
            Check(bad, "版1 没有 \"+\" 时不给加号（构建 4.9.3.0（日期））", nohash.Contains("4.9.3.0") && !nohash.Contains("+"));
            Check(bad, "版1 日期取不到时括号整段省掉（不留空括号），哈希超过 8 位截到 8 位", !nodate.Contains("（）") && nodate.Contains("abc123de") && !nodate.Contains("abc123def"));
            Check(bad, "版1 解析失败（InformationalVersion 读不到／空白）→ 整行空，只剩标题区的版本号", none == "" && blank == "");
            Check(bad, "版1 真读一遍本机构建行：不为空且含四段版本号", !string.IsNullOrWhiteSpace(live) && live.Contains(AppVersion.Full));
            // 商用线 V1.0.0（51 号）就地改判：版本号改判 1.0.0.0，标题行期望 v1.0（原断言 Contains("v4.9") 作废，条目不删）。
            // 72 号令再就地改判（条目不删）：v1.1.0 起版本进位，期望值改从 AppVersion.Display 取（一处事实源），
            // 并加钉"三段 v主.次.修订"的形状——这样下次进位不会再红，而硬编码应用名/两段号仍会被抓住。
            string disp = AppVersion.Display;
            int dots = disp.Length - disp.Replace(".", "").Length;
            Check(bad, "V1.0.0 改判·版1 标题行＝应用名 + v主.次.修订（两边都从资源键取，不硬编码应用名；72 号起期望值取 AppVersion.Display 不钉死字面版本号）",
                AboutPage.AppTitleLine().StartsWith(K("s.AppTitle")) && AboutPage.AppTitleLine().Contains(disp)
                && disp.StartsWith("v", StringComparison.Ordinal) && dots == 2);

            sb.AppendLine("— 域名/网址字面量清单（54 号令·B 起：主路基址唯一出处＝AboutLinks.SiteBase，Pages 降兜底）：ApiBase=" + UpdateChecker.ApiBase
                + "　兜底域=" + AboutLinks.SiteBaseCf
                + "　Legacy=" + UpdateChecker.LegacyApiBase
                + "　反馈页=" + FeedbackSite.UrlBase
                + "　官网=" + AboutLinks.HomeUrl
                + "　license.html 那一页深链已随 73 号令删除（AboutLinks 不再有 ContactUrl）");
            Check(bad, "版1 官网与反馈页同域不同用途（首页不带 /feedback.html）；64 号改判：printpile.cn 上线（主路/兜底=同站双域），仍钉派生关系与兜底常量字面值",
                AboutLinks.HomeUrl == AboutLinks.SiteBase
                && FeedbackSite.UrlBase.StartsWith(AboutLinks.HomeUrl) && FeedbackSite.UrlBase != AboutLinks.HomeUrl
                && AboutLinks.SiteBaseCf == "https://www.printpile.cn");
            // 59 号令（2026-10-09 需求方拍板闭源）就地改判，条目不删：原「版1 GitHub 地址＝仓库根」断的是 AboutLinks.GitHubUrl，
            // 该常量与「GitHub」按钮、s.AboutBtnGitHub／s.AboutOpenedGitHub 两对键已一并删除——本条改断"清零彻底"：
            // 中英两本资源字典里都不再存在这两枚键（缺一条即视为删不干净，留着就是死键）。
            bool ghKeysGone = true;
            foreach (string dict in new[] { "Localization/zh-CN.xaml", "Localization/en-US.xaml" })
            {
                try
                {
                    var rd = new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) };
                    if (rd.Contains("s.AboutBtnGitHub") || rd.Contains("s.AboutOpenedGitHub")) ghKeysGone = false;
                }
                catch { ghKeysGone = false; }
            }
            Check(bad, "版1 GitHub 入口（59 号改判为『清零断言』）：GitHubUrl 常量与 s.AboutBtnGitHub／s.AboutOpenedGitHub 两对键全工程已删净，中英同一份口径",
                ghKeysGone);
            Check(bad, "版1 数据目录与日志目录都取自既有口径（便携根下 data\\ 与 FileLogger 的日志目录，不另起一套）",
                AboutLinks.DataDirectory == AppPaths.DataDir && AboutLinks.LogDirectory == FileLogger.LogDirectory);
            return bad;
        }

        // ===== 版1 ②：关于页窗口上的四枚按钮 =====
        private static List<string> V493Page(StringBuilder sb, string dir)
        {
            var bad = new List<string>();
            var w = new MainWindow();
            w.ShowPanelForTest("about");
            var page = new AboutPage();
            try
            {
                var site = page.SiteButtonForTest;
                var gh = page.GitHubButtonForTest;      // 78 号·B3：开源仓库双按钮回归（59 号曾随闭源口径移除）
                var gt = page.GiteeButtonForTest;
                var data = page.DataButtonForTest;
                var log = page.LogButtonForTest;
                int inTree = CountTextButtons(page);
                sb.AppendLine("— 顶部三枚按钮在位＋仓库双按钮回归（78 号改判：59 号移除的 GitHub 连同新增 Gitee 一并回来）：官网=\"" + (site == null ? "无" : site.Content)
                    + "\"　GitHub=\"" + (gh == null ? "无" : gh.Content) + "\"　Gitee=\"" + (gt == null ? "无" : gt.Content)
                    + "\"　数据目录=\"" + (data == null ? "无" : data.Content) + "\"　日志=\"" + (log == null ? "无" : log.Content)
                    + "\"　逻辑树里数到带文字的按钮=" + inTree);
                Check(bad, "版1 按钮都在关于页里（官网/数据目录/日志三枚对象实取＋逻辑树≥3；78 号改判：GitHub/Gitee 仓库双按钮在位且文字＝品牌名原文、不占资源键）",
                    site != null && gh != null && gt != null && data != null && log != null && inTree >= 5
                    && Equals(site.Content, K("s.AboutBtnSite"))
                    && Equals(data.Content, K("s.AboutBtnData")) && Equals(log.Content, K("s.AboutOpenLog"))
                    && (gh.Content as string) == "GitHub" && (gt.Content as string) == "Gitee");
                Check(bad, "版1 「打开日志」按钮文字已改口成\"打开日志\"（原 expander 里那枚同名按钮随块移除，键值沿用）",
                    log != null && (string)log.Content == "打开日志");

                // 网址按钮（59 号起只剩「官网」一枚）：注入启动器，断"打开的就是那个地址"，成功只写页内那一行
                var urls = new List<string>();
                AboutLinks.OpenUrlOverride = u => urls.Add(u);
                string n1 = page.PressSite();
                sb.AppendLine("— 注入启动器后依次收到：" + string.Join(" , ", urls));
                sb.AppendLine("　状态行1=\"" + n1 + "\"");
                Check(bad, "版1 「官网」＝用系统浏览器打开官网首页（注入断 URL；59 号改判：只剩这一枚网址按钮，GitHub 已移除故调用次数＝1）",
                    urls.Count == 1 && urls[0] == AboutLinks.HomeUrl && n1.Contains(AboutLinks.HomeUrl));
                Check(bad, "版1 网址按钮的结果只落在页内那一行（LinkNoteText 可见且等于最后一句）——没有走任何弹窗",
                    page.LinkNoteShownForTest && page.LinkNoteForTest == n1);
                AboutLinks.OpenUrlOverride = null;

                // 打开日志：目录本来就在，断"不新建也能打开、状态行不写已新建"
                var seen = new List<string>();
                AboutLinks.OpenFolderOverride = d => seen.Add(d);
                string n3 = page.PressLog();
                Check(bad, "版1 「打开日志」＝资源管理器打开日志目录（41 号修E 保留的那个入口，如今由这枚按钮承接）",
                    seen.Count == 1 && seen[0] == FileLogger.LogDirectory && n3.Contains(FileLogger.LogDirectory)
                    && !n3.Contains(K("s.AboutCreatedFmt")));

                // 不存在先创建（真的建出来）／已存在不重复建
                string probe = Path.Combine(dir, "新数据目录");
                var o1 = AboutLinks.OpenFolder(probe, true);
                var o2 = AboutLinks.OpenFolder(probe, true);
                sb.AppendLine("— 目录分支：不存在先创建 Ok=" + o1.Ok + " Created=" + o1.Created + " 目录真的生成=" + Directory.Exists(probe)
                    + "　第二次 Created=" + o2.Created);
                Check(bad, "版1 「不存在先创建」分支是真的：目录被建出来、结果里 Created=true、再点一次不重复创建",
                    o1.Ok && o1.Created && Directory.Exists(probe) && o2.Ok && !o2.Created);

                // 断路径：拿一个"同名文件挡路"的坏路径 → 创建必失败，断它只回错误、不抛不弹
                string blocker = Path.Combine(dir, "挡路的文件.bin");
                File.WriteAllText(blocker, "x");
                var o3 = AboutLinks.OpenFolder(blocker + "\\子目录", true);
                string n4 = page.ShowLinkNote(o3, "s.AboutOpenedData");
                sb.AppendLine("— 断路径分支：Ok=" + o3.Ok + "　Error 非空=" + !string.IsNullOrEmpty(o3.Error) + "　页内状态行=\"" + n4 + "\"");
                Check(bad, "版1 断路径不抛异常、不弹框，只回 Ok=false 并在页内说一句\"没能打开\"（带路径）",
                    !o3.Ok && !string.IsNullOrEmpty(o3.Error) && n4.Contains(blocker));
                AboutLinks.OpenFolderOverride = null;

                // 41 号修E 那一行说明仍在（49 号起版本展示由标题区+构建行承担，这条不删）
                Check(bad, "版1 41 号修E 的「检查更新」行说明仍在位（键未删、句里含当前版本号）",
                    !string.IsNullOrEmpty(page.UpdateDescForTest) && page.UpdateDescForTest.Contains(AppVersion.Full));
                // 52 号令·返修② 就地改判：版3 渠道下拉已随整卡移除，原"开关与渠道下拉都在位"断言改为
                // "自动检查开关仍在位（初值取自 settings）＋渠道卡已移除（FindName("ChannelBox") 落空）"。
                Check(bad, "V1.0.0·52 号改判：版2 自动检查开关在位（初值取自 settings）；版3 渠道下拉已按拍板移除（ChannelBox 不在名域）",
                    page.AutoSwitchForTest != null
                    && page.AutoCheckOnForTest == SettingsService.AutoCheckUpdate
                    && page.FindName("ChannelBox") == null);
            }
            catch (Exception ex) { bad.Add("版1 窗口异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally
            {
                AboutLinks.OpenUrlOverride = null;
                AboutLinks.OpenFolderOverride = null;
                // 不 Close：41 号起 MainWindow 的 ✕ 走模态提示窗，无头里一 Close 就把整轮挂死（45 号踩过）
            }
            return bad;
        }

        /// <summary>逻辑树里数"带文字的按钮"（无头下可视树没建，VisualTreeHelper 走不通——46 号同一口径）。</summary>
        private static int CountTextButtons(DependencyObject root)
        {
            int n = 0;
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) n += CountTextButtons(child);
            var b = root as Button;
            if (b != null && b.Content is string) n++;
            return n;
        }

        // ===== 版2/版3：三枚设置新键（真 settings.json 往返） =====
        private static List<string> V493Settings(StringBuilder sb)
        {
            var bad = new List<string>();
            string path = AppPaths.SettingsPath;
            string backup = null;
            try { if (File.Exists(path)) backup = File.ReadAllText(path); } catch { }
            try
            {
                Check(bad, "版2 老配置无 AutoCheckUpdate 键 → 按默认开（ParseAutoCheck(null)=true，关掉才 false）",
                    SettingsService.ParseAutoCheck(null) && SettingsService.ParseAutoCheck(true) && !SettingsService.ParseAutoCheck(false));
                Check(bad, "版3 老配置无 UpdateChannel 键 → 正式版；未知值也回落正式版（大小写不敏感）",
                    SettingsService.ParseChannel(null) == UpdateChannel.Stable && SettingsService.ParseChannel("beta") == UpdateChannel.Stable
                    && SettingsService.ParseChannel("Preview") == UpdateChannel.Preview);
                Check(bad, "版2 时刻串写坏了当\"没查过\"（宁可多查一次，也不能永远不再查）",
                    SettingsService.ParseUtc("昨天下午") == null && SettingsService.ParseUtc(null) == null);

                SettingsService.SetAutoCheckUpdate(true);
                SettingsService.SetUpdateChannel(UpdateChannel.Stable);
                SettingsService.SetAutoCheckUpdate(false);
                SettingsService.SetUpdateChannel(UpdateChannel.Preview);
                var t = new DateTime(2026, 10, 3, 8, 9, 10, DateTimeKind.Utc);
                SettingsService.SetLastAutoCheckUtc(t);
                SettingsService.Load();
                string json = "";
                try { json = File.ReadAllText(path); } catch { }
                sb.AppendLine("— 真文件往返：AutoCheckUpdate=" + SettingsService.AutoCheckUpdate + "　渠道=" + SettingsService.UpdateChannel
                    + "　上次时刻=" + SettingsService.LastAutoCheckUtc
                    + "　文件里三枚键=" + (json.Contains("AutoCheckUpdate") ? "在" : "缺")
                    + "/" + (json.Contains("UpdateChannel") ? "在" : "缺") + "/" + (json.Contains("LastAutoCheckUtc") ? "在" : "缺"));
                Check(bad, "版2 关闭后存 false、重启读回 false（真 settings.json 往返，不是内存里的值）",
                    SettingsService.AutoCheckUpdate == false && json.Contains("\"AutoCheckUpdate\":false"));
                Check(bad, "版3 切到测试版后持久化往返仍是 preview（文件里写小写键值，与 ParseChannel 一一对应）",
                    SettingsService.UpdateChannel == UpdateChannel.Preview && json.Contains("\"preview\""));
                Check(bad, "版2 发起时刻按 ISO 往返到秒", SettingsService.LastAutoCheckUtc.HasValue && SettingsService.LastAutoCheckUtc.Value == t);

                File.WriteAllText(path, "{\"Theme\":\"light\",\"Language\":\"zh\",\"PrintReport\":false,\"HistoryLimit\":\"200\",\"CloseBehavior\":\"ask\"}");
                SettingsService.Load();
                sb.AppendLine("— 老配置（完全没有这三枚键）读回：AutoCheckUpdate=" + SettingsService.AutoCheckUpdate + "　渠道=" + SettingsService.UpdateChannel
                    + "　上次时刻=" + (SettingsService.LastAutoCheckUtc == null ? "没查过" : "有值"));
                Check(bad, "版2/版3 老配置升级：自动检查＝开、渠道＝正式版、上次时刻＝没查过（第一次加载就会查）",
                    SettingsService.AutoCheckUpdate && SettingsService.UpdateChannel == UpdateChannel.Stable && SettingsService.LastAutoCheckUtc == null);
            }
            catch (Exception ex) { bad.Add("版2/版3 设置往返异常 " + ex.Message); }
            finally
            {
                try { if (backup != null) File.WriteAllText(path, backup); SettingsService.Load(); } catch { }
            }
            return bad;
        }

        // ===== 版2：自动检查（注入时钟 + 注入网络 + 注入气泡） =====
        private static List<string> V493Auto(StringBuilder sb)
        {
            var bad = new List<string>();
            string path = AppPaths.SettingsPath;
            string backup = null;
            try { if (File.Exists(path)) backup = File.ReadAllText(path); } catch { }
            var clock = new FixedClock { Moment = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc) };
            try
            {
                AutoUpdate.Clock = clock;
                var cur = AutoUpdate.CurrentVersion();

                bool d1 = AutoUpdate.Due(true, null, clock.Moment);
                bool d2 = AutoUpdate.Due(true, clock.Moment.AddHours(-23), clock.Moment);
                bool d3 = AutoUpdate.Due(true, clock.Moment.AddHours(-24).AddMinutes(-1), clock.Moment);
                bool d4 = AutoUpdate.Due(false, clock.Moment.AddHours(-72), clock.Moment);
                sb.AppendLine("— Due 四例：没查过=" + d1 + "　23 小时前=" + d2 + "　24 小时 01 分前=" + d3 + "　关掉且 72 小时前=" + d4);
                Check(bad, "版2 注入时钟三例：启用且超 24h→到点；24h 内→不到点；关掉→永不到点（哪怕 9 天没查）", d1 && !d2 && d3 && !d4);

                // ① 到点 → 真发起一次 + 写回时刻 + 出气泡
                SettingsService.SetAutoCheckUpdate(true);
                SettingsService.SetLastAutoCheckUtc(clock.Moment.AddHours(-30));
                SettingsService.Load();
                int hits = 0;
                string foundJson = "{\"versions\":[{\"version\":\"9.9.9.9\",\"date\":\"2026-10-02\",\"url\":\"" + UpdateChecker.ApiBase + "/dl/a.exe\",\"active\":true}]}";
                UpdateChecker.FetchOverride = u => { hits++; return Task.FromResult(foundJson); };
                var bubbles = new List<string>();
                AutoUpdate.BubbleOverride = (title, text) => bubbles.Add(title + "|" + text);
                bool ran = AutoUpdate.RunOnceAsync(cur).GetAwaiter().GetResult();
                SettingsService.Load();
                sb.AppendLine("— 超 24h 发起：RunOnce=" + ran + "　网络请求次数=" + hits + "　写回后重读=" + SettingsService.LastAutoCheckUtc);
                sb.AppendLine("　气泡：" + string.Join(" / ", bubbles));
                Check(bad, "版2 到点确实发起一次（网络计数 +1）并把 LastAutoCheckUtc 写回 settings（重读一致）",
                    ran && hits == 1 && SettingsService.LastAutoCheckUtc == clock.Moment);
                Check(bad, "版2 发现新版本 → 托盘气泡一条：标题走「检查更新」键、正文含版本号且说\"关于页可查看\"（不提自动下载）",
                    bubbles.Count == 1 && bubbles[0].Contains("9.9.9.9")
                    && bubbles[0] == K("s.AboutUpdateTitle") + "|" + AutoUpdate.BubbleTextForTest("9.9.9.9")
                    && !bubbles[0].Contains("下载并安装"));

                // ② 24h 内 → 不触发
                UpdateChecker.FetchOverride = u => { hits++; return Task.FromResult("{\"versions\":[]}"); };
                bool ran2 = AutoUpdate.RunOnceAsync(cur).GetAwaiter().GetResult();
                sb.AppendLine("— 刚查过（同一时刻）：RunOnce=" + ran2 + "　网络请求次数仍=" + hits);
                Check(bad, "版2 24 小时内再跑一次＝不触发（不发网络、不写回）", !ran2 && hits == 1);

                // ③ 关掉开关 → 9 天没查也不触发
                SettingsService.SetAutoCheckUpdate(false);
                SettingsService.SetLastAutoCheckUtc(clock.Moment.AddDays(-9));
                bool ran3 = AutoUpdate.RunOnceAsync(cur).GetAwaiter().GetResult();
                sb.AppendLine("— 开关关掉（9 天没查）：RunOnce=" + ran3 + "　网络请求次数仍=" + hits);
                Check(bad, "版2 开关关掉 → 即使 9 天没查也不触发", !ran3 && hits == 1);
                SettingsService.SetAutoCheckUpdate(true);

                // ④ 失败静默：日志有、界面零动作
                bubbles.Clear();
                UpdateChecker.FetchOverride = u => { throw new HttpRequestException("No such host is known."); };
                bool ran4 = false;
                try { ran4 = AutoUpdate.RunOnceAsync(cur).GetAwaiter().GetResult(); }
                catch (Exception ex) { bad.Add("版2 自动检查失败把异常抛到了外面（应当静默）：" + ex.Message); }
                string log = ReadRecentLogs("自动检查更新失败");
                SettingsService.Load();
                sb.AppendLine("— 失败静默：RunOnce=" + ran4 + "　气泡新增=" + bubbles.Count + "　日志留有失败行="
                    + (log.Contains("自动检查更新失败") ? "是" : "否") + "　时刻仍写回=" + (SettingsService.LastAutoCheckUtc == clock.Moment));
                Check(bad, "版2 检查失败＝只写日志：不抛异常、不出气泡、不做任何界面动作",
                    ran4 && bubbles.Count == 0 && log.Contains("自动检查更新失败"));
                Check(bad, "版2 失败那一次也写回发起时刻（否则后端抖一小时，界面就每小时重试打它一次）",
                    SettingsService.LastAutoCheckUtc == clock.Moment);

                // ⑤ 已是最新 → 什么都不做（不出气泡）
                bubbles.Clear();
                SettingsService.SetLastAutoCheckUtc(clock.Moment.AddHours(-25));
                UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[]}");
                AutoUpdate.RunOnceAsync(cur).GetAwaiter().GetResult();
                Check(bad, "版2 已是最新＝不提示不打扰（气泡零条）", bubbles.Count == 0);

                // ⑥ 四项自检模式不起定时器（重复 Start 也不叠第二个）
                bool gate = App.GateMode;
                AutoUpdate.Start();
                bool running = AutoUpdate.RunningForTest;
                AutoUpdate.Start();
                sb.AppendLine("— 定时器：GateMode=" + gate + "　调 Start 之后 Running=" + running + "（再调一次仍=" + AutoUpdate.RunningForTest + "）");
                Check(bad, "版2 四项自检/对话框模式不起自动检查定时器（自检里 MainWindow 反复构造，起表就会真发网络；重复 Start 也不叠第二个）",
                    gate && !running && !AutoUpdate.RunningForTest);
                // 52 号令·返修② 就地改判：60 分钟周期 tick 已移除——只断 30 秒首查与 24h 闸门。
                Check(bad, "V1.0.0·52 号改判·版2 首查 30 秒、24h 闸门写死（不可配置）；60 分钟周期 tick 已按拍板移除",
                    AutoUpdate.FirstDelaySeconds == 30 && AutoUpdate.Interval == TimeSpan.FromHours(24));
            }
            catch (Exception ex) { bad.Add("版2 异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally
            {
                UpdateChecker.FetchOverride = null;
                AutoUpdate.BubbleOverride = null;
                AutoUpdate.Clock = new SystemClock();
                try { if (backup != null) File.WriteAllText(path, backup); SettingsService.Load(); } catch { }
            }
            return bad;
        }

        /// <summary>
        /// 读最近的日志文本。两个坑：① Serilog 的共享文件汇一直持有写句柄，<c>File.ReadAllText</c> 会抛
        /// "being used by another process"（V4.9 修6 就撞过一次）→ 走既有的 <c>FileLogger.RecentLines</c>；
        /// ② 落盘有 flush 间隔 → 最多等 1.5 秒再判"日志里没有"。
        /// </summary>
        private static string ReadRecentLogs(string mustContain)
        {
            for (int i = 0; i < 6; i++)
            {
                string all = "";
                try { all = FileLogger.RecentLines(2000); } catch { }
                if (all.Contains(mustContain)) return all;
                try { System.Threading.Thread.Sleep(250); } catch { }
            }
            try { return FileLogger.RecentLines(2000); } catch { return ""; }
        }

        // ===== 版3 → 52 号令·返修② 改判：更新渠道卡整卡移除、渠道锁定 stable、自动检查改"每天首次打开一次" =====
        private static List<string> V493Channel(StringBuilder sb)
        {
            var bad = new List<string>();
            // 原版3 的 preview 解析矩阵就地降级为"解析函数仍可用的单元回归"（TryPrereleaseVersion／带渠道重载保留——
            // 两参版与 previewChannel=false 逐字同行为，是 48 号那批断言的地基）；原 preview 渠道 UI 断言作废（条目不删），
            // 补三条新断言：UI 无渠道卡、UpdateChannel 键读取兼容（行为恒 stable）、同日第二次启动零请求。
            string url = UpdateChecker.ApiBase + "/dl/a.exe";
            string betaNow = "{\"versions\":[{\"version\":\"4.9.3.0-beta1\",\"date\":\"2026-10-01\",\"url\":\"" + url + "\",\"active\":true}]}";
            string betaNewer = "{\"versions\":[{\"version\":\"4.9.4-beta1\",\"date\":\"2026-10-02\",\"url\":\"" + url + "\",\"active\":true}]}";
            string malformed = "{\"versions\":["
                + "{\"version\":\"-beta\",\"date\":\"\",\"url\":\"" + url + "\",\"active\":true},"
                + "{\"version\":\"beta1\",\"date\":\"\",\"url\":\"" + url + "\",\"active\":true}]}";
            string oddSuffix = "{\"versions\":[{\"version\":\"4.9.9.9-not-a-number\",\"date\":\"\",\"url\":\"" + url + "\",\"active\":true}]}";
            var cur492 = new Version(4, 9, 2, 0);
            var cur493 = new Version(4, 9, 3, 0);

            var s1 = UpdateChecker.NewestUsable(betaNow, cur492, false);
            var m2 = SafeNewest(malformed, cur492, true);
            var m3 = SafeNewest(malformed, cur492, false);
            var o2 = SafeNewest(oddSuffix, cur492, false);
            sb.AppendLine("— 52 号改判后矩阵（只保 stable 口径）：本地 4.9.2.0 见 4.9.3.0-beta1 → " + Name(s1)
                + "；畸形清单两例 → " + Name(m2) + "/" + Name(m3) + "；4.9.9.9-not-a-number → " + Name(o2));
            Check(bad, "V1.0.0·52 号改判·版3 stable 口径：预发布条目一律跳过（4.9.3.0-beta1 → 判已是最新）", s1 == null);
            Check(bad, "V1.0.0·52 号改判·版3 畸形串两例（\"-beta\"／\"beta1\"）跳过且不崩", m2 == null && m3 == null);
            Check(bad, "V1.0.0·52 号改判·版3 后缀不讲究字面（4.9.9.9-not-a-number）stable 照旧跳过", o2 == null);
            Version vv;
            Check(bad, "V1.0.0·52 号改判·版3 解析函数逐例（保留作单元回归）：4.9.3.0-beta1→4.9.3.0；v4.9-rc2→4.9；\"-beta\"→false；空串→false",
                UpdateChecker.TryPrereleaseVersion("4.9.3.0-beta1", out vv) && vv == new Version(4, 9, 3, 0)
                && UpdateChecker.TryPrereleaseVersion("v4.9-rc2", out vv) && vv.Major == 4 && vv.Minor == 9
                && !UpdateChecker.TryPrereleaseVersion("-beta", out vv) && !UpdateChecker.TryPrereleaseVersion("", out vv));

            // —— 52 号·返修② 新断言①：渠道卡不在位（ChannelBox 控件与「更新渠道」标题文字均不在关于页） ——
            var mode0 = Application.Current.ShutdownMode;
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            App.ApplyTheme(AppTheme.Light);
            App.ApplyLanguage(AppLanguage.ZhCN);
            var vPage = new AboutPage();
            var channelBox = vPage.FindName("ChannelBox") as ComboBox;
            string channelTitle = "更新渠道";   // s.AboutChannelTitle 键已随渠道卡删除，这里只用字面兜底扫描
            bool cardGone = channelBox == null && !WpfTreeText(vPage, channelTitle);
            sb.AppendLine("— 52 号·返修②：关于页 ChannelBox=" + (channelBox == null ? "不存在" : "仍在！")
                + "；渠道标题文字在页内=" + (channelBox != null || WpfTreeText(vPage, channelTitle)));
            Check(bad, "V1.0.0·52 号·返修②：关于页不再有「更新渠道」卡片（ChannelBox 控件与「更新渠道」标题字样均不在可视树）", cardGone);
            Application.Current.ShutdownMode = mode0;

            // —— 新断言②：UpdateChannel 键读取兼容（存量 preview 读回枚举）但行为锁定 stable（预发布条目不参与） ——
            string cfgOld = SnapshotCfg();
            WriteCfg("{\"Theme\":\"light\",\"UpdateChannel\":\"preview\",\"AutoCheckUpdate\":true}");
            SettingsService.Load();
            bool legacyReadBack = SettingsService.UpdateChannel == UpdateChannel.Preview;
            UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[{\"version\":\"5.0.0.0-preview1\",\"date\":\"2026-10-06\",\"url\":\"" + url + "\",\"active\":true}]}");
            var rStable = UpdateChecker.CheckAsync(new Version(1, 0, 0, 0)).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            RestoreCfg(cfgOld);
            sb.AppendLine("— 52 号·返修②：存量 preview 键读回=" + legacyReadBack
                + "（枚举兼容）；锁定 stable 后 5.0.0.0-preview1 不参与（检查结果=" + rStable.Outcome + "）");
            Check(bad, "V1.0.0·52 号·返修②：UpdateChannel 键保留读取兼容（preview 读回枚举）但行为锁定 stable（预发布条目不参与、不崩）",
                legacyReadBack && rStable.Outcome == UpdateChecker.Outcome.UpToDate);

            // —— 新断言③：每日一查——同日第二次启动零请求（Due 闸门），隔日才再查，发起即写回（含失败那次） ——
            var clock0 = AutoUpdate.Clock;
            var clock = new FixedClock { Moment = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc) };
            AutoUpdate.Clock = clock;
            try
            {
                bool auto0 = SettingsService.AutoCheckUpdate;
                DateTime? last0 = SettingsService.LastAutoCheckUtc;
                SettingsService.SetAutoCheckUpdate(true);
                SettingsService.SetLastAutoCheckUtc(new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc));   // 7 小时前查过
                int requests = 0;
                UpdateChecker.FetchOverride = u => { requests++; return Task.FromResult("{\"versions\":[]}"); };
                bool fired = AutoUpdate.RunOnceAsync(new Version(1, 0, 0, 0)).GetAwaiter().GetResult();
                bool suppressed = fired == false && requests == 0;
                clock.Moment = clock.Moment.AddDays(1);   // 隔日
                bool firedNext = AutoUpdate.RunOnceAsync(new Version(1, 0, 0, 0)).GetAwaiter().GetResult();
                UpdateChecker.FetchOverride = null;
                DateTime? written = SettingsService.LastAutoCheckUtc;
                if (last0.HasValue) SettingsService.SetLastAutoCheckUtc(last0.Value);
                SettingsService.SetAutoCheckUpdate(auto0);
                sb.AppendLine("— 52 号·返修②：同日二次（7h 前）fired=" + fired + " 请求数=" + requests
                    + "；隔日 fired=" + firedNext + " 累计请求=" + requests + "；发起写回=" + (written != null));
                Check(bad, "V1.0.0·52 号·返修②：同日第二次启动零请求（Due 闸门挡住），隔日才再查一次且发起即写回",
                    suppressed && firedNext && requests == 1 && written != null);
            }
            finally { AutoUpdate.Clock = clock0; UpdateChecker.FetchOverride = null; }
            return bad;
        }

        /// <summary>可视树全文是否含指定文字（渠道卡移除断言用）。</summary>
        private static bool WpfTreeText(DependencyObject root, string text)
        {
            if (root == null) return false;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is System.Windows.Controls.TextBlock tb && (tb.Text ?? "").Contains(text)) return true;
                if (WpfTreeText(c, text)) return true;
            }
            return false;
        }

        private static UpdateChecker.Entry SafeNewest(string json, Version cur, bool preview)
        {
            try { return UpdateChecker.NewestUsable(json, cur, preview); }
            catch (Exception ex) { FileLogger.Write("V493Channel 解析崩了：" + ex.Message); throw; }
        }

        private static string Name(UpdateChecker.Entry e) { return e == null ? "已是最新" : e.Version; }

        private static bool Same(UpdateChecker.Entry a, UpdateChecker.Entry b)
        {
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            return a.Version == b.Version && a.Url == b.Url;
        }

        // ===== 版4：立即检查的内联呈现 =====
        private static List<string> V493Inline(StringBuilder sb)
        {
            var bad = new List<string>();
            var cur = typeof(AboutPage).Assembly.GetName().Version;
            string url = UpdateChecker.ApiBase + "/dl/Setup.exe";
            var states = new List<KeyValuePair<string, UpdateChecker.Result>>();
            try
            {
                UpdateChecker.FetchOverride = u => { throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 8 seconds elapsing."); };
                states.Add(Pair("超时", UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult()));
                UpdateChecker.FetchOverride = u => { throw new HttpRequestException("No such host is known. (printpile.cn:443)"); };
                states.Add(Pair("网络", UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult()));
                UpdateChecker.FetchOverride = u => Task.FromResult("这不是 JSON");
                states.Add(Pair("读不懂", UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult()));
                UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[]}");
                states.Add(Pair("已是最新", UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult()));
                UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[{\"version\":\"4.9.4.0\",\"date\":\"2026-10-05\",\"url\":\"" + url + "\",\"notes\":\"修了三处超界提示\",\"active\":true}]}");
                states.Add(Pair("发现新版本", UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult()));
                UpdateChecker.FetchOverride = null;
                states.Add(Pair("空结果", null));

                foreach (AppLanguage lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
                {
                    App.ApplyLanguage(lang);
                    string ls = lang == AppLanguage.EnUS ? "英文" : "中文";
                    var page = new AboutPage();
                    var texts = new Dictionary<string, string>();
                    foreach (var kv in states) texts[kv.Key] = page.ShowInline(kv.Value, cur, "");
                    sb.AppendLine("— 内联六态（" + ls + "）：");
                    foreach (var kv in states) sb.AppendLine("　" + kv.Key + "＝" + (texts[kv.Key] ?? "").Replace("\n", "⏎"));

                    string latest = texts["已是最新"], found = texts["发现新版本"];
                    string timeout = texts["超时"], network = texts["网络"], unrec = texts["读不懂"], nullr = texts["空结果"];
                    Check(bad, "版4 " + ls + "：六态内联文案齐备（已是最新含当前四段版本号、发现新版本含 4.9.4.0 与日期）",
                        latest.Contains(cur.ToString(4)) && found.Contains("4.9.4.0") && found.Contains("2026-10-05")
                        && timeout.Length > 0 && network.Length > 0 && unrec.Length > 0 && nullr.Length > 0);
                    Check(bad, "版4 " + ls + "：失败两态与读不懂态里一行异常原文都没上屏（HttpClient/canceled/Exception/JSON/域名端口都不出现）",
                        !timeout.Contains("HttpClient") && !timeout.Contains("canceled") && !timeout.Contains("TaskCanceled")
                        && !network.Contains("Exception") && !network.Contains("443") && !network.Contains("printpile.cn")
                        && !unrec.Contains("JSON") && !unrec.Contains("Json"));
                    Check(bad, "版4 " + ls + "：除「发现新版本」之外三态内联句逐字等于 48 号 NoticeFor（文案一字未改）",
                        timeout == AboutPage.NoticeFor(Res(states, "超时"), cur) && latest == AboutPage.NoticeFor(Res(states, "已是最新"), cur)
                        && unrec == AboutPage.NoticeFor(Res(states, "读不懂"), cur) && nullr == AboutPage.NoticeFor(null, cur));
                    Check(bad, "版4 " + ls + "：「发现新版本」那句去掉弹窗时代的 YesNo 问句（内联区下面就是「打开下载页」按钮），版本与日期仍在",
                        found.Contains("4.9.4.0") && !found.Contains(K("s.UpdateAskDownload"))
                        && found == AboutPage.NoticeFor(Res(states, "发现新版本"), cur).Replace("\n\n" + K("s.UpdateAskDownload"), ""));

                    // 内联区可见性与「打开下载页」的出现条件（逐个态重放，不依赖字典顺序）
                    page.ShowInline(Res(states, "已是最新"), cur, "");
                    bool latestShown = page.UpdateResultShownForTest && !page.DownloadButtonShownForTest;
                    page.ShowInline(Res(states, "发现新版本"), cur, "");
                    Check(bad, "版4 " + ls + "：只有「发现新版本」才出现「打开下载页」内联小按钮，「已是最新」不出现",
                        latestShown && page.DownloadButtonShownForTest);
                }
                App.ApplyLanguage(AppLanguage.ZhCN);

                var p3 = new AboutPage();
                string auto = p3.ShowInline(Res(states, "发现新版本"), cur, K("s.UpdateAutoPrefix"));
                Check(bad, "版2/版4 自动检查发现新版本 → 关于页内联区同步，并多一句\"自动检查：\"前缀",
                    auto.StartsWith(K("s.UpdateAutoPrefix")) && auto.Contains("4.9.4.0"));

                // 白名单拒绝分支：恶意 url 不打开，只在内联区说一句
                var urls = new List<string>();
                AboutLinks.OpenUrlOverride = u => urls.Add(u);
                var evil = new UpdateChecker.Entry { Version = "5.0.0.0", Date = "2026-11-01", Url = "https://evil.example.com/x.exe", Active = true };
                string after = p3.OpenDownloadForTest(evil);
                AboutLinks.OpenUrlOverride = null;
                sb.AppendLine("— 白名单拒绝分支：内联=\"" + after + "\"　浏览器启动器被调用次数=" + urls.Count);
                Check(bad, "版4 「打开下载页」白名单拒绝分支：恶意 url 一次都不打开（启动器零调用），只在内联区提示失败",
                    urls.Count == 0 && after == K("s.UpdateOpenFailed"));
                Check(bad, "版4 白名单放行分支仍认官网域（48 号口径未回退）", UpdateChecker.IsAllowedUrl(url));

                // 70 号·追补（需求十五）：弱网抗抖——主域请求失败自动切同站备用域，第二候选成功即返回（成功路径不重试）
                int failoverCalls = 0;
                UpdateChecker.FetchOverride = u =>
                {
                    failoverCalls++;
                    if (u.StartsWith(AboutLinks.SiteBase)) throw new HttpRequestException("timed out (simulated)");
                    return Task.FromResult("{\"versions\":[]}");
                };
                var rFailover = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
                UpdateChecker.FetchOverride = null;
                sb.AppendLine("— 弱网抗抖：主域挂→备用域接，调用 " + failoverCalls + " 次，结果=" + rFailover.Outcome);
                Check(bad, "70 号·追补：主域请求失败自动切备用域（第二候选成功即返回，总调用 2 次、结论已是最新）",
                    rFailover.Outcome == UpdateChecker.Outcome.UpToDate && failoverCalls == 2);
                UpdateChecker.FetchOverride = u => { throw new TaskCanceledException("simulated timeout on every candidate"); };
                var rAllFail = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
                UpdateChecker.FetchOverride = null;
                Check(bad, "70 号·追补：全部候选都超时仍按 Timeout 分类给出失败文案（不崩、不静默成功）",
                    rAllFail.Outcome == UpdateChecker.Outcome.Failed && rAllFail.Fail == UpdateChecker.FailKind.Timeout);

                // 检查中 + 防连点：把网络挂住，看中间态与第二次调用
                var tcs = new TaskCompletionSource<string>();
                int hits = 0;
                UpdateChecker.FetchOverride = u => { hits++; return tcs.Task; };
                var first = p3.CheckNowAsync();
                string mid = p3.UpdateResultForTest;
                bool disabled = !p3.CheckButtonEnabledForTest;
                var second = p3.CheckNowAsync();
                bool secondDone = second.IsCompleted;
                sb.AppendLine("— 检查中：内联=\"" + mid + "\"　按钮禁用=" + disabled + "　第二次调用立即返回=" + secondDone + "　网络请求次数=" + hits);
                Check(bad, "版4 点「立即检查」先写\"检查中…\"并把按钮禁掉", mid == K("s.UpdateChecking") && p3.UpdateResultShownForTest && disabled);
                Check(bad, "版4 检查没回来之前再点一次不重入（网络请求只发一次）", secondDone && hits == 1);
                tcs.SetResult("{\"versions\":[]}");
                PumpToLoaded();
                first.GetAwaiter().GetResult();
                string done = p3.UpdateResultForTest;
                bool enabled = p3.CheckButtonEnabledForTest;
                sb.AppendLine("— 检查回来：内联=\"" + done + "\"　按钮恢复=" + enabled + "　「打开下载页」显示=" + p3.DownloadButtonShownForTest);
                Check(bad, "版4 检查回来之后按钮恢复、内联换成结果文案（这一轮给的是空清单＝已是最新）",
                    enabled && done == K("s.UpdateLatestFmt").Replace("{0}", cur.ToString(4)) && !p3.DownloadButtonShownForTest);
                UpdateChecker.FetchOverride = null;
            }
            catch (Exception ex) { bad.Add("版4 异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally { UpdateChecker.FetchOverride = null; AboutLinks.OpenUrlOverride = null; }
            return bad;
        }

        private static KeyValuePair<string, UpdateChecker.Result> Pair(string k, UpdateChecker.Result v)
        {
            return new KeyValuePair<string, UpdateChecker.Result>(k, v);
        }

        private static UpdateChecker.Result Res(List<KeyValuePair<string, UpdateChecker.Result>> states, string name)
        {
            foreach (var kv in states) if (kv.Key == name) return kv.Value;
            return null;
        }

        // ===== 渲染取证 =====

        /// <summary>造一次"发现新版本"的检查结果（走 FetchOverride，不打真网络）。</summary>
        private static UpdateChecker.Result FoundSample(Version cur)
        {
            UpdateChecker.FetchOverride = u => Task.FromResult(
                "{\"versions\":[{\"version\":\"4.9.4.0\",\"date\":\"2026-10-05\",\"url\":\"" + UpdateChecker.ApiBase
                + "/dl/Setup-4.9.4.0.exe\",\"notes\":\"修了三处超界提示；预览支持 Ctrl+滚轮缩放。\",\"active\":true}]}");
            var r = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            return r;
        }

        /// <summary>造一次"检查失败（网络）"的检查结果。</summary>
        private static UpdateChecker.Result FailedSample(Version cur)
        {
            UpdateChecker.FetchOverride = u => { throw new HttpRequestException("No such host is known. (printpile.cn:443)"); };
            var r = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            return r;
        }

        internal static int V493Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            var cur = typeof(AboutPage).Assembly.GetName().Version;
            try
            {
                var shots = new[]
                {
                    Tuple.Create("浅色", AppTheme.Light, AppLanguage.ZhCN),
                    Tuple.Create("深色", AppTheme.Dark, AppLanguage.ZhCN),
                    Tuple.Create("英文", AppTheme.Light, AppLanguage.EnUS),
                };
                foreach (var cfg in shots)
                {
                    App.ApplyTheme(cfg.Item2);
                    App.ApplyLanguage(cfg.Item3);
                    var page = new AboutPage();
                    sb.AppendLine("—" + cfg.Item1 + "：关于页整页出图（标题区「" + page.TitleForTest + "」＋构建行「" + page.BuildLineForTest
                        + "」＋三按钮行＋自动检查开关＋更新渠道＋立即检查卡＋反馈行＋独立性卡；「日志位置」expander 已移除）");
                    code |= RenderControl("V493关于页" + cfg.Item1 + "自检.txt", "V493关于页-" + cfg.Item1 + ".png", page, 900, 860);
                }

                // ② 内联两态特写（浅色）：把「立即检查」那张真卡片从页面里摘出来单独出图（真控件，不是复刻）
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var pairs = new[]
                {
                    new KeyValuePair<string, UpdateChecker.Result>("发现新版本", FoundSample(cur)),
                    new KeyValuePair<string, UpdateChecker.Result>("检查失败", FailedSample(cur)),
                };
                foreach (var p in pairs)
                {
                    var page = new AboutPage();
                    string text = page.ShowInline(p.Value, cur, "");
                    var card = page.UpdateCardForTest;
                    var host = card == null ? null : LogicalTreeHelper.GetParent(card) as Panel;
                    if (host != null) host.Children.Remove(card);
                    string ts = p.Key;
                    sb.AppendLine("—" + ts + "特写（浅色）：真卡片摘出来单出图；内联文案＝「" + text.Replace("\n", "⏎") + "」　「打开下载页」按钮显示＝"
                        + page.DownloadButtonShownForTest);
                    code |= RenderControl("V493内联" + ts + "自检.txt", "V493内联" + ts + "-浅色.png", card, 660, 240);
                }
                sb.AppendLine("—48 号那三张「检查更新弹窗」复刻图随 MessageBox 呈现退役而不再重绘（文件保留不删，见 00-项目说明报备）。");
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex)
            {
                sb.AppendLine("渲染异常：" + ex);
                code |= 1;
            }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V493关于页渲染自检.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
