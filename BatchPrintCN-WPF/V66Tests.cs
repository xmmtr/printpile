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
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 66 号令 T4：更新体验断言（左下角橙色按钮 → 更新弹窗 → 下载 → 安装分叉）。
    /// 铁律：<b>全程离线</b>——清单走 <see cref="UpdateChecker.FetchOverride"/> 注入、下载走
    /// <see cref="UpdateDownloader.FetchOverride"/> 假流、起进程与开文件夹走
    /// <see cref="UpdateInstaller.LaunchOverride"/>／<see cref="UpdateInstaller.OpenFolderOverride"/>，
    /// 不碰真网络、不真起安装程序、不真开资源管理器；注入点用完一律还原。
    /// 颜色不钉字面值（主题兼容）：只断按钮样式＝资源键 BtnWarning 那一份 Style、其背景 setter 是 DynamicResource 且键名以 Brush 结尾。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>推事件循环直到条件成立（async void 的续跑与 Progress 回调都靠它），超时兜底不死等。</summary>
        private static void PumpUntil(Func<bool> done, int timeoutMs = 8000)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            int budget = Math.Max(1, timeoutMs / 20);
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (s, e) =>
            {
                bool ok = false;
                try { ok = done(); } catch { ok = true; }
                if (ok || --budget <= 0) { timer.Stop(); frame.Continue = false; }
            };
            timer.Start();
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static UpdateChecker.Entry UpdEntry(string version, string url, string notes)
        {
            return new UpdateChecker.Entry { Version = version, Url = url, Notes = notes, Active = true, Date = "2026-10-11" };
        }

        internal static string V66Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();

            var oldFetch = UpdateChecker.FetchOverride;
            var oldDown = UpdateDownloader.FetchOverride;
            var oldLaunch = UpdateInstaller.LaunchOverride;
            var oldOpen = UpdateInstaller.OpenFolderOverride;
            var oldExit = UpdateDialog.ExitOverride;
            var oldBubble = AutoUpdate.BubbleOverride;
            var oldClock = AutoUpdate.Clock;
            bool oldAuto = SettingsService.AutoCheckUpdate;
            DateTime? oldLast = SettingsService.LastAutoCheckUtc;
            UpdateState.ResetForTest();
            try
            {
                // ===== ① 纯函数三件：百分比／版本净化／临时文件命名 =====
                Check(bad, "66 号 PercentOf 逐数（0/0=0、50/100=50、150/100 钉在 100、总量未知=0）",
                    UpdateDownloader.PercentOf(0, 0) == 0 && UpdateDownloader.PercentOf(50, 100) == 50
                    && UpdateDownloader.PercentOf(150, 100) == 100 && UpdateDownloader.PercentOf(10, -1) == 0);
                Check(bad, "66 号 版本净化挡住被篡改的清单（../../win.ini→unknown、1.0.1.0-beta→1.0.1.0、空→unknown）",
                    UpdateDownloader.SafeVersion("../../win.ini") == "unknown"
                    && UpdateDownloader.SafeVersion("1.0.1.0-beta") == "1.0.1.0"
                    && UpdateDownloader.SafeVersion(null) == "unknown");
                string zipPath = UpdateDownloader.TempPathFor(UpdEntry("1.0.1.0", "https://printpile.cn/dl/PrintPile-v1.0.1.0-portable.zip", "n"));
                string exePath = UpdateDownloader.TempPathFor(UpdEntry("1.0.1.0", "https://printpile.cn/dl/PrintPile-Setup-v1.0.1.exe", "n"));
                Check(bad, "66 号 临时文件命名＝%TEMP%\\PrintPile-update-<版本>.<exe|zip>（扩展名按 URL 尾巴判，查询串不参与）",
                    zipPath.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
                    && zipPath.EndsWith("PrintPile-update-1.0.1.0.zip", StringComparison.OrdinalIgnoreCase)
                    && exePath.EndsWith("PrintPile-update-1.0.1.0.exe", StringComparison.OrdinalIgnoreCase)
                    && UpdateDownloader.TempPathFor(UpdEntry("1.0.1.0", "https://printpile.cn/a.zip?x=1", "n")).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                // ===== ② 白名单：清单被换指向也别下载 =====
                Check(bad, "66 号 下载白名单：主路域与 www 放行、外域／相对地址／空拒绝",
                    UpdateDownloader.UrlAllowed("https://printpile.cn/a.zip") && UpdateDownloader.UrlAllowed("https://www.printpile.cn/a.zip")
                    && !UpdateDownloader.UrlAllowed("https://evil.example.com/a.zip") && !UpdateDownloader.UrlAllowed("/rel/a.zip")
                    && !UpdateDownloader.UrlAllowed(null));
                bool fakeCalled = false;
                UpdateDownloader.FetchOverride = (u, p, c) => { fakeCalled = true; return Task.FromResult(UpdateDownloader.Outcome.Success("x")); };
                var rej = UpdateDownloader.RunAsync(UpdEntry("1.0.1.0", "https://evil.example.com/a.zip", "n"), null, CancellationToken.None).Result;
                Check(bad, "66 号 白名单外 URL 直接判拒（假下载流一次都没被调用、结果失败且不带路径）", !rej.Ok && !fakeCalled && rej.Path == null);

                // ===== ③ 下载进度→完成（同步假流：三拍回调后成功） =====
                UpdateDownloader.FetchOverride = (u, p, c) =>
                {
                    if (p != null)
                    {
                        p.Report(new UpdateDownloader.Progress { Received = 25, Total = 100 });
                        p.Report(new UpdateDownloader.Progress { Received = 60, Total = 100 });
                        p.Report(new UpdateDownloader.Progress { Received = 100, Total = 100 });
                    }
                    return Task.FromResult(UpdateDownloader.Outcome.Success("fake"));
                };
                long seen = 0;
                var okTask = UpdateDownloader.RunAsync(UpdEntry("1.0.1.0", "https://printpile.cn/a.zip", "n"),
                    new Progress<UpdateDownloader.Progress>(p => seen = p.Received), CancellationToken.None);
                PumpUntil(() => okTask.IsCompleted, 3000);
                PumpUntil(() => seen == 100, 2000);   // Progress<T> 是按投递上下文排队的，得让事件循环转一拍才落
                var okDl = okTask.Result;
                Check(bad, "66 号 假流下载走完＝成功、带回路径、进度回调最后一拍＝100 字节位（" + seen + "）", okDl.Ok && !string.IsNullOrEmpty(okDl.Path) && seen == 100);

                // ===== ④ 取消／异常：删半截文件、不抛到界面 =====
                UpdateDownloader.FetchOverride = (u, p, c) =>
                {
                    if (p != null) p.Report(new UpdateDownloader.Progress { Received = 10, Total = 100 });
                    throw new OperationCanceledException("cancel");
                };
                var ent4 = UpdEntry("1.0.1.0", "https://printpile.cn/a.zip", "n");
                var cancel = UpdateDownloader.RunAsync(ent4, null, CancellationToken.None).Result;
                Check(bad, "66 号 取消/异常路径：判失败且临时文件不留（半截包不许躺在 %TEMP%）", !cancel.Ok && !UpdateDownloader.TempExists(ent4));

                // ===== ⑤ 超时口径与异常分类（沿用 48 号：原文只进日志） =====
                Check(bad, "66 号 下载超时＝8 分钟（与清单检查的 10 秒分开——70 号·追补放宽清单超时并启用主/备域切换，下载口径不动）；超时异常归 Timeout、其余归 Network",
                    UpdateDownloader.TimeoutSeconds == 480 && UpdateChecker.TimeoutSeconds == 10
                    && UpdateChecker.Classify(new TaskCanceledException()) == UpdateChecker.FailKind.Timeout
                    && UpdateChecker.Classify(new InvalidOperationException()) == UpdateChecker.FailKind.Network);

                // ===== ⑥ 状态机四拍（按钮可见性的唯一来源） =====
                UpdateState.ResetForTest();
                var ent5 = UpdEntry("1.0.1.0", "https://printpile.cn/a.zip", "n");
                bool vis0 = UpdateState.ButtonVisible;
                UpdateState.ReportFound(ent5);
                bool vis1 = UpdateState.ButtonVisible;
                UpdateState.SetDownloading(true);
                bool vis2 = UpdateState.ButtonVisible, en2 = UpdateState.Downloading;
                UpdateState.SetDownloading(false);
                UpdateState.ClearForInstall();
                bool vis3 = UpdateState.ButtonVisible;
                Check(bad, "66 号 状态机四拍：无更新不显→报更新即显→下载中仍显但置灰→交棒安装后消失",
                    !vis0 && vis1 && vis2 && en2 && !vis3 && UpdateState.Pending == null);
                Check(bad, "66 号 交棒之后再来更新也不回头（Installing 锁存，本会话不再提示）",
                    !UpdateState.ButtonVisible);

                // ===== ⑦ 主窗左下角：在位、初始隐藏、报更新后现身、样式取主题键 =====
                var mw = new MainWindow();
                mw.ShowPanelForTest("print");
                mw.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                var btn = mw.FindName("BtnUpdate") as Button;
                var ver = mw.FindName("VersionLabel") as TextBlock;
                bool hiddenAtFirst = btn != null && btn.Visibility != Visibility.Visible;
                UpdateState.ResetForTest();
                UpdateState.ReportFound(UpdEntry("1.0.1.0", "https://printpile.cn/a.zip", "n"));
                PumpToLoaded();
                bool shownAfter = btn != null && btn.Visibility == Visibility.Visible;
                var warnStyle = Application.Current != null ? Application.Current.TryFindResource("BtnWarning") as Style : null;
                bool styleIsWarning = btn != null && ReferenceEquals(btn.Style, warnStyle);
                bool orangeFromTheme = false; string bgKey = "";
                if (btn != null && btn.Style != null)
                {
                    foreach (Setter st in btn.Style.Setters.OfType<Setter>())
                    {
                        if (st.Property != Control.BackgroundProperty) continue;
                        var dre = st.Value as DynamicResourceExtension;
                        bgKey = dre == null ? "(非动态)" : (dre.ResourceKey as string ?? "");
                        orangeFromTheme = dre != null && bgKey.EndsWith("Brush", StringComparison.Ordinal) && !bgKey.StartsWith("#", StringComparison.Ordinal);
                    }
                }
                UpdateState.SetDownloading(true); PumpToLoaded();
                bool greyWhileDownloading = btn != null && !btn.IsEnabled;
                UpdateState.SetDownloading(false); PumpToLoaded();
                UpdateState.ResetForTest(); PumpToLoaded();
                bool hiddenAfterReset = btn != null && btn.Visibility != Visibility.Visible;
                double btnFont = btn == null ? 0 : btn.FontSize;
                double verFont = ver == null ? 0 : ver.FontSize;
                string upBtn = K("s.UpBtn") ?? "";
                sb.AppendLine("— 66 号 主窗左下角：按钮在位=" + (btn != null) + " 初始隐藏=" + (hiddenAtFirst ? "是" : "否")
                    + " 报更新后显示=" + (shownAfter ? "是" : "否") + " 下载中置灰=" + (greyWhileDownloading ? "是" : "否")
                    + " 清状态后隐藏=" + (hiddenAfterReset ? "是" : "否") + "；样式＝BtnWarning 同一份=" + (styleIsWarning ? "是" : "否")
                    + " 背景资源键=" + bgKey + "；字号 按钮=" + btnFont + " 版本字样=" + verFont + "；文案「" + upBtn + "」" + upBtn.Length + " 字");
                Check(bad, "66 号 主窗左下角橙色按钮：初始不显示、UpdateState 报更新后显示、清状态后收回（同一状态源派生，不自建第二份）",
                    btn != null && hiddenAtFirst && shownAfter && hiddenAfterReset);
                Check(bad, "66 号 按钮样式＝主题里那份 BtnWarning、背景是 DynamicResource 的 Brush 键（断言不钉颜色字面量，换主题不破）",
                    styleIsWarning && orangeFromTheme);
                Check(bad, "66 号 下载中按钮置灰（防重复起下载）", greyWhileDownloading);
                Check(bad, "66 号 按钮与版本字样同字号量级（一行内、垂直居中，不抢主操作位置）",
                    btnFont > 0 && Math.Abs(btnFont - verFont) <= 2);
                Check(bad, "66 号 按钮文案四字以内（实测「" + upBtn + "」" + upBtn.Length + " 字）",
                    upBtn.Length >= 1 && upBtn.Length <= 5 && upBtn != "s.UpBtn");

                // ===== ⑧ 弹窗三段（安装版）：确认→下载进度→安装确认→交棒 Setup 并退出 =====
                string launched = null, opened = null; bool exited = false;
                UpdateInstaller.LaunchOverride = p => { launched = p; return true; };
                UpdateInstaller.OpenFolderOverride = p => { opened = p; return true; };
                UpdateDialog.ExitOverride = () => exited = true;
                UpdateDownloader.FetchOverride = async (u, p, c) =>
                {
                    if (p != null) { p.Report(new UpdateDownloader.Progress { Received = 30, Total = 100 }); p.Report(new UpdateDownloader.Progress { Received = 100, Total = 100 }); }
                    await Task.Yield();
                    return UpdateDownloader.Outcome.Success(Path.Combine(Path.GetTempPath(), "PrintPile-update-1.0.1.0.exe"));
                };
                UpdateState.ResetForTest();
                var dlg = new UpdateDialog(UpdEntry("1.0.1.0", "https://printpile.cn/PrintPile-Setup-v1.0.1.0.exe", "修 3 处"), new Version(1, 0, 0, 0), true);
                bool p0 = dlg.CurrentPhaseForTest == UpdateDialog.Phase.Confirm;
                string firstPrimary = dlg.PrimaryTextForTest;
                dlg.PressPrimaryForTest();
                PumpUntil(() => dlg.CurrentPhaseForTest != UpdateDialog.Phase.Downloading);
                bool p1 = dlg.CurrentPhaseForTest == UpdateDialog.Phase.ReadyInstalled;
                double pct = dlg.PercentForTest; string ptxt = dlg.ProgressTextForTest;
                string hint1 = dlg.HintTextForTest ?? "";
                dlg.PressPrimaryForTest();
                PumpUntil(() => launched != null);
                bool handed = launched != null && UpdateState.Installing && exited;
                sb.AppendLine("— 66 号 弹窗（安装版）：确认段主按钮「" + firstPrimary + "」→ 百分比=" + pct + " 进度文本=\"" + ptxt
                    + "\" → 安装确认段提示=\"" + hint1 + "\" → 起进程=\"" + launched + "\" 退出钩子=" + exited);
                Check(bad, "66 号 弹窗三段（安装版）：确认→下载进度到 100%→给出「即将关闭并打开安装向导」确认→点确认才起 Setup 并交棒退出",
                    p0 && p1 && Math.Abs(pct - 100) < 0.01 && hint1.Length > 0 && handed);
                Check(bad, "66 号 交棒后左下角按钮消失（状态进 Installing，本会话不再提示）",
                    !UpdateState.ButtonVisible && UpdateState.Pending == null);
                Check(bad, "66 号 起的就是下载到的那个文件（路径取下载结果，不另猜名字）",
                    launched != null && launched.EndsWith("PrintPile-update-1.0.1.0.exe", StringComparison.OrdinalIgnoreCase));

                // ===== ⑨ 弹窗（便携版）：不自动覆盖，给解压提示＋【打开所在文件夹】 =====
                UpdateState.ResetForTest();
                var dlg2 = new UpdateDialog(UpdEntry("1.0.1.0", "https://printpile.cn/PrintPile-v1.0.1.0-portable.zip", "n"), new Version(1, 0, 0, 0), false);
                dlg2.PressPrimaryForTest();
                PumpUntil(() => dlg2.CurrentPhaseForTest != UpdateDialog.Phase.Downloading);
                bool portableReady = dlg2.CurrentPhaseForTest == UpdateDialog.Phase.ReadyPortable;
                string hint2 = dlg2.HintTextForTest ?? "";
                dlg2.PressPrimaryForTest();
                PumpUntil(() => opened != null);
                sb.AppendLine("— 66 号 弹窗（便携版）：ReadyPortable=" + portableReady + " 提示=\"" + hint2 + "\" 打开文件夹=\"" + opened + "\"");
                Check(bad, "66 号 便携版不自动覆盖：下载完成进 ReadyPortable、给「解压覆盖到当前目录」提示、点【打开所在文件夹】才调 explorer",
                    portableReady && hint2.Length > 0 && opened != null && opened.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

                // ===== ⑩ 取消语义：只关窗，按钮保留 =====
                UpdateState.ResetForTest();
                UpdateState.ReportFound(UpdEntry("1.0.1.0", "https://printpile.cn/a.zip", "n"));
                var dlg3 = new UpdateDialog(UpdateState.Pending, new Version(1, 0, 0, 0), true);
                dlg3.PressSecondaryForTest();
                bool keptAfterCancel = UpdateState.ButtonVisible && UpdateState.Pending != null;
                sb.AppendLine("— 66 号 取消语义：确认段点取消后 状态仍在=" + keptAfterCancel + " 段=" + dlg3.CurrentPhaseForTest);
                Check(bad, "66 号 弹窗「取消」只关本窗：不清状态、左下角按钮保留（下次启动仍检测到更新会再出现）", keptAfterCancel);

                // ===== ⑪ 下载失败→失败段（可重试） =====
                UpdateState.ResetForTest();
                UpdateDownloader.FetchOverride = (u, p, c) => Task.FromResult(UpdateDownloader.Outcome.Failed(UpdateChecker.FailKind.Timeout, "timeout"));
                var dlg4 = new UpdateDialog(UpdEntry("1.0.1.0", "https://printpile.cn/a.zip", "n"), new Version(1, 0, 0, 0), true);
                dlg4.PressPrimaryForTest();
                PumpUntil(() => dlg4.CurrentPhaseForTest != UpdateDialog.Phase.Downloading);
                string retryText = dlg4.PrimaryTextForTest;
                sb.AppendLine("— 66 号 下载失败段：段=" + dlg4.CurrentPhaseForTest + " 主按钮文案=\"" + retryText + "\" 提示=\"" + (dlg4.HintTextForTest ?? "") + "\"");
                Check(bad, "66 号 下载失败进失败段、主按钮变「重试」、不弹第二层框也不崩",
                    dlg4.CurrentPhaseForTest == UpdateDialog.Phase.Failed && !string.IsNullOrEmpty(retryText) && (dlg4.HintTextForTest ?? "").Length > 0);

                // ===== ⑫ 自动检查通路：注入假清单→RunOnceAsync→按钮该亮；开关关着＝不发起 =====
                UpdateState.ResetForTest();
                AutoUpdate.BubbleOverride = (t, x) => { };
                DateTime baseUtc = oldLast ?? new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc);
                AutoUpdate.Clock = new FixedClock { Moment = baseUtc.AddHours(25) };   // 一定过 24h 闸门，且不动用户设置里的时刻
                SettingsService.SetAutoCheckUpdate(true);
                UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[{\"version\":\"1.0.1.0\",\"date\":\"2026-10-11\",\"url\":\"https://printpile.cn/PrintPile-Setup-v1.0.1.0.exe\",\"notes\":\"自动检查假清单\",\"active\":true}]}");
                var tAuto = AutoUpdate.RunOnceAsync(new Version(1, 0, 0, 0));
                PumpUntil(() => tAuto.IsCompleted, 5000);
                bool fired = tAuto.IsCompleted && tAuto.Result;
                PumpUntil(() => UpdateState.Pending != null, 2000);
                bool autoLit = UpdateState.ButtonVisible && UpdateState.Pending != null;
                SettingsService.SetAutoCheckUpdate(false);
                UpdateState.ResetForTest();
                var tOff = AutoUpdate.RunOnceAsync(new Version(1, 0, 0, 0));
                PumpUntil(() => tOff.IsCompleted, 5000);
                bool firedOff = tOff.IsCompleted && tOff.Result;
                sb.AppendLine("— 66 号 自动检查通路：开关开＝发起=" + fired + " 按钮亮=" + autoLit
                    + "；开关关＝发起=" + firedOff + " 按钮亮=" + UpdateState.ButtonVisible);
                Check(bad, "66 号 自动检查发现新版＝同样点亮左下角按钮（假清单注入，不打真网络）", fired && autoLit);
                Check(bad, "66 号 「自动检查更新」关闭时＝根本不发起检查、按钮不自动出现", !firedOff && !UpdateState.ButtonVisible);
                Check(bad, "66 号 到点判据纯函数不动设置（关＝false；没查过＝true；满 24h＝true；差 1 分钟＝false）",
                    !AutoUpdate.Due(false, null, baseUtc) && AutoUpdate.Due(true, null, baseUtc)
                    && AutoUpdate.Due(true, baseUtc.AddHours(-24), baseUtc) && !AutoUpdate.Due(true, baseUtc.AddHours(-23), baseUtc));

                // ===== ⑬ 安装版/便携版判据（临时目录造两种；注册表探针只断"不抛"） =====
                string probeRoot = Path.Combine(Path.GetTempPath(), "PrintPile-66-probe-" + Guid.NewGuid().ToString("N"));
                string instDir = Path.Combine(probeRoot, "installed"), portDir = Path.Combine(probeRoot, "portable");
                Directory.CreateDirectory(instDir); Directory.CreateDirectory(portDir);
                File.WriteAllText(Path.Combine(instDir, "unins000.exe"), "x");
                bool fileProbe = UpdateInstaller.HasUninstallerFile(instDir) && !UpdateInstaller.HasUninstallerFile(portDir);
                string sub = Path.Combine(instDir, "bin"); Directory.CreateDirectory(sub);
                bool upProbe = UpdateInstaller.HasUninstallerFile(sub);
                bool regProbe = true;
                try { UpdateInstaller.HasRegistryUninstallEntry(); } catch { regProbe = false; }
                try { Directory.Delete(probeRoot, true); } catch { }
                sb.AppendLine("— 66 号 安装判据：unins000.exe 探针（有=True／便携=False）=" + fileProbe + " 上级回溯=" + upProbe + " 注册表探针不抛=" + regProbe);
                Check(bad, "66 号 安装版判据：运行目录（含上一级）有 unins000.exe 即安装版、没有即便携版；注册表探针不抛异常", fileProbe && upProbe && regProbe);
                Check(bad, "66 号 AppId 是标准 GUID（与 PrintPile-Setup.iss 同一枚，改一处必改另一处）",
                    UpdateInstaller.AppId.Length == 38 && UpdateInstaller.AppId[0] == '{' && Guid.TryParse(UpdateInstaller.AppId.Trim('{', '}'), out _));

                // ===== ⑭ 新文案 14 枚中英都取到值（不露键名） =====
                string[] keys = { "s.UpBtn", "s.UpTitle", "s.UpFoundFmt", "s.UpCurrentFmt", "s.UpSizeFmt", "s.UpBtnDownload",
                    "s.UpDownloading", "s.UpFailed", "s.UpBtnInstall", "s.UpBtnLater", "s.UpExitConfirm", "s.UpPortableHint",
                    "s.UpBtnFolder", "s.UpOpening" };
                var missing = keys.Where(k => { var v = K(k); return string.IsNullOrEmpty(v) || v == k; }).ToList();
                sb.AppendLine("— 66 号 新增文案键：" + keys.Length + " 枚，取不到值的=" + missing.Count + (missing.Count == 0 ? "" : "：" + string.Join(",", missing)));
                Check(bad, "66 号 新增 14 枚更新文案键当前语言全部取到值（中英成对由资源键守护那条核）", missing.Count == 0);
            }
            catch (Exception ex)
            {
                sb.AppendLine("探针异常：" + ex);
                bad.Add("异常 " + ex.Message);
            }
            finally
            {
                UpdateChecker.FetchOverride = oldFetch;
                UpdateDownloader.FetchOverride = oldDown;
                UpdateInstaller.LaunchOverride = oldLaunch;
                UpdateInstaller.OpenFolderOverride = oldOpen;
                UpdateDialog.ExitOverride = oldExit;
                AutoUpdate.BubbleOverride = oldBubble;
                AutoUpdate.Clock = oldClock;
                SettingsService.SetAutoCheckUpdate(oldAuto);
                if (oldLast.HasValue) SettingsService.SetLastAutoCheckUtc(oldLast.Value);   // RunOnceAsync 会写发起时刻：能还原就还原，
                UpdateState.ResetForTest();                                                 // 原本没查过（null）则留一个时刻，下次启动晚 24h 再查，属可接受
            }
            WriteDiag("66号更新按钮定位.txt", sb.ToString());            return bad.Count == 0
                ? "通过（66 号：百分比/版本净化防穿越/临时命名/白名单拒下/进度→完成/取消删半截/超时分类 七项纯函数与通路；主窗左下角按钮由 UpdateState 派生（初始隐藏→报更新显示→下载中置灰→交棒消失）、样式取主题 BtnWarning 不钉色值、文案四字以内；弹窗三段＝确认→下载→安装确认，安装版点确认才起 Setup 并交棒退出、便携版给解压提示＋打开所在文件夹；取消只关窗状态保留；下载失败进可重试段；自动检查通路（假清单注入）开关开=点亮、关=不发起；安装判据 unins000.exe＋注册表不抛；AppId GUID 合规；新增 14 枚文案键全取到值；全程离线不起真进程；逐条数字见 66号更新按钮定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>66 号令渲染取证：主窗左下角橙色按钮一张＋更新弹窗三态（确认／安装版就绪／便携版就绪）各一张。</summary>
        internal static int V66Render(string log)
        {
            int code = 0;
            var sb = new StringBuilder();
            var oldDown = UpdateDownloader.FetchOverride;
            var oldExit = UpdateDialog.ExitOverride;
            try
            {
                UpdateDialog.ExitOverride = () => { };
                UpdateDownloader.FetchOverride = (u, p, c) => Task.FromResult(UpdateDownloader.Outcome.Success(
                    Path.Combine(Path.GetTempPath(), "PrintPile-update-1.0.1.0.exe")));
                var ent = UpdEntry("1.0.1.0", "https://printpile.cn/PrintPile-Setup-v1.0.1.0.exe", "1) 修复批量打印后托盘图标残留\n2) 历史记录支持按打印机筛选\n3) 新增橙色更新提示");

                App.ApplyTheme(AppTheme.Light);
                UpdateState.ResetForTest();
                UpdateState.ReportFound(ent);
                var mw = new MainWindow();
                code |= RenderWindow("66号主窗更新按钮自检.txt", "66号主窗-左下角更新按钮.png", mw, 1120, 700,
                    new[] { "VersionLabel", "BtnUpdate" });
                sb.AppendLine("主窗左下角（报更新态）＝66号主窗-左下角更新按钮.png");

                var dlg1 = new UpdateDialog(ent, new Version(1, 0, 0, 0), true);
                code |= RenderDialog(dlg1, "66号更新弹窗确认态.txt", "66号更新弹窗-确认.png");
                var dlg2 = new UpdateDialog(ent, new Version(1, 0, 0, 0), true);
                dlg2.PressPrimaryForTest();
                PumpUntil(() => dlg2.CurrentPhaseForTest != UpdateDialog.Phase.Downloading);
                code |= RenderDialog(dlg2, "66号更新弹窗安装态.txt", "66号更新弹窗-安装确认.png");
                var entZip = UpdEntry("1.0.1.0", "https://printpile.cn/PrintPile-v1.0.1.0-portable.zip", "1) 修复批量打印后托盘图标残留\n2) 历史记录支持按打印机筛选\n3) 新增橙色更新提示");
                var dlg3 = new UpdateDialog(entZip, new Version(1, 0, 0, 0), false);
                dlg3.PressPrimaryForTest();
                PumpUntil(() => dlg3.CurrentPhaseForTest != UpdateDialog.Phase.Downloading);
                code |= RenderDialog(dlg3, "66号更新弹窗便携态.txt", "66号更新弹窗-便携版.png");
                sb.AppendLine("弹窗三态＝确认 / 安装版「即将关闭并打开安装向导」/ 便携版「解压覆盖到当前目录」；下载中态由断言逐数核对（百分比与「已下载／总大小」文本）。");
            }
            catch (Exception ex) { sb.AppendLine("渲染异常：" + ex); code |= 1; }
            finally
            {
                UpdateDownloader.FetchOverride = oldDown;
                UpdateDialog.ExitOverride = oldExit;
                UpdateState.ResetForTest();
                App.ApplyTheme(AppTheme.Light);
            }
            string txt = sb.ToString();
            txt += "\r\n说明：66 号令更新体验出图——左下角橙色按钮紧贴版本字样（样式走主题 BtnWarning，深浅主题各自取橙）；"
                 + "更新弹窗三段同窗（确认→下载→安装），便携版只给「打开所在文件夹」＋解压覆盖提示，v1 不自动覆盖。\r\n";
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }

        /// <summary>把一只没 Show 过的对话框内容垫一层主题底色出图（同 61 号"内容元素单出"口径，避免无窗口透明成黑块）。</summary>
        private static int RenderDialog(Window dlg, string logName, string pngName)
        {
            var content = dlg.Content as FrameworkElement;
            dlg.Content = null;
            var host = new System.Windows.Controls.Border
            {
                Background = (Brush)(Application.Current != null ? Application.Current.TryFindResource("WindowBgBrush") : null) ?? Brushes.White,
                Child = content
            };
            return RenderControl(logName, pngName, host, 460, 260);
        }
    }
}
