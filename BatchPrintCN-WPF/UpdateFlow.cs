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
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BatchPrintCN
{
    /// <summary>
    /// 66 号令 T1：更新状态机——**主窗左下角橙色按钮的可见性只由这里派生**（不另设第二份状态，也不钉颜色字面量）。
    /// 语义（需求方 2026-10-11 拍板）：
    /// ①「发现更新」有两条入口——自动检查（受设置页「自动检查更新」开关与 24 小时闸门约束）与关于页手动检查（用户明确要的结果），
    ///   两条都调 <see cref="ReportFound"/>，所以关掉自动检查后手动查到仍会出按钮；
    /// ②弹窗里点「取消」**只关窗、不清状态**（按钮保留，下次启动若仍检测到更新会再出现）；
    /// ③只有"安装已交给安装程序"才 <see cref="ClearForInstall"/> 让按钮消失。
    /// </summary>
    public static class UpdateState
    {
        /// <summary>状态变化广播（主窗订阅后刷新按钮可见性）。参数无意义，取状态请直接读属性。</summary>
        public static event Action Changed;

        /// <summary>本次会话要提示的那一条（null＝无更新可提示）。</summary>
        public static UpdateChecker.Entry Pending { get; private set; }

        /// <summary>是否正在下载（下载中不允许再开第二个下载，也不重复提示气泡）。</summary>
        public static bool Downloading { get; private set; }

        /// <summary>橙色按钮该不该出现——**唯一判据**：有待更新的条目且没在安装中。</summary>
        public static bool ButtonVisible { get { return Pending != null && !Installing; } }

        /// <summary>已把安装交给 Setup（本会话不再提示）。</summary>
        public static bool Installing { get; private set; }

        public static void ReportFound(UpdateChecker.Entry e)
        {
            if (e == null || Installing) return;
            Pending = e;
            Raise();
        }

        public static void SetDownloading(bool v) { Downloading = v; Raise(); }

        /// <summary>安装已启动：按钮消失（会话内不再提示），并留下日志一行便于回溯。</summary>
        public static void ClearForInstall()
        {
            Pending = null; Installing = true; Downloading = false;
            Raise();
        }

        /// <summary>自检收尾用：把状态清干净并广播（不广播的话界面会留着上一段"按钮亮着"的假象）。</summary>
        internal static void ResetForTest()
        {
            Pending = null; Downloading = false; Installing = false;
            Raise();
        }

        private static void Raise() { var h = Changed; if (h != null) h(); }
    }

    /// <summary>
    /// 66 号令 T2：更新包下载器。**只下到 %TEMP%**（不落用户目录、不需要新设置键），文件名固定
    /// `PrintPile-update-&lt;版本&gt;.&lt;exe|zip&gt;`；版本串先做白字符净化再拼路径——清单被篡改写成 `..\..\x` 也越不出去。
    /// 三条铁律：①下载前再过一次 <see cref="UpdateChecker.IsAllowedUrl"/>（点按钮那一刻清单可能已被换掉，48 号修 2 口径）；
    /// ②超时 8 分钟（大文件，与清单检查的 8 秒是两个量级）；③取消或失败**必删半截文件**，绝不留给用户一个"看着能装"的残包。
    /// 自检走 <see cref="FetchOverride"/> 注入假流：不打真网络，进度回调与字节数都能逐数核对。
    /// </summary>
    public static class UpdateDownloader
    {
        /// <summary>下载超时（秒）：8 分钟。</summary>
        public const int TimeoutSeconds = 8 * 60;

        /// <summary>进度快照（Total 未知时为 -1，界面按"已下载 X／大小未知"显示）。</summary>
        public sealed class Progress
        {
            public long Received;
            public long Total;
            public double Percent { get { return PercentOf(Received, Total); } }
            public string TempPath;
        }

        public sealed class Outcome
        {
            public bool Ok;
            public string Path;
            public string Error;
            public UpdateChecker.FailKind Fail;
            public bool Cancelled;
            public static Outcome Success(string p) { return new Outcome { Ok = true, Path = p }; }
            public static Outcome Failed(UpdateChecker.FailKind k, string err) { return new Outcome { Ok = false, Fail = k, Error = err }; }
            public static Outcome Canceled(string err) { return new Outcome { Ok = false, Cancelled = true, Error = err }; }
        }

        /// <summary>自检注入点：非 null 时代替真实 HTTP 下载（假流按同样口径回调进度）。</summary>
        internal static Func<string, IProgress<Progress>, CancellationToken, Task<Outcome>> FetchOverride;

        /// <summary>自检/真机共用：URL 是否允许下载（再过白名单，空 URL 直接拒）。</summary>
        public static bool UrlAllowed(string url) { return UpdateChecker.IsAllowedUrl(url); }

        /// <summary>百分比（纯函数，自检逐数核对）：总量未知或非正 → 0；上限钉 100。</summary>
        public static double PercentOf(long received, long total)
        {
            if (total <= 0 || received < 0) return 0;
            double p = received * 100.0 / total;
            return p > 100 ? 100 : p;
        }

        /// <summary>版本号净化：只留数字与点（清单里的 version 是不可信输入，防路径穿越与奇怪文件名）。</summary>
        public static string SafeVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return "unknown";
            var sb = new System.Text.StringBuilder(version.Length);
            foreach (char c in version) { if (char.IsDigit(c) || c == '.') sb.Append(c); }
            string s = sb.ToString().Trim('.');
            return string.IsNullOrEmpty(s) ? "unknown" : s;
        }

        /// <summary>临时文件完整路径（扩展名按 URL 尾巴判：.zip→zip，其余一律 exe）。</summary>
        public static string TempPathFor(UpdateChecker.Entry e)
        {
            string url = e == null ? null : e.Url;
            bool isZip = !string.IsNullOrEmpty(url) && url.Split('?')[0].EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            return Path.Combine(Path.GetTempPath(), "PrintPile-update-" + SafeVersion(e == null ? null : e.Version) + (isZip ? ".zip" : ".exe"));
        }

        /// <summary>字节数成人话（弹窗那行"已下载 X／Y"用）。</summary>
        public static string SizeText(long bytes)
        {
            if (bytes < 0) return "?";
            if (bytes >= 1024L * 1024 * 1024) return (bytes / 1073741824.0).ToString("0.0") + " GB";
            if (bytes >= 1024L * 1024) return (bytes / 1048576.0).ToString("0.0") + " MB";
            if (bytes >= 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            return bytes + " B";
        }

        /// <summary>
        /// 下载一条更新。进度按 <paramref name="progress"/> 回调（含 0% 与 100%）；
        /// 取消/异常一律删临时文件后返回失败结果——**不抛异常到界面**（48 号口径：原文只进日志）。
        /// </summary>
        public static async Task<Outcome> RunAsync(UpdateChecker.Entry e, IProgress<Progress> progress, CancellationToken ct)
        {
            if (e == null || !UrlAllowed(e.Url))
            {
                FileLogger.Write("更新下载被拒：URL 不在白名单内（" + (e == null ? "(空条目)" : e.Url) + "）");
                return Outcome.Failed(UpdateChecker.FailKind.Network, "url-not-allowed");
            }
            string path = TempPathFor(e);
            var fake = FetchOverride;
            if (fake != null)
            {
                try
                {
                    var o = await fake(e.Url, progress, ct).ConfigureAwait(false);
                    if (o != null && o.Ok) o.Path = path;
                    if (o != null && !o.Ok && !o.Cancelled) o.Path = null;
                    return o ?? Outcome.Failed(UpdateChecker.FailKind.Network, "no-result");
                }
                catch (OperationCanceledException) { TryDelete(path); return Outcome.Canceled("canceled"); }
                catch (Exception ex) { TryDelete(path); return Outcome.Failed(UpdateChecker.Classify(ex), ex.Message); }
            }
            try
            {
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) })
                using (var resp = await http.GetAsync(e.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength.HasValue ? resp.Content.Headers.ContentLength.Value : -1L;
                    long received = 0;
                    Report(progress, 0, total, path);
                    using (var src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        var buf = new byte[8192];
                        int read;
                        while ((read = await src.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false)) > 0)
                        {
                            await dst.WriteAsync(buf, 0, read, ct).ConfigureAwait(false);
                            received += read;
                            Report(progress, received, total, path);
                        }
                        await dst.FlushAsync(ct).ConfigureAwait(false);
                    }
                    Report(progress, received, total <= 0 ? received : total, path);
                    FileLogger.Write("更新包下载完成：" + path + "（" + SizeText(received) + "）");
                    return Outcome.Success(path);
                }
            }
            catch (OperationCanceledException) { TryDelete(path); return Outcome.Canceled("canceled"); }
            catch (Exception ex)
            {
                TryDelete(path);
                FileLogger.Write("更新下载失败：" + ex.Message);
                return Outcome.Failed(UpdateChecker.Classify(ex), ex.Message);
            }
        }

        private static void Report(IProgress<Progress> p, long received, long total, string path)
        {
            if (p == null) return;
            try { p.Report(new Progress { Received = received, Total = total, TempPath = path }); } catch { }
        }

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { FileLogger.Write("更新下载：临时文件没删掉（" + path + "）：" + ex.Message); }
        }

        /// <summary>自检取用：取消/失败后确认临时文件确实不在了。</summary>
        internal static bool TempExists(UpdateChecker.Entry e) { return File.Exists(TempPathFor(e)); }
    }

    /// <summary>
    /// 66 号令 T3：安装分叉。安装版与便携版的判据＝**运行目录里有 unins000.exe**（Inno 卸载器，安装版独有），
    /// 再看 HKCU 卸载项里本产品的 AppId 键（便携版跑在只读目录、或用户手工拷走时兜底）。
    /// 两条探针都写成可注入/可单测的纯函数，自检不碰注册表也不起真进程。
    /// AppId 与 `06-脚本与工具\脚本\PrintPile-Setup.iss` 里的 AppId 必须一致（改一处必改另一处）。
    /// </summary>
    public static class UpdateInstaller
    {
        /// <summary>Inno 的 AppId（一次性生成、长期复用；64 号 B3 定的那枚 GUID）。</summary>
        public const string AppId = "{30253F37-6E3C-4CA1-BD52-0A4D31DDC8BF}";

        /// <summary>自检注入点：非 null 时代替"真的把安装程序拉起来"，参数＝安装包路径。</summary>
        internal static Func<string, bool> LaunchOverride;

        /// <summary>自检注入点：非 null 时代替"真的打开资源管理器"，参数＝要选中并显示的文件路径。</summary>
        internal static Func<string, bool> OpenFolderOverride;

        /// <summary>探针①：运行目录（含上一级）是否存在 unins000.exe。</summary>
        public static bool HasUninstallerFile(string baseDir)
        {
            try
            {
                if (string.IsNullOrEmpty(baseDir)) return false;
                if (File.Exists(Path.Combine(baseDir, "unins000.exe"))) return true;
                var up = Directory.GetParent(baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return up != null && File.Exists(Path.Combine(up.FullName, "unins000.exe"));
            }
            catch { return false; }
        }

        /// <summary>探针②：HKCU 卸载项里有没有本产品 AppId 的键（Inno 在 lowest 权限下写 HKCU）。</summary>
        public static bool HasRegistryUninstallEntry()
        {
            try
            {
                using (var root = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                {
                    if (root == null) return false;
                    string want = AppId.Trim('{', '}') + "_is1";
                    foreach (string name in root.GetSubKeyNames())
                    {
                        if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            catch (Exception ex) { FileLogger.Write("更新：读卸载注册表项失败（按未安装处理）：" + ex.Message); }
            return false;
        }

        /// <summary>是不是"安装版"在跑（判据写进回执：文件探针为主，注册表为辅）。</summary>
        public static bool LooksInstalled()
        {
            return HasUninstallerFile(AppContext.BaseDirectory) || HasRegistryUninstallEntry();
        }

        /// <summary>拉起安装程序（/SILENT＝用户仍能看到安装向导进度条，只是不问路径与选项）。</summary>
        public static bool LaunchSetup(string path)
        {
            var fake = LaunchOverride;
            if (fake != null) return fake(path);   // 自检注入点在最前：假下载流不会真落盘，文件存在性检查留给真机分支
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                FileLogger.Write("更新：安装包不在，起不了安装程序（" + path + "）");
                return false;
            }
            try
            {
                Process.Start(new ProcessStartInfo(path, "/SILENT") { UseShellExecute = true });
                FileLogger.Write("更新：已启动安装程序 " + path + " /SILENT");
                return true;
            }
            catch (Exception ex) { FileLogger.Write("更新：启动安装程序失败：" + ex.Message); return false; }
        }

        /// <summary>便携版：打开所在文件夹并选中下载的 zip（explorer 的 /select 需要绝对路径）。</summary>
        public static bool OpenFolderSelecting(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var fake = OpenFolderOverride;
            if (fake != null) return fake(path);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                return true;
            }
            catch (Exception ex) { FileLogger.Write("更新：打开所在文件夹失败：" + ex.Message); return false; }
        }
    }
}
