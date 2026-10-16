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

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9（V4.8 功能改进与优化版，39 号开工令）断言：
    /// ①关闭按钮行为三选（ask/minimize/exit 各一条 + 任务中变体 + 设置页即时生效）——
    ///   V4.9 版本内修整第二批·修A 把 minimize 的语义升级为「最小化到系统托盘」（断言随之改判：窗口隐藏 + 托盘图标在场）；
    /// ②问题反馈——V4.9-2 的客户端直发窗口在 41 号·修C 退役（生产 workers.dev 在部分网络被 SNI 封锁），
    ///   这一组断言改为「跳官网反馈页 + 日志目录进剪贴板 + 应用内状态文字」；
    /// ③打印机属性加固留痕。全程注入式（ClosePromptWindow.Override / FeedbackSite.OpenOverride），不打真网络、不真开浏览器。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>把一次关闭请求走真实 OnClosing：返回（窗口是否真关了），并带出关窗后的可见性/窗口状态/托盘图标是否在位。</summary>
        private static bool CloseOnceAndReport(out WindowState state, out bool hidden, out bool tray)
        {
            var w = new MainWindow();
            bool closed = false;
            w.Closed += (s, e) => closed = true;
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            w.Close();
            PumpToLoaded();
            state = w.WindowState;
            hidden = w.Visibility != Visibility.Visible;
            tray = TrayIcon.IsVisible;
            TrayIcon.Dispose();       // 自检跑完别把图标留在真机托盘上
            return closed;
        }

        private static bool CloseOnceAndReport(out WindowState state)
        {
            bool h, t;
            return CloseOnceAndReport(out state, out h, out t);
        }

        private static List<string> V49Close(StringBuilder sb)
        {
            var bad = new List<string>();
            string old = SnapshotCfg();
            WriteCfg("{\"Theme\":\"light\"}");
            SettingsService.Load();

            // 无头自检里窗口会真关，必须先把关闭模式改成显式，否则可能把整轮自检提前 Shutdown 掉
            var mode = Application.Current.ShutdownMode;
            Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                // —— ① settings.json 的 CloseBehavior 解析（老配置缺键/未知值都按 ask） ——
                var pLog = new[] { "", "ask", "minimize", "exit", "MINIMIZE", "garbage", null }
                    .Select(k => "\"" + (k ?? "(缺)") + "\"→" + SettingsService.ParseCloseBehavior(k));
                sb.AppendLine("— ① CloseBehavior 解析：" + string.Join(" ", pLog));
                Check(bad, "V4.9-1 解析：ask/minimize/exit 各归位（大小写不敏感），缺键与未知值一律 ask（老配置升级不改行为）",
                    SettingsService.ParseCloseBehavior("") == CloseBehavior.Ask
                    && SettingsService.ParseCloseBehavior(null) == CloseBehavior.Ask
                    && SettingsService.ParseCloseBehavior("ask") == CloseBehavior.Ask
                    && SettingsService.ParseCloseBehavior("minimize") == CloseBehavior.Minimize
                    && SettingsService.ParseCloseBehavior("MINIMIZE") == CloseBehavior.Minimize
                    && SettingsService.ParseCloseBehavior("exit") == CloseBehavior.Exit
                    && SettingsService.ParseCloseBehavior("garbage") == CloseBehavior.Ask);

                // —— ② 提示窗本体：文案 / 默认按钮 / 勾选默认不勾 / 任务中变体 ——
                string plain = ClosePromptWindow.BodyText(false);
                string busyLine = ClosePromptWindow.BodyText(true);
                var pw = new ClosePromptWindow(true);
                var pwIdle = new ClosePromptWindow(false);
                sb.AppendLine("— ② 提示窗：正文=\"" + plain.Replace("\n", "⏎") + "\"（" + plain.Length + " 字）；空闲态与任务中不同＝"
                    + (busyLine != plain) + "；窗口实际两行：正文行=" + (pw.BodyLine == plain.Split('\n')[0]) + " 警示行=" + (pw.WarnLine == Res("s.ClosePromptBusy"))
                    + "（空闲态警示行=\"" + pwIdle.WarnLine + "\"）；默认按钮=\"" + pw.KeepButton.Content + "\" IsDefault=" + pw.KeepButton.IsDefault
                    + "；次按钮=\"" + pw.ExitButton.Content + "\"；勾选框默认不勾=" + (pw.RememberBox.IsChecked == false) + " 文案=\"" + pw.RememberBox.Content + "\"");
                // 修A 口径：按钮文字改成「最小化」，正文当时一字未动（41 号需求方口径），所以这里分别钉住：正文那一问、按钮取新键值。
                // 44 号·缺2 改判（就地写明，不删条）：修A 之后正文仍是任务栏那套说法，与按钮/行为图文不符，
                // 需求方拍板新正文改口为「最小化到系统托盘继续运行」——故"正文不变"这半句作废，改为钉住新文案逐字（逐字断言在 V49cTests）。
                Check(bad, "V4.9-1 提示窗：正文问「进托盘继续运行」还是「完全退出」（44 号·缺2 起改口为托盘口径）+ 默认按钮＝「最小化」（修A 起＝最小化到托盘）+ 勾选框默认不勾",
                    plain == Res("s.ClosePromptBody")
                    && plain.Contains("退出") && plain.Contains("关闭窗口") && plain.Contains("托盘")
                    && pw.KeepButton.IsDefault && !pw.ExitButton.IsDefault
                    && pw.KeepButton.Content as string == Res("s.CloseMinimizeBtn")
                    && Res("s.CloseMinimizeBtn").Length > 0 && Res("s.CloseMinimizeBtn") != "s.CloseMinimizeBtn"
                    && pw.ExitButton.Content as string == Res("s.CloseExitApp")
                    && pw.RememberBox.IsChecked == false && pw.RememberBox.Content as string == Res("s.CloseRemember"));
                Check(bad, "V4.9-1 任务进行中变体：同一扇窗追加「将中断」警示行（空闲态该行空着），不另开第二套弹窗",
                    busyLine != plain && busyLine.StartsWith(plain, StringComparison.Ordinal)
                    && busyLine.Contains(Res("s.ClosePromptBusy")) && Res("s.ClosePromptBusy").Contains("中断")
                    && pw.BodyLine == plain.Split('\n')[0] && pw.WarnLine == Res("s.ClosePromptBusy")
                    && pwIdle.WarnLine == "");

                // —— ③ 三种设置各走一遍真实 OnClosing（修A：minimize＝窗口 Hide + 托盘图标在场，不再是置 Minimized） ——
                SettingsService.SetCloseBehavior(CloseBehavior.Ask);
                int prompts = 0;
                ClosePromptWindow.Override = b => { prompts++; return new ClosePromptResult { Choice = CloseBehavior.Minimize, Remember = true }; };
                WindowState st; bool hid, tr;
                bool closedAsk = CloseOnceAndReport(out st, out hid, out tr);
                sb.AppendLine("— ③ ask（注入：选「最小化」+ 勾选不再提示）：弹窗次数=" + prompts + " 窗口关闭=" + closedAsk
                    + " 窗口已隐藏=" + hid + " 托盘图标在位=" + tr + " WindowState=" + st
                    + " settings.json=" + (File.ReadAllText(AppPaths.SettingsPath).Contains("\"CloseBehavior\":\"minimize\"") ? "已写 minimize" : "未写对"));
                Check(bad, "V4.9-1+修A ask→弹提示窗（本次走注入），选「最小化」后主窗口不关而是**隐藏 + 托盘图标在场**，勾选「不再提示」即把选择记进 settings.json",
                    prompts == 1 && !closedAsk && hid && tr
                    && SettingsService.CloseBehavior == CloseBehavior.Minimize
                    && File.ReadAllText(AppPaths.SettingsPath).Contains("\"CloseBehavior\":\"minimize\""));

                prompts = 0;
                SettingsService.SetCloseBehavior(CloseBehavior.Minimize);
                bool closedMin = CloseOnceAndReport(out st, out hid, out tr);
                sb.AppendLine("— ③ minimize（修A＝进托盘）：弹窗次数=" + prompts + " 窗口关闭=" + closedMin
                    + " 窗口已隐藏=" + hid + " 托盘图标在位=" + tr + "（任务栏按钮随窗口隐藏而消失）");
                Check(bad, "V4.9-1+修A minimize→不弹窗、直接进托盘：窗口 Hide（任务栏无按钮）、托盘图标在场、窗口未关闭",
                    prompts == 0 && !closedMin && hid && tr);

                prompts = 0;
                SettingsService.SetCloseBehavior(CloseBehavior.Exit);
                bool closedExit = CloseOnceAndReport(out st, out hid, out tr);
                sb.AppendLine("— ③ exit：弹窗次数=" + prompts + " 窗口关闭=" + closedExit + " 托盘图标残留=" + tr);
                Check(bad, "V4.9-1 exit→不弹窗、直接走既有退出流程（Closed 真触发），且退出后托盘图标已释放（不留死图标）",
                    prompts == 0 && closedExit && !tr);

                // —— ④ 设置页「关闭行为」三选：改选即时生效 + 回显 ——
                var w = new MainWindow();
                w.ShowPanelForTest("settings");
                w.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                var sp = FindByType(w.Content as FrameworkElement, typeof(SettingsPage)).Cast<SettingsPage>().FirstOrDefault();
                var cb = sp == null ? null : sp.FindName("CloseBox") as ComboBox;
                if (cb == null) { bad.Add("V4.9-1 设置页取不到「关闭行为」下拉"); sb.AppendLine("— ④ 设置页关闭行为下拉：未找到（失败）"); }
                else
                {
                    SettingsService.SetCloseBehavior(CloseBehavior.Ask);
                    sp.RefreshCloseBoxForTest();
                    int i0 = cb.SelectedIndex;
                    cb.SelectedIndex = 2;                        // 用户改选「直接退出」→ 走真实 SelectionChanged 处理器
                    PumpToLoaded();
                    string cfg = File.ReadAllText(AppPaths.SettingsPath);
                    sb.AppendLine("— ④ 设置页：初值 SelectedIndex=" + i0 + "（" + Res("s.CloseAsk") + "）；改选第 2 项后 CloseBehavior="
                        + SettingsService.CloseBehavior + " 文件含 exit＝" + cfg.Contains("\"CloseBehavior\":\"exit\"")
                        + "；三项文案＝" + string.Join("/", cb.Items.Cast<ComboBoxItem>().Select(it => it.Content)));
                    Check(bad, "V4.9-1 设置页三选组：默认「每次询问」，改选「直接退出」即时写进 settings.json（沿用本页现有保存机制）",
                        i0 == 0 && SettingsService.CloseBehavior == CloseBehavior.Exit && cfg.Contains("\"CloseBehavior\":\"exit\"")
                        && cb.Items.Count == 3
                        && cb.Items.Cast<ComboBoxItem>().Select(it => it.Content as string).SequenceEqual(
                            new[] { Res("s.CloseAsk"), Res("s.CloseMinimize"), Res("s.CloseExit") }));
                    Check(bad, "修A 第二项文案已升级为「最小化到托盘」（中英各自成对，不再写任务栏）",
                        Res("s.CloseMinimize").Contains("托盘") || Res("s.CloseMinimize").ToLowerInvariant().Contains("tray"));
                    SettingsService.SetCloseBehavior(CloseBehavior.Ask);
                    sp.RefreshCloseBoxForTest();
                    Check(bad, "V4.9-1 反向回显：设置值改回 ask 后下拉回到第 0 项（别的入口改了也算）", cb.SelectedIndex == 0);
                }
            }
            finally
            {
                ClosePromptWindow.Override = null;
                Application.Current.ShutdownMode = mode;
                TrayIcon.Dispose();
                RestoreCfg(old);
            }
            return bad;
        }

        /// <summary>
        /// 修C（41 号）：反馈入口改跳官网。V4.9-2 那五组"直发成功/限频/断网存草稿/超长拦截/未勾日志"的断言随
        /// FeedbackWindow.cs / FeedbackClient.cs 一起退役（文件移 %TEMP%，不删），这里换成：URL 唯一字面量与参数、
        /// 注入式跳转、日志目录进剪贴板（取完还原，不污染用户剪贴板）、关于页那一行在位与状态文字。
        /// </summary>
        private static List<string> V49Jump(StringBuilder sb)
        {
            var bad = new List<string>();
            string clipBefore = "";
            try { if (Clipboard.ContainsText()) clipBefore = Clipboard.GetText(); } catch { }
            try
            {
                // —— ① URL：唯一一份官网地址字面量 + from=app + v=当前版本 ——
                string url = FeedbackSite.Url();
                string ver = AppVersion.Full;
                sb.AppendLine("— ① 跳转地址=" + url + "（基址常量=" + FeedbackSite.UrlBase + "，版本段=" + ver + "）");
                Check(bad, "修C 跳转地址＝官网基址 + ?from=app&v=当前版本，且全工程只有这一份官网地址字面量（rg 断言见 00-项目说明）"
                    + "【54 号令·B 就地改判，条目不删】：基址字面量已收口到 AboutLinks.SiteBase 一处，本断言不再钉 Pages 域，"
                    + "改钉「反馈页由主路基址派生＋整条 URL 里基址只出现一次＋不含旧 Workers 域」",
                    url == FeedbackSite.UrlBase + "?from=app&v=" + Uri.EscapeDataString(ver)
                    && FeedbackSite.UrlBase == AboutLinks.SiteBase + "/feedback.html"
                    && url.StartsWith(AboutLinks.SiteBase + "/feedback.html?from=app&v=", StringComparison.Ordinal)
                    && url.IndexOf(AboutLinks.SiteBase, StringComparison.Ordinal) == url.LastIndexOf(AboutLinks.SiteBase, StringComparison.Ordinal)
                    && url.IndexOf("workers.dev", StringComparison.Ordinal) < 0);

                // —— ② 注入式跳转：不真开浏览器，但走同一段码路；日志目录确实进了剪贴板 ——
                string seen = null;
                FeedbackSite.OpenOverride = u => { seen = u; return true; };
                var j = FeedbackSite.Open(autoLogs: false);   // 52 号令·返修③：旧行为断言显式走"不自动打包"分支（默认勾选态会真打包直传）
                string clipNow = "";
                try { clipNow = Clipboard.ContainsText() ? Clipboard.GetText() : ""; } catch { }
                sb.AppendLine("— ② 注入跳转：回调收到=" + seen + " BrowserLaunched=" + j.BrowserLaunched
                    + " 日志路径已复制=" + j.LogPathCopied + " 剪贴板实取=" + clipNow + " 日志目录=" + j.LogDirectory + " 错误=" + (j.Error ?? "(无)"));
                // 剪贴板是机器级共享资源：别的程序（或本机沙箱）正开着它时 OpenClipboard 会失败
                //（CLIPBRD_E_CANT_OPEN，Windows 上常见瞬时失败，代码里已重试三次）。两种结果都要"说真话"：
                // 写进去了就实测内容一致，没写进去就必须改口成"请手动复制"，绝不允许谎报成功。
                bool clipOk = j.LogPathCopied && clipNow == j.LogDirectory;
                bool clipBusy = !j.LogPathCopied && !string.IsNullOrEmpty(j.Error);
                sb.AppendLine("    剪贴板分支：" + (clipOk ? "已复制且与日志目录逐字一致"
                    + "（重试 " + "0" + " 次即成）" : (clipBusy ? "本机剪贴板打不开→走\"没能复制\"分支（错误已带出：" + j.Error + "）" : "两分支都不成立（失败）")));
                Check(bad, "修C 点按钮＝打开官网反馈页（注入，不真开浏览器）；日志目录这一步要么实测与剪贴板内容一致、要么如实改口报\"没能复制\"（本机剪贴板被占用时走后者，不谎报成功）",
                    j.BrowserLaunched && seen == url && !string.IsNullOrEmpty(j.LogDirectory) && (clipOk || clipBusy));

                // —— ②b 52 号令·返修③：自动打包日志两分支（可控样张目录 + UploadOverride 注入，不打真网络）——
                string sampleDir = Path.Combine(Path.GetTempPath(), "BatchPrintCN-logpkg-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(sampleDir);
                File.WriteAllText(Path.Combine(sampleDir, "app-20261006.log"), new string('x', 40000) + "\n\u81EA\u68C0\u6837\u5F20\u65E5\u5FD7\u4E00\n");
                File.WriteAllText(Path.Combine(sampleDir, "app-20261005.log"), "\n\u81EA\u68C0\u6837\u5F20\u65E5\u5FD7\u4E8C\n");
                LogPackager.DirectoryOverride = () => sampleDir;
                long gotBytes = 0;
                LogPackager.UploadOverride = z => { gotBytes = z.Length; return "feedback-logs/20261006/v100-selftest.zip"; };
                var jUp = FeedbackSite.Open(autoLogs: true);
                bool upOk = jUp.AutoLogAttempted && jUp.LogPackageKey != null && jUp.Url.Contains("logpkg=")
                    && gotBytes > 0 && gotBytes <= LogPackager.MaxBytes;
                LogPackager.UploadOverride = z => { throw new IOException("HTTP 503"); };
                var jUpFail = FeedbackSite.Open(autoLogs: true);
                LogPackager.UploadOverride = null;
                LogPackager.DirectoryOverride = null;
                try { Directory.Delete(sampleDir, true); } catch { }
                bool failGraceful = jUpFail.AutoLogAttempted && jUpFail.LogPackageKey == null
                    && !string.IsNullOrEmpty(jUpFail.UploadError) && jUpFail.BrowserLaunched
                    && jUpFail.Url.IndexOf("logpkg=", StringComparison.Ordinal) < 0;   // 失败降级＝URL 不带 logpkg
                sb.AppendLine("\u2014 \u246b \u81EA\u52A8\u6253\u5305\uFF1Aattempted=" + jUp.AutoLogAttempted + " key=" + (jUp.LogPackageKey ?? "(null)")
                    + " url\u5E26logpkg=" + jUp.Url.Contains("logpkg=") + " gotBytes=" + gotBytes
                    + "\uFF1B\u5931\u8D25\u5206\u652F\u964D\u7EA7=" + failGraceful + "\uFF08\u539F\u56E0=" + (jUpFail.UploadError ?? "-") + "\uFF09");
                Check(bad, "V1.0.0\u00B752 \u53F7\u00B7\u8FD4\u4FEE\u2462\uFF1A\u52FE\u9009\u81EA\u52A8\u6253\u5305\u2192zip \u76F4\u4F20\u62FF key\u2192URL \u8FFD\u52A0 logpkg\uFF1B\u4E0A\u4F20\u5931\u8D25\u2192\u964D\u7EA7\u65E7\u884C\u4E3A\uFF08URL \u65E0 logpkg\u3001\u72B6\u6001\u884C\u8BF4\u660E\u539F\u56E0\uFF09",
                    upOk && failGraceful);
                // —— ③ 关于页那一行：入口在位、按钮右缘、点一次出状态文字（不弹模态） ——
                var aw = new MainWindow();
                aw.ShowPanelForTest("about");
                aw.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                var ap = FindByType(aw.Content as FrameworkElement, typeof(AboutPage)).Cast<AboutPage>().FirstOrDefault();
                var btn = ap == null ? null : ap.FindName("BtnFeedback") as Button;
                if (btn == null) { bad.Add("修C 关于页取不到「问题反馈」按钮"); sb.AppendLine("— ③ 关于页反馈按钮：未找到（失败）"); }
                else
                {
                    var card = V47Ancestor(btn, typeof(Wpf.Ui.Controls.CardControl));
                    double[] bb = V47Box(btn, ap), cbx = card == null ? new double[] { 0, 0, 0, 0 } : V47Box(card, ap);
                    var hdr = card == null ? null : FindByType(card, typeof(TextBlock)).Cast<TextBlock>()
                        .FirstOrDefault(t => t.Text == Res("s.FbEntryTitle"));
                    string noteBefore = ap.FeedbackNoteForTest;
                    string note = ap.ShowFeedbackJump(j);
                    sb.AppendLine("— ③ 关于页：按钮=\"" + btn.Content + "\" 标题在位=" + (hdr != null)
                        + " 按钮右缘 " + bb[2].ToString("0.#") + " 卡片右缘 " + cbx[2].ToString("0.#")
                        + "；点前状态文字=\"" + (noteBefore ?? "") + "\" 点后=\"" + note + "\" 已显示=" + ap.FeedbackNoteShownForTest);
                    string wantNote = j.LogPathCopied ? Res("s.FbCopied") : Res("s.FbCopyFailed").Replace("{0}", j.LogDirectory);
                    Check(bad, "修C 关于页「问题反馈」行仍在位（标题+说明+右侧按钮贴卡片右缘），点完在本行下方留一次应用内状态文字（不弹模态；内容跟着剪贴板那一步的真实结果走）",
                        hdr != null && btn.Content as string == Res("s.FbEntryBtn")
                        && cbx[2] - bb[2] >= -0.5 && cbx[2] - bb[2] <= 40
                        && string.IsNullOrEmpty(noteBefore) && note == wantNote && ap.FeedbackNoteShownForTest);
                    // 浏览器打不开时要说清是哪件事没成（不能把"已复制"当成成功糊过去）
                    var jFail = new FeedbackSite.Jump { Url = url, BrowserLaunched = false, LogPathCopied = true, LogDirectory = "X" };
                    string note2 = ap.ShowFeedbackJump(jFail);
                    Check(bad, "修C 浏览器没开起来时状态文字改口为\"请手动访问 + 地址\"，不谎报成功",
                        note2 == Res("s.FbOpenFailed").Replace("{0}", url) && note2.Contains(url));
                    var jNoClip = new FeedbackSite.Jump { Url = url, BrowserLaunched = true, LogPathCopied = false, LogDirectory = "D:/某日志目录" };
                    string note3 = ap.ShowFeedbackJump(jNoClip);
                    Check(bad, "修C 剪贴板没写进去时状态文字改口为\"请手动复制 + 路径\"（与上面浏览器失败那条各管一半，不混为一谈）",
                        note3 == Res("s.FbCopyFailed").Replace("{0}", "D:/某日志目录") && note3.Contains("某日志目录"));
                }

                // —— ④ 退役面：客户端直发的 22 枚窗口键与两枚错误码键全清，中英两侧各 0 枚；入口三枚键保留且已改口径 ——
                // 修C 之后 s.Fb* 前缀只剩 6 枚：关于页入口 3 枚 + 跳转状态文字 3 枚；直发窗口的 22 枚必须全清
                // 52 号令·返修③ 就地改判：+4 枚自动打包日志键（勾选/说明/上传成功/上传失败）——共 10 枚
                var fbAllow = new[] { "s.FbEntryTitle", "s.FbEntryDesc", "s.FbEntryBtn",
                    "s.FbAutoLogsTitle", "s.FbAutoLogsDesc", "s.FbLogPackSent", "s.FbLogPackFailedFmt",
                    "s.FbCopied", "s.FbCopyFailed", "s.FbOpenFailed" };
                foreach (var dict in new[] { "Localization/zh-CN.xaml", "Localization/en-US.xaml" })
                {
                    var rd = new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) };
                    var names = rd.Keys.Cast<object>().Select(k => k.ToString())
                        .Where(k => k.StartsWith("s.Fb", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList();
                    var extra = names.Except(fbAllow).ToList();
                    var missing = fbAllow.Except(names).ToList();
                    sb.AppendLine("— ④ " + dict + "：s.Fb* 共 " + names.Count + " 枚＝" + string.Join(",", names)
                        + "；越名单=" + extra.Count + " 缺=" + missing.Count);
                    Check(bad, "修C 退役彻底：" + dict + " 里 s.Fb* 恰好只剩「入口三枚 + 跳转状态三枚 + 自动打包四枚」这 10 枚（直发窗口的 22 枚全清、无死键、中英同一份名单）",
                        extra.Count == 0 && missing.Count == 0 && names.Count == 10);
                }
                Check(bad, "修C 入口说明与按钮文案已改成\"跳官网\"口径（不再承诺\"发送您填写的内容\"）",
                    Res("s.FbEntryDesc").Contains("官网") || Res("s.FbEntryDesc").ToLowerInvariant().Contains("website"));
            }
            finally
            {
                FeedbackSite.OpenOverride = null;
                try { Clipboard.SetText(clipBefore ?? ""); } catch { }   // 还用户原来的剪贴板
            }
            return bad;
        }

        /// <summary>V4.9 自检行：①关闭行为 ②反馈跳官网 ③打印机属性加固，逐条数字落 V49改进版定位.txt。</summary>
        internal static string V49Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== V4.9-1 关闭按钮行为三选（ask / minimize〔修A 升级为托盘〕 / exit） ========");
                bad.AddRange(V49Close(sb));
                sb.AppendLine("======== V4.9-2 → 41 号·修C：反馈入口改跳官网（客户端直发已退役） ========");
                bad.AddRange(V49Jump(sb));
                sb.AppendLine("======== V4.9-3 打开打印机属性加固（拼接写法消除） ========");
                // 行为零变化的纵深防御：源码里那串字符串拼接已消除（rg 核对留痕见 00-项目说明），
                // 这里守住"入口与按钮仍在、打印中仍按修整19 禁用"，防止加固时把功能改没。
                var w = new MainWindow();
                w.ShowPanelForTest("settings");
                w.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                var sp = FindByType(w.Content as FrameworkElement, typeof(SettingsPage)).Cast<SettingsPage>().FirstOrDefault();
                var b = sp == null ? null : sp.FindName("BtnOpenPrinterProps") as Button;
                sb.AppendLine("— 按钮在位=" + (b != null) + " 文案=\"" + (b == null ? "" : b.Content) + "\" 空闲态可用=" + (b != null && b.IsEnabled));
                if (b != null)
                {
                    // 注意形参口径：SettingsPage.SetPrintBusy(enabled) 传的是"可用"，主页那边调的是 SetPrintBusy(!busy)
                    sp.SetPrintBusy(true); bool idleOn = b.IsEnabled;
                    sp.SetPrintBusy(false); bool busyOff = b.IsEnabled;
                    sp.SetPrintBusy(true);
                    sb.AppendLine("— 打印中禁用回归：空闲可用=" + idleOn + " 打印中=" + busyOff);
                    Check(bad, "V4.9-3 加固后入口未丢：按钮在位、空闲可用、打印中仍按修整19 置灰", b.IsEnabled && idleOn && !busyOff);
                }
                else bad.Add("V4.9-3 打印机属性按钮丢失");
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V49改进版定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（V4.9-1 关闭行为三设置各一条实测：ask 弹窗选「最小化」后不关窗而是**窗口隐藏 + 托盘图标在场**＋勾选记忆写进 settings.json / minimize 不弹窗直接进托盘（任务栏无按钮）/ exit 直接走既有退出流程且退出后不留死图标；"
                  + "任务中同一扇窗追加「将中断」警示、正文与按钮同一口径（44 号·缺2 起改口为「最小化到系统托盘继续运行」）；设置页三选改选即时生效＋反向回显＋第二项文案已是「最小化到托盘」 / "
                  + "修C 反馈改跳官网：地址＝官网基址+?from=app&v=版本（唯一字面量、不含 workers.dev）、注入式跳转收到同一 URL、日志目录实测进了剪贴板（取完还原用户原内容）、"
                  + "关于页那一行在位贴右缘、点完出状态文字且浏览器失败时改口不谎报、退役后 s.Fb* 中英各只剩入口 3 枚 / "
                  + "V4.9-3 打印机属性 ArgumentList 逐参数照旧，入口与打印中禁用回归通过；逐条数字见 V49改进版定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>V4.9 渲染取证：关闭提示窗（任务中变体）浅/深 + 设置页关闭行为行 + 关于页反馈行（反馈窗口图随修C 退役）。</summary>
        internal static int V49Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";

                    var cp = new ClosePromptWindow(true);
                    code |= SnapDialog(cp, 470, "V49关闭提示窗-" + ts + ".png", sb, ts);
                    sb.AppendLine("—" + ts + "：关闭提示窗（任务进行中警示变体，默认按钮已是「最小化」）出图");
                }

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var w = new MainWindow();
                w.ShowPanelForTest("settings");
                w.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V49设置页浅色自检.txt", "V49设置页-关闭行为-浅色.png", w, 1120, 700, new string[0], 0);

                var wa = new MainWindow();
                wa.ShowPanelForTest("about");
                wa.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V49关于页浅色自检.txt", "V49关于页-反馈行-浅色.png", wa, 1120, 700, new string[0], 0);
                sb.AppendLine("—浅色：设置页「关闭行为」三选行 + 关于页「问题反馈」行 整窗出图（本页其余四块与顶部标题卡已在 41 号·修D/修E 移除，取证图见 V49b 系列）");
            }
            catch (Exception ex) { sb.AppendLine("探针异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            txt += "\r\n说明：V4.9（39 号主体 + 40 号打印范围反馈 + 41 号第二批）取证——关闭提示窗（含任务进行中警示变体、默认按钮改名「最小化」）浅/深各一张、"
                 + "设置页「关闭行为」三选行与关于页「问题反馈」行整窗各一张。V4.9-2 的反馈窗口图随客户端直发退役（不再出图）。"
                 + "自检全程注入式，不打真网络、不真开浏览器、不弹真模态框。"
                 + (txt.Contains("失败") ? " 判定：以自检行与逐条数字为准（本段为出图记录，含失败字样）" : " 判定：出图完整");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }

        /// <summary>把一扇没 Show 过的窗口的内容拍成 PNG（沿用 SnapFlat 的垫底色口径）。</summary>
        private static int SnapDialog(Window dlg, double width, string png, StringBuilder sb, string ts)
        {
            var el = dlg.Content as FrameworkElement;
            if (el == null) { sb.AppendLine("— " + png + "：取不到内容（失败）"); return 1; }
            el.Measure(new Size(width, 1400));
            // DesiredSize 含元素自身 Margin：Arrange 必须带上，否则 RenderSize 被 Margin 吃掉一圈（首版取证图右、下各短一截）；
            // 再留 32 的余量——末行勾选框的模板最小高度比 Grid 给的 Auto 行高略大，不留余量会在图里被切掉半行
            var d = el.DesiredSize;
            el.Arrange(new Rect(0, 0, Math.Max(width, d.Width), Math.Max(1, d.Height) + 32));
            el.UpdateLayout();
            PumpToLoaded();
            string r = SnapFlat(el, png, 240);
            sb.AppendLine("— " + ts + " " + png + "（排布 " + el.ActualWidth + "x" + el.ActualHeight + "）：" + r);
            return r.Contains("失败") || r.Contains("未渲染") ? 1 : 0;
        }
    }
}
