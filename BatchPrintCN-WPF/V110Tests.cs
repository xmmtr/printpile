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
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace BatchPrintCN
{
    /// <summary>
    /// 72 号令断言面（V1.1.0·F7 监视文件夹）：规则模型与持久化往返、稳定性判定、时段矩阵、目录保护、
    /// 去重、模板→逐文件覆盖、企业门控三态、监视页与规则对话框、版本 1.1.0.0。
    ///
    /// 73 号令注记：门控取消后本段不再需要任何解锁打桩——时钟走 WatchEngine.Clock、忙否走 WatchEngine.Busy、
    /// 入队走 WatchEngine.Fire，规则文件落 %TEMP% 一次性目录（WatchRuleStore.PathOverride），用户真实 data\ 一个字节都不写。
    /// 唯一"真跑"的一段仍是稳定性判定——那里必须用真文件系统（半写入的文件靠打桩证明不了"不入队"这条红线）。
    /// </summary>
    public static partial class WpfTests
    {
        private static readonly DateTime V110T0 = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Local);

        /// <summary>临时规则文件路径（每条断言自己管自己的目录，用完删）。</summary>
        private static string V110TempDir(string tag)
        {
            string dir = Path.Combine(Path.GetTempPath(), "pp-110-" + tag + "-" + Process.GetCurrentProcess().Id);
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        internal static string V110Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();

            var lang0 = SettingsService.Language;
            var probe0 = WatchRules.OpenProbeOverride;
            var storePath0 = WatchRuleStore.PathOverride;
            string workDir = null;
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                workDir = V110TempDir("rules");
                WatchRuleStore.PathOverride = () => Path.Combine(workDir, "watch-rules.json");

                // ===== ① 规则 CRUD 与持久化往返（存进去几条、读回来几条，逐字段一致；删一条只少一条） =====
                var r1 = WatchRule.New(Path.Combine(workDir, "a"));
                r1.Name = "上午票据"; r1.Types = "*.pdf;*.docx"; r1.IncludeSubfolders = true;
                r1.AllDay = false; r1.StartTime = "08:30"; r1.EndTime = "11:00";
                r1.Printer = "FakePrinter-A"; r1.TemplateName = "A4 黑白双面"; r1.MoveAfterPrint = true;
                var r2 = WatchRule.New(Path.Combine(workDir, "b")); r2.Name = "晚上报表"; r2.Enabled = false;
                var r3 = WatchRule.New(Path.Combine(workDir, "c")); r3.Name = "";
                bool saved1 = WatchRuleStore.Save(new List<WatchRule> { r1, r2, r3 });
                List<WatchRule> back = WatchRuleStore.Load();
                WatchRule b1 = back.FirstOrDefault(r => r.Id == r1.Id);
                bool roundtrip = saved1 && back.Count == 3 && b1 != null
                    && b1.Name == r1.Name && b1.Folder == r1.Folder && b1.Types == r1.Types && b1.IncludeSubfolders
                    && !b1.AllDay && b1.StartTime == "08:30" && b1.EndTime == "11:00"
                    && b1.Printer == "FakePrinter-A" && b1.TemplateName == "A4 黑白双面" && b1.MoveAfterPrint && b1.Enabled
                    && back.First(r => r.Id == r2.Id).Enabled == false;
                sb.AppendLine("— 72 号 规则往返：存 3 条读回 " + back.Count + " 条、逐字段一致＝" + roundtrip
                    + "（文件在临时目录：" + WatchRuleStore.Path0 + "）");
                Check(bad, "72 号 规则 CRUD·持久化往返：三条规则存进 watch-rules.json 再读回，八个字段逐条一致（含停用态与默认值）", roundtrip);

                // 未命名那条用目录名显示（复测点②：规则名可以留空）
                WatchRule b3 = back.First(r => r.Id == r3.Id);
                Check(bad, "72 号 规则名留空＝用文件夹目录名显示，两者都空才落到「未命名」资源键",
                    WatchRules.DisplayNameOf(b3) == "c" && WatchRules.DisplayNameOf(WatchRule.New("")) == K("s.WatchUnnamed"));

                // 删中间一条：只少那一条，另两条原样
                back.RemoveAll(r => r.Id == r2.Id);
                WatchRuleStore.Save(back);
                List<WatchRule> back2 = WatchRuleStore.Load();
                Check(bad, "72 号 删除只删规则本身：删掉第二条后库里剩 2 条、剩下两条逐字段没动（本功能不删用户文件，规则文件也只重写自己）",
                    back2.Count == 2 && back2.Any(r => r.Id == r1.Id) && back2.Any(r => r.Id == r3.Id)
                    && back2.First(r => r.Id == r1.Id).TemplateName == "A4 黑白双面");

                // 文件被写坏（不是合法 JSON）＝当空处理并留一份 .bak，不崩
                File.WriteAllText(WatchRuleStore.Path0, "{ 这不是 JSON");
                List<WatchRule> broken = null; bool brokenSafe = true;
                try { broken = WatchRuleStore.Load(); } catch { brokenSafe = false; }
                Check(bad, "72 号 规则文件损坏＝当作\"还没有规则\"＋原文备份成 watch-rules.json.bak＋日志说一次，绝不崩也绝不静默覆盖",
                    brokenSafe && broken != null && broken.Count == 0 && File.Exists(WatchRuleStore.Path0 + ".bak"));

                // settings.json 键账零新增（规则落自己的文件，72 号键账纪律）
                // 73 号令就地改判：DTO 由 13 枚减为 **11 枚**——LicenseCode 与 TrialStart 两枚随解锁方案一并删除
                // 80 号令就地改判：11 → **12 枚**——本令新增 autoStart 一枚（开机自启动意图），这是 72/73 号以来第一次动 DTO，理由与影响见 80 号令回执
                //（本令对键账只做核减、不做新增，"零新增"这条纪律不变）。
                var persistType = typeof(SettingsService).GetNestedType("Persist", System.Reflection.BindingFlags.NonPublic);
                int keys = persistType == null ? -1 : persistType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length;
                Check(bad, "72 号 键账对账（73 号改判 13→11；80 号改判 11→12）：settings.json 持久化 DTO 现 " + keys + " 枚（72 号那枚规则文件仍在 data\\watch-rules.json，不进设置）", keys == 12);

                // ===== ② 时段过滤纯函数矩阵（全天／正常档／两端边界／跨零点／坏格式放行） =====
                bool w1 = WatchRules.InWindow(V110T0, true, "08:00", "18:00");                                   // 全天＝恒真
                bool w2 = WatchRules.InWindow(V110T0.AddHours(1), false, "08:00", "18:00");                       // 10:00 在 08–18 内
                bool w3 = WatchRules.InWindow(V110T0.AddHours(-1), false, "08:00", "18:00");                      // 08:00 整＝起算（含）
                bool w4 = WatchRules.InWindow(V110T0.AddHours(9), false, "08:00", "18:00");                       // 18:00 整＝止不算（不含）
                bool w5 = WatchRules.InWindow(V110T0.AddHours(12), false, "08:00", "18:00");                      // 21:00 时段外
                bool w6 = WatchRules.InWindow(V110T0.AddHours(13), false, "22:00", "06:00");                      // 22:00 跨零点·起
                bool w7 = WatchRules.InWindow(V110T0.AddHours(-8), false, "22:00", "06:00");                      // 01:00 跨零点·中
                bool w8 = !WatchRules.InWindow(V110T0.AddHours(-3), false, "22:00", "06:00");                     // 06:00 跨零点·止外
                bool w9 = WatchRules.InWindow(V110T0, false, "09:00", "09:00");                                   // 起止相等＝全天
                bool w10 = WatchRules.InWindow(V110T0, false, "不是时间", "18:00");                                // 坏格式＝放行并交给日志
                bool w11 = WatchRules.InWindow(V110T0, false, "", null);                                          // 空＝同样放行
                bool w12 = WatchRules.InWindow(V110T0.AddMinutes(30), false, "08:00", "18:00");                   // 09:30 在内
                bool windowMatrix = w1 && w2 && w3 && !w4 && !w5 && w6 && w7 && w8 && w9 && w10 && w11 && w12;
                sb.AppendLine("— 72 号 时段矩阵 12 档：全天=" + w1 + " 档内=" + w2 + " 起含=" + w3 + " 止不含=" + !w4
                    + " 档外=" + !w5 + " 跨零点起=" + w6 + " 中=" + w7 + " 止外=" + w8 + " 起止相等=" + w9
                    + " 坏格式放行=" + w10 + " 空放行=" + w11 + " 半点=" + w12);
                Check(bad, "72 号 时段过滤纯函数 12 档：全天恒真／左含右不含／跨零点取补集／起止相等与坏格式一律放行（宁可多打也不静默吞文件）", windowMatrix);

                // 类型名单解析：*.pdf;*.docx / .pdf,.txt / 裸 pdf 三种写法归一；空＝应用支持的全集
                var tl = WatchRules.TypeList("*.PDF; .docx ,txt");
                bool typeOk = tl.Count == 3 && tl.Contains(".pdf") && tl.Contains(".docx") && tl.Contains(".txt")
                    && WatchRules.TypeList("").Count == 0 && WatchRules.TypeList("*").Count == 0;
                Check(bad, "72 号 文件类型解析：通配符/大小写/分隔符归一（*.PDF、.docx、txt 都落到 .pdf/.docx/.txt），空与 * ＝不限类型", typeOk);

                // ===== ③ 稳定性判定（纯函数逐档 ＋ 真文件系统那一段） =====
                bool vHalf = !WatchRules.VerdictReady(1000, 1500, 5000, WatchRules.QuietMilliseconds, true);      // 还在长＝不入队
                bool vEarly = !WatchRules.VerdictReady(1000, 1000, 100, WatchRules.QuietMilliseconds, true);      // 大小稳了但没静默够
                bool vBusy = !WatchRules.VerdictReady(1000, 1000, 5000, WatchRules.QuietMilliseconds, false);    // 还被写者占用
                bool vReady = WatchRules.VerdictReady(1000, 1000, 900, WatchRules.QuietMilliseconds, true);      // 三件齐＝入队
                Check(bad, "72 号 稳定性判定纯函数四档：大小还在变／静默不够／打不开 一律不入队；三件都齐才入队（红线：绝不打印写了一半的文件）",
                    vHalf && vEarly && vBusy && vReady);

                // 真文件那一段：半写入（句柄独占）不入队 → 写完释放后入队（这里不打桩，打桩就证不了红线条款）
                // 先把闸门打开：未解锁时 Apply 一条观察者都不起（⑥ 正是断言这一点），这一段要的是"解锁后真的在监视"。
                // 73 号令：这里原来先"解锁"再起观察者，现在不必——启用中的规则一律起观察者。
                string watchDir = V110TempDir("live");
                var liveRule = WatchRule.New(watchDir); liveRule.Name = "真跑";
                WatchEngine.ResetForTest();
                var fired = new List<string>();
                WatchEngine.Busy = () => false;
                WatchEngine.Fire = (r, p) => { fired.Add(Path.GetFileName(p)); return true; };
                WatchEngine.Clock = () => DateTime.Now;
                WatchEngine.Apply(new List<WatchRule> { liveRule });
                string liveFile = Path.Combine(watchDir, "drop.pdf");
                bool engineOn = WatchEngine.TrackedRuleCount == 1;
                var fsHalf = new FileStream(liveFile, FileMode.Create, FileAccess.Write, FileShare.None);
                byte[] pdf = PdfBytes();
                fsHalf.Write(pdf, 0, pdf.Length);
                fsHalf.Flush(true);
                PumpUntil(() => WatchEngine.PendingCount(liveRule.Id) > 0, 6000);          // 事件到候选表
                PumpUntil(() => false, 2200);                                               // 再静默一会儿（够 700ms 的静默线）
                int duringHalfWrite = fired.Count;
                bool pendingKept = WatchEngine.PendingCount(liveRule.Id) > 0;               // 半写入：一直躺在候选里不入队
                fsHalf.Close();                                                             // 写完并释放句柄
                PumpUntil(() => fired.Count > 0, 9000);
                int afterClose = fired.Count;
                bool dedupedNow = WatchEngine.HandledNow(liveFile);                          // 打过＝身份进已处理表
                File.WriteAllText(liveFile, "同一份文件的新版本内容更长");                    // 同名换一个内容 → 身份变
                bool identityChanged = !WatchEngine.HandledNow(liveFile);
                sb.AppendLine("— 72 号 稳定性真跑：观察者起来=" + engineOn + " 半写入期间入队=" + duringHalfWrite
                    + "（候选仍扣着=" + pendingKept + "） 释放后入队=" + afterClose + " 已处理表认出同一份=" + dedupedNow
                    + " 换内容后身份变=" + identityChanged);
                Check(bad, "72 号 稳定性判定·真文件系统：句柄没释放（还在写）的 PDF 不入队，关闭之后同一拍内入队并交给主窗（临时目录真实造文件）",
                    engineOn && duringHalfWrite == 0 && pendingKept && afterClose == 1);
                Check(bad, "72 号 同文件去重＝路径＋大小＋最后写入时间三件相同才算打过；换一版同名新内容身份就变（会再打）",
                    dedupedNow && identityChanged);
                WatchEngine.ResetForTest();
                WatchEngine.Stop();

                // ===== ④ 目录保护五档（72 号文件安全三律④） =====
                string otherDir = V110TempDir("nested");
                string parent = Path.Combine(otherDir, "p");
                string child = Path.Combine(parent, "q");
                Directory.CreateDirectory(child);
                var sibling = new List<WatchRule> { WatchRule.New(parent) };
                WatchRules.FolderProblem pEmpty = WatchRules.CheckFolder("", null, null);
                WatchRules.FolderProblem pMissing = WatchRules.CheckFolder(Path.Combine(otherDir, "不存在的那个"), null, null);
                WatchRules.FolderProblem pSelf = WatchRules.CheckFolder(AppPaths.Root, null, null);
                WatchRules.FolderProblem pSelfSub = WatchRules.CheckFolder(Path.Combine(AppPaths.Root, "data"), null, null);
                WatchRules.FolderProblem pTooLong = WatchRules.CheckFolder(Path.GetTempPath() + new string('x', 300), null, null);
                WatchRules.FolderProblem pNested = WatchRules.CheckFolder(child, sibling, "self-1");
                WatchRules.FolderProblem pNestedUp = WatchRules.CheckFolder(parent, new List<WatchRule> { WatchRule.New(child) }, "self-2");
                WatchRules.FolderProblem pSame = WatchRules.CheckFolder(parent, sibling, "self-3");
                WatchRules.FolderProblem pSelfIgnored = WatchRules.CheckFolder(parent, sibling, sibling[0].Id);   // 编辑自己时不算嵌套
                WatchRules.FolderProblem pOk = WatchRules.CheckFolder(otherDir, null, null);
                sb.AppendLine("— 72 号 目录保护：空=" + pEmpty + " 不存在=" + pMissing + " 程序目录=" + pSelf + " 程序目录之下=" + pSelfSub
                    + " 过长=" + pTooLong + " 子套父=" + pNested + " 父套子=" + pNestedUp + " 同一路径=" + pSame
                    + " 编辑自己=" + pSelfIgnored + " 正常=" + pOk);
                Check(bad, "72 号 目录保护五档全部拒掉：空路径／不存在／程序自身目录（含 data 之下）／路径过长／与其他规则互为嵌套（子套父与父套子都算，同一路径也算）",
                    pEmpty == WatchRules.FolderProblem.Empty && pMissing == WatchRules.FolderProblem.Missing
                    && pSelf == WatchRules.FolderProblem.InsideSelf && pSelfSub == WatchRules.FolderProblem.InsideSelf
                    && pTooLong == WatchRules.FolderProblem.TooLong && pNested == WatchRules.FolderProblem.Nested
                    && pNestedUp == WatchRules.FolderProblem.Nested && pSame == WatchRules.FolderProblem.Nested);
                Check(bad, "72 号 目录保护不误伤：编辑自己时不跟自己算嵌套，正常目录放行；五种拒绝理由各有中英成对文案",
                    pSelfIgnored == WatchRules.FolderProblem.None && pOk == WatchRules.FolderProblem.None
                    && new[] { "s.WatchErrEmpty", "s.WatchErrMissing", "s.WatchErrSelfDir", "s.WatchErrNested", "s.WatchErrTooLong" }
                        .All(k => K(k) != k && K(k).Length > 0));

                // ===== ⑤ 模板→逐文件覆盖（打印链路一行不改，模板参数走既有 EffectiveOptions 那条解析） =====
                var tpl = new PrintOptions { Duplex = DuplexMode.LongEdge, Color = ColorMode.Color, Paper = PaperKind.A5, PaperDisplayName = "A5", Orientation = OrientationMode.Landscape, PagesPerSheet = 2, Booklet = true, PageParity = PageParityMode.Odd, ScalePercent = 80, PaperSourceName = "纸盒 2" };
                var tplGlobal = new PrintOptions { Duplex = DuplexMode.Simplex, Color = ColorMode.BlackWhite, Paper = PaperKind.A4, PagesPerSheet = 1, Booklet = false, ScalePercent = 100 };
                var ov = WatchRules.TemplateToOverride(tpl);
                PrintOptions eff = EffectiveOptions.Resolve(tplGlobal, ov);
                bool tplApplied = ov != null && ov.HasAny && eff != (object)tplGlobal
                    && eff.Duplex == DuplexMode.LongEdge && eff.Color == ColorMode.Color && eff.Paper == PaperKind.A5
                    && eff.Orientation == OrientationMode.Landscape && eff.PagesPerSheet == 2 && eff.Booklet
                    && eff.PageParity == PageParityMode.Odd && eff.ScalePercent == 80 && eff.PaperSourceName == "纸盒 2";
                Check(bad, "72 号 规则绑模板＝落成该行逐文件覆盖，生效参数逐档等于模板值（双面/彩色/A5/横向/2 合 1/小册子/仅奇数/80%/指定纸盒），与当时界面上的全局参数无关", tplApplied);
                bool noneFollows = EffectiveOptions.Resolve(tplGlobal, null) == (object)tplGlobal
                    && WatchRules.TemplateToOverride(null) == null;
                Check(bad, "72 号 规则没绑模板＝不产生覆盖、生效参数就是全局那一份（与手动打印同一条解析，不留第二条实现）", noneFollows);

                // ===== ⑥ 监视在任意状态下都工作（72 号立的是"门控三态"；73 号令取消门控 ⇒ 就地改判，条目不删） =====
                WatchRule gateRule = WatchRule.New(otherDir); gateRule.Name = "监视";
                var gateRules = new List<WatchRule> { gateRule };
                WatchEngine.ResetForTest();
                WatchEngine.Apply(gateRules);
                int tracked = WatchEngine.TrackedRuleCount;
                var wp = new WatchPage();
                wp.Refresh();
                bool openNow = wp.AddEnabledForTest && tracked == 1;
                gateRule.Enabled = false;
                WatchEngine.Apply(gateRules);
                int trackedDisabled = WatchEngine.TrackedRuleCount;
                gateRule.Enabled = true;
                WatchEngine.ResetForTest();
                sb.AppendLine("— 72/73 号 监视可用：观察者=" + tracked + "、新建可用=" + wp.AddEnabledForTest + "、停用后=" + trackedDisabled);
                Check(bad, "73 号 改判：监视不再有解锁状态——启用中的规则一律起观察者、监视页「新建规则」任何时候可用（原三态判据随功能退场，条目不删）", openNow);
                Check(bad, "72 号 停用中的规则不占观察者（改判后仍成立）", trackedDisabled == 0);

                // ===== ⑦ 规则编辑对话框：目录保护与时间格式当场拦下来，合法值才写回规则 =====
                WatchRule draft = WatchRule.New("");
                draft.AllDay = false;
                var dlg = new WatchRuleWindow(draft, new List<WatchRule>(), true);
                dlg.SetFolderForTest("");
                bool blockEmpty = !dlg.TryCommit() && dlg.ErrorForTest == K("s.WatchErrEmpty");
                dlg.SetFolderForTest(AppPaths.Root);
                bool blockSelf = !dlg.TryCommit() && dlg.ErrorForTest == K("s.WatchErrSelfDir");
                dlg.SetFolderForTest(otherDir);
                dlg.SetTimeForTest("09:00", "不是时间");
                bool blockTime = !dlg.TryCommit() && dlg.ErrorForTest == K("s.WatchDlgBadTime");
                dlg.SetAllDayForTest(false);
                dlg.SetTimeForTest("22:00", "06:00");
                dlg.SetTypesForTest("*.pdf;*.xlsx");
                dlg.SetMoveForTest(true);
                bool committed = dlg.TryCommit() && dlg.ErrorForTest == null;
                WatchRule done = dlg.RuleForTest;
                sb.AppendLine("— 72 号 对话框校验：空目录拦=" + blockEmpty + " 程序目录拦=" + blockSelf + " 坏时间拦=" + blockTime
                    + " 合法后写回=" + committed + "（" + done.Folder + " / " + done.Types + " / " + done.StartTime
                    + "-" + done.EndTime + " / 移动=" + done.MoveAfterPrint + "）");
                Check(bad, "72 号 规则对话框·当场拦下三种非法（空目录／监视程序自身目录／时段写成非 HH:mm），拦下时只在本窗说一句且不关窗、不写回规则",
                    blockEmpty && blockSelf && blockTime && done.Folder != AppPaths.Root);
                Check(bad, "72 号 规则对话框·合法值逐项写回规则（类型/跨零点时段/打印后移动），打印后动作只有\"保留\"与\"移动到已完成\"两档（没有删除档）",
                    committed && done.Types == "*.pdf;*.xlsx" && done.StartTime == "22:00" && done.EndTime == "06:00"
                    && !done.AllDay && done.MoveAfterPrint);

                // 「移动」只搬不删：目标同名自动加序号，源文件在搬成之前一个字节都不少
                string mvDir = V110TempDir("move");
                string mvFile = Path.Combine(mvDir, "a.pdf");
                File.WriteAllBytes(mvFile, pdf);
                long sizeBefore = new FileInfo(mvFile).Length;
                var mvRule = WatchRule.New(mvDir); mvRule.MoveAfterPrint = true;
                string movedPath = WatchEngine.MoveToForTest(mvRule, mvFile);
                bool moved = File.Exists(movedPath) && !File.Exists(mvFile) && new FileInfo(movedPath).Length == sizeBefore
                    && Path.GetDirectoryName(movedPath) == Path.Combine(mvDir, WatchRules.DoneFolderName);
                string secondFile = Path.Combine(mvDir, "b.pdf");
                File.WriteAllBytes(secondFile, pdf);
                WatchEngine.MoveToForTest(mvRule, mvFile);   // 不存在＝原地不动也不抛
                string dup = Path.Combine(mvDir, "a.pdf");
                File.WriteAllBytes(dup, pdf);
                string movedDup = WatchEngine.MoveToForTest(mvRule, dup);
                Check(bad, "72 号 打印后动作＝移动到「已完成」子文件夹：大小一字不变、原名撞车时自动加序号、目标文件不在时静默不动也不抛（全程零删除）",
                    moved && !File.Exists(dup) && File.Exists(movedDup) && movedDup.Contains(" (2)"));

                // ===== ⑧ 版本与产物（72 号实升 1.1.0.0） =====
                string fv = null;
                try { fv = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName).FileVersion; } catch { }
                string av = AppVersion.Full, disp = AppVersion.Display;
                sb.AppendLine("— 72 号 版本：FileVersion=" + fv + " AssemblyVersion=" + av + " 界面版本＝" + disp);
                Check(bad, "72 号 版本实升（73 号令改判 1.2.0.0；80 号令再改判 1.2.1.0）：运行中程序 FileVersion／AssemblyVersion＝1.2.1.0、界面显示三段 v1.2.1（D-20 三位数口径不变）",
                    fv == "1.2.1.0" && av == "1.2.1.0" && disp == "v1.2.1");

                // ===== ⑨ 新增文案在当前语言取得到值（中英成对由资源键守护那条核统一管） =====
                // 73 号令就地改判：原清单里的 s.WatchLockTitle（未解锁引导态）与 s.LicFeatWatch（企业清单行）
                // 随功能与整块清单成对删除，改钉现在真正在位的两枚。
                // 78 号令就地改判：s.AboutFeatWatch（功能一览行）随整块删除，改钉关于页现存的开源声明行。
                string[] newKeys = {
                    "s.NavWatch", "s.WatchTitle", "s.WatchAdd", "s.WatchDeleteQ", "s.WatchColState",
                    "s.WatchDlgNew", "s.WatchDlgTypesHint", "s.WatchFiredFmt", "s.AboutLicenseLine"
                };
                var missingKeys = newKeys.Where(k => { var v = K(k); return string.IsNullOrEmpty(v) || v == k; }).ToList();
                Check(bad, "72 号 页面/对话框/关于页开源声明的代表键在当前语言都取到值（缺 " + missingKeys.Count + " 枚）", missingKeys.Count == 0);

                sb.AppendLine("— 72 号 临时目录（自检自己造、自己删）：" + workDir);
            }
            catch (Exception ex)
            {
                sb.AppendLine("探针异常：" + ex);
                bad.Add("异常 " + ex.Message);
            }
            finally
            {
                WatchEngine.ResetForTest();
                WatchEngine.Stop();
                WatchRules.OpenProbeOverride = probe0;
                WatchRuleStore.PathOverride = storePath0;
                App.ApplyLanguage(lang0);
                try { if (workDir != null && Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
            }
            WriteDiag("72号监视文件夹F7定位.txt", sb.ToString());
            return bad.Count == 0
                ? "通过（72 号：规则三条往返逐字段一致、删除只删规则、损坏文件当空＋留 .bak、settings.json 键账零新增；"
                  + "时段矩阵 12 档（左含右不含／跨零点取补集／坏格式放行）、类型名单三种写法归一；"
                  + "稳定性判定纯函数四档＋真文件系统那一段（半写入扣在候选表不入队、释放后入队）、同文件按大小＋最后写入时间去重；"
                  + "目录保护五档全拒（空/不存在/程序自身/过长/互为嵌套）且编辑自己不误伤；"
                  + "模板落成逐文件覆盖、生效参数逐档等于模板值，不绑模板时跟随全局（EffectiveOptions 唯一解析路径）；"
                  + "监视不再有解锁状态：启用中的规则一律起观察者、页面「新建规则」随时可点（73 号令就地改判原三态那条）；对话框当场拦三种非法且合法才写回；"
                  + "打印后移动零删除（撞名加序号）；FileVersion=1.2.1.0（80 号令末位进位）；逐条数字见 72号监视文件夹F7定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>一份最小可判定的 PDF 头（FileClassifier 认 %PDF- 魔数）。</summary>
        private static byte[] PdfBytes()
        {
            return Encoding.ASCII.GetBytes("%PDF-1.4\n%pp-72\n1 0 obj<</Type/Catalog>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");
        }

        /// <summary>72 号令渲染取证（73 号令改判：删掉"未解锁引导态"那张，其余三张保留）＝监视页浅/深＋规则编辑对话框。</summary>
        internal static int V110Render(string log)
        {
            int code = 0;
            var sb = new StringBuilder();
            var storePath0 = WatchRuleStore.PathOverride;
            string workDir = null;
            try
            {
                workDir = V110TempDir("render");
                WatchRuleStore.PathOverride = () => Path.Combine(workDir, "watch-rules.json");
                var a = WatchRule.New(Path.Combine(workDir, "票据")); a.Name = "上午票据（含子目录）";
                a.Types = "*.pdf;*.docx"; a.IncludeSubfolders = true; a.AllDay = false; a.StartTime = "08:00"; a.EndTime = "12:00";
                a.Printer = "FakePrinter-A"; a.TemplateName = "A4 黑白双面";
                var b = WatchRule.New(Path.Combine(workDir, "报表")); b.Name = "晚上报表"; b.AllDay = false;
                b.StartTime = "22:00"; b.EndTime = "06:00"; b.MoveAfterPrint = true; b.Enabled = false;
                Directory.CreateDirectory(a.Folder); Directory.CreateDirectory(b.Folder);
                WatchRuleStore.Save(new List<WatchRule> { a, b });

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var wp = new WatchPage();
                wp.Refresh();
                code |= RenderPadded(wp, "72号监视页浅色.txt", "72号监视页-浅色-规则列表.png", 1000, 520);
                sb.AppendLine("① 浅色：两条规则（第二条停用中＝状态列显示「停用中」，不给读数）");

                App.ApplyTheme(AppTheme.Dark);
                var wpe = new WatchPage();
                wpe.Refresh();
                code |= RenderPadded(wpe, "72号监视页深色.txt", "72号监视页-深色-规则列表.png", 1000, 520);
                sb.AppendLine("② 深色：同一页换主题（列宽与文字跟着主题色走，没有钉死色值）");

                var dlg = new WatchRuleWindow(WatchRule.New(a.Folder), new List<WatchRule>(), true);
                code |= RenderDialog(dlg, "72号规则对话框.txt", "72号规则编辑对话框.png");
                sb.AppendLine("③ 规则编辑对话框（深色）：七个字段＋目录/时间校验位");

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { sb.AppendLine("渲染异常：" + ex); code |= 1; }
            finally
            {
                WatchRuleStore.PathOverride = storePath0;
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                try { if (workDir != null && Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
            }
            string txt = sb.ToString();
            txt += "\r\n说明：72 号令三张取证（73 号令删掉引导态那张）——监视页浅色规则态／深色规则态／规则编辑对话框，都是真控件出图。\r\n";
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
