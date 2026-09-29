using System;
using System.Runtime.InteropServices;

namespace BatchPrintCN
{
    /// <summary>
    /// 打印机队列操作（R2 停止打印支撑）：取消目标打印机队列中尚未打印完成的任务。
    /// 全部尽力而为：任何失败静默跳过，绝不影响打印主流程。
    /// </summary>
    public static class PrinterQueue
    {
        private const uint JOB_CONTROL_CANCEL = 3;
        private const uint JOB_CONTROL_DELETE = 5;
        private const uint MAX_JOBS = 99;

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool OpenPrinter(string pPrinterName, out IntPtr phPrinter, IntPtr pDefault);

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumJobs(IntPtr hPrinter, uint firstJob, uint noJobs, uint level,
            IntPtr pJob, uint cbBuf, out uint pcbNeeded, out uint pcReturned);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool SetJob(IntPtr hPrinter, uint jobId, uint level, IntPtr pJob, uint command);

        [DllImport("winspool.drv", SetLastError = true)]
        private static extern bool ClosePrinter(IntPtr hPrinter);

        /// <summary>取消指定打印机队列中的所有任务，返回成功取消的数量。</summary>
        public static int CancelAllJobs(string printerName)
        {
            if (string.IsNullOrWhiteSpace(printerName)) return 0;
            if (!OpenPrinter(printerName, out IntPtr h, IntPtr.Zero)) return 0;
            int cancelled = 0;
            try
            {
                if (!EnumJobs(h, 0, MAX_JOBS, 1, IntPtr.Zero, 0, out uint needed, out _)) return 0;
                if (needed <= 0) return 0;
                IntPtr buf = Marshal.AllocHGlobal((int)needed);
                try
                {
                    if (!EnumJobs(h, 0, MAX_JOBS, 1, buf, needed, out needed, out uint returned)) return 0;
                    int jobInfoSize = Marshal.SizeOf(typeof(JobInfo1));
                    for (uint i = 0; i < returned; i++)
                    {
                        IntPtr p = IntPtr.Add(buf, (int)(i * jobInfoSize));
                        var job = (JobInfo1)Marshal.PtrToStructure(p, typeof(JobInfo1));
                        if (SetJob(h, job.JobId, 0, IntPtr.Zero, JOB_CONTROL_CANCEL)
                            || SetJob(h, job.JobId, 0, IntPtr.Zero, JOB_CONTROL_DELETE))
                            cancelled++;
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            catch { }
            finally { ClosePrinter(h); }
            return cancelled;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JobInfo1
        {
            public uint JobId;
            [MarshalAs(UnmanagedType.LPWStr)] public string pPrinterName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pMachineName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pUserName;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDocument;
            [MarshalAs(UnmanagedType.LPWStr)] public string pDatatype;
            [MarshalAs(UnmanagedType.LPWStr)] public string pStatus;
            public uint Status;
            public uint Priority;
            public uint Position;
            public uint TotalPages;
            public uint PagesPrinted;
            public SystemTime Submitted;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemTime
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }
    }
}
