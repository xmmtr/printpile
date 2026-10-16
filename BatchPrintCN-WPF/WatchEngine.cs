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
using System.Windows.Threading;

namespace BatchPrintCN
{
    /// <summary>
    /// 72 号令（V1.1.0·F7）：监视引擎——每条启用规则一个 <see cref="FileSystemWatcher"/>，
    /// 事件只入队到候选表，真正的判定与"交给主窗打印"全部在 DispatcherTimer 那一拍里做（<b>不在 watcher 线程上碰 UI 与磁盘状态</b>）。
    ///
    /// 三道闸（顺序即优先级）：
    /// ① <b>类型</b>：模式命中且应用支持（含魔数校验）；
    /// ② <b>稳定性</b>：大小连续两次采样一致、静默满 <see cref="WatchRules.QuietMilliseconds"/>、且能独占打开 ⇒ 才算写完
    ///    （红线：绝不打印写了一半的文件；不满足就下一拍再查，最长 <see cref="MaxWaitMilliseconds"/> 后放弃并记日志）；
    /// ③ <b>时段</b>：落在生效时段内才入队；时段外<b>不打也不囤</b>——直接记一条"时段外跳过"的日志（宁可让用户手动补，也不在半夜擅自出纸）。
    ///
    /// 与手动流程的关系：命中后只调主窗注入的 <see cref="Fire"/> 那一个回调，由主窗把文件<b>加进同一个列表、走同一条队列</b>；
    /// 本引擎不构造 BatchPrinter、不碰 PrintCore/PrintPlan/OfficeExport（72 号 Rule 3 引擎边界）。
    /// 主窗正忙（打印中）时候选原地等待，下一拍重试——不丢文件也不并发第二条打印线程。
    /// </summary>
    internal static class WatchEngine
    {
        /// <summary>泵的节奏（毫秒）：稳定性采样与重试都靠它。</summary>
        internal const int PumpMilliseconds = 500;

        /// <summary>一个文件从"出现"到"放弃等待"的上限（写不完/一直占用/一直忙 ⇒ 超时记日志，不无限囤）。</summary>
        internal const long MaxWaitMilliseconds = 30 * 60 * 1000;

        /// <summary>已处理表的上限（防长跑进程内存无界增长；超了就按最早插入的清一批）。</summary>
        private const int HandledCap = 4000;

        // ===== 注入点（产品代码不赋值；自检走桩，全程不真起 watcher、不真打印） =====
        // 就地初始化 = null 是给 CS0649（TreatWarningsAsErrors）看的。
        internal static Func<bool> Busy = null;                       // 主窗是否正忙（忙则本轮不接）
        internal static Func<WatchRule, string, bool> Fire = null;    // 交给主窗入队打印；true＝已接受
        internal static Func<DateTime> Clock = null;                  // 时段判定用的"现在"（自检固定时钟）
        internal static Action<WatchRule, string> MoveProbe = null;   // 打印后动作的落地（自检桩；null＝走本类 MoveToDone）

        /// <summary>规则运行读数（监视页展示：本轮已触发/等待中/跳过），按规则 Id 计。</summary>
        internal sealed class RuleStats
        {
            public int Fired;
            public int SkippedWindow;
            public int SkippedType;
            public int TimedOut;
            public int Moved;
            public string LastFile = "";
        }

        private sealed class Cand
        {
            public string Path;
            public long Size;              // 上一次采样到的大小
            public long LastChangeMs;      // 上一次"大小发生变化"的时刻
            public long FirstMs;           // 第一次看到的时刻
        }

        private sealed class State
        {
            public WatchRule Rule;
            public FileSystemWatcher Watcher;
            public readonly List<Cand> Pending = new List<Cand>();
            public readonly RuleStats Stats = new RuleStats();
        }

        private static readonly object Sync = new object();
        private static readonly Dictionary<string, State> States = new Dictionary<string, State>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> Handled = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // 路径 → 身份串（大小＋最后写入时间）
        private static DispatcherTimer _pump;
        private static long _startedAtTicks;

        internal static bool IsRunning { get { lock (Sync) return _pump != null; } }

        internal static DateTime Now()
        {
            var fake = Clock;
            return fake != null ? fake() : DateTime.Now;
        }

        private static long NowMs() { return (Now().Ticks - _startedAtTicks) / TimeSpan.TicksPerMillisecond; }

        /// <summary>
        /// 按当前规则集重建监视（增删改规则、开关规则之后都调这一句）。
        /// 73 号令：全功能免费开放，这里不再有解锁状态判断——启用中的规则一律起观察者。
        /// </summary>
        internal static void Apply(IEnumerable<WatchRule> rules)
        {
            lock (Sync)
            {
                StopWatchersOnly();
                if (rules == null) return;
                foreach (WatchRule r in rules)
                {
                    if (r == null || !r.Enabled || string.IsNullOrWhiteSpace(r.Folder)) continue;
                    if (WatchRules.CheckFolder(r.Folder, rules, r.Id) != WatchRules.FolderProblem.None) continue;   // 目录已不存在/被保护 ⇒ 不监视（页面里另有提示）
                    var st = new State { Rule = r };
                    try
                    {
                        st.Watcher = new FileSystemWatcher(r.Folder)
                        {
                            IncludeSubdirectories = r.IncludeSubfolders,
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                            EnableRaisingEvents = true
                        };
                        st.Watcher.Created += (s, e) => OnEvent(st, e.FullPath);
                        st.Watcher.Renamed += (s, e) => OnEvent(st, e.FullPath);
                        st.Watcher.Error += (s, e) => FileLogger.Write("监视「" + WatchRules.DisplayNameOf(r) + "」观察者报错：" + (e.GetException() == null ? "" : e.GetException().Message));
                    }
                    catch (Exception ex)
                    {
                        FileLogger.Write("监视「" + WatchRules.DisplayNameOf(r) + "」起不来（已跳过这一条）：" + ex.Message);
                        continue;
                    }
                    States[r.Id] = st;
                }
                if (States.Count == 0) { _pump = null; return; }
                _startedAtTicks = Now().Ticks;
                if (_pump == null)
                {
                    _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PumpMilliseconds) };
                    _pump.Tick += (s, e) => { Pump(); PumpMoves(); };   // 同一拍里先看新文件，再处理"打印后移动"
                }
                _pump.Start();
            }
        }

        /// <summary>主窗 Loaded 之后调这一句（GateMode 挡掉，自检直接调 <see cref="Apply"/>）；沿用 AutoUpdate 的幂等口径。</summary>
        internal static void StartFromMain(Func<bool> busyProbe, Func<WatchRule, string, bool> fire)
        {
            if (App.GateMode) return;
            lock (Sync)
            {
                Busy = busyProbe ?? Busy;
                Fire = fire ?? Fire;
            }
            Apply(WatchRuleStore.Load());
        }

        internal static void Stop()
        {
            lock (Sync)
            {
                StopWatchersOnly();
                States.Clear();
                Handled.Clear();
            }
        }

        private static void StopWatchersOnly()
        {
            foreach (State st in States.Values)
            {
                try { if (st.Watcher != null) { st.Watcher.EnableRaisingEvents = false; st.Watcher.Dispose(); } } catch { }
            }
            States.Clear();
            try { if (_pump != null) _pump.Stop(); } catch { }
        }

        /// <summary>读数快照（监视页展示与自检取用）。</summary>
        internal static RuleStats StatsOf(string ruleId)
        {
            lock (Sync)
            {
                State st;
                return States.TryGetValue(ruleId ?? "", out st) ? st.Stats : new RuleStats();
            }
        }

        internal static int PendingCount(string ruleId)
        {
            lock (Sync)
            {
                State st;
                return States.TryGetValue(ruleId ?? "", out st) ? st.Pending.Count : 0;
            }
        }

        internal static int TrackedRuleCount { get { lock (Sync) return States.Count; } }

        // ===== 事件入口（watcher 线程上只做一件事：进候选表） =====

        private static void OnEvent(State st, string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                lock (Sync)
                {
                    if (st.Pending.Any(c => string.Equals(c.Path, path, StringComparison.OrdinalIgnoreCase))) return;   // 同一文件已在候选里
                    long size = SafeSize(path);
                    long now = NowMs();
                    st.Pending.Add(new Cand { Path = path, Size = size, LastChangeMs = now, FirstMs = now });
                }
            }
            catch (Exception ex) { FileLogger.Write("监视事件处理异常（已忽略这一条）：" + ex.Message); }
        }

        private static long SafeSize(string path)
        {
            try { return new FileInfo(path).Length; } catch { return -1; }
        }

        // ===== 泵：稳定性判定 → 时段 → 交给主窗 =====

        internal static void Pump()
        {
            List<KeyValuePair<string, string>> toFire = new List<KeyValuePair<string, string>>();
            List<string> toMove = new List<string>();
            List<string> windowSkips = new List<string>();
            List<string> typeSkips = new List<string>();
            List<string> timeouts = new List<string>();
            Dictionary<string, WatchRule> ruleOf = new Dictionary<string, WatchRule>(StringComparer.Ordinal);

            lock (Sync)
            {
                bool busy = Busy != null && Busy();
                foreach (State st in States.Values)
                {
                    ruleOf[st.Rule.Id] = st.Rule;
                    for (int i = st.Pending.Count - 1; i >= 0; i--)
                    {
                        Cand c = st.Pending[i];
                        if (!File.Exists(c.Path)) { st.Pending.RemoveAt(i); continue; }     // 文件已被用户移走/改名
                        long size = SafeSize(c.Path);
                        long now = NowMs();
                        if (size != c.Size) { c.Size = size; c.LastChangeMs = now; continue; }   // 还在写：重置静默计时
                        if (!WatchRules.VerdictReady(c.Size, size, now - c.LastChangeMs, WatchRules.QuietMilliseconds, WatchRules.CanOpenExclusive(c.Path)))
                        {
                            if (now - c.FirstMs > MaxWaitMilliseconds) { timeouts.Add(st.Rule.Id + "|" + c.Path); st.Pending.RemoveAt(i); }
                            continue;                                                        // 没写完/打不开：下一拍再查
                        }
                        if (!WatchRules.MatchesType(c.Path, st.Rule.Types))
                        { typeSkips.Add(st.Rule.Id + "|" + c.Path); st.Pending.RemoveAt(i); continue; }
                        if (IsAlreadyHandled(c.Path)) { st.Pending.RemoveAt(i); continue; }
                        if (!WatchRules.InWindow(Now(), st.Rule.AllDay, st.Rule.StartTime, st.Rule.EndTime))
                        { windowSkips.Add(st.Rule.Id + "|" + c.Path); st.Pending.RemoveAt(i); continue; }
                        if (busy) break;                                                     // 主窗在打印：这一条留到下一拍
                        st.Pending.RemoveAt(i);
                        MarkHandled(c.Path, size);
                        st.Stats.Fired++;
                        st.Stats.LastFile = FileName(c.Path);
                        toFire.Add(new KeyValuePair<string, string>(st.Rule.Id, c.Path));
                        if (st.Rule.MoveAfterPrint) toMove.Add(st.Rule.Id + "|" + c.Path);
                    }
                }
                foreach (string s in windowSkips) { State st = StateOf(s.Split('|')[0]); if (st != null) { st.Stats.SkippedWindow++; st.Stats.LastFile = FileName(s.Split('|')[1]); } }
                foreach (string s in typeSkips) { State st = StateOf(s.Split('|')[0]); if (st != null) st.Stats.SkippedType++; }
                foreach (string s in timeouts) { State st = StateOf(s.Split('|')[0]); if (st != null) st.Stats.TimedOut++; }
            }

            foreach (var kv in toFire)
            {
                WatchRule r;
                if (ruleOf.TryGetValue(kv.Key, out r)) RaiseFire(r, kv.Value);
            }
            foreach (string s in timeouts)
                FileLogger.Write("监视：文件等了 30 分钟仍没写完或一直被打不开，已放弃这一条：" + FileName(s.Split('|')[1]));
            foreach (string s in windowSkips)
                FileLogger.Write("监视：「" + FileName(s.Split('|')[1]) + "」落在生效时段外，未打印（要改时段到「监视」页调整）");
            foreach (string s in typeSkips)
                FileLogger.Write("监视：「" + FileName(s.Split('|')[1]) + "」不在本规则的文件类型里或应用不支持该格式，已跳过");

            // 打印后动作：等主窗空了、文件也释放了再移动（移动失败就原地保留并记日志——绝不删除）
            foreach (string s in toMove)
            {
                string[] parts = s.Split('|');
                WatchRule r;
                if (ruleOf.TryGetValue(parts[0], out r)) ScheduleMove(r, parts[1]);
            }
        }

        private static void RaiseFire(WatchRule r, string path)
        {
            var fire = Fire;
            if (fire == null) return;
            try { fire(r, path); }
            catch (Exception ex) { FileLogger.Write("监视：交给打印队列失败（" + FileName(path) + "）：" + ex.Message); }
        }

        private static State StateOf(string id)
        {
            lock (Sync)   // Monitor 可重入：Pump 里已持锁时调用也安全；PumpMoves 那条路径不持锁，这里补上
            {
                State st;
                return States.TryGetValue(id ?? "", out st) ? st : null;
            }
        }

        private static string FileName(string p)
        {
            try { return Path.GetFileName(p ?? ""); } catch { return p ?? ""; }
        }

        // ===== 去重（同一路径＋同一身份只打一次；用户重新放一个同名新文件会再打） =====

        private static string HandledIdentity(WatchRule r, string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists) return null;
                return fi.Length + "@" + fi.LastWriteTimeUtc.Ticks;
            }
            catch { return null; }
        }

        private static bool IsAlreadyHandled(string path)
        {
            string old;
            if (!Handled.TryGetValue(path, out old)) return false;
            string now = HandledIdentity(null, path);
            return now != null && string.Equals(old, now, StringComparison.Ordinal);
        }

        private static void MarkHandled(string path, long size)
        {
            if (Handled.Count >= HandledCap)
            {
                var kill = Handled.Keys.Take(Handled.Count / 2).ToList();
                foreach (string k in kill) Handled.Remove(k);
            }
            Handled[path] = size + "@" + SafeWriteUtc(path).Ticks;
        }

        private static DateTime SafeWriteUtc(string path)
        {
            try { return File.GetLastWriteTimeUtc(path); } catch { return DateTime.MinValue; }
        }

        // ===== 打印后动作：移动到「已完成」子文件夹（永不删除） =====

        private static readonly List<KeyValuePair<WatchRule, string>> MoveQueue = new List<KeyValuePair<WatchRule, string>>();

        private static void ScheduleMove(WatchRule r, string path)
        {
            lock (Sync)
            {
                if (!MoveQueue.Any(x => x.Key == r && string.Equals(x.Value, path, StringComparison.OrdinalIgnoreCase)))
                    MoveQueue.Add(new KeyValuePair<WatchRule, string>(r, path));
            }
        }

        /// <summary>
        /// 移动队列的一拍（主窗空了才动）：目标＝规则目录下的「已完成」子文件夹；同名自动加序号；
        /// <b>任何失败都只记日志、原文件留在原地</b>（红线：不做删除，也不因为搬不动就把任务判死）。
        /// </summary>
        internal static void PumpMoves()
        {
            List<KeyValuePair<WatchRule, string>> snapshot;
            lock (Sync) snapshot = new List<KeyValuePair<WatchRule, string>>(MoveQueue);
            if (snapshot.Count == 0) return;
            if (Busy != null && Busy()) return;
            foreach (var kv in snapshot)
            {
                WatchRule r = kv.Key;
                string path = kv.Value;
                if (!File.Exists(path)) { lock (Sync) MoveQueue.Remove(kv); continue; }
                if (!WatchRules.CanOpenExclusive(path)) continue;      // 还在被占用：下一拍再试
                string moved = TryMove(r, path);
                lock (Sync) MoveQueue.Remove(kv);
                State st = StateOf(r.Id);
                if (st != null)
                {
                    if (moved != null) { st.Stats.Moved++; }
                    if (moved != path) st.Stats.LastFile = FileName(path);
                }
            }
        }

        private static string TryMove(WatchRule r, string path)
        {
            try
            {
                var probe = MoveProbe;
                if (probe != null) { probe(r, path); return path; }
                string dir = Path.Combine(r.Folder, WatchRules.DoneFolderName);
                Directory.CreateDirectory(dir);
                string target = Path.Combine(dir, Path.GetFileName(path));
                int n = 2;
                while (File.Exists(target))
                {
                    string stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
                    target = Path.Combine(dir, stem + " (" + n + ")" + ext);
                    n++;
                }
                File.Move(path, target);
                FileLogger.Write("监视：「" + Path.GetFileName(path) + "」已按规则「" + WatchRules.DisplayNameOf(r) + "」移动到 " + target);
                return target;
            }
            catch (Exception ex)
            {
                FileLogger.Write("监视：移动「" + FileName(path) + "」没成功，文件已原地保留（本功能不删除任何文件）：" + ex.Message);
                return path;
            }
        }

        /// <summary>
        /// 自检直取：这个路径的<b>当前身份</b>（大小＋最后写入时间）是否已经在"已处理"表里——
        /// 在＝同一份文件已经打过一次，不该再打；换一版同名新内容＝身份变了，会再打。
        /// </summary>
        internal static bool HandledNow(string path)
        {
            lock (Sync)
            {
                string old;
                return path != null && Handled.TryGetValue(path, out old)
                    && string.Equals(old, HandledIdentity(null, path), StringComparison.Ordinal);
            }
        }

        /// <summary>自检直取：按"打印后动作"把文件搬进「已完成」（走产品同一条 <see cref="TryMove"/>，撞名自动加序号；文件不存在就原样返回、不抛）。</summary>
        internal static string MoveToForTest(WatchRule rule, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return path;
            return TryMove(rule, path);
        }

        /// <summary>自检用：把内部状态清干净（不影响产品行为）。</summary>
        internal static void ResetForTest()
        {
            lock (Sync)
            {
                StopWatchersOnly();
                States.Clear();
                Handled.Clear();
                MoveQueue.Clear();
                Busy = null; Fire = null; Clock = null; MoveProbe = null;
            }
        }
    }
}
