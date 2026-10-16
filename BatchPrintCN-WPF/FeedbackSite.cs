using System;
using System.Diagnostics;
using System.Windows;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.9 版本内修整第二批·修C：反馈入口改为**跳官网反馈页**（架构修正）。
    /// 为什么不再由客户端直发：生产后端 batchprint-api.xmmtr.workers.dev 在中国大陆被 SNI 层封锁，
    /// 客户端 POST 在真实用户那里大概率失败（V4.9-2 的直发路径因此退役，见 FeedbackWindow/FeedbackClient 移档）；
    /// 官网 Pages 域名可达，反馈页由另一会话按 42 号建设。
    /// 本类做四件事：拼 URL（全工程唯一一份官网地址字面量）、复制到剪贴板（日志目录）、
    /// **52 号令·返修③：勾选"自动打包日志"时把日志目录打包 zip 直传站点 /api/feedback/logs 拿短期 key、
    /// URL 追加 &amp;logpkg=<key>（反馈页识别后显示"已自动附带日志包"，提交时关联到反馈单）**、用系统默认浏览器打开。
    /// 文档内容永不上传这条红线不变——自动打包的只是 logs 目录里的运行日志（勾选下方有小字说明）；
    /// 打包/上传失败一律静默降级走旧行为（跳转＋复制日志路径），失败原因只进日志与状态行。
    /// </summary>
    public static class FeedbackSite
    {
        /// <summary>官网反馈页地址（54 号令·B：不再自带字面量，基址取自 AboutLinks.SiteBase，改域名只改那一行）。</summary>
        public static readonly string UrlBase = AboutLinks.SiteBase + "/feedback.html";

        /// <summary>来源标记（官网侧据此区分"从程序内点进来"的反馈）。</summary>
        public const string SourceTag = "app";

        /// <summary>完整跳转地址：?from=app&amp;v=当前版本号（&amp;logpkg= 由 <see cref="Open"/> 按勾选态追加）。</summary>
        public static string Url()
        {
            return UrlBase + "?from=" + SourceTag + "&v=" + Uri.EscapeDataString(AppVersion.Full);
        }

        /// <summary>跳转结果（自检直取，不真开浏览器）。</summary>
        public sealed class Jump
        {
            public string Url;
            public bool BrowserLaunched;
            public bool LogPathCopied;
            public string LogDirectory;
            public string Error;
            // ===== 52 号令·返修③：自动打包日志 =====
            /// <summary>是否走了自动打包路径（勾选且尝试了打包；上传失败也 true，看 UploadError）。</summary>
            public bool AutoLogAttempted;
            /// <summary>日志包上传成功＝服务端返回的短期 key（URL 已带 logpkg）。</summary>
            public string LogPackageKey;
            /// <summary>打包/上传失败原因（只进状态行与日志，浏览器照开＝旧行为兜底）。</summary>
            public string UploadError;
        }

        /// <summary>自检注入点：替换"真开浏览器"这一步（null＝真开）。与 UpdateChecker.FetchOverride 同款口径。</summary>
        internal static Func<string, bool> OpenOverride;

        /// <summary>
        /// 执行跳转：先把日志目录路径放进剪贴板（失败不影响开网页），再按 settings 的 FeedbackAutoLogs 勾选态决定
        /// 是否打包直传日志包，最后用系统默认浏览器打开官网反馈页。
        /// 浏览器启动用 <see cref="ProcessStartInfo"/> 且 URL 作为**单个** FileName 传入（UseShellExecute=true 交给系统选默认浏览器）——
        /// 这里没有多参数拼接面（V4.9-3 那处要防的是把系统枚举出的打印机名拼进命令行），故不套 ArgumentList，
        /// 套的话只能借 cmd.exe /c start，反而多开一层 shell。口径已报备。
        /// </summary>
        public static Jump Open()
        {
            return Open(SettingsService.FeedbackAutoLogs);
        }

        /// <summary>带勾选态的跳转（真机走 settings；自检显式传，便于两分支都断）。</summary>
        public static Jump Open(bool autoLogs)
        {
            var j = new Jump { Url = Url(), LogDirectory = SafeLogDir() };
            if (!string.IsNullOrEmpty(j.LogDirectory))
            {
                // OpenClipboard 会被别的程序短暂占着（CLIPBRD_E_CANT_OPEN 是 Windows 上的常见瞬时失败），
                // 真机与自检都重试三次；全失败也只影响这句提示，不影响打开网页。
                for (int i = 0; i < 3 && !j.LogPathCopied; i++)
                {
                    try { Clipboard.SetText(j.LogDirectory); j.LogPathCopied = true; }
                    catch (Exception ex) { j.Error = ex.Message; }
                    if (!j.LogPathCopied) System.Threading.Thread.Sleep(60);
                }
            }

            // 52 号令·返修③：自动打包日志（同步等结果——URL 要带上 key 才有意义；10MB 内通常秒级）。
            if (autoLogs)
            {
                j.AutoLogAttempted = true;
                try
                {
                    LogPackager.Package pkg = LogPackager.Build(LogPackager.ResolveDirectory());
                    if (!pkg.Ok)
                    {
                        j.UploadError = pkg.Reason ?? "日志打包失败";
                        FileLogger.Write("反馈日志包：打包失败——" + j.UploadError);
                    }
                    else
                    {
                        string key = LogPackager.UploadAsync(pkg.Zip, AppVersion.Full).GetAwaiter().GetResult();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            j.LogPackageKey = key;
                            j.Url = j.Url + "&logpkg=" + Uri.EscapeDataString(key);
                            FileLogger.Write("反馈日志包：已上传（" + pkg.FileCount + " 个文件，" + pkg.Size + "B）key=" + key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    j.UploadError = "日志包上传失败：" + ex.Message;
                    FileLogger.Write("反馈日志包：上传失败（降级为旧行为）——" + ex.Message);
                }
            }

            try
            {
                Func<string, bool> ov = OpenOverride;
                if (ov != null) j.BrowserLaunched = ov(j.Url);
                else
                {
                    Process.Start(new ProcessStartInfo(j.Url) { UseShellExecute = true });
                    j.BrowserLaunched = true;
                }
            }
            catch (Exception ex) { j.Error = (j.Error == null ? "" : j.Error + "；") + ex.Message; }
            return j;
        }

        private static string SafeLogDir()
        {
            try { return FileLogger.LogDirectory; } catch { return ""; }
        }
    }
}
