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
using System.Printing;

namespace BatchPrintCN
{
    public enum QueueState { Unknown, Queued, Printing, Completed, Failed }
    public enum QueueReason { None, PaperOut, Offline, Deleting, Blocked }

    /// <summary>V4.2.3 G2：队列状态结构化结果（文案由 UI 按资源键出，不再靠字符串前缀判断）。</summary>
    public sealed class QueueStatus
    {
        public QueueState State;
        public QueueReason Reason;
    }

    /// <summary>
    /// F4 队列实时状态轮询：通过 PrintQueue/PrintSystemJobInfo 查询指定打印机上某打印任务的状态。
    /// 轮询由 UI 层 DispatcherTimer 驱动（worker 运行期间每 2 秒一次）。
    /// </summary>
    public static class QueueMonitor
    {
        /// <summary>查询打印机队列中指定文档名的最近任务状态；未找到或异常返回 null（可能已完成/已出队）。</summary>
        public static QueueStatus PollJobStatus(string printerName, string documentName)
        {
            if (string.IsNullOrWhiteSpace(printerName) || string.IsNullOrWhiteSpace(documentName)) return null;
            try
            {
                using (var server = new LocalPrintServer())
                {
                    using (PrintQueue queue = server.GetPrintQueue(printerName))
                    {
                            PrintJobInfoCollection jobs = queue.GetPrintJobInfoCollection();
                            using (jobs)
                            {
                                PrintSystemJobInfo best = null;
                                DateTime bestTime = DateTime.MinValue;
                                foreach (PrintSystemJobInfo job in jobs)
                                {
                                    // v1.2.2 修（2026-11-08 拍板）：删掉"找不到精确同名任务就 IndexOf 子串模糊回退"——
                                    // 文档名互为前缀/包含时会把别的任务的状态错挂过来；未命中就如实返回 null
                                    //（轮询下一拍再来，与"任务已完成出队"同一处理）。多条同名取最近提交的一条（原口径）。
                                    if (!string.Equals(job.Name, documentName, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (best == null || job.TimeJobSubmitted > bestTime) { best = job; bestTime = job.TimeJobSubmitted; }
                                }
                                if (best == null) return null;

                            if ((best.JobStatus & PrintJobStatus.Completed) != 0) return new QueueStatus { State = QueueState.Completed };
                            if ((best.JobStatus & PrintJobStatus.Error) != 0 || (best.JobStatus & PrintJobStatus.Blocked) != 0)
                                return new QueueStatus { State = QueueState.Failed, Reason = BlockedReason(best.JobStatus) };
                            if ((best.JobStatus & PrintJobStatus.Printing) != 0) return new QueueStatus { State = QueueState.Printing };
                            return new QueueStatus { State = QueueState.Queued };
                        }
                    }
                }
            }
            catch { return null; }
        }

        private static QueueReason BlockedReason(PrintJobStatus s)
        {
            if ((s & PrintJobStatus.PaperOut) != 0) return QueueReason.PaperOut;
            if ((s & PrintJobStatus.Offline) != 0) return QueueReason.Offline;
            if ((s & PrintJobStatus.Deleting) != 0) return QueueReason.Deleting;
            if ((s & PrintJobStatus.Blocked) != 0) return QueueReason.Blocked;
            return QueueReason.None;
        }
    }
}
