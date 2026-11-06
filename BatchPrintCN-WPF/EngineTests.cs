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
    // V4.2.3 M4 拆分：引擎与出纸链路断言（自检主流程、虚拟打印机回归、样本 PDF 构造）。

        // ===== 44 号·缺1：分段耗时表、看门狗与自检收尾（先有数据，再谈"跑多久/为什么不退出"） =====
        private static readonly System.Diagnostics.Stopwatch _gateWatch = new System.Diagnostics.Stopwatch();
        private static readonly List<string> _phaseMs = new List<string>();
        private static long _lastMark;
        private static volatile string _lastSegment;
        private static string _gateMode = "gate";

        /// <summary>
        /// 44 号·缺1 拍板口径：门禁自己带看门狗——跑过硬上限就带着"最后跑到哪一段"退出（退出码 8），
        /// 让"挂住不退"这一类缺陷在命令行直跑时自己现形，不必靠人盯到 100 秒才发现。
        /// 上限取 15 分钟＝本机实测（--selftest ~63 秒、--themetest ~61 秒）的十倍余量：防误杀，也不防到慢十倍以外的机器。
        /// </summary>
        internal const int GateWatchdogSeconds = 900;
        internal const int GateExitTimeout = 8;
        private static System.Threading.Timer _watchdog;
        private static int _gateDone;

        /// <summary>每个门禁入口先复位计时并上看门狗，一轮里各阶段的耗时才有可比口径。</summary>
        private static void BeginGate(string mode)
        {
            _gateMode = mode;
            _lastSegment = null;
            _phaseMs.Clear();
            _gateWatch.Restart();
            _lastMark = 0;
            System.Threading.Interlocked.Exchange(ref _gateDone, 0);
            DisposeWatchdog();
            _watchdog = new System.Threading.Timer(_ =>
            {
                if (System.Threading.Interlocked.CompareExchange(ref _gateDone, 1, 0) != 0) return;
                WriteConsole("[selftest/" + _gateMode + "] result=TIMEOUT elapsed=" + Sec(_gateWatch.ElapsedMilliseconds)
                    + " exit=" + GateExitTimeout + " limit=" + GateWatchdogSeconds + "s");
                WriteConsole("  最后完成段：" + (_lastSegment ?? "（一段都没登记完）"));
                WriteConsole("  " + GateTimings());
                Environment.Exit(GateExitTimeout);
            }, null, GateWatchdogSeconds * 1000L, System.Threading.Timeout.Infinite);
        }

        private static void DisposeWatchdog()
        {
            try { var t = _watchdog; _watchdog = null; if (t != null) t.Dispose(); } catch { }
        }

        private static string Sec(long ms) { return (ms / 1000.0).ToString("0.0") + "s"; }

        /// <summary>给主流程里不便包成 lambda 的连续语句段打时间点（记的是"距上一个已登记的段"）。</summary>
        private static void Mark(string name)
        {
            Record(name, _gateWatch.ElapsedMilliseconds - _lastMark);
        }

        /// <summary>给一个返回结论文本的阶段计时：只包一层，断言本体一字不改。</summary>
        private static string Phase(string name, Func<string> body)
        {
            long t0 = _gateWatch.ElapsedMilliseconds;
            string r = body();
            Record(name, _gateWatch.ElapsedMilliseconds - t0);
            return r;
        }

        /// <summary>给一个返回退出码的阶段计时（渲染类阶段用）。</summary>
        private static int PhaseCode(string name, Func<int> body)
        {
            long t0 = _gateWatch.ElapsedMilliseconds;
            int r = body();
            Record(name, _gateWatch.ElapsedMilliseconds - t0);
            return r;
        }

        /// <summary>
        /// Phase 与 Mark 共用一个游标：Mark 记的是"距上一个已登记的段"，两种记法混着排也不会把同一秒算两遍。
        /// _lastSegment 单独留一份引用给看门狗读（计时线程只读这一个字段，不去遍历正在追加的 List）。
        /// </summary>
        private static void Record(string name, long ms)
        {
            string line = name + "＝" + Sec(ms);
            _phaseMs.Add(line);
            _lastSegment = line;
            _lastMark = _gateWatch.ElapsedMilliseconds;
        }

        /// <summary>耗时表：按秒数从大到小排，最长的排最前——要压时间只看头部。</summary>
        internal static string GateTimings()
        {
            var sorted = _phaseMs.OrderByDescending(s =>
            {
                int i = s.LastIndexOf('＝');
                double v;
                return i >= 0 && double.TryParse(s.Substring(i + 1).TrimEnd('s'), out v) ? v : -1;
            }).Take(12).ToArray();
            return "阶段耗时 Top" + sorted.Length + "（本轮门禁合计 " + Sec(_gateWatch.ElapsedMilliseconds) + "，共 " + _phaseMs.Count + " 段）："
                + string.Join("　", sorted);
        }

        /// <summary>全量耗时表（按执行顺序），写进自检报告备查。</summary>
        private static string GateTimingsFull()
        {
            return "合计 " + Sec(_gateWatch.ElapsedMilliseconds) + "（" + _phaseMs.Count + " 段，执行顺序）：" + string.Join("　", _phaseMs.ToArray());
        }

        /// <summary>
        /// 44 号·缺1 要求①③：四项自检跑完检查后的**唯一**收尾出口。
        /// 三件事按顺序做：释放托盘图标（NotifyIcon 自带一个隐藏消息窗口，不 Dispose 就把死图标留在托盘上）、
        /// 把结论打到标准输出（GUI 子系统程序平时没有控制台，重定向时这几行就是命令行直跑能看见的结果）、
        /// 再 Environment.Exit 交退出码——不再依赖 WPF 自己什么时候把消息循环拆干净。
        /// </summary>
        internal static void FinishGate(string mode, int code)
        {
            // 先看门狗有没有已经交过码（超时退出）；抢不到就说明这一轮已被判挂，不再重复交码
            if (System.Threading.Interlocked.CompareExchange(ref _gateDone, 1, 0) != 0) return;
            DisposeWatchdog();
            try { TrayIcon.Dispose(); } catch { }      // 缺1：交退出码之前先把图标和它那个隐藏消息窗口释放掉
            WriteConsole(GateAsciiLine(mode, code));   // 先给自动化读的一行（纯 ASCII，不受码页影响）
            WriteConsole(GateResultLine(mode, code));  // 再给人读的一行
            WriteConsole("  " + GateTimings());
            Environment.Exit(code);
        }

        /// <summary>
        /// 机器读的一行：只用 ASCII——GUI 子系统程序把 stdout 重定向到管道时，中文那行会按系统 OEM 码页（本机 936）落盘，
        /// 父进程或脚本按 UTF-8 读就成了花脸；所以"结果/用时/退出码"再给一份 ASCII，脚本抓这行就够。
        /// </summary>
        internal static string GateAsciiLine(string mode, int code)
        {
            return "[selftest/" + mode + "] result=" + (code == 0 ? "PASS" : "FAIL")
                + " elapsed=" + Sec(_gateWatch.ElapsedMilliseconds) + " exit=" + code
                + " segments=" + _phaseMs.Count
                + " build=" + BuildStamp();
        }

        /// <summary>
        /// 59 号·产物指纹：编译期就烘进映像的 InformationalVersion 提交号前 8 位（dotnet 发布自带，非运行时读取）。
        /// 为什么放这里——并发构建竞态下，一个更早启动、仍持旧映像的自检进程可能在新构建之后才写出报告，
        /// 光看文件时间戳会误判"产物没跟着源码重建"；报告行里带 build 就能一眼对上是哪一份代码跑出来的。
        /// 读不到（无 PDB/SourceLink 信息）时回 "?"，绝不抛。
        /// </summary>
        internal static string BuildStamp()
        {
            try
            {
                // 不用扩展方法（本文件没 using System.Reflection），走非扩展的 GetCustomAttributes(Type)
                var attrs = typeof(WpfTests).Assembly.GetCustomAttributes(
                    typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                var info = attrs.Length > 0 ? attrs[0] as System.Reflection.AssemblyInformationalVersionAttribute : null;
                string iv = info == null ? null : info.InformationalVersion;
                if (string.IsNullOrEmpty(iv)) return "?";
                int plus = iv.IndexOf('+');
                if (plus < 0) return iv.Length > 8 ? iv.Substring(0, 8) : iv;
                string sha = iv.Substring(plus + 1);
                if (sha.Length == 0) return "?";
                return sha.Length > 8 ? sha.Substring(0, 8) : sha;
            }
            catch { return "?"; }
        }

        /// <summary>结论行（抽出来好断言）：命令行直跑时要一眼看到"跑完了没/多久/退出码多少"三样。</summary>
        internal static string GateResultLine(string mode, int code)
        {
            return "[自检/" + mode + "] " + (code == 0 ? "结果=通过" : "结果=失败")
                + " 用时=" + Sec(_gateWatch.ElapsedMilliseconds) + " 退出码=" + code;
        }

        private static void WriteConsole(string text)
        {
            // 无控制台时 Console.Out 是 TextWriter.Null（写了等于没写，不抛）；管道被提前关闭会抛 IOException，
            // 收尾打印不该反过来把退出码弄丢，所以整条包起来。
            try
            {
                Console.Out.WriteLine(text);
                Console.Out.Flush();
            }
            catch { }
        }

        public static int EngineSelfTest()
        {
            BeginGate("selftest");
            string logPath = Path.Combine(AppContext.BaseDirectory, "自检报告.txt");
            var sb = new StringBuilder();
            bool ok = true;
            try
            {
                sb.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] WPF PrintPile 自检");
                var ver = typeof(WpfTests).Assembly.GetName().Version;
                sb.AppendLine("程序版本: " + ver);
                sb.AppendLine("pdfium.dll 存在: " + File.Exists(Path.Combine(AppContext.BaseDirectory, "pdfium.dll")));
                string tmp = Path.Combine(Path.GetTempPath(), "PrintPile-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                string pdfPath = Path.Combine(tmp, "测试文档.pdf");
                BuildSamplePdf(pdfPath);
                using (PdfiumFile pdf = PdfiumFile.Open(pdfPath))
                {
                    sb.AppendLine("PDF 页数: " + pdf.PageCount);
                    using (var bmp = pdf.RenderPage(0, 144f)) sb.AppendLine("渲染尺寸: " + bmp.Width + "x" + bmp.Height);
                }
                Mark("样本PDF构造+pdfium读取");
                string[] printers = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
                sb.AppendLine("打印机数量: " + printers.Length);
                foreach (string id in new[] { "Kwps.Application", "Word.Application", "Ket.Application", "Excel.Application", "Kwpp.Application", "PowerPoint.Application" })
                    if (Type.GetTypeFromProgID(id) != null) sb.AppendLine("可用 Office 引擎: " + id);
                Mark("打印机枚举+Office ProgID 探测");

                // v2.0 H2 回归：非连续页码 "1,3-5"（共 8 页）应解析为 1,3,4,5 而不是 1-5
                int[] parsed = BatchPrinter.ParseRange("1,3-5", 8);
                bool rangeOk = parsed.Length == 4 && parsed[0] == 0 && parsed[1] == 2 && parsed[2] == 3 && parsed[3] == 4;
                sb.AppendLine("页码解析 '1,3-5' (共8页) -> " + string.Join(",", parsed.Select(i => (i + 1).ToString()).ToArray()) + (rangeOk ? " 通过" : " 失败"));
                ok = ok && rangeOk;
                // v2.0 H2 回归：连续区间 1-5 应解析为 1..5
                int[] parsed2 = BatchPrinter.ParseRange("1-5", 8);
                bool range2Ok = parsed2.Length == 5;
                sb.AppendLine("页码解析 '1-5' (共8页) -> " + parsed2.Length + " 页" + (range2Ok ? " 通过" : " 失败"));
                ok = ok && range2Ok;

                // v2.0 M2 回归：日志目录可写
                FileLogger.Write("自检日志写入测试");
                bool logOk = Directory.Exists(FileLogger.LogDirectory);
                sb.AppendLine("日志目录可写: " + FileLogger.LogDirectory + " -> " + (logOk ? "通过" : "失败"));
                ok = ok && logOk;

                // V3.0 回归：转换子进程（Worker）存在性与参数校验冒烟
                // 64 号：单文件双包布局——发布态 Worker 在 bin\ 子目录；开发态平铺兼容
                string workerFlat = Path.Combine(AppContext.BaseDirectory, "PrintPileWorker.exe");
                string workerBin = Path.Combine(AppContext.BaseDirectory, "bin", "PrintPileWorker.exe");
                string workerPath = File.Exists(workerFlat) ? workerFlat : workerBin;
                bool workerExists = File.Exists(workerPath);
                sb.AppendLine("转换子进程 PrintPileWorker.exe 存在: " + workerExists + "（实际取 " + (workerExists ? workerPath : "两路径皆缺") + "）");
                ok = ok && workerExists;
                if (workerExists)
                {
                    int wcode = 99;
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(workerPath) { CreateNoWindow = true, UseShellExecute = false };
                        using (System.Diagnostics.Process wp = System.Diagnostics.Process.Start(psi))
                        {
                            if (wp != null && wp.WaitForExit(15000)) wcode = wp.ExitCode;
                        }
                    }
                    catch { }
                    sb.AppendLine("Worker 无参冒烟退出码（期望 2=参数不足）: " + wcode + (wcode == 2 ? " 通过" : " 失败"));
                    ok = ok && wcode == 2;
                }
                Mark("页码解析/日志/转换子进程冒烟(V3.0)");

                // V3.1 虚拟打印机自动化回归：以 Microsoft Print to PDF 为回归打印机，输出 PDF 自动比对页数/纸张
                string vp = Phase("虚拟打印机回归(V3.1)", () => VirtualPrintRegression());
                sb.AppendLine("虚拟打印机回归: " + vp);
                ok = ok && !vp.StartsWith("失败");

                // F1 引擎自动检测冒烟：探测结果应可读（有无 COM 均正常）
                EngineDetector.Detect();
                sb.AppendLine("F1 引擎探测: " + EngineDetector.Describe() + "（" + string.Join("/", EngineDetector.DetectedIds()) + "）");

                // F13 勾选断言：新行默认勾选
                bool f13Ok = new RowVm(new PrintJob { FilePath = pdfPath, Kind = DocKind.Pdf }).Checked;
                sb.AppendLine("F13 新文件默认勾选: " + (f13Ok ? "通过" : "失败"));
                ok = ok && f13Ok;

                // F2 SQLite 建库冒烟：EnsureDb 后库文件存在且可查询（不写测试记录，避免污染真实历史）
                bool f2Ok = false;
                try
                {
                    SqliteStore.EnsureDb();
                    string dbFile = Path.Combine(AppPaths.DataDir, "history.db");   // V4.1 便携目录
                    f2Ok = File.Exists(dbFile) && SqliteStore.QueryHistory().Count >= 0;
                }
                catch { }
                sb.AppendLine("F2 SQLite 建库: " + (f2Ok ? "通过" : "失败"));
                ok = ok && f2Ok;

                // V4.1 便携目录断言：日志/数据目录生成在程序目录下
                bool portOk = AppPaths.Root == AppContext.BaseDirectory
                    && Directory.Exists(AppPaths.LogsDir)
                    && Directory.Exists(AppPaths.DataDir)
                    && Directory.Exists(AppPaths.TmpDir);
                sb.AppendLine("V4.1 便携目录: " + (portOk ? "通过" : "失败") + "（" + AppPaths.Describe() + "）");
                ok = ok && portOk;

                // V4.1 真实 SQLite 读写冒烟（临时库，不污染正式库）
                string sqliteTest = Phase("SQLite读写冒烟(V4.1)", () => SqliteStore.SelfTest(Path.Combine(Path.GetTempPath(), "BatchPrintCNSt-" + Guid.NewGuid().ToString("N"))));
                sb.AppendLine("V4.1 SQLite 读写冒烟: " + sqliteTest);
                ok = ok && sqliteTest.StartsWith("通过");

                // V4.1 历史页交互冒烟：创建→显隐切换→变更筛选下拉→不死循环正常返回
                string histTest = Phase("历史页交互冒烟(V4.1)", () => HistoryPageSmoke());
                sb.AppendLine("V4.1 历史页交互冒烟: " + histTest);
                ok = ok && histTest.StartsWith("通过");

                // V4.2 断言①：正式库 folder 列存在 + 旧库幂等迁移冒烟（临时库，不污染正式库）
                string folderCheck = SqliteStore.FolderColumnCheck();
                sb.AppendLine("V4.2 folder 列检查: " + folderCheck);
                ok = ok && folderCheck.StartsWith("通过");
                string folderMig = Phase("folder迁移冒烟(V4.2)", () => SqliteStore.SelfTestV42(Path.Combine(Path.GetTempPath(), "BatchPrintCNV42-" + Guid.NewGuid().ToString("N"))));
                sb.AppendLine("V4.2 folder 迁移冒烟: " + folderMig);
                ok = ok && folderMig.StartsWith("通过");

                // V4.2 断言②：搜索过滤冒烟（3 行数据，关键字命中 1 行；文件夹列参与匹配；空关键字全命中）
                bool searchOk = TestSearchFilter();
                sb.AppendLine("V4.2 搜索过滤冒烟: " + (searchOk ? "通过（3 行命中 1 行）" : "失败"));
                ok = ok && searchOk;

                // V4.2 断言③：设置页不再含「历史显示」控件（HistoryLimitBox），历史页工具栏含有（LimitBox）
                bool limitUiOk = false;
                try
                {
                    var stPage = new SettingsPage();
                    var hiPage = new HistoryPage();
                    limitUiOk = stPage.FindName("HistoryLimitBox") == null && hiPage.FindName("LimitBox") != null;
                }
                catch { }
                sb.AppendLine("V4.2 档位归位: " + (limitUiOk ? "通过（设置页无 HistoryLimitBox / 历史页有 LimitBox）" : "失败"));
                ok = ok && limitUiOk;

                // ===== V4.2 版本内修整（三批合并）自检 =====
                // 断言①：主页工具栏不再有模板控件，打印设置弹窗含模板三控件
                string tplMove = Phase("模板控件归属(V4.2修整二)", () => TemplateControlsMoved());
                sb.AppendLine("V4.2 修整二·模板控件归属: " + tplMove);
                ok = ok && tplMove.StartsWith("通过");

                // 断言③：模板套用冒烟（存→读→套到弹窗控件→控件值一致→删）
                string tplApply = Phase("模板套用冒烟(V4.2修整二)", () => TemplateApplySmoke());
                sb.AppendLine("V4.2 修整二·模板套用冒烟: " + tplApply);
                ok = ok && tplApply.StartsWith("通过");

                // 断言④：任务规划窗含设置摘要行/模板行/「修改设置」按钮
                string planSmoke = Phase("规划窗冒烟(V4.2修整三)", () => TaskPlanSmoke());
                sb.AppendLine("V4.2 修整三·规划窗冒烟: " + planSmoke);
                ok = ok && planSmoke.StartsWith("通过");

                // 修整一：历史页工具栏高度统一 + 按钮文字居中不折行 + 档位项不折行
                string toolbar = Phase("历史页工具栏(V4.2修整一)", () => HistoryToolbarSmoke());
                sb.AppendLine("V4.2 修整一·历史页工具栏: " + toolbar);
                ok = ok && toolbar.StartsWith("通过");

                // V4.2.5 U2 换骨断言：外壳三件套 + 弹窗内容面板 + NumberBox
                string u2 = Phase("U2换骨冒烟(V4.2.5)", () => U2ShellSmoke());
                sb.AppendLine("V4.2.5 U2 换骨冒烟: " + u2);
                ok = ok && u2.StartsWith("通过");

                // V4.3 F6 断言①③②：奇偶页页序 / 小册子骑马订 / 9/16 版输出页数 + 默认回归
                string f6 = Phase("F6页序计划(V4.3)", () => F6PlanSmoke());
                sb.AppendLine("V4.3 F6 页序计划: " + f6);
                ok = ok && f6.StartsWith("通过");

                // V4.3 F6 断言①②③④端到端：虚拟打印机取证（奇偶/版数/小册子/纸盒回退）
                string v43 = Phase("F6虚拟打印取证(V4.3)", () => V43PrintSmoke());
                sb.AppendLine("V4.3 F6 虚拟打印取证: " + v43);
                ok = ok && v43.StartsWith("通过");

                // V4.3 F15/F16 断言⑦⑧：主页无打印机设置按钮/设置页分组/优先级灰字/列头漏斗/引擎列隐藏/排序提示
                string f1516 = Phase("F15/F16界面断言(V4.3)", () => F15F16Smoke());
                sb.AppendLine("V4.3 F15/F16 界面断言: " + f1516);
                ok = ok && f1516.StartsWith("通过");

                // ===== V4.4 F5 逐文件参数卡片（开工令 ①②③④⑦）=====
                string v44r = Phase("F5①解析矩阵(V4.4)", () => V44ResolveMatrix());
                sb.AppendLine("V4.4 F5 ① 生效参数解析矩阵: " + v44r);
                ok = ok && v44r.StartsWith("通过");

                string v44p = Phase("F5②双纸虚拟打印(V4.4)", () => V44TwoFilePaper());
                sb.AppendLine("V4.4 F5 ② 双文件不同纸张虚拟打印: " + v44p);
                ok = ok && v44p.StartsWith("通过");

                string v44g = Phase("F5③规划分组(V4.4)", () => V44PlanGrouping());
                sb.AppendLine("V4.4 F5 ③ 任务规划按生效参数分组: " + v44g);
                ok = ok && v44g.StartsWith("通过");

                string v44c = Phase("F5④⑦参数弹窗(V4.4)", () => V44RowCardUi());
                sb.AppendLine("V4.4 F5 ④⑦ 逐文件参数弹窗与置灰联动: " + v44c);
                ok = ok && v44c.StartsWith("通过");

                // ===== V4.4 二批（2026-09-20 真机二批反馈）：修整9~15 =====
                string v44f2 = Phase("修整9~15(V4.4二批)", () => V44Fix2Ui());
                sb.AppendLine("V4.4 二批 修整9~15（全选一次点到位/截断悬浮全名/删大小列/输入框铺满/缩放自适应/边框连贯/列头居中加粗）: " + v44f2);
                ok = ok && v44f2.StartsWith("通过");
                string v44f3 = Phase("深色缺陷A/B(V4.4三批)", () => V44Fix3Ui());
                sb.AppendLine("V4.4 三批 深色缺陷A/B（数据行底色随主题·浅色零变化/两页样式对象一致/浅底按钮固定字色+对比度/实底按钮白字）: " + v44f3);
                ok = ok && v44f3.StartsWith("通过");
                string v44f4 = Phase("修整16/17(V4.4四批)", () => V44Fix4Ui());
                sb.AppendLine("V4.4 四批 修整16/17（状态列收窄·1120 参数按钮不滚/三态颜色只看结构化种类·浅深中英对比度≥4.5/份数输入框水平+垂直铺满）: " + v44f4);
                ok = ok && v44f4.StartsWith("通过");
                string v44f5 = Phase("修整18~23(V4.4五批)", () => V44Fix5Ui());
                sb.AppendLine("V4.4 五批 修整18~23（长列表补一拍不滚/打印中禁用清单全量+异常恢复/进度第X·N与剩余预估/历史页最小窗+气泡/目录行初始显示/日志去重）: " + v44f5);
                ok = ok && v44f5.StartsWith("通过");

                // ===== V4.5（2026-09-25 需求方拍板五项）=====
                string v45 = Phase("V4.5四项", () => V45Ui());
                sb.AppendLine("V4.5 四项（按钮字色按主题分治/历史页状态列汉化/打印中锁交互不锁滚动/全选清除行上√即时消失；F7 监视文件夹已移出）: " + v45);
                ok = ok && v45.StartsWith("通过");

                // ===== V4.7 拆分自检 =====
                string v47 = Phase("V4.7拆分+V4.8", () => V47Ui());
                sb.AppendLine("V4.7 拆分（监视已移除/模板限 5/历史限 200/PolyForm 随包/措辞中性化）+ 版本内修整（设置页按钮右对齐/关于页检查更新）+ V4.8（后端地址写死生产常量、不可被 settings.json 改）+ 48 号（检查更新基址切 Pages 域、旧 Workers 域仅进下载白名单；失败文案分三类中文化，异常原文只进日志不上屏）: " + v47);
                ok = ok && v47.StartsWith("通过");
                string v49 = Phase("V4.9改进优化版", () => V49Ui());   // V4.9（V4.8 功能改进与优化版）：关闭行为三选 + 问题反馈 + 打印机属性加固
                sb.AppendLine("V4.9 改进优化版（关闭按钮行为三选/问题反馈/打印机属性 ArgumentList 加固；不含任何 PDF 功能）: " + v49);
                ok = ok && v49.StartsWith("通过");
                string v49r = Phase("V4.9范围反馈(40号)", () => V49RangeUi());   // V4.9 版本内修整（40 号）：打印范围反馈优化，固定行高，不动打印引擎
                sb.AppendLine("V4.9 版本内修整·打印范围反馈（修1 全角归一化/修2 页数列三态联动/修3 聚焦气泡/修4 无效标红/修5 悬停提示/修6 状态列范围无效；行高锁死、打印行为零变化）: " + v49r);
                ok = ok && v49r.StartsWith("通过");
                string v49b = Phase("V4.9第二批(41号)", () => V49bUi());   // V4.9 版本内修整第二批（41 号）：托盘 + 单实例 + 反馈跳官网 + 设置页/关于页精简
                sb.AppendLine("V4.9 版本内修整第二批（修A 最小化到系统托盘/修B 单实例守卫/修C 反馈入口改跳官网/修D 设置页精简四块/修E 关于页精简标题块；打印引擎与 40 号成果零改动）: " + v49b);
                ok = ok && v49b.StartsWith("通过");
                string v49c = Phase("V4.9缺陷返修(44号)", () => V49cUi());   // 44 号：缺1 自检命令行直跑自行退出 + 缺2 提示窗正文改口为托盘
                sb.AppendLine("V4.9 缺陷返修（缺1 自检入口归一化+收尾释放托盘并交退出码+结论上 stdout+真子进程复现验收打法/缺2 关闭提示窗正文与托盘行为对齐；托盘等用户可见行为零改动）: " + v49c);
                ok = ok && v49c.StartsWith("通过");
                string v492 = Phase("V4.9.2三项(45号)", () => V492Ui());   // 45 号：改1 超界检测（两口径）+ 改2 作业摘要日志 + 改3 打印预览
                sb.AppendLine("V4.9.2 三项（改1 转换后超界检测与状态列黄警告/改2 逐文件作业摘要日志/改3 双击行打印预览；打印行为与 40、41、44 号成果零改动）: " + v492);
                ok = ok && v492.StartsWith("通过");
                string v492c = Phase("V4.9.2预览交互(46+47号)", () => V492cUi());   // 46 号：预览 Ctrl+滚轮缩放（锚点）；47 号修订：滚轮改页内滚动 + 缩放／翻页按钮 + PgUp/PgDn
                sb.AppendLine("V4.9.2 预览交互增强（增1 以指针为锚点的 Ctrl+滚轮缩放 25~400% 档位、按目标 DPI 重渲染、Ctrl+0/Ctrl+1 恢复口；"
                    + "47 号修订：非 Ctrl 滚轮不再翻页（滚动权交还 ScrollViewer＝页内连续滚动）、信息条补「−」「＋」缩放按钮、翻页改「上一页／第 x / N 页／下一页」按钮＋PgUp/PgDn 统一走 GoToPage（边界 no-op＋按钮置灰、缩放保留、缩略图同步）；"
                    + "45 号三项与打印行为零改动）: " + v492c);
                ok = ok && v492c.StartsWith("通过");
                string v493 = Phase("V4.9.3关于页(49号)", () => V493Ui());   // 49 号：关于页改版（三按钮行）+ 自动检查更新 + 更新渠道 + 立即检查内联
                sb.AppendLine("V4.9.3 关于页改版（版1 标题区与三枚按钮（官网/数据目录/日志，「日志位置」expander 由按钮承接）；"
                    + "版2 自动检查更新（默认开、24 小时一次、失败静默只写日志、发现新版出托盘气泡＋关于页内联同步、四项自检模式不起定时器）；"
                    + "版3 更新渠道 stable/preview 预发布解析矩阵；版4 立即检查内联六态（中英各一遍）＋白名单拒绝分支＋检查中防连点；"
                    + "48 号 UpdateChecker 判定/分类/白名单/文案原样复用，打印管线与 40~48 号成果零改动零回退）: " + v493);
                ok = ok && v493.StartsWith("通过");
                string v100 = Phase("52号主题引擎", () => V100Ui());   // 73 号令起本段只剩 52 号主题引擎（验签/机器码/门控/许可区断言随功能删除）
                sb.AppendLine("52 号 主题引擎（12 令牌·内置主题·.bptheme 导入·设置页色卡·深浅基座联动）: " + v100);
                ok = ok && v100.StartsWith("通过");
                string v66 = Phase("66号更新按钮", () => V66Ui());   // 66 号：左下角橙色更新按钮＋弹窗三段（确认→下载→安装），全程离线注入
                sb.AppendLine("66 号 更新体验（左下角橙色按钮→弹窗确认→下载到 %TEMP%→安装版交棒 Setup／便携版解压提示；白名单与取消语义）: " + v66);
                ok = ok && v66.StartsWith("通过");
                string v68 = Phase("68号模板与更新卡", () => V68Ui());   // 73 号令起本段只剩内置默认模板与检查更新卡布局两组
                sb.AppendLine("68 号 剩余两项（四枚内置默认模板＋「恢复默认模板」入口／检查更新 found 态三档窗宽×中英不竖排；限时体验弹窗与机器码两组断言随 73 号令功能删除一并退场）: " + v68);
                ok = ok && v68.StartsWith("通过");
                // 73 号令：70 号那一段（导出 CSV 置灰＋限时体验起点双写）随功能一并删除——不再有闸，也没有限时体验。
                string v110 = Phase("72号监视文件夹", () => V110Ui());   // 72 号：F7 规则模型/持久化/稳定性判定/时段矩阵/目录保护/去重/入队接缝
                sb.AppendLine("72 号 监视文件夹 F7（规则 CRUD 与 watch-rules.json 往返、稳定性判定（半写入不入队／写完入队）、时段过滤纯函数矩阵（含跨零点）、"
                    + "目录保护五档、同文件去重、模板→逐文件覆盖（生效参数与全局无关）、监视在任意状态下工作（73 号令取消门控，原三态就地改判）、版本随主线（80 号起 1.2.1.0）；"
                    + "打印执行链路零改动、settings.json 键账零新增）: " + v110);
                ok = ok && v110.StartsWith("通过");
                string v120 = Phase("73号开源改造", () => V120Ui());   // 73 号：类型不在场／遗留键容忍／全功能可用矩阵／关于页重排／字样清查／版本随主线
                sb.AppendLine("73 号 free & open 改造（D-36 推翻 D-24）：码制／验签／限时体验／功能上限整体拆除——"
                    + "①解锁体系类型在两程序集里全部不在场 ②老 settings.json 的遗留键读取忽略、回写不再带、其余键零丢失 "
                    + "③导出 CSV 常亮＋历史五档含无限制且不截断＋监视随时可用 ④关于页解锁区与功能一览整块消失、只留 GNU GPLv3 声明＋仓库双按钮（78 号改判）"
                    + "⑤中英本文案被禁六词 0 命中 ⑥FileVersion 三处一致（80 号改判随主线 1.2.1.0）: " + v120);
                ok = ok && v120.StartsWith("通过");
                string v121 = Phase("80号开机自启动", () => V121Ui());   // 80 号：拼路径纯函数／真实 HKCU 往返／幂等／老配置缺键读作关／设置页开关／版本 1.2.1.0
                sb.AppendLine("80 号 开机自启动（V1.2.1）：设置页「关闭行为」卡底部第二行开关（默认关，中英各加 2 枚键）——"
                    + "①BuildRunKeyValue 带引号全路径三例 ②真实 HKCU 开→读到→关→读不到（本机原值测毕还原，不抹别人已开的自启动）"
                    + "③连开两次值不变·连关两次不抛 ④老配置缺该键读作 false 且回写带上·settings DTO 11→12 枚 "
                    + "⑤设置页开关实取在位·默认关·翻动真的写删注册表 ⑥FileVersion=1.2.1.0: " + v121);
                ok = ok && v121.StartsWith("通过");

                // 断言⑤：历史页交互冒烟（V4.1 起防重入闪退，本轮修整后复跑上方 histTest 结果）
                sb.AppendLine("V4.2 修整·历史页交互冒烟: " + histTest);
                ok = ok && histTest.StartsWith("通过");

                // ===== V4.2.3 M6 守护断言（41 号·修A 改判，见下） =====
                // ② 类库化产物在位（原样）。
                // ① 编译期依赖：M6 当年立的是"主程序不引用 WinForms 程序集"，而它自己的注释就写明
                //    "M2 实测**不省体积**——System.Drawing.Common 仍带 Windows 桌面框架包，发布目录里 System.Windows.Forms*.dll 依旧存在"。
                //    V4.9 版本内修整第二批·修A 的托盘图标按需求方拍板用 WinForms 的 NotifyIcon（.NET 自带、零第三方依赖），
                //    引用因此出现——体积代价实测为零（发布目录里这批文件启用前后一字不差）。
                //    故本条**改判为守护真正关心的那件事**：引用允许有，但发布目录不得因引 WinForms 多出任何一枚程序集
                //    （基线名单＝修A 之前该目录里已有的 WinForms/VisualBasic 系文件；多出来即失败）。已报备。
                var refs = typeof(WpfTests).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
                bool formsReferenced = refs.Contains("System.Windows.Forms");
                var formsBaseline = new[] { "Microsoft.VisualBasic.Core.dll", "Microsoft.VisualBasic.Forms.dll", "Microsoft.VisualBasic.dll",
                    "System.Windows.Forms.Design.Editors.dll", "System.Windows.Forms.Design.dll", "System.Windows.Forms.Primitives.dll",
                    "System.Windows.Forms.dll", "WindowsFormsIntegration.dll" };
                var formsFound = new List<string>();
                try
                {
                    formsFound = Directory.GetFiles(AppContext.BaseDirectory, "*.dll").Select(Path.GetFileName)
                        .Where(n => n.StartsWith("System.Windows.Forms", StringComparison.OrdinalIgnoreCase)
                                 || n.StartsWith("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase)
                                 || n.Equals("WindowsFormsIntegration.dll", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                }
                catch { }
                var formsExtra = formsFound.Except(formsBaseline, StringComparer.OrdinalIgnoreCase).ToList();
                var formsMissing = formsBaseline.Except(formsFound, StringComparer.OrdinalIgnoreCase).ToList();
                // 64 号·B 就地改判（条目不删）：单文件形态把所有程序集内嵌进 exe，"看目录里有没有 dll"这条判据不再成立——
                // 平铺形态仍看文件在位，单文件形态改看 Core 程序集是否真的已加载（能加载＝随包且可用，比看文件名更硬）。
                bool coreDll = File.Exists(Path.Combine(AppContext.BaseDirectory, "BatchPrintCN.Core.dll"))
                    || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "BatchPrintCN.Core");
                // 同上：单文件形态目录里 0 枚 WinForms 系属预期；平铺形态仍须"恰好基线、不多不少"。
                bool formsShapeOk = formsFound.Count == 0 || formsMissing.Count == 0;
                sb.AppendLine("V4.2.3→41 号·修A WinForms 口径（64 号·B：单文件＝目录 0 枚，平铺＝恰好基线 8 枚）: " + (formsReferenced && formsExtra.Count == 0 && formsShapeOk ? "通过" : "失败")
                    + "（主程序引用 System.Windows.Forms＝" + formsReferenced + "，托盘 NotifyIcon 所需；"
                    + "发布目录 WinForms/VisualBasic 系程序集 " + formsFound.Count + " 枚，相对修A 之前基线"
                    + "多出 " + formsExtra.Count + " 枚" + (formsExtra.Count == 0 ? "" : "：" + string.Join(",", formsExtra))
                    + "、少了 " + formsMissing.Count + " 枚" + (formsMissing.Count == 0 ? "" : "：" + string.Join(",", formsMissing))
                    + "；基线名单=" + string.Join(",", formsBaseline) + "）");
                sb.AppendLine("V4.2.3 引擎类库 BatchPrintCN.Core: " + (coreDll ? "通过（64 号·B 改判：平铺看目录 dll 在位／单文件看 Core 程序集已加载）" : "失败：既不在发布目录，也未随单文件包加载"));
                ok = ok && formsReferenced && formsExtra.Count == 0 && formsShapeOk && coreDll;
                Mark("M6 守护(读引用+扫发布目录)");

                // ② 版本单一来源：Directory.Build.props 出的程序集版本 = NOTICE 里的版本
                string noticeVer = NoticeVersion();
                string asmVer = typeof(WpfTests).Assembly.GetName().Version.ToString(3);
                bool verOk = !string.IsNullOrEmpty(noticeVer) && noticeVer == asmVer;
                sb.AppendLine("V4.2.3 版本一致（程序集 " + asmVer + " / NOTICE " + (noticeVer ?? "未找到") + "）: " + (verOk ? "通过" : "失败"));
                ok = ok && verOk;

                // ⑤ 中英资源键一一对应 + 界面零"未解析键"残留
                string res = Phase("资源键守护(V4.2.3)", () => ResourceKeysCheck());
                sb.AppendLine("V4.2.3 资源键守护: " + res);
                ok = ok && res.StartsWith("通过");


                sb.AppendLine("门禁看门狗: 上限 " + GateWatchdogSeconds + " 秒、超时退出码 " + GateExitTimeout
                    + "（挂起会带着「最后完成段」自行现形）；命令行直跑（裸写或带 -- 都认）跑完必自行退出。");
                sb.AppendLine("分段耗时: " + GateTimingsFull());
                sb.AppendLine(ok ? "自检结果: 通过" : "自检结果: 失败");
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                sb.AppendLine("自检失败");
                sb.AppendLine(ex.ToString());
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(true));
                return 1;
            }
        }

        // V4.2.3 M6 守护②：读发布目录里的 NOTICE 版本行（"版本：vX.Y.Z"）
        private static string NoticeVersion()
        {
            try
            {
                // 64 号·B 就地改判（条目不删）：单文件发布把 NOTICE 收拢进 licenses\，两处都认（平铺形态仍在根下）。
                string f = new[] { Path.Combine(AppContext.BaseDirectory, "NOTICE"), Path.Combine(AppContext.BaseDirectory, "licenses", "NOTICE") }
                    .FirstOrDefault(p => File.Exists(p));
                if (f == null) return null;
                foreach (string line in File.ReadAllLines(f, Encoding.UTF8))
                {
                    if (line.IndexOf("版本", StringComparison.Ordinal) < 0) continue;
                    string s = line.Replace("版本：", "").Replace("版本:", "").Replace("v", "").Trim();
                    if (s.Length > 0 && char.IsDigit(s[0])) return s;
                }
            }
            catch { }
            return null;
        }

        // V4.2.3 M6 守护⑤：中英资源键一一对应 + 六个界面渲染后零"未解析键"残留
        private static string ResourceKeysCheck()
        {
            try
            {
                var zh = new ResourceDictionary { Source = new Uri("Localization/zh-CN.xaml", UriKind.Relative) };
                var en = new ResourceDictionary { Source = new Uri("Localization/en-US.xaml", UriKind.Relative) };
                var zk = zh.Keys.Cast<object>().Select(k => k.ToString()).ToList();
                var ek = en.Keys.Cast<object>().Select(k => k.ToString()).ToList();
                var onlyZh = zk.Except(ek).ToList();
                var onlyEn = ek.Except(zk).ToList();
                if (onlyZh.Count > 0 || onlyEn.Count > 0)
                    return "失败：中英键不对应 仅中文[" + string.Join(",", onlyZh.Take(5)) + "] 仅英文[" + string.Join(",", onlyEn.Take(5)) + "]";

                var surfaces = new List<FrameworkElement>
                {
                    new MainWindow(), new SettingsPage(), new HistoryPage(), new AboutPage(),
                    new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF"),
                    new TaskPlanPanel(new List<TaskPlanGroup>(), "1 个文件 → 1 个任务", "双面-长边 / A4", null)
                };
                var bad = new List<string>();
                foreach (FrameworkElement raw in surfaces)
                {
                    FrameworkElement fe = raw is Window ? ((Window)raw).Content as FrameworkElement : raw;
                    if (fe == null) continue;
                    fe.Measure(new Size(1400, 900));
                    fe.Arrange(new Rect(0, 0, 1400, 900));
                    fe.UpdateLayout();
                    ScanUnresolved(fe, bad);
                }
                if (bad.Count > 0) return "失败：界面残留未解析键 " + string.Join(" | ", bad.Distinct().Take(6));
                return "通过（中英 " + zk.Count + " 个键一一对应 / 六个界面 0 处未解析：主页·历史·设置·关于 + 打印设置弹窗·任务规划弹窗；V4.7 已移出「监视文件夹」页）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        private static void ScanUnresolved(DependencyObject node, List<string> bad)
        {
            var tb = node as System.Windows.Controls.TextBlock;
            if (tb != null && !string.IsNullOrEmpty(tb.Text) && tb.Text.StartsWith("s.")) bad.Add(tb.Text);
            var cc = node as System.Windows.Controls.ContentControl;
            if (cc != null && cc.Content is string && ((string)cc.Content).StartsWith("s.")) bad.Add((string)cc.Content);
            int n = 0;
            try { n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); } catch { n = 0; }
            for (int i = 0; i < n; i++) ScanUnresolved(System.Windows.Media.VisualTreeHelper.GetChild(node, i), bad);
        }

        // ===== V4.3 F6：页序计划纯计算断言（①奇偶 ③小册子 ②版数 + 默认回归） =====
        private static string F6PlanSmoke()
        {
            try
            {
                var odd = new PrintOptions { PageParity = PageParityMode.Odd };
                int[] oddSeq = PrintPlan.PageSequence(5, null, null, odd);
                var even = new PrintOptions { PageParity = PageParityMode.Even };
                int[] evenSeq = PrintPlan.PageSequence(5, null, null, even);
                bool parityOk = oddSeq.SequenceEqual(new[] { 0, 2, 4 }) && evenSeq.SequenceEqual(new[] { 1, 3 });
                bool defaultOk = PrintPlan.PageSequence(5, null, null, new PrintOptions()).SequenceEqual(new[] { 0, 1, 2, 3, 4 });
                var b8 = new PrintOptions { Booklet = true };
                int[] bseq = PrintPlan.PageSequence(8, null, null, b8);
                bool bookletOk = bseq.SequenceEqual(new[] { 7, 0, 1, 6, 3, 4, 5, 2 }) && PrintPlan.OutputPageCount(8, b8) == 4;
                var n9 = new PrintOptions { PagesPerSheet = 9 };
                var n16 = new PrintOptions { PagesPerSheet = 16 };
                bool nUpOk = PrintPlan.OutputPageCount(9, n9) == 1 && PrintPlan.OutputPageCount(9, n16) == 1;
                if (!parityOk) return "失败：奇偶页页序不符（奇=" + string.Join(",", oddSeq) + " 偶=" + string.Join(",", evenSeq) + "）";
                if (!defaultOk) return "失败：默认参数改变了现状页序";
                if (!bookletOk) return "失败：8 页小册子页序/输出页数不符（" + string.Join(",", bseq) + "）";
                if (!nUpOk) return "失败：9/16 版输出页数不符";
                return "通过（5页 奇→3/偶→2；8页小册子→4页且页序=(8,1)(2,7)(4,5)(6,3)；9页 9版/16版→1页；默认回归一致）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        // ===== V4.3 F6：虚拟打印端到端取证（①奇偶 ②版数 ③小册子 ④纸盒回退） =====
        private static string V43PrintSmoke()
        {
            try
            {
                string[] printers = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
                string vp = printers.FirstOrDefault(p => p.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0);
                if (vp == null) return "跳过（未安装 Microsoft Print to PDF）";
                string root = Path.Combine(Path.GetTempPath(), "BatchPrintCNV43-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(root);
                string in5 = Path.Combine(root, "in5.pdf"); BuildSamplePdf(in5, 5);
                string in8 = Path.Combine(root, "in8.pdf"); BuildSamplePdf(in8, 8);
                string in9 = Path.Combine(root, "in9.pdf"); BuildSamplePdf(in9, 9);
                int odd = PrintSample(vp, root, "odd", new PrintOptions { PageParity = PageParityMode.Odd }, in5);
                int n9 = PrintSample(vp, root, "n9", new PrintOptions { PagesPerSheet = 9 }, in9);
                int n16 = PrintSample(vp, root, "n16", new PrintOptions { PagesPerSheet = 16 }, in9);
                int bk = PrintSample(vp, root, "booklet", new PrintOptions { Booklet = true }, in8);
                int tray = PrintSample(vp, root, "tray", new PrintOptions { PaperSourceName = "不存在的纸盒" }, in5);
                bool ok = odd == 3 && n9 == 1 && n16 == 1 && bk == 4 && tray == 5;
                return (ok ? "通过" : "失败") + "（仅奇数→" + odd + "页/9版→" + n9 + "页/16版→" + n16 + "页/小册子→" + bk + "页/纸盒回退→" + tray + "页）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        /// <summary>V4.3：以 Microsoft Print to PDF 落盘一份样本，返回输出 PDF 页数（失败返回负数）。</summary>
        private static int PrintSample(string printer, string root, string caseName, PrintOptions opt, string pdfPath)
        {
            string outDir = Path.Combine(root, caseName);
            Directory.CreateDirectory(outDir);
            opt.PrintoutDirectory = outDir;   // Microsoft Print to PDF 自动落盘到独立子目录
            var bp = new BatchPrinter(printer, opt, line => { }, null, null);
            var job = new PrintJob { FilePath = pdfPath, Kind = DocKind.Pdf, Copies = 1 };
            var result = bp.Run(new List<PrintJob> { job });
            if (result.Ok != 1) return -1;
            string outPdf = Path.Combine(outDir, Path.GetFileNameWithoutExtension(pdfPath) + ".pdf");
            if (!File.Exists(outPdf)) return -2;
            using (PdfiumFile p = PdfiumFile.Open(outPdf)) return p.PageCount;
        }

        // V4.3 F15/F16 界面断言⑦⑧的实现放在 UiRenderTests.cs（同属 WpfTests partial 类）

        // V3.1 虚拟打印机自动化回归：以 Microsoft Print to PDF 为回归打印机，
        // 渲染测试 PDF 打印输出，自动比对输出 PDF 的页数与页面尺寸（A4≈595x842pt）。
        private static string VirtualPrintRegression()
        {
            try
            {
                string[] printers = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
                string vp = printers.FirstOrDefault(p => p.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) >= 0);
                if (vp == null) return "跳过（未安装 Microsoft Print to PDF）";

                string tmp = Path.Combine(Path.GetTempPath(), "BatchPrintCNVpr-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                string inPdf = Path.Combine(tmp, "in.pdf");
                string outPdf = Path.Combine(tmp, "out.pdf");
                BuildSamplePdf(inPdf);

                using (PdfiumFile src = PdfiumFile.Open(inPdf))
                {
                    int expectPages = src.PageCount;
                    var doc = new PrintDocument();
                    doc.PrinterSettings.PrinterName = vp;
                    doc.PrinterSettings.PrintToFile = true;
                    doc.PrinterSettings.PrintFileName = outPdf;
                    doc.PrinterSettings.Copies = 1;
                    doc.DefaultPageSettings.Margins = new System.Drawing.Printing.Margins(0, 0, 0, 0);
                    doc.PrintController = new StandardPrintController();
                    bool pageDone = false;
                    doc.PrintPage += (s, e) =>
                    {
                        if (pageDone) { e.HasMorePages = false; return; }
                        pageDone = true;
                        using (var bmp = src.RenderPage(0, 144f))
                        {
                            e.Graphics.DrawImage(bmp, e.PageBounds);
                        }
                        e.HasMorePages = false;
                    };
                    doc.Print();

                    if (!File.Exists(outPdf) || new FileInfo(outPdf).Length == 0) return "失败：输出 PDF 未生成";
                    using (PdfiumFile outp = PdfiumFile.Open(outPdf))
                    {
                        string msg = "输出 " + outp.PageCount + " 页 / 期望 " + expectPages + " 页";
                        if (outp.PageCount != expectPages) return "失败：" + msg;
                        if (outp.PageCount > 0)
                        {
                            var sz = outp.GetPageSizePt(0);
                            msg += "，页面 " + sz.Width.ToString("0") + "x" + sz.Height.ToString("0") + "pt（A4≈595x842）";
                            if (Math.Abs(sz.Width - 595) > 20 || Math.Abs(sz.Height - 842) > 20) return "失败：页面尺寸异常 " + msg;
                        }
                        return "通过：" + msg;
                    }
                }
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }


        private static void BuildSamplePdf(string path) { BuildSamplePdf(path, 1); }

        /// <summary>V4.3：多页样本 PDF（每页内容带页码标注），供奇偶/版数/小册子取证。</summary>
        private static void BuildSamplePdf(string path, int pages)
        {
            var objs = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pages).Select(i => (3 + i * 2) + " 0 R")) + "] /Count " + pages + " >>"
            };
            int fontObj = 3 + pages * 2;   // 字体对象号（每页占 Page+Contents 两个对象）
            for (int i = 0; i < pages; i++)
            {
                // 单页样本文本与 V4.2.5 基线逐字节一致（A/B 对照⑤要求样本内容同源，否则输出 PDF 字节差来自样本而非行为）
                string label = pages == 1 ? "BatchPrintCN WPF selftest" : "BatchPrintCN WPF selftest page " + (i + 1) + "/" + pages;
                string content = "BT /F1 20 Tf 72 760 Td (" + label + ") Tj ET\n";
                objs.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 " + fontObj + " 0 R >> >> /Contents " + (4 + i * 2) + " 0 R >>");
                objs.Add("<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "endstream");
            }
            objs.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
            var sb = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<long>();
            long pos = Encoding.ASCII.GetByteCount(sb.ToString());
            for (int i = 0; i < objs.Count; i++)
            {
                offsets.Add(pos);
                string s = (i + 1) + " 0 obj\n" + objs[i] + "\nendobj\n";
                sb.Append(s);
                pos += Encoding.ASCII.GetByteCount(s);
            }
            long xrefPos = pos;
            sb.Append("xref\n0 " + (objs.Count + 1) + "\n0000000000 65535 f \n");
            foreach (long o in offsets) sb.Append(o.ToString("0000000000") + " 00000 n \n");
            sb.Append("trailer\n<< /Size " + (objs.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xrefPos + "\n%%EOF\n");
            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
        }
    }
}
