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
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整第二批·修B：单实例守卫。
    /// 为什么要它：修A 之后程序可以藏进托盘，再双击 exe 就会起第二个实例——两个托盘图标两份进程，
    /// 还都在往同一个便携数据目录（程序目录\）里写 settings.json / history.db。
    ///
    /// 命名口径（报备）：① 不加 "Global\" 前缀 → 落在**当前登录会话**的 Local 命名空间，
    /// 同一台机器上不同用户各自会话各跑一份互不干扰（数据目录随程序目录走，跨会话抢锁反而会让别人打不开自己的副本）；
    /// ② 名字后缀取**可执行文件全路径**的 SHA-256 前 6 字节 → 同一份 exe 的多个副本（v4.8 目录、v4.9 目录、U 盘拷贝）
    /// 各自独立，不会互相拦住；③ 抢锁失败只退回"没有别的实例"继续启动，绝不因为拿不到内核对象就把用户挡在门外。
    ///
    /// 通知口径：新进程用同名 <see cref="EventWaitHandle"/> 置信号后自行退出（不弹任何错误框）；
    /// 现有实例在线程池上注册等待，收到后经 Dispatcher 回 UI 线程还原主窗口（托盘态与最小化态都能还原）。
    /// </summary>
    public static class SingleInstance
    {
        /// <summary>--instance-probe 退出码：抢到＝此前没有实例（0）；没抢到＝已有实例在跑（3）。</summary>
        public const int ProbeCodeFree = 0;
        public const int ProbeCodeOwnedByOther = 3;

        private static Mutex _gate;
        private static EventWaitHandle _request;
        private static RegisteredWaitHandle _reg;
        private static string _exePath;

        public static bool Acquired { get { return _gate != null; } }

        internal static string MutexNameFor(string exePath) { return "PrintPile-" + Hash(exePath) + "-Gate"; }
        internal static string EventNameFor(string exePath) { return "PrintPile-" + Hash(exePath) + "-Restore"; }

        private static string Hash(string exePath)
        {
            string p = (exePath ?? "").Trim().ToLowerInvariant();
            try
            {
                using (var sha = SHA256.Create())
                {
                    byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(p));
                    return BitConverter.ToString(h, 0, 6).Replace("-", "");
                }
            }
            catch { return BitConverter.ToString(Encoding.UTF8.GetBytes(p.Length > 6 ? p.Substring(p.Length - 6) : p)).Replace("-", ""); }
        }

        /// <summary>当前 exe 的路径（自检里子进程与父进程同路径 → 同一把锁，可直接跨进程断言）。</summary>
        public static string CurrentExePath
        {
            get
            {
                try { return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName; }
                catch { return AppContext.BaseDirectory + "PrintPile.exe"; }
            }
        }

        /// <summary>抢锁。true＝本进程是第一个（继续正常启动）；false＝已有实例在跑（调用方应通知它并退出）。</summary>
        public static bool TryAcquire(string exePath)
        {
            if (_gate != null) return false;            // 本进程已持有：直接算"已有实例"，且不能把已开着的句柄丢掉
                                                        //（丢了引用句柄也不会关，内核对象会一直活到进程退出——自检里
                                                        // 第二次调用就踩过这个坑，导致释放后子进程探针仍报"已存在"）
            bool createdNew;
            Mutex m;
            try { m = new Mutex(false, MutexNameFor(exePath), out createdNew); }
            catch { return true; }                      // 内核对象都建不出来＝奇异环境，不因此挡住用户
            if (!createdNew) { m.Close(); return false; }
            _gate = m; _exePath = exePath;
            return true;
        }

        /// <summary>现有实例开始等待"再双击"的信号（回调一定在 UI 线程上跑）。</summary>
        public static void Watch(Action onRestoreRequested)
        {
            if (_gate == null || onRestoreRequested == null) return;
            try
            {
                bool createdNew;
                _request = new EventWaitHandle(false, EventResetMode.AutoReset, EventNameFor(_exePath), out createdNew);
                _reg = ThreadPool.RegisterWaitForSingleObject(_request, (s, timedOut) =>
                {
                    if (timedOut) return;
                    var app = Application.Current;
                    if (app == null) { onRestoreRequested(); return; }
                    try { app.Dispatcher.BeginInvoke(onRestoreRequested); }
                    catch { onRestoreRequested(); }
                }, null, Timeout.Infinite, false);
            }
            catch { }
        }

        /// <summary>新进程：给现有实例发"还原窗口"信号。返回 true＝确实有实例在跑（信号已置）。</summary>
        public static bool NotifyExisting(string exePath)
        {
            bool createdNew;
            try
            {
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, EventNameFor(exePath), out createdNew))
                {
                    if (createdNew) return false;       // 连事件都没人建过＝没有持有者
                    ev.Set();
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>释放（进程退出前调用；内核对象随最后一个句柄关闭而消失）。</summary>
        public static void Release()
        {
            try { if (_reg != null) _reg.Unregister(null); } catch { }
            _reg = null;
            try { if (_request != null) _request.Close(); } catch { }
            _request = null;
            try { if (_gate != null) _gate.Close(); } catch { }
            _gate = null;
            _exePath = null;
        }

        /// <summary>
        /// --instance-probe：只回答"有没有别人在跑"就退出（不开窗口、不读设置、不写日志），
        /// 自检据此做**真跨进程**断言：先由自己抢锁 → 起子进程应得 3；自己释放后 → 子进程应得 0。
        /// </summary>
        public static int RunProbe()
        {
            string exe = CurrentExePath;
            bool mine = TryAcquire(exe);
            Release();
            return mine ? ProbeCodeFree : ProbeCodeOwnedByOther;
        }
    }
}
