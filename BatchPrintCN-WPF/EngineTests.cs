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
        public static int EngineSelfTest()
        {
            string logPath = Path.Combine(AppContext.BaseDirectory, "自检报告.txt");
            var sb = new StringBuilder();
            bool ok = true;
            try
            {
                sb.AppendLine("[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] WPF 批量打印助手自检");
                var ver = typeof(WpfTests).Assembly.GetName().Version;
                sb.AppendLine("程序版本: " + ver);
                sb.AppendLine("pdfium.dll 存在: " + File.Exists(Path.Combine(AppContext.BaseDirectory, "pdfium.dll")));
                string tmp = Path.Combine(Path.GetTempPath(), "BatchPrintCNWpf-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tmp);
                string pdfPath = Path.Combine(tmp, "测试文档.pdf");
                BuildSamplePdf(pdfPath);
                using (PdfiumFile pdf = PdfiumFile.Open(pdfPath))
                {
                    sb.AppendLine("PDF 页数: " + pdf.PageCount);
                    using (var bmp = pdf.RenderPage(0, 144f)) sb.AppendLine("渲染尺寸: " + bmp.Width + "x" + bmp.Height);
                }
                string[] printers = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
                sb.AppendLine("打印机数量: " + printers.Length);
                foreach (string id in new[] { "Kwps.Application", "Word.Application", "Ket.Application", "Excel.Application", "Kwpp.Application", "PowerPoint.Application" })
                    if (Type.GetTypeFromProgID(id) != null) sb.AppendLine("可用 Office 引擎: " + id);

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
                string workerPath = Path.Combine(AppContext.BaseDirectory, "BatchPrintCNWorker.exe");
                bool workerExists = File.Exists(workerPath);
                sb.AppendLine("转换子进程 BatchPrintCNWorker.exe 存在: " + workerExists);
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

                // V3.1 虚拟打印机自动化回归：以 Microsoft Print to PDF 为回归打印机，输出 PDF 自动比对页数/纸张
                string vp = VirtualPrintRegression();
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
                string sqliteTest = SqliteStore.SelfTest(Path.Combine(Path.GetTempPath(), "BatchPrintCNSt-" + Guid.NewGuid().ToString("N")));
                sb.AppendLine("V4.1 SQLite 读写冒烟: " + sqliteTest);
                ok = ok && sqliteTest.StartsWith("通过");

                // V4.1 历史页交互冒烟：创建→显隐切换→变更筛选下拉→不死循环正常返回
                string histTest = HistoryPageSmoke();
                sb.AppendLine("V4.1 历史页交互冒烟: " + histTest);
                ok = ok && histTest.StartsWith("通过");

                // V4.2 断言①：正式库 folder 列存在 + 旧库幂等迁移冒烟（临时库，不污染正式库）
                string folderCheck = SqliteStore.FolderColumnCheck();
                sb.AppendLine("V4.2 folder 列检查: " + folderCheck);
                ok = ok && folderCheck.StartsWith("通过");
                string folderMig = SqliteStore.SelfTestV42(Path.Combine(Path.GetTempPath(), "BatchPrintCNV42-" + Guid.NewGuid().ToString("N")));
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
                string tplMove = TemplateControlsMoved();
                sb.AppendLine("V4.2 修整二·模板控件归属: " + tplMove);
                ok = ok && tplMove.StartsWith("通过");

                // 断言③：模板套用冒烟（存→读→套到弹窗控件→控件值一致→删）
                string tplApply = TemplateApplySmoke();
                sb.AppendLine("V4.2 修整二·模板套用冒烟: " + tplApply);
                ok = ok && tplApply.StartsWith("通过");

                // 断言④：任务规划窗含设置摘要行/模板行/「修改设置」按钮
                string planSmoke = TaskPlanSmoke();
                sb.AppendLine("V4.2 修整三·规划窗冒烟: " + planSmoke);
                ok = ok && planSmoke.StartsWith("通过");

                // 修整一：历史页工具栏高度统一 + 按钮文字居中不折行 + 档位项不折行
                string toolbar = HistoryToolbarSmoke();
                sb.AppendLine("V4.2 修整一·历史页工具栏: " + toolbar);
                ok = ok && toolbar.StartsWith("通过");

                // V4.2.5 U2 换骨断言：外壳三件套 + 弹窗内容面板 + NumberBox
                string u2 = U2ShellSmoke();
                sb.AppendLine("V4.2.5 U2 换骨冒烟: " + u2);
                ok = ok && u2.StartsWith("通过");

                // V4.3 F6 断言①③②：奇偶页页序 / 小册子骑马订 / 9/16 版输出页数 + 默认回归
                string f6 = F6PlanSmoke();
                sb.AppendLine("V4.3 F6 页序计划: " + f6);
                ok = ok && f6.StartsWith("通过");

                // V4.3 F6 断言①②③④端到端：虚拟打印机取证（奇偶/版数/小册子/纸盒回退）
                string v43 = V43PrintSmoke();
                sb.AppendLine("V4.3 F6 虚拟打印取证: " + v43);
                ok = ok && v43.StartsWith("通过");

                // V4.3 F15/F16 断言⑦⑧：主页无打印机设置按钮/设置页分组/优先级灰字/列头漏斗/引擎列隐藏/排序提示
                string f1516 = F15F16Smoke();
                sb.AppendLine("V4.3 F15/F16 界面断言: " + f1516);
                ok = ok && f1516.StartsWith("通过");

                // ===== V4.4 F5 逐文件参数卡片（开工令 ①②③④⑦）=====
                string v44r = V44ResolveMatrix();
                sb.AppendLine("V4.4 F5 ① 生效参数解析矩阵: " + v44r);
                ok = ok && v44r.StartsWith("通过");

                string v44p = V44TwoFilePaper();
                sb.AppendLine("V4.4 F5 ② 双文件不同纸张虚拟打印: " + v44p);
                ok = ok && v44p.StartsWith("通过");

                string v44g = V44PlanGrouping();
                sb.AppendLine("V4.4 F5 ③ 任务规划按生效参数分组: " + v44g);
                ok = ok && v44g.StartsWith("通过");

                string v44c = V44RowCardUi();
                sb.AppendLine("V4.4 F5 ④⑦ 逐文件参数弹窗与置灰联动: " + v44c);
                ok = ok && v44c.StartsWith("通过");

                // ===== V4.4 二批（2026-09-20 真机二批反馈）：修整9~15 =====
                string v44f2 = V44Fix2Ui();
                sb.AppendLine("V4.4 二批 修整9~15（全选一次点到位/截断悬浮全名/删大小列/输入框铺满/缩放自适应/边框连贯/列头居中加粗）: " + v44f2);
                ok = ok && v44f2.StartsWith("通过");
                string v44f3 = V44Fix3Ui();
                sb.AppendLine("V4.4 三批 深色缺陷A/B（数据行底色随主题·浅色零变化/两页样式对象一致/浅底按钮固定字色+对比度/实底按钮白字）: " + v44f3);
                ok = ok && v44f3.StartsWith("通过");
                string v44f4 = V44Fix4Ui();
                sb.AppendLine("V4.4 四批 修整16/17（状态列收窄·1120 参数按钮不滚/三态颜色只看结构化种类·浅深中英对比度≥4.5/份数输入框水平+垂直铺满）: " + v44f4);
                ok = ok && v44f4.StartsWith("通过");
                string v44f5 = V44Fix5Ui();
                sb.AppendLine("V4.4 五批 修整18~23（长列表补一拍不滚/打印中禁用清单全量+异常恢复/进度第X·N与剩余预估/历史页最小窗+气泡/目录行初始显示/日志去重）: " + v44f5);
                ok = ok && v44f5.StartsWith("通过");

                // ===== V4.5（2026-09-25 需求方拍板五项）=====
                string v45 = V45Ui();
                sb.AppendLine("V4.5 四项（按钮字色按主题分治/历史页状态列汉化/打印中锁交互不锁滚动/全选清除行上√即时消失；F7 监视文件夹已移出）: " + v45);
                ok = ok && v45.StartsWith("通过");

                // ===== V4.7 拆分自检 =====
                string v47 = V47Ui();
                sb.AppendLine("V4.7 拆分（监视已移除/模板限 5/历史限 200/PolyForm 随包/措辞中性化）+ 版本内修整（设置页按钮右对齐/关于页检查更新）: " + v47);
                ok = ok && v47.StartsWith("通过");

                // 断言⑤：历史页交互冒烟（V4.1 起防重入闪退，本轮修整后复跑上方 histTest 结果）
                sb.AppendLine("V4.2 修整·历史页交互冒烟: " + histTest);
                ok = ok && histTest.StartsWith("通过");

                // ===== V4.2.3 M6 守护断言 =====
                // ① 编译期依赖：主程序不再引用 WinForms 程序集；② 类库化产物在位
                //    （注意：M2 实测**不省体积**——System.Drawing.Common 仍带 Windows 桌面框架包，
                //     发布目录里 System.Windows.Forms*.dll 依旧存在，故此处查"引用"而非"文件"。）
                var refs = typeof(WpfTests).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
                bool noFormsRef = !refs.Contains("System.Windows.Forms") && !refs.Contains("System.Windows.Forms.Primitives")
                    && !refs.Contains("Microsoft.VisualBasic.Forms");
                bool coreDll = File.Exists(Path.Combine(AppContext.BaseDirectory, "BatchPrintCN.Core.dll"));
                sb.AppendLine("V4.2.3 无 WinForms 编译期引用: " + (noFormsRef ? "通过" : "失败：引用了 " + string.Join(",", refs.Where(r => r.Contains("Forms")))));
                sb.AppendLine("V4.2.3 引擎类库 BatchPrintCN.Core.dll: " + (coreDll ? "通过" : "失败：未生成/未随包"));
                ok = ok && noFormsRef && coreDll;

                // ② 版本单一来源：Directory.Build.props 出的程序集版本 = NOTICE 里的版本
                string noticeVer = NoticeVersion();
                string asmVer = typeof(WpfTests).Assembly.GetName().Version.ToString(3);
                bool verOk = !string.IsNullOrEmpty(noticeVer) && noticeVer == asmVer;
                sb.AppendLine("V4.2.3 版本一致（程序集 " + asmVer + " / NOTICE " + (noticeVer ?? "未找到") + "）: " + (verOk ? "通过" : "失败"));
                ok = ok && verOk;

                // ⑤ 中英资源键一一对应 + 界面零"未解析键"残留
                string res = ResourceKeysCheck();
                sb.AppendLine("V4.2.3 资源键守护: " + res);
                ok = ok && res.StartsWith("通过");


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
                string f = Path.Combine(AppContext.BaseDirectory, "NOTICE");
                if (!File.Exists(f)) return null;
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
