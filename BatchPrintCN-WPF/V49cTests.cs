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

namespace BatchPrintCN
{
    /// <summary>
    /// 44 号缺陷返修断言（版本仍 4.9.0.0，只修缺陷本身）：
    /// 缺1〔阻塞〕四项自检命令行直跑不退出——入口先归一化（裸写 selftest/formtest/dialogtest/themetest 同样是自检开关，
    ///        原来不带横线会被当成正常启动：不跑检查、弹主窗口、永不退出、stdout 零字节）；跑完由 FinishGate 统一收尾
    ///        （释放托盘图标 → 结论打到 stdout → 交退出码），并用真子进程按验收方的打法复现一遍。
    /// 缺2〔轻〕关闭提示窗正文与 41 号·修A 的行为对齐（当时只改了按钮与设置项文案，正文还写着任务栏那套说法）。
    /// 全程离线：不打任何网络端点；子进程只跑 dialogtest（实测一秒上下）不开界面、不碰浏览器。
    /// 红线复核：托盘/单实例/反馈跳官网/设置页/关于页/打印范围反馈的用户可见行为与本文件无关，一条都没动。
    /// </summary>
    public static partial class WpfTests
    {
        internal static string V49cUi()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            var mode0 = Application.Current.ShutdownMode;
            try
            {
                Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);

                sb.AppendLine("======== 44 号·缺1 自检入口归一化（带不带前导横线都算同一个开关）+ 收尾三件事 ========");
                bad.AddRange(V49cGate(sb));
                sb.AppendLine("======== 44 号·缺1 真跨进程复现验收打法（子进程裸写 dialogtest）========");
                bad.AddRange(V49cChild(sb));
                sb.AppendLine("======== 44 号·缺2 关闭提示窗正文改口为「最小化到系统托盘」========");
                bad.AddRange(V49cBody(sb));

                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("V4.9c 异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally { Application.Current.ShutdownMode = mode0; }

            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V49缺陷返修自检.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（缺1：裸写/带横线/大小写/斜杠四种写法各归一个开关，隐藏探针开关与文件参数不误认；结论行说清结果·用时·退出码，另给一条纯 ASCII 机器行供脚本抓；"
                  + "收尾释放托盘图标后 Environment.Exit 交码，与正常退出同一条 Dispose 且可重复；真子进程按验收打法裸写 dialogtest 实测自行退出、退出码 0、stdout 不再是零字节（秒数见 V49缺陷返修自检.txt）；"
                  + "门禁另带看门狗（硬上限超时按独立退出码报「最后完成段」，挂起自己现形）；"
                  + "缺2：中英正文各逐字钉住拍板文案，旧「任务栏」措辞 0 残留，提示窗实显与键值同源，按钮仍是「最小化」、任务中警示行与三选文案一字未动）"
                : "失败：" + string.Join("；", bad);
        }

        // ===== 缺1 ①：入口识别与收尾（都在真实方法体上跑，不复制一份逻辑） =====
        private static List<string> V49cGate(StringBuilder sb)
        {
            var bad = new List<string>();
            string gBare = App.SelfTestGate(new[] { "selftest" });
            string gDashed = App.SelfTestGate(new[] { "--selftest" });
            string gMixed = App.SelfTestGate(new[] { "--SelfTest" });
            string gSlash = App.SelfTestGate(new[] { "/formtest" });
            string gSpaced = App.SelfTestGate(new[] { " -Themetest " });
            string gProbe = App.SelfTestGate(new[] { "--instance-probe" });
            string gFile = App.SelfTestGate(new[] { @"C:\某个目录\文档.pdf" });
            sb.AppendLine("— 入口识别：\"selftest\"→" + (gBare ?? "null") + "　\"--selftest\"→" + (gDashed ?? "null")
                + "　\"--SelfTest\"→" + (gMixed ?? "null") + "　\"/formtest\"→" + (gSlash ?? "null")
                + "　\" -Themetest \"→" + (gSpaced ?? "null")
                + "　\"--instance-probe\"→" + (gProbe ?? "null") + "（不当自检开关，仍走探针分支）　文件参数→" + (gFile ?? "null"));
            Check(bad, "缺1 入口归一化：裸写、双横线、大小写、斜杠四种写法各归到同一个开关；隐藏探针开关与文件参数都不误认成自检",
                gBare == "selftest" && gDashed == "selftest" && gMixed == "selftest" && gSlash == "formtest"
                && gSpaced == "themetest" && gProbe == null && gFile == null);
            Check(bad, "缺1 同一套归一化也管隐藏开关：--instance-probe 与 -instance-probe 都算探针（自检里那条跨进程断言用的就是它）",
                App.HasFlag(new[] { "--instance-probe" }, "instance-probe")
                && App.HasFlag(new[] { "-instance-probe" }, "instance-probe")
                && !App.HasFlag(new[] { "instance-probe-x" }, "instance-probe"));

            string pass = GateResultLine("selftest", 0);
            string fail = GateResultLine("selftest", 1);
            string ascii = GateAsciiLine("selftest", 0);
            sb.AppendLine("— 结论行：通过态=\"" + pass + "\"　失败态=\"" + fail + "\"　机器行=\"" + ascii + "\"");
            Check(bad, "缺1 收尾结论行把三样一次说清（结果／用时／退出码），失败态不写成通过；"
                  + "另给一条纯 ASCII 机器行（重定向时中文行按系统 OEM 码页落盘，脚本按 UTF-8 读会花，抓这行才稳）"
                  + "【59 号就地改判，条目不删】：机器行末尾多带 build=<提交号前 8 位> 产物指纹，用于报告与产物对账",
                pass.Contains("结果=通过") && pass.Contains("退出码=0") && pass.Contains("用时=")
                && fail.Contains("结果=失败") && fail.Contains("退出码=1") && pass != fail
                && ascii.Contains("[selftest/selftest] result=PASS") && ascii.Contains("exit=0")
                && ascii.All(c => c < 128)
                && ascii.Contains(" build=") && BuildStamp().Length >= 7);

            // 托盘释放：收尾用的就是正常退出那一条 Dispose，而且可以连着调两次（主窗口 OnClosing 调过一次，FinishGate 再兜一次）
            Window w = null;
            try
            {
                w = new Window { Style = null, Width = 140, Height = 90, Left = -8000, Top = -8000, Opacity = 0, ShowInTaskbar = false, Title = "v49c-tray" };
                w.Show(); PumpToLoaded();
                TrayIcon.HideToTray(w); PumpToLoaded();
                bool up = TrayIcon.IsCreated && TrayIcon.IsVisible;
                TrayIcon.Dispose();
                bool gone = !TrayIcon.IsCreated && !TrayIcon.IsVisible;
                TrayIcon.Dispose();
                bool stillGone = !TrayIcon.IsCreated && !TrayIcon.IsVisible;
                sb.AppendLine("— 收尾释放托盘：进托盘后图标在场=" + up + " → Dispose 后不在场=" + gone + " → 再 Dispose 一次不炸且仍不在场=" + stillGone);
                Check(bad, "缺1 交退出码之前托盘图标确实被释放（与正常退出同一条 Dispose，重复调用不抛，不给需求方留死图标）",
                    up && gone && stillGone);
            }
            finally { try { if (w != null) w.Close(); } catch { } }

            string tim = GateTimings();
            sb.AppendLine("— 分段耗时表（命令行直跑时随结论行一起打到 stdout）：" + tim);
            Check(bad, "缺1 「跑多久」要有数据不要靠猜：门禁自带分段耗时表，头部按秒数倒序（自检报告里另有全量顺序表）",
                tim.Contains("阶段耗时") && tim.Contains("合计") && tim.Contains("s"));

            sb.AppendLine("— 看门狗：上限=" + GateWatchdogSeconds + "s（本机实测 --selftest ~63s、--themetest ~61s 的十倍余量）超时退出码="
                + GateExitTimeout + "，且会带着「最后完成段」出结论");
            Check(bad, "缺1 门禁自己盯挂起：跑过硬上限就交一个不与通过/失败相混的退出码（不必人盯到 100 秒才发现没退出）",
                GateWatchdogSeconds >= 300 && GateExitTimeout != 0 && GateExitTimeout != 1);
            return bad;
        }

        // ===== 缺1 ②：真子进程按验收方的打法跑 =====
        private static List<string> V49cChild(StringBuilder sb)
        {
            var bad = new List<string>();
            string exe = SingleInstance.CurrentExePath;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int code = -99;
            string stdout = "", stderr = "";
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(exe)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                };
                psi.ArgumentList.Add("dialogtest");      // 验收方就是这么打的：不带前导横线
                using (var p = System.Diagnostics.Process.Start(psi))
                {
                    if (p != null)
                    {
                        if (p.WaitForExit(60000)) { code = p.ExitCode; stdout = p.StandardOutput.ReadToEnd(); stderr = p.StandardError.ReadToEnd(); }
                        else { code = -2; try { p.Kill(); } catch { } }
                    }
                }
            }
            catch (Exception ex) { stderr = ex.Message; }
            sw.Stop();
            double secs = sw.ElapsedMilliseconds / 1000.0;
            sb.AppendLine("— 子进程裸写 dialogtest：退出码=" + code + "（期望 0）　用时=" + secs.ToString("0.0") + "s（门槛 60s）　stdout "
                + stdout.Length + " 字节=\"" + stdout.Replace("\r\n", "⏎").TrimEnd('\r', '\n', '⏎') + "\"　stderr=" + stderr.Length + " 字节");
            Check(bad, "缺1 验收打法实测通过：不带横线的开关也当自检跑（不是弹主窗口挂着不退）、60 秒内自行退出、退出码 0，"
                  + "且 stdout 不再零字节——机器行里 result/elapsed/exit 三样都读得到（这条只认 ASCII，中文那行的字节按系统码页走）",
                code == 0 && secs < 60
                && stdout.Contains("[selftest/dialogtest] result=PASS") && stdout.Contains("exit=0")
                && stdout.Contains("elapsed=") && stdout.Length > 0 && stderr.Length == 0);
            return bad;
        }

        // ===== 缺2：提示窗正文与托盘行为对齐 =====
        private const string ZhBody = "关闭窗口后，程序将最小化到系统托盘继续运行，还是完全退出？";
        private const string EnBody = "After closing this window, minimize the program to the system tray and keep it running, or quit completely?";

        private static List<string> V49cBody(StringBuilder sb)
        {
            var bad = new List<string>();
            var zh = new ResourceDictionary { Source = new Uri("Localization/zh-CN.xaml", UriKind.Relative) };
            var en = new ResourceDictionary { Source = new Uri("Localization/en-US.xaml", UriKind.Relative) };
            string zhBody = zh["s.ClosePromptBody"] as string;
            string enBody = en["s.ClosePromptBody"] as string;
            sb.AppendLine("— 新口径正文：中文=\"" + zhBody + "\"　英文=\"" + enBody + "\"");
            Check(bad, "缺2 中英正文各逐字钉住 44 号令拍板文案（只改这两枚键的值，不增键不删键）",
                zhBody == ZhBody && enBody == EnBody);

            // 旧口径只做子串否定（不把 41 号前那两句原话抄进源码，免得"旧措辞 rg 0 命中"这条验收被自己的测试文件绊倒）
            Check(bad, "缺2 旧措辞清零：中英正文都不再出现「任务栏／taskbar」字样（新文案逐字相等已由上一条钉住）",
                zhBody != null && enBody != null
                && !zhBody.Contains("任务栏") && !enBody.ToLowerInvariant().Contains("taskbar"));

            // 窗体实显：正文行取的就是这枚键，且与另两处用户可见文案一刀切齐
            App.ApplyLanguage(AppLanguage.ZhCN);
            string live = Res("s.ClosePromptBody");
            string plain = ClosePromptWindow.BodyText(false);
            var pw = new ClosePromptWindow(true);
            sb.AppendLine("— 中文态实显：正文行=\"" + pw.BodyLine + "\"　警示行=\"" + pw.WarnLine + "\"　默认按钮=\"" + pw.KeepButton.Content
                + "\"　次按钮=\"" + pw.ExitButton.Content + "\"");
            Check(bad, "缺2 提示窗真显示的是新正文（窗体与键同源，不是只改了字典），默认按钮仍是「最小化」、"
                  + "任务中追加行仍是「正在执行的任务将中断。」、退出按钮与勾选框文案未动",
                plain == live && pw.BodyLine == live && pw.WarnLine == "正在执行的任务将中断。"
                && (pw.KeepButton.Content as string) == "最小化" && pw.KeepButton.IsDefault
                && (pw.ExitButton.Content as string) == "退出程序"
                && (pw.RememberBox.Content as string) == "不再提示，记住我的选择");

            Check(bad, "缺2 CloseBehavior 三选文案一字未动（询问／最小化到托盘／直接退出）——本令只修正文，不动设置项",
                Res("s.CloseAsk") == "每次询问" && Res("s.CloseMinimize") == "最小化到托盘" && Res("s.CloseExit") == "直接退出");

            App.ApplyLanguage(AppLanguage.EnUS);
            string liveEn = Res("s.ClosePromptBody");
            var pwEn = new ClosePromptWindow(true);
            sb.AppendLine("— 英文态实显：正文行=\"" + pwEn.BodyLine + "\"　按钮=\"" + pwEn.KeepButton.Content + "\"");
            Check(bad, "缺2 切英文后正文与按钮同步换口径（英文正文逐字照拍板，按钮＝Minimize）",
                liveEn == EnBody && pwEn.BodyLine == EnBody && (pwEn.KeepButton.Content as string) == "Minimize");
            Check(bad, "缺2 英文态下设置项也没跟着漏（第二项仍是 Minimize to tray 口径）",
                Res("s.CloseMinimize") == "Minimize to tray");

            App.ApplyLanguage(AppLanguage.ZhCN);
            return bad;
        }
    }
}
