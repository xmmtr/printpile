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
using System.Threading.Tasks;

namespace BatchPrintCN
{
    /// <summary>49 号·版2：取时口径。自检注入假时钟，真机走系统 UTC——"距上次是否满 24 小时"必须能在自检里逐数核对。</summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }

    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow { get { return DateTime.UtcNow; } }
    }

    /// <summary>
    /// 49 号·版2 → 52 号令·返修②（2026-10-06 需求方拍板）：自动检查更新＝**每天首次打开才检查一次**——
    /// 规则＝开关开着（默认开）且"距上次发起 ≥ 24 小时"（从没查过也算到点）才发起；每次发起都把时刻写回
    /// settings.json（<b>含失败的那一次</b>——否则后端抖一小时，界面就每小时打它一次）。
    /// 49 号的 60 分钟周期定时器（StartCycle/TickMinutes）按拍板移除：程序常开一整天也只查"启动 30 秒后"那一次，
    /// 再查要等下一次打开程序。有新版＝托盘气泡一条 + 通知关于页内联结果区；检查失败＝只写日志，绝不弹任何窗；
    /// 已是最新＝不打扰。定时器只挂在主窗口（<see cref="Start"/> 由 MainWindow 加载后调用一次），
    /// 四项自检模式不启动、重复构造窗口也不会叠第二个定时器。
    /// </summary>
    public static class AutoUpdate
    {
        /// <summary>已定（52 号令沿用）：检查频率不可配置——写死 24 小时闸门。</summary>
        public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        /// <summary>主窗口加载后先等 30 秒再查第一次（避开启动那一串转换/枚举打印机）。这是**每天唯一一次**自动检查。</summary>
        public const int FirstDelaySeconds = 30;

        /// <summary>自检注入点：假时钟（不注入就走系统 UTC）。</summary>
        internal static IClock Clock = new SystemClock();

        /// <summary>自检注入点：非 null 时用它代替真的出托盘气泡（断"该不该出、内容对不对"，不在开发机上弹东西）。</summary>
        internal static Action<string, string> BubbleOverride;

        /// <summary>发现新版本时广播（关于页开着就同步内联结果区）。参数＝检查结果、当前版本。</summary>
        internal static event Action<UpdateChecker.Result, Version> NewVersionFound;

        /// <summary>到没到点（纯函数）：关掉自动检查一律不到点；从没查过＝到点；否则看是否满 24 小时。</summary>
        public static bool Due(bool enabled, DateTime? lastUtc, DateTime nowUtc)
        {
            if (!enabled) return false;
            if (!lastUtc.HasValue) return true;
            return nowUtc - lastUtc.Value >= Interval;
        }

        /// <summary>当前程序集版本（四段，与清单里的 version 同口径）。</summary>
        public static Version CurrentVersion()
        {
            return typeof(AutoUpdate).Assembly.GetName().Version;
        }

        /// <summary>
        /// 跑一次自动检查（定时器与自检共用这一段）。返回"这一次是否真的发起了检查"。
        /// 网络与解析异常都在这一段吞掉只写日志——自动检查的任何失败都不许变成窗口。
        /// </summary>
        public static async Task<bool> RunOnceAsync(Version current)
        {
            DateTime now = (Clock ?? new SystemClock()).UtcNow;
            if (!Due(SettingsService.AutoCheckUpdate, SettingsService.LastAutoCheckUtc, now)) return false;
            SettingsService.SetLastAutoCheckUtc(now);          // 发起即写回（含后面失败的这一次）
            try
            {
                // 52 号令·返修②：更新渠道锁定 stable——UpdateChannel 设置键保留读取兼容（存量 preview 值不再产生 preview 行为），
                // 两参 CheckAsync(current, false) 与旧 stable 渠道逐字同行为。
                UpdateChecker.Result r = await UpdateChecker.CheckAsync(current ?? CurrentVersion());
                if (r == null) return true;
                if (r.Outcome == UpdateChecker.Outcome.Found)
                {
                    string v = r.Latest == null ? "" : r.Latest.Version;
                    FileLogger.Write("自动检查更新：发现新版本 " + v + "（渠道 stable）");
                    NotifyBubble(v);
                    UpdateState.ReportFound(r.Latest);   // 66 号令 T1：自动检查发现新版 → 左下角橙色按钮出现（本段只在开关开着时才会跑到）
                    var handlers = NewVersionFound;
                    if (handlers != null) handlers(r, current ?? CurrentVersion());
                }
                else if (r.Outcome == UpdateChecker.Outcome.Failed)
                {
                    // 48 号口径：原文只进日志。自动检查连"分类文案"都不显示——静默。
                    FileLogger.Write("自动检查更新失败（界面零动作）：" + (r.Error ?? r.Fail.ToString()));
                }
                // UpToDate：什么都不做，不打扰
            }
            catch (Exception ex)
            {
                FileLogger.Write("自动检查更新异常（界面零动作）：" + ex.Message);
            }
            return true;
        }

        /// <summary>托盘气泡那一条（内容走资源键，中英都对；NotifyIcon 的 Text 有长度上限，键值须短）。</summary>
        private static void NotifyBubble(string version)
        {
            string title = L("s.AboutUpdateTitle");
            string text = L("s.TrayNewVersionFmt").Replace("{0}", version ?? "");
            var fake = BubbleOverride;
            if (fake != null) { fake(title, text); return; }
            try { TrayIcon.ShowBubble(title, text); }
            catch (Exception ex) { FileLogger.Write("自动检查更新：托盘气泡没发出去：" + ex.Message); }
        }

        /// <summary>自检取用：气泡文案（不注入 BubbleOverride 时也能先看内容对不对）。</summary>
        internal static string BubbleTextForTest(string version)
        {
            return L("s.TrayNewVersionFmt").Replace("{0}", version ?? "");
        }

        // ===== 定时器本体（只有主窗口会调 Start） =====

        private static System.Windows.Threading.DispatcherTimer _firstTimer;

        /// <summary>是否已经起了自动检查（重复调用 Start 不会再起一个）。</summary>
        public static bool IsRunning { get { return _firstTimer != null; } }

        /// <summary>自检取用：同 <see cref="IsRunning"/>（断"四项自检模式不起定时器"）。</summary>
        internal static bool RunningForTest { get { return IsRunning; } }

        /// <summary>
        /// 主窗口加载后调用。四项自检/对话框模式（<see cref="App.GateMode"/>）直接返回——自检里 MainWindow 会被
        /// 反复构造几十次，起了定时器就会真发网络请求；已起过也不再起第二个。
        /// 52 号令·返修②：只挂"启动 30 秒后"这一发——到点后 <see cref="Fire"/> 里 <see cref="Due"/> 的 24 小时闸门
        /// 决定真发不发；不再有 60 分钟周期补查（同一天内第二次启动也只在这 30 秒那一次，且被 24h 闸门挡住＝零请求）。
        /// </summary>
        public static void Start()
        {
            if (App.GateMode || IsRunning) return;
            _firstTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(FirstDelaySeconds) };
            _firstTimer.Tick += (s, e) =>
            {
                _firstTimer.Stop();
                _firstTimer = null;
                Fire();
            };
            _firstTimer.Start();
        }

        private static void Fire()
        {
            var ignored = RunOnceAsync(CurrentVersion());
        }

        /// <summary>退出/自检收尾时停表（定时器不停也不会拖住进程退出，但图标与状态要干净）。</summary>
        public static void Stop()
        {
            try { if (_firstTimer != null) _firstTimer.Stop(); } catch { }
            _firstTimer = null;
        }

        private static string L(string key)
        {
            try
            {
                var o = System.Windows.Application.Current != null
                    ? System.Windows.Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }
    }
}
