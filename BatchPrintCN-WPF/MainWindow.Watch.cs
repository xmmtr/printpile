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
using System.Windows;

namespace BatchPrintCN
{
    /// <summary>
    /// 72 号令（V1.1.0·F7）：监视引擎 ↔ 主窗口的<b>唯一接缝</b>。
    ///
    /// 设计要点：
    /// ① <b>不新建第二条打印通路</b>——命中文件由这里加进同一个 <c>_rows</c>、走同一套 <c>MergeOverrides → StartWorker</c>，
    ///    与手动"添加文件 + 开始打印"共用执行链路（PrintCore/PrintPlan/OfficeExport 一行未改）；
    /// ② 唯一差别＝<b>跳过 F3 任务规划确认框</b>（那是给人点的，自动任务没人点就会永远卡在确认上）；
    /// ③ 规则绑定的模板<b>落成该行自己的 <see cref="PerFileOverride"/></b>（每项都写值），所以既不碰全局 <c>_options</c>，
    ///    也不会被用户当时界面上的全局参数串改——生效解析仍走既有的 <c>EffectiveOptions.Resolve</c>；
    /// ④ 主窗正忙时由引擎把该文件留在候选表下一拍重试（这里只回 false），不并发第二条打印线程；
    /// ⑤ 打印日志与状态行标注来源＝监视（规则名），历史表结构在 Core 里冻结，本令不加列（来源落在日志与监视页读数）。
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>本轮/上一次由监视带进来的行（供监视页"最近触发"读数与日志对照，不参与打印决策）。</summary>
        private readonly List<string> _watchLog = new List<string>();

        /// <summary>引擎探针：主窗是否忙到不能开始新一轮（打印中或续打队列还没清空）。</summary>
        internal bool WatchBusyProbe()
        {
            return _printBusy || (_worker != null && _worker.IsAlive) || (_remaining != null && _remaining.Count > 0);
        }

        /// <summary>
        /// 引擎交来一个稳定文件（<b>必须在 UI 线程上跑</b>——引擎的泵就是 DispatcherTimer，天然满足）。
        /// 返回 true＝已接受并开始打印；false＝主窗此刻不能接（引擎下一拍重试）。
        /// </summary>
        internal bool WatchFire(WatchRule rule, string path)
        {
            if (rule == null || string.IsNullOrWhiteSpace(path)) return false;
            if (WatchBusyProbe()) return false;
            try
            {
                if (!File.Exists(path)) return false;
                PrintJob job = FileClassifier.CreateJob(path);
                if (job == null) return false;                                   // 应用不支持这一类（引擎那边已按类型过滤，这里再兜一次）

                string printer = ResolveWatchPrinter(rule);
                if (string.IsNullOrWhiteSpace(printer) || printer == L("s.MsgNoPrinter"))
                {
                    AppendLog(L("s.WatchNoPrinter") + "（" + WatchRules.DisplayNameOf(rule) + "）");
                    FileLogger.Write("监视：没有可用打印机，「" + Path.GetFileName(path) + "」未打印");
                    return false;
                }

                RowVm row = _rows.FirstOrDefault(r => string.Equals(r.Job.FilePath, path, StringComparison.OrdinalIgnoreCase));
                if (row == null) { row = new RowVm(job); _rows.Add(row); }
                else row.SetStatus("", false);                                    // 同一路径再次落盘（改名回来/重放）＝复用那一行，不叠第二行

                row.Checked = true;
                row.Override = WatchRules.TemplateToOverride(WatchTemplateOptions(rule));
                _lastPrinter = printer;
                _lastError = null;

                MergeOverrides(new List<RowVm> { row });
                List<PrintJob> jobs = new List<PrintJob> { row.Job };
                _records = new List<PrintRecord>();
                _remaining = null;
                _progressTotal = 1; _progressBase = 0; _progDoneRun = 0; _curFileStart = null;
                _fileSeconds.Clear();

                SetControlsBusy(true);
                Progress.Minimum = 0; Progress.Maximum = 1; Progress.Value = 0;
                UpdateProgressText();
                StatusText.Text = L("s.StatusPrinting").Replace("{0}", "1");

                string line = L("s.WatchLogFired")
                    .Replace("{0}", Path.GetFileName(path))
                    .Replace("{1}", WatchRules.DisplayNameOf(rule))
                    .Replace("{2}", string.IsNullOrWhiteSpace(rule.TemplateName) ? L("s.WatchFollowGlobal") : rule.TemplateName)
                    .Replace("{3}", printer);
                AppendLog(line);
                FileLogger.Write("监视：来源＝监视（" + WatchRules.DisplayNameOf(rule) + "）→ " + Path.GetFileName(path) + " → " + printer);
                lock (_watchLog) { _watchLog.Add(line); if (_watchLog.Count > 200) _watchLog.RemoveAt(0); }

                StartWorker(jobs);
                return true;
            }
            catch (Exception ex)
            {
                AppendLog(L("s.WatchFireFailed") + Path.GetFileName(path) + " → " + ex.Message);
                FileLogger.Write("监视：入队异常：" + ex);
                return false;
            }
        }

        /// <summary>规则的目标打印机：写了就用写的，没写就用当前界面选择（回落到上次选择/系统默认）。</summary>
        private string ResolveWatchPrinter(WatchRule rule)
        {
            if (rule != null && !string.IsNullOrWhiteSpace(rule.Printer)) return rule.Printer.Trim();
            string cur = (PrinterBox != null ? PrinterBox.Text : null) ?? "";
            cur = cur.Trim();
            if (cur.Length > 0 && cur != L("s.MsgNoPrinter")) return cur;
            return SettingsService.LastPrinter;
        }

        /// <summary>规则绑定的模板参数（按名从库里取；模板被删了或没绑＝返回 null，这一路就跟随全局，与手动行为一致）。</summary>
        private static PrintOptions WatchTemplateOptions(WatchRule rule)
        {
            if (rule == null || string.IsNullOrWhiteSpace(rule.TemplateName)) return null;
            try
            {
                var hit = SqliteStore.LoadTemplates().FirstOrDefault(t => string.Equals(t.Name, rule.TemplateName.Trim(), StringComparison.Ordinal));
                return hit.Name == null ? null : hit.Options;
            }
            catch { return null; }
        }

        /// <summary>规则集变了 ⇒ 重建监视（监视页的增删改与启停都走这一句）。</summary>
        internal void WatchReapply()
        {
            try { WatchEngine.Apply(WatchRuleStore.Load()); }
            catch (Exception ex) { FileLogger.Write("监视：重建监视失败：" + ex.Message); }
        }

        /// <summary>主窗口 Loaded 时恢复监视（复测点⑤）：把"忙不忙"与"交回队列"两个探针交给引擎，然后按规则集起观察者。
        /// 只在真界面起来；四项自检（App.GateMode）由 <see cref="WatchEngine.StartFromMain"/> 自己挡掉，自检直接调 Apply。</summary>
        private void StartWatchFromMain()
        {
            try
            {
                WatchEngine.StartFromMain(WatchBusyProbe, WatchFire);
                int n = WatchEngine.TrackedRuleCount;
                if (n > 0) AppendLog(L("s.WatchStarted").Replace("{0}", n.ToString()));
            }
            catch (Exception ex) { FileLogger.Write("监视：启动恢复失败：" + ex.Message); }
        }

        /// <summary>监视页删除规则后在主窗日志说一句（AppendLog 本体是私有的，这里给监视页开一个口子）。</summary>
        internal void AppendWatchLog(string line) { AppendLog(line); }


        /// <summary>监视页"最近触发"读数（最多回 20 条，新→旧）。</summary>
        internal List<string> WatchRecentForTest(int max = 20)
        {
            lock (_watchLog)
            {
                return _watchLog.Skip(Math.Max(0, _watchLog.Count - max)).Reverse().ToList();
            }
        }
    }
}
