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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace BatchPrintCN
{
    /// <summary>
    /// 72 号令（V1.1.0·F7）：一条监视规则＝"哪个文件夹里的什么文件，在什么时段，打到哪台打印机，按哪个模板，打完怎么处理"。
    /// 全部是<b>属性</b>（不是公开字段）——本类走 System.Text.Json 默认配置直接序列化到 <c>data\watch-rules.json</c>，
    /// 与 settings.json 无关（<b>不新增设置键</b>，键账零变化，见 72 号令键账纪律）。
    /// </summary>
    public sealed class WatchRule
    {
        public string Id { get; set; }                // Guid("N")，创建后不变（编辑靠它定位）
        public string Name { get; set; }              // 规则名；空＝显示时用文件夹目录名兜
        public string Folder { get; set; }            // 绝对路径
        public bool IncludeSubfolders { get; set; }   // 含子文件夹
        public string Types { get; set; }             // "*.pdf;*.docx"；空＝应用支持的全部打印格式
        public bool AllDay { get; set; }              // 全天生效（默认 true）
        public string StartTime { get; set; }         // "HH:mm"，AllDay=false 时生效
        public string EndTime { get; set; }           // "HH:mm"，允许跨零点（22:00–06:00）
        public string Printer { get; set; }           // 空＝用当前默认/上次选择的打印机
        public string TemplateName { get; set; }      // 空＝不套模板（跟随全局参数）
        public bool MoveAfterPrint { get; set; }      // 打印后移动到「已完成」子文件夹；默认 false＝保留原文件
        public bool Enabled { get; set; }             // 独立启停

        public static WatchRule New(string folder)
        {
            return new WatchRule
            {
                Id = Guid.NewGuid().ToString("N"),
                Folder = folder,
                Name = "",
                Types = "",
                AllDay = true,
                StartTime = "08:00",
                EndTime = "18:00",
                Printer = "",
                TemplateName = "",
                MoveAfterPrint = false,
                Enabled = true
            };
        }

        /// <summary>显示名：规则名 → 文件夹目录名 → "(未命名)"（最后一档取资源键，见 WatchRules.DisplayNameOf）。</summary>
        public string Display { get { return string.IsNullOrWhiteSpace(Name) ? SafeDirName(Folder) : Name.Trim(); } }

        private static string SafeDirName(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return "";
                string f = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.GetFileName(f);
            }
            catch { return ""; }
        }
    }

    /// <summary>
    /// 72 号令：规则集合的<b>纯函数</b>判据（时段／类型／稳定性／目录保护）＋规则名显示与模板转覆盖。
    /// 全部不读不写任何外部状态，自检逐档直取（复利 M-20261011-01 的"判据写成纯函数"口径）。
    /// </summary>
    internal static class WatchRules
    {
        /// <summary>「已完成」子文件夹名（打印后动作唯一落点；<b>本功能不删除任何用户文件</b>，移动失败就原地保留并告警）。</summary>
        public const string DoneFolderName = "已完成";

        /// <summary>稳定性判定：文件大小连续两次采样不变、且静默这么久（毫秒）以上，才算"写完"。</summary>
        public const int QuietMilliseconds = 700;

        /// <summary>
        /// 时段过滤（纯函数）：全天＝恒真；起止任一解析不出来＝<b>按全天放行并交给调用方记日志</b>（宁可多打一次，也不静默吞掉用户的文件）；
        /// 起止相等＝全天；跨零点（起 &gt; 止，如 22:00–06:00）＝取补集。
        /// </summary>
        internal static bool InWindow(DateTime now, bool allDay, string start, string end)
        {
            if (allDay) return true;
            int s, e;
            if (!TryMinute(start, out s) || !TryMinute(end, out e)) return true;
            if (s == e) return true;
            int m = now.Hour * 60 + now.Minute;
            return s < e ? (m >= s && m < e) : (m >= s || m < e);
        }

        /// <summary>时段格式校验（规则编辑对话框在"非全天"时把门用）：能按 HH:mm 解析才算数。</summary>
        internal static bool IsValidTime(string hhmm)
        {
            int m;
            return TryMinute(hhmm, out m);
        }

        private static bool TryMinute(string hhmm, out int minute)
        {
            minute = 0;
            if (string.IsNullOrWhiteSpace(hhmm)) return false;
            string[] parts = hhmm.Trim().Split(':');
            int h, mi;
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out h)) return false;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out mi)) return false;
            if (h < 0 || h > 23 || mi < 0 || mi > 59) return false;
            minute = h * 60 + mi;
            return true;
        }

        /// <summary>文件类型模式 → 小写扩展名集合（支持 "*.pdf;*.docx"、".pdf,.txt"、"pdf"；通配符 * 与空白忽略）。</summary>
        internal static List<string> TypeList(string types)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(types)) return list;
            foreach (string raw in types.Split(new[] { ';', ',', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = raw.Trim().ToLowerInvariant();
                if (t == "*" || t == "*.*" || t == ".") continue;      // 裸通配符＝不限类型（与留空同一义，不进名单当扩展名）
                if (t.StartsWith("*.", StringComparison.Ordinal)) t = t.Substring(1);
                else if (t.Length > 0 && t[0] != '.') t = "." + t;
                if (t.Length > 1 && !list.Contains(t)) list.Add(t);
            }
            return list;
        }

        /// <summary>
        /// 类型匹配（纯函数）：模式为空＝应用支持的全部打印格式（交给 FileClassifier 判，含魔数校验）；
        /// 否则扩展名要在名单里<b>且</b>应用确实支持这一类（名单里写了 .exe 也不会去打）。
        /// </summary>
        internal static bool MatchesType(string path, string types)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            List<string> list = TypeList(types);
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (list.Count > 0 && !list.Contains(ext)) return false;
            return FileClassifier.IsSupported(path);
        }

        /// <summary>
        /// 稳定性判定（纯函数）：两次采样大小一致 ＋ 已静默够久 ＋ 能独占打开 ⇒ 可以入队。
        /// 任何一项不满足都不入队（下一拍再查）——这是"绝不打印写了一半的文件"这条红线的唯一执行点。
        /// </summary>
        internal static bool VerdictReady(long sizePrev, long sizeNow, double elapsedMs, int quietMs, bool exclusiveOpen)
        {
            if (sizePrev != sizeNow) return false;
            if (elapsedMs < quietMs) return false;
            return exclusiveOpen;
        }

        /// <summary>独占打开试探（真文件用；自检走 <see cref="OpenProbeOverride"/> 桩，不碰磁盘）。</summary>
        internal static Func<string, bool> OpenProbeOverride = null;

        internal static bool CanOpenExclusive(string path)
        {
            var fake = OpenProbeOverride;
            if (fake != null) return fake(path);
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                { return true; }
            }
            catch (IOException) { return false; }        // 还被写者占用
            catch (UnauthorizedAccessException) { return false; }
            catch { return false; }
        }

        /// <summary>目录保护判定结果（None＝放行；其余＝拒绝，值即资源键）。</summary>
        internal enum FolderProblem { None, Empty, Missing, InsideSelf, Nested, TooLong }

        /// <summary>对应资源键（页面与对话框都从这里取文案，中英成对）。</summary>
        internal static string ProblemKey(FolderProblem p)
        {
            switch (p)
            {
                case FolderProblem.Empty: return "s.WatchErrEmpty";
                case FolderProblem.Missing: return "s.WatchErrMissing";
                case FolderProblem.InsideSelf: return "s.WatchErrSelfDir";
                case FolderProblem.Nested: return "s.WatchErrNested";
                case FolderProblem.TooLong: return "s.WatchErrTooLong";
                default: return "";
            }
        }

        /// <summary>
        /// 目录保护（纯函数，逐档断言）：空／不存在或不是目录／路径过长／落在程序自身目录内（含 data\、config\、logs\ 与 exe 所在目录本身）
        /// ／与已有规则<b>互为嵌套</b>（同一路径、或谁是谁的子目录——否则同一个文件会被两条规则各打一次）。
        /// </summary>
        internal static FolderProblem CheckFolder(string folder, IEnumerable<WatchRule> others, string selfId)
        {
            if (string.IsNullOrWhiteSpace(folder)) return FolderProblem.Empty;
            string f = folder.Trim().TrimEnd('\\', '/');
            if (f.Length > 240) return FolderProblem.TooLong;
            try
            {
                if (!Directory.Exists(f)) return FolderProblem.Missing;
            }
            catch { return FolderProblem.Missing; }

            string norm = Normalize(f);
            string root = Normalize(AppPaths.Root ?? "");
            // 程序自身目录：等于根、在根之下、或根在它之下（选到盘符根这种极端情况也一并拒掉，监视整盘风险太大）
            if (string.Equals(norm, root, StringComparison.OrdinalIgnoreCase)
                || norm.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)
                || root.StartsWith(norm + "\\", StringComparison.OrdinalIgnoreCase)) return FolderProblem.InsideSelf;

            if (others != null)
            {
                foreach (WatchRule r in others)
                {
                    if (r == null || string.Equals(r.Id, selfId, StringComparison.Ordinal)) continue;
                    string o = Normalize((r.Folder ?? "").Trim());
                    if (o.Length == 0) continue;
                    if (string.Equals(norm, o, StringComparison.OrdinalIgnoreCase)
                        || norm.StartsWith(o + "\\", StringComparison.OrdinalIgnoreCase)
                        || o.StartsWith(norm + "\\", StringComparison.OrdinalIgnoreCase)) return FolderProblem.Nested;
                }
            }
            return FolderProblem.None;
        }

        private static string Normalize(string p)
        {
            return (p ?? "").Trim().TrimEnd('\\', '/').Replace('/', '\\');
        }

        /// <summary>
        /// 规则绑定的模板 → 逐文件覆盖（<see cref="PerFileOverride"/>）。
        /// 走这条既有通道的原因：<b>打印执行链路一行都不能改</b>（72 号 Rule 3），而 EffectiveOptions.Resolve 会把覆盖合并成生效参数，
        /// 与手动"逐文件改参数"是同一条解析路径，不留第二条实现。
        /// 这里把模板里<b>每一项</b>都写成覆盖值（不留 null），所以本规则的产出与"当前全局参数"无关——
        /// 用户在全局里改了什么，不会串到监视任务上。
        /// </summary>
        internal static PerFileOverride TemplateToOverride(PrintOptions t)
        {
            if (t == null) return null;
            return new PerFileOverride
            {
                Duplex = t.Duplex,
                Color = t.Color,
                Paper = t.Paper,
                PaperDisplayName = string.IsNullOrWhiteSpace(t.PaperDisplayName) ? t.Paper.ToString() : t.PaperDisplayName,
                Orientation = t.Orientation,
                Scale = t.EffectiveScale,
                ScalePercent = t.ScalePercent,
                PagesPerSheet = t.PagesPerSheet,
                Booklet = t.Booklet,
                PageParity = t.PageParity,
                PaperSourceOverride = string.IsNullOrWhiteSpace(t.PaperSourceName) ? null : t.PaperSourceName
            };
        }

        /// <summary>去重键（同一台机器上路径大小写不敏感）。</summary>
        internal static string Key(string path) { return (path ?? "").Trim(); }

        /// <summary>规则名（空→目录名→资源键兜底），页面与日志共用一处。</summary>
        internal static string DisplayNameOf(WatchRule r)
        {
            if (r == null) return "";
            string d = r.Display;
            return string.IsNullOrWhiteSpace(d) ? WatchRules.L("s.WatchUnnamed") : d;
        }

        internal static string L(string key)
        {
            try
            {
                object o = System.Windows.Application.Current != null ? System.Windows.Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }
    }

    /// <summary>
    /// 72 号令：规则持久化——<c>data\watch-rules.json</c>（便携化口径，跟着程序目录走；卸载保留用户数据那条规矩同样适用）。
    /// 读写全程 try/catch：文件损坏＝当作"还没有规则"并把原文备份成 .bak 后重建，绝不因为读不懂而崩（也绝不静默吃掉用户写过的规则）。
    /// </summary>
    internal static class WatchRuleStore
    {
        private static readonly object Sync = new object();

        /// <summary>自检注入点：非 null 时代替 <see cref="AppPaths"/> 给出的正式路径（规则往返断言写在临时目录，不碰用户数据）。</summary>
        internal static Func<string> PathOverride = null;

        private sealed class File0 { public int Version { get; set; } public List<WatchRule> Rules { get; set; } }

        internal static string Path0
        {
            get
            {
                var fake = PathOverride;
                if (fake != null) return fake();
                return System.IO.Path.Combine(AppPaths.DataDir, "watch-rules.json");
            }
        }

        internal static List<WatchRule> Load()
        {
            lock (Sync)
            {
                try
                {
                    string p = Path0;
                    if (!File.Exists(p)) return new List<WatchRule>();
                    var dto = JsonSerializer.Deserialize<File0>(File.ReadAllText(p, System.Text.Encoding.UTF8));
                    if (dto == null || dto.Rules == null) return new List<WatchRule>();
                    return dto.Rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Folder)).ToList();
                }
                catch (Exception ex)
                {
                    TryBackupBroken();
                    FileLogger.Write("监视规则读取失败，已按空处理（原文件已备份为 watch-rules.json.bak）：" + ex.Message);
                    return new List<WatchRule>();
                }
            }
        }

        internal static bool Save(List<WatchRule> rules)
        {
            lock (Sync)
            {
                try
                {
                    string p = Path0;
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    var dto = new File0 { Version = 1, Rules = rules ?? new List<WatchRule>() };
                    string tmp = p + ".tmp";
                    File.WriteAllText(tmp, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
                    return true;
                }
                catch (Exception ex)
                {
                    FileLogger.Write("监视规则保存失败：" + ex.Message);
                    return false;
                }
            }
        }

        private static void TryBackupBroken()
        {
            try
            {
                string p = Path0, bak = p + ".bak";
                if (File.Exists(p)) File.Copy(p, bak, true);
            }
            catch { }
        }
    }
}
