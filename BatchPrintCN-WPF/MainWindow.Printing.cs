using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

using System.Windows.Input;
using Microsoft.Win32;

namespace BatchPrintCN
{

    // V4.2.3 M4 拆分：打印机选择、开始/继续/停止/重试、任务规划衔接、队列轮询、完成统计与 CSV 导出（原样搬移）。
    public partial class MainWindow
    {
        private void OnRefresh(object sender, RoutedEventArgs e) { LoadPrinters(); }

        // F15（V4.3）：打印机选择持久化（设置页「打印机」分组与主页共用同一 settings 键，双向同步）
        private void OnPrinterChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PrinterBox == null) return;
            string p = PrinterBox.SelectedItem as string;
            if (!string.IsNullOrWhiteSpace(p) && p != L("s.MsgNoPrinter")) SettingsService.SetLastPrinter(p);
        }

        /// <summary>F15：设置页改了打印机 → 主页下拉跟随（事件来自 SettingsService.LastPrinterChanged）。</summary>
        private void OnLastPrinterChanged()
        {
            string p = SettingsService.LastPrinter;
            if (string.IsNullOrEmpty(p) || PrinterBox == null) return;
            if (PrinterBox.Items.Contains(p) && PrinterBox.Text != p) PrinterBox.SelectedItem = p;
        }

        private async void OnPrintSetup(object sender, RoutedEventArgs e) { await OpenPrintSettingsAsync(); }

        /// <summary>V4.2：打开打印设置（U2-4 起为 ContentDialog，模板套用/保存也在弹窗内），确定后记录最近套用的模板名。
        /// F6（V4.3）：把当前所选打印机传入面板，用于枚举 PaperSources（纸盒下拉）。</summary>
        private async System.Threading.Tasks.Task OpenPrintSettingsAsync()
        {
            var (applied, templateName) = await PrintSettingsPanel.ShowAsync(_options, (PrinterBox.Text ?? "").Trim());
            if (!applied) return;
            _templateName = templateName;
            UpdateStatus();
            AppendLog(L("s.PrintSettingsUpdated") + _options.Summary()
                + (string.IsNullOrEmpty(_templateName) ? "" : "（" + L("s.TemplateLabel") + _templateName + "）"));
        }

        private async void OnStart(object sender, RoutedEventArgs e)
        {
            if (_worker != null && _worker.IsAlive) return;

            string printer = (PrinterBox.Text ?? "").Trim();
            if (printer == L("s.MsgNoPrinter") || printer.Length == 0) { MessageBox.Show(this, L("s.MsgNoPrinter"), L("s.Warning"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _lastPrinter = printer;
            _lastError = null;

            List<PrintJob> jobs;
            if (_remaining != null && _remaining.Count > 0)
            {
                // R2：继续打印剩余文件（跳过已完成，不重复）
                // 修整1/B-1（V4.4）：续打走"行"而不是 job 快照——中途改过该文件参数（或列里的份数/页码）照常生效
                jobs = TakeRemainingJobs();
                if (_records == null) _records = new List<PrintRecord>();
            }
            else
            {
                if (_rows.Count == 0) { MessageBox.Show(this, L("s.MsgNoFiles"), L("s.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
                // F13：只处理勾选文件；未勾选行标「已跳过」并记日志
                var checkedRows = _rows.Where(r => r.Checked).ToList();
                var skippedRows = _rows.Where(r => !r.Checked).ToList();
                foreach (RowVm r in skippedRows) r.SetStatus(L("s.StatusSkipped"), false);
                if (checkedRows.Count == 0) { MessageBox.Show(this, L("s.MsgNoChecked"), L("s.Hint"), MessageBoxButton.OK, MessageBoxImage.Information); return; }
                if (skippedRows.Count > 0) AppendLog(L("s.LogSkipped").Replace("{0}", skippedRows.Count.ToString()));
                MergeOverrides(checkedRows);
                jobs = checkedRows.Select(r => r.Job).ToList();
                _records = new List<PrintRecord>();

                // F5（V4.4）：日志留痕"这一批里几个文件用了自己的参数"
                int customCount = checkedRows.Count(r => r.IsCustom);
                if (customCount > 0) AppendLog(L("s.LogCustom").Replace("{0}", customCount.ToString()));

                // 修整6（V4.4）：进度条分母 = 本次开始打印时勾选的文件总数（停止后续打从原进度续滚，不归零）
                _progressTotal = checkedRows.Count;
                _progressBase = 0;
                // 修整20（五批）：新开一轮 → 失败计数与耗时样本清零（停止→继续 走上面那条分支，样本要累计）
                _progFailTotal = 0;
                _fileSeconds.Clear();

                // F3：任务规划确认（U2-6 起为 ContentDialog）；修整4：「修改设置」改完回到规划框，取消/确认才离开
                // V4.7 拆分1：F7 监视自动开始已移出，开始打印一律走这张规划确认框——与 V4.4 行为一致。
                if (!await ConfirmPlanAsync(checkedRows, printer)) return;
            }

            SetControlsBusy(true);
            Progress.Minimum = _progressBase;
            Progress.Maximum = _progressTotal;
            Progress.Value = _progressBase;
            _progDoneRun = 0; _curFileStart = null;   // 修整20：引擎的 done 每轮从 0 重数，分子靠 _progressBase 续
            UpdateProgressText();
            StatusText.Text = L("s.StatusPrinting").Replace("{0}", jobs.Count.ToString());
            StartWorker(jobs);
        }

        /// <summary>F5（V4.4）+ 断言盲区①：把「列表列的份数/页码」与「该行逐文件覆盖」合并进 job。
        /// 开始打印 / 重试 / 续打三条路径共用这一段（唯一真相，B-2 口径：份数与页码只在列表列录入）。</summary>
        internal void MergeOverrides(List<RowVm> rows)
        {
            foreach (RowVm r in rows)
            {
                int n;
                int colCopies = int.TryParse(r.Copies, out n) && n >= 1 ? n : 1;
                r.Job.Copies = EffectiveOptions.Copies(r.Override, colCopies);
                r.Job.RangeText = EffectiveOptions.RangeText(r.Override, r.RangeText);
                r.Job.PageIndexes = null;
                r.Job.Override = r.Override;
                r.SetStatus("", false);
            }
        }

        /// <summary>修整1/B-1（V4.4）：取出停止时剩余的行 → 按各行"当前"参数重新合并 → 续打任务列表。
        /// 修整6：同时把进度基线接回停止处的值。</summary>
        internal List<PrintJob> TakeRemainingJobs()
        {
            var rows = _remaining ?? new List<RowVm>();
            _remaining = null;
            MergeOverrides(rows);
            if (_progressTotal < rows.Count) { _progressTotal = rows.Count; _progressBase = 0; }
            else _progressBase = _progressTotal - rows.Count;
            return rows.Select(r => r.Job).ToList();
        }

        internal enum PlanStep { Abort, Start, ShowSettingsThenPlan }

        internal static PlanStep NextPlanStep(TaskPlanOutcome outcome)
        {
            if (outcome == TaskPlanOutcome.Confirmed) return PlanStep.Start;
            if (outcome == TaskPlanOutcome.ModifySettings) return PlanStep.ShowSettingsThenPlan;
            return PlanStep.Abort;
        }

        /// <summary>
        /// 修整4（V4.4）：任务规划确认循环——「修改设置」→ 打印设置弹窗 → 关闭后**回到规划框**
        /// （设置没改＝原规划原样再显示；改了＝按新参数重新规划后显示）；只有用户在规划框点「取消」或「确认打印」才离开。
        /// showPlan / openSettings 可注入（自检用假实现走同一段控制流，真机用默认的真实弹窗）。
        /// </summary>
        internal async System.Threading.Tasks.Task<bool> ConfirmPlanAsync(List<RowVm> rows, string printer,
            System.Func<List<TaskPlanGroup>, System.Threading.Tasks.Task<TaskPlanOutcome>> showPlan = null,
            System.Func<System.Threading.Tasks.Task> openSettings = null)
        {
            if (showPlan == null)
            {
                showPlan = groups => TaskPlanPanel.ShowPlanAsync(this, groups, PlanSummary(rows, groups), _options.Summary(), _templateName);
            }
            if (openSettings == null) openSettings = OpenPrintSettingsAsync;

            for (; ; )
            {
                List<TaskPlanGroup> groups = BuildTaskPlan(rows, printer);
                var outcome = await showPlan(groups);
                switch (NextPlanStep(outcome))
                {
                    case PlanStep.Start: return true;
                    case PlanStep.ShowSettingsThenPlan: await openSettings(); break;   // 回到循环顶部 = 再次显示规划框
                    default: return false;   // 取消 / Esc：用户明确放弃，回主页
                }
            }
        }

        private string PlanSummary(List<RowVm> rows, List<TaskPlanGroup> groups)
        {
            return L("s.TaskPlanSummary").Replace("{0}", rows.Count.ToString()).Replace("{1}", groups.Count.ToString());
        }

        // F3：任务规划——参数（份数/页码范围/打印设置）相同的文件合并为同一任务，不同的拆分
        // F5（V4.4）：分组键改用「每个文件的生效参数」（全局打印设置 + 该行逐文件覆盖解析后的结果），
        //             自定义了不同参数的文件自动拆成不同任务；组头展示生效参数并标注自定义了哪些项。
        internal List<TaskPlanGroup> BuildTaskPlan(List<RowVm> rows, string printer)
        {
            var groups = new List<TaskPlanGroup>();
            var map = new Dictionary<string, TaskPlanGroup>();
            foreach (RowVm r in rows)
            {
                PrintOptions eff = EffectiveOptions.Resolve(_options, r.Override);
                string custom = CustomMark(r.Override);
                string key = r.Job.Copies + "|" + (r.Job.RangeText ?? "") + "|" + eff.Summary() + "|" + custom;
                if (!map.TryGetValue(key, out TaskPlanGroup g))
                {
                    g = new TaskPlanGroup
                    {
                        Key = key,
                        Header = printer + " · 份数 " + r.Job.Copies + " · " + eff.Summary()
                            + (string.IsNullOrEmpty(r.Job.RangeText) ? "" : " · 页码 " + r.Job.RangeText)
                            + (custom.Length == 0 ? "" : " · " + custom)
                    };
                    map[key] = g;
                    groups.Add(g);
                }
                g.Files.Add(r.Name);
                g.Jobs.Add(r.Job);
            }
            return groups;
        }

        /// <summary>F5（V4.4）：规划窗组头的"自定义"标注（字段名走资源键，中英都跟界面语言）。</summary>
        internal string CustomMark(PerFileOverride ov)
        {
            if (ov == null || !ov.HasAny) return "";
            return L("s.PlanCustomFields").Replace("{0}", string.Join(L("s.ListSep"), ov.CustomFields(L)));
        }

        private void StartWorker(List<PrintJob> jobs)
        {
            _worker = new Thread(() =>
            {
                // M5：加密 PDF 需要密码时，回 UI 线程弹窗输入（Dispatcher.Invoke 阻塞等待用户输入）
                Func<string, string> askPwd = file =>
                {
                    string result = null;
                    try
                    {
                        Dispatcher.Invoke(new Action(() =>
                        {
                            var win = new PdfPasswordWindow(Path.GetFileName(file), L("s.PdfPasswordHint"));
                            win.Owner = this;
                            result = win.ShowDialog() == true ? win.Password : null;
                        }));
                    }
                    catch { }
                    return result;
                };
                // V3.2：注入落盘/保留产物设置到打印选项
                _options.KeepConvertedPdf = SettingsService.KeepConvertedPdf;
                _options.ConvertOutputDirectory = SettingsService.ConvertOutputDirectory;
                _options.PrintoutDirectory = SettingsService.PrintoutDirectory;

                var printerObj = new BatchPrinter(_lastPrinter, _options, line => AppendLog(line), done =>
                    // 修整6（V4.4）：整个打印项目一条进度——分子 = 停止前已完成(_progressBase) + 本轮已完成
                    // 修整20（五批）：同一个 done 值也喂给进度文本（条与「第 X / N 个」永远同一个分子）
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _progDoneRun = done;
                        Progress.Value = Math.Min(_progressBase + done, Progress.Maximum);
                        UpdateProgressText();
                    })), askPwd);
                printerObj.ConverterEngine = SettingsService.ConverterEngine == ConverterEngine.MiniPdf ? "minipdf"
                    : SettingsService.ConverterEngine == ConverterEngine.Com ? "com" : "auto";   // F1 引擎偏好（auto 自动检测）
                printerObj.Progress = OnPrintProgress;   // V4.2.3 G2：结构化进度回调（行状态/重试/失败原因）
                _activePrinter = printerObj;
                // 修整19（V4.4 五批）：Run 抛异常也必须走收尾——否则打印中的锁定态（SetControlsBusy(true)）
                // 永远不恢复，等于整条 UI 被永久锁死（M1 全局兜底只接崩溃、不管控件状态）。
                // 兜底口径＝"本轮未打的文件全部留在续打队列"，用户点「继续打印」可接着打，不丢任务。
                PrintRunResult result = null;
                try
                {
                    result = printerObj.Run(jobs);
                }
                catch (Exception ex)
                {
                    AppendLog(L("s.Error") + ": " + ex.Message);
                }
                finally
                {
                    var final = result ?? new PrintRunResult { Ok = 0, Fail = 0, Remaining = new List<PrintJob>(jobs) };
                    Dispatcher.BeginInvoke(new Action(() => FinishPrint(final)));
                }
            });
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.IsBackground = true;
            _worker.Start();
            StartQueuePolling();   // F4：worker 运行期间轮询打印机队列实时状态
        }

        // F4：队列实时状态轮询（每 2 秒查当前打印任务的队列状态，补充失败/阻塞原因）
        private System.Windows.Threading.DispatcherTimer _queueTimer;

        private void StartQueuePolling()
        {
            try
            {
                _queueTimer?.Stop();
                _queueTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _queueTimer.Tick += (s, e) =>
                {
                    if (_currentRow == null || string.IsNullOrWhiteSpace(_lastPrinter)) return;
                    QueueStatus q = QueueMonitor.PollJobStatus(_lastPrinter, _currentRow.Name);
                    if (q != null && q.State == QueueState.Failed)
                        // 修整16（四批）口径：队列阻塞原因虽由失败触发，文字色维持现状（不上红），故显式给 Plain
                        _currentRow.SetStatus(L("s.StatusFailedRow") + "：" + QueueReasonText(q.Reason), true, RowStatusKind.Plain);   // 卡纸/缺纸/离线等阻塞原因实时上屏
                };
                _queueTimer.Start();
            }
            catch { }
        }

        private void StopQueuePolling()
        {
            try { _queueTimer?.Stop(); _queueTimer = null; } catch { }
        }

        // F4：失败行重试（只重试该文件）
        private void OnRetryClicked(object sender, RoutedEventArgs e)
        {
            if (_worker != null && _worker.IsAlive) return;
            var btn = sender as Button;
            var row = btn?.Tag as RowVm;
            if (row == null) return;
            string printer = (PrinterBox.Text ?? "").Trim();
            if (printer.Length == 0 || printer == L("s.MsgNoPrinter")) { MessageBox.Show(this, L("s.MsgNoPrinter"), L("s.Warning"), MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            _lastPrinter = printer;
            _lastError = null;
            MergeOverrides(new List<RowVm> { row });   // 断言盲区①：重试与开始/续打共用同一段合并（唯一真相）
            _records = _records ?? new List<PrintRecord>();
            SetControlsBusy(true);
            Progress.Minimum = 0;
            Progress.Maximum = 1;
            Progress.Value = 0;
            // 修整20：重试＝单文件的一轮（N=1），耗时样本沿用（不清零，预估才有底子）
            _progressTotal = 1; _progressBase = 0; _progDoneRun = 0; _curFileStart = null;
            UpdateProgressText();
            StatusText.Text = L("s.StatusPrinting").Replace("{0}", "1");
            StartWorker(new List<PrintJob> { row.Job });        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            // 卫生项③停止竞态：仅当 worker 仍在运行才执行取消（防任务已结束的残留点击）
            if (_worker == null || !_worker.IsAlive) return;
            if (_activePrinter != null) _activePrinter.CancelRequested = true;
            // R2：尽量取消打印机队列中尚未打印的任务，立即停住
            try
            {
                int cancelled = PrinterQueue.CancelAllJobs(_lastPrinter);
                if (cancelled > 0) AppendLog(L("s.LogQueueCancelled").Replace("{0}", cancelled.ToString()));
            }
            catch { }
            BtnStop.IsEnabled = false;
            StatusText.Text = L("s.StatusStopping");
        }

        private void FinishPrint(PrintRunResult result)
        {
            StopQueuePolling();   // F4：停止队列轮询
            _activePrinter = null;
            if (_records == null) _records = new List<PrintRecord>();
            _records.AddRange(result.Records);
            // 修整1/B-1（V4.4）：剩余任务改存"行"（job 只是那一刻的快照），续打时按各行当前参数重新合并
            var rem = new HashSet<PrintJob>(result.Remaining ?? new List<PrintJob>());
            _remaining = _rows.Where(r => rem.Contains(r.Job)).ToList();

            // F2：历史入库（SQLite，循环写入每条记录）
            try { foreach (PrintRecord r in result.Records) SqliteStore.InsertHistory(r); }
            catch { }

            if (_remaining.Count > 0)
            {
                // R2：被停止，剩余任务保留，按钮变"继续打印"
                SetControlsBusy(false);
                BtnStart.Content = L("s.ContinuePrint");
                StatusText.Text = L("s.StatusPaused").Replace("{0}", _remaining.Count.ToString());
                AppendLog(L("s.LogPaused").Replace("{0}", _remaining.Count.ToString()));
                _curFileStart = null;
                UpdateProgressText();   // 修整20：暂停态继续显示「第 X / N 个」（X 停在已完成数，续打时按新基线接着走）
                return;
            }

            // 全部完成
            SetControlsBusy(false);
            _progressTotal = 0;   // 修整6（V4.4）：一轮打印结束，进度基线复位（下一次「开始打印」重新按勾选总数计）
            _progressBase = 0;
            _progDoneRun = 0; _curFileStart = null;
            UpdateProgressText();   // 修整20：N 归零 → 计数文本清空（完成/失败数由 StatusText 与 Toast 承担）
            BtnStart.Content = L("s.StartPrint");
            StatusText.Text = L("s.StatusDone").Replace("{0}", result.Ok.ToString()).Replace("{1}", result.Fail.ToString());
            AppendLog(L("s.LogEnd").Replace("{0}", result.Ok.ToString()).Replace("{1}", result.Fail.ToString()));

            // R3：打印完成后输出统计表格（设置开关，默认关）
            if (SettingsService.PrintReport) ExportReport(_records);

            if (result.Fail > 0)
            {
                string msg = L("s.ToastFail") + " " + L("s.ToastFailCount").Replace("{0}", result.Fail.ToString());
                if (!string.IsNullOrWhiteSpace(_lastError)) msg += "\n" + _lastError;
                ToastAlert.ShowError(msg, this);
            }
            else
            {
                ToastAlert.ShowSuccess(L("s.ToastSuccess") + " " + L("s.ToastSuccessCount").Replace("{0}", result.Ok.ToString()), this);
            }
        }

        private void ExportReport(List<PrintRecord> records)
        {
            try
            {
                string dir = SettingsService.ReportDirectory;
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "打印统计-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv");
                var sb = new StringBuilder();
                sb.AppendLine("文件名,后缀,份数,页数,状态,时间,打印机,引擎");
                foreach (var r in records)
                    sb.AppendLine(string.Join(",", EscapeCsv(r.Name), EscapeCsv(r.Ext), r.Copies.ToString(), r.Pages.ToString(), r.Success ? "成功" : "失败", r.Time.ToString("yyyy-MM-dd HH:mm:ss"), EscapeCsv(r.Printer), EscapeCsv(r.Engine ?? "-")));
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
                AppendLog(L("s.LogReportSaved") + file);
                ToastAlert.ShowSuccess(L("s.ToastReportSaved") + " " + file, this);
            }
            catch (Exception ex) { AppendLog(L("s.LogReportFailed") + ex.Message); }
        }

        private static string EscapeCsv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private bool _printBusy;

        // ===== 修整19/20（V4.4 五批）自检钩子：走真实方法本体，不复制逻辑（防"测的与跑的不是同一段"） =====
        internal void SetControlsBusyForTest(bool busy) { SetControlsBusy(busy); }
        internal void FinishPrintForTest(PrintRunResult r) { FinishPrint(r); }
        internal void PrintProgressForTest(PrintProgress p) { OnPrintProgress(p); }
        internal void SetProgressForTest(int total, int doneBase, int doneRun)
        {
            _progressTotal = total; _progressBase = doneBase; _progDoneRun = doneRun;
            UpdateProgressText();
        }
        internal void AddFileSampleForTest(double seconds) { _fileSeconds.Add(seconds); UpdateProgressText(); }
        internal void ResetFileSamplesForTest() { _fileSeconds.Clear(); }
        internal string ProgressTextForTest { get { return ProgressText == null ? null : ProgressText.Text; } }
        /// <summary>修整20 断言用：失败数那一段 Run 的实际字色（未上色返回 null）。</summary>
        internal System.Windows.Media.Color? ProgressFailColorForTest
        {
            get
            {
                var sb = _failRun == null ? null : _failRun.Foreground as System.Windows.Media.SolidColorBrush;
                return sb == null ? (System.Windows.Media.Color?)null : sb.Color;
            }
        }

        // ===== 修整20（V4.4 五批）：文件级计数与剩余时间预估 =====
        // 计时全在 UI 侧（Started→Completed/Failed 两个结构化回调之间取时间差），引擎一行没改。
        private int _progDoneRun;                                        // 本轮已完成文件数（引擎 done 回调口径，含失败）
        private int _progFailTotal;                                      // 累计失败（含重试前，只增不减）
        private System.DateTime? _curFileStart;                          // 当前文件的开始时刻（UTC）
        private readonly List<double> _fileSeconds = new List<double>(); // 单文件耗时样本（秒，最多留 40 个）
        private const int EtaSampleWindow = 5;                           // 滑动平均窗口
        private const long EtaMaxSeconds = 6 * 3600;                     // 预估超过 6 小时＝不可信，不显示

        /// <summary>修整20：底栏进度文本「第 X / N 个 · 预计剩余 ≈ … · 失败 Z」。
        /// X 口径与修整6 的整项目进度一致（分子含停止前已完成 + 当前这个）；N=0（一轮结束/空闲）即清空。</summary>
        private void UpdateProgressText()
        {
            if (ProgressText == null) return;
            ProgressText.Inlines.Clear();
            _failRun = null;
            if (_progressTotal <= 0) return;
            int doneIdx = Math.Min(_progressBase + _progDoneRun, _progressTotal);
            int x = Math.Min(doneIdx + 1, _progressTotal);
            AddProgressRun(L("s.ProgCount").Replace("{0}", x.ToString()).Replace("{1}", _progressTotal.ToString()), null);
            string eta = EtaText(doneIdx);
            if (eta != null) AddProgressRun(" · " + L("s.ProgEta").Replace("{0}", eta), null);
            if (_progFailTotal > 0) AddProgressRun(" · " + L("s.ProgFail").Replace("{0}", _progFailTotal.ToString()), "StatusFailTextBrush");
        }

        /// <summary>修整20：失败数用修整16 的状态红键，且把颜色落在 Run 自身（隐式 TextBlock 样式会压过继承＝缺陷B 教训）。</summary>
        private System.Windows.Documents.Run _failRun;
        private void AddProgressRun(string text, string brushKey)
        {
            var r = new System.Windows.Documents.Run(text);
            if (brushKey != null)
            {
                r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, brushKey);
                _failRun = r;
            }
            ProgressText.Inlines.Add(r);
        }

        /// <summary>修整20：剩余时间预估＝最近 5 个文件耗时的滑动平均 × 剩余个数。
        /// 宁缺勿错：样本 <2、平均过小、方差过大（标准差/均值 >1）、或预估 >6 小时 → 返回 null（不显示）。</summary>
        private string EtaText(int doneIdx)
        {
            int remain = _progressTotal - doneIdx;
            if (remain <= 0 || _fileSeconds.Count < 2) return null;
            var win = _fileSeconds.Skip(Math.Max(0, _fileSeconds.Count - EtaSampleWindow)).ToList();
            double avg = win.Average();
            if (avg <= 0.2) return null;
            double variance = win.Sum(v => (v - avg) * (v - avg)) / win.Count;
            if (Math.Sqrt(variance) / avg > 0.8) return null;   // 方差过大＝这台机器上每个文件差太多，估了也不准，宁缺勿错
            long secs = (long)Math.Round(avg * remain);
            if (secs > EtaMaxSeconds) return null;
            if (secs < 60) return L("s.ProgEtaSec").Replace("{0}", secs.ToString());
            if (secs < 3600) return L("s.ProgEtaMS").Replace("{0}", (secs / 60).ToString()).Replace("{1}", (secs % 60).ToString());
            return L("s.ProgEtaHS").Replace("{0}", (secs / 3600).ToString()).Replace("{1}", ((secs % 3600) / 60).ToString());
        }

        /// <summary>修整20：给当前文件记一个耗时样本（成功与失败都消耗时间，都算样本）。</summary>
        private void CloseFileSample()
        {
            if (_curFileStart == null) return;
            _fileSeconds.Add(Math.Max(0.2, (System.DateTime.UtcNow - _curFileStart.Value).TotalSeconds));
            if (_fileSeconds.Count > 40) _fileSeconds.RemoveAt(0);
            _curFileStart = null;
        }

        /// <summary>修整19（V4.4 五批）：当前是否处于"打印中锁定"态（自检断言用）。</summary>
        internal bool PrintBusyForTest { get { return _printBusy; } }

        /// <summary>
        /// 修整19（V4.4 五批，需求方 2026-09-22 真机反馈）：打印途中不得改动任务清单与打印参数——
        /// 一个方法管全部入口（逐个点名，防漏防散），停止/完成/异常都从这里恢复。
        /// 需求方点名四项：打印机下拉、打印设置、清空列表、移除选中；
        /// 同类入口（同一原则）：添加文件/添加文件夹、拖拽导入、Ctrl+V、刷新打印机列表；
        /// 行内份数/页码输入、行勾选框、表头全选、参数「修改」与「重试」都在主列表里 → 整表 IsEnabled=false 一起锁；
        /// 设置页 F15 的打印机下拉与「打开打印机属性」是同一 LastPrinter 的另一端，只堵主页等于没堵 → 一并锁。
        /// 禁用观感走各控件既有禁用态（BtnBase 的 50% 透明口径），不新造样式。
        /// </summary>
        private void SetControlsBusy(bool busy)
        {
            _printBusy = busy;
            BtnStart.IsEnabled = !busy;
            BtnStop.IsEnabled = busy;
            // V4.5-3（决9a）：这里原来是 Grid.IsEnabled = !busy —— 整表禁用会沿可视树传染给内部 ScrollViewer，
            // 打印中连进度都滑不动看不了（需求方真机反馈）。改为"锁交互不锁滚动"：表格本体始终可用，
            // 行内四类控件（勾选框、份数/页码输入、参数「修改」、失败「重试」）绑 PrintLock.Idle 各自置灰。
            PrintLock.Instance.Set(busy);
            PrinterBox.IsEnabled = !busy;
            BtnPrintSetup.IsEnabled = !busy;
            BtnClear.IsEnabled = !busy;
            BtnRemove.IsEnabled = !busy;
            BtnAddFiles.IsEnabled = !busy;
            BtnAddFolder.IsEnabled = !busy;
            BtnRefresh.IsEnabled = !busy;
            AllowDrop = !busy;                       // 拖拽导入：光标/放置反馈与事件守卫两头都挡（守卫见 OnDragOver/OnDrop）
            if (PageSettings != null) PageSettings.SetPrintBusy(!busy);   // F15 设置页两处入口同步
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            SettingsService.LanguageChanged -= OnLanguageChanged;
            SettingsService.LastPrinterChanged -= OnLastPrinterChanged;   // F15
            if (_worker != null && _worker.IsAlive)
            {
                if (_activePrinter != null) _activePrinter.CancelRequested = true;
                var r = MessageBox.Show(this, L("s.MsgExitConfirm"), L("s.ExitTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) { e.Cancel = true; return; }
            }
            base.OnClosing(e);
        }
    }
}
