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
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    public partial class AboutPage : UserControl
    {
        private bool _checkingUpdate;
        private bool _wiring;                                  // 构造期给开关/下拉赋值时不触发"改动即存"
        private UpdateChecker.Entry _found;                     // 「发现新版本」时留着那一条，供内联的「打开下载页」按钮用
        private static AboutPage _live;                         // 自动检查结果只发给最近构造的这一页（避免静态事件越攒越多）
        private static bool _autoSubscribed;

        public AboutPage()
        {
            InitializeComponent();
            _wiring = true;
            TitleText.Text = AppTitleLine();
            BuildText.Text = BuildInfoText();
            UpdateDescText.Text = VersionedDesc();
            TglAutoCheck.IsChecked = SettingsService.AutoCheckUpdate;
            CbkAutoLogs.IsChecked = SettingsService.FeedbackAutoLogs;   // 52 号令·返修③：默认勾、可关、持久化
            // 52 号令·返修②：更新渠道卡已整卡移除（不再向用户暴露 stable/preview），UpdateChannel 逻辑锁定 stable。
            _wiring = false;

            // 49 号·版2：自动检查发现新版本 → 这一页的内联结果区同步（静态事件只挂一次，转发给最近构造的实例）
            _live = this;
            if (!_autoSubscribed)
            {
                _autoSubscribed = true;
                AutoUpdate.NewVersionFound += (r, v) =>
                {
                    var p = _live;
                    if (p == null) return;
                    try { p.Dispatcher.Invoke(new Action(() => p.ShowAutoResult(r, v))); } catch { }
                };
            }

            // 标题行与构建行是代码拼的（其余文字走 DynamicResource 自动跟随），所以切语言时要重拼一次；
            // 订阅挂在 Loaded/Unloaded 上：本页实例在自检里会被反复构造，挂在构造期会越攒越多。
            Loaded += (s, e) =>
            {
                SettingsService.LanguageChanged -= OnLangChanged; SettingsService.LanguageChanged += OnLangChanged;
            };
            Unloaded += (s, e) =>
            {
                SettingsService.LanguageChanged -= OnLangChanged;
            };
        }

        private void OnLangChanged()
        {
            var d = UpdateDescText;
            if (d == null) return;
            d.Dispatcher.Invoke(new Action(() =>
            {
                try
                {
                    d.Text = VersionedDesc();
                    if (TitleText != null) TitleText.Text = AppTitleLine();
                    if (BuildText != null) BuildText.Text = BuildInfoText();
                }
                catch { }
            }));
        }

        /// <summary>版1 标题行：应用名 + 版本（如「PrintPile · 批量打印 1.0.0」；62 号·B 起品牌取自 s.AppTitle，一处改全生效）。</summary>
        internal static string AppTitleLine()
        {
            return L("s.AppTitle") + " " + AppVersion.Display;
        }

        /// <summary>
        /// 版1 构建信息行：「构建 4.9.3.0+af8a606（2026-10-03）」。
        /// 版本号取程序集 AssemblyVersion（四段，含修订号），哈希取 InformationalVersion 里 "+" 之后的前 8 位，
        /// 日期取本程序集文件的写入时间（构建那一刻）；<b>读不到 InformationalVersion 就整行空着</b>（开工令：解析失败只显示版本号不显示构建段）。
        /// </summary>
        internal static string BuildInfoText()
        {
            try
            {
                var asm = typeof(AboutPage).Assembly;
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                string date = "";
                try
                {
                    string file = asm.Location;
                    if (!string.IsNullOrWhiteSpace(file) && System.IO.File.Exists(file))
                        date = System.IO.File.GetLastWriteTime(file).ToString("yyyy-MM-dd");
                }
                catch { }
                return FormatBuildLine(info == null ? null : info.InformationalVersion, AppVersion.Full, date);
            }
            catch { return ""; }
        }

        /// <summary>
        /// 构建行的拼法（纯函数，自检逐数核对）：InformationalVersion 形如「4.9.3+af8a606a678b…」，
        /// 取 "+" 之后前 8 位当哈希，四段号（含修订号）用 AssemblyVersion；
        /// <b>InformationalVersion 读不到就返回空串</b>——开工令：解析失败只显示版本号、不显示构建段。
        /// </summary>
        internal static string FormatBuildLine(string informationalVersion, string fourPart, string date)
        {
            if (string.IsNullOrWhiteSpace(informationalVersion)) return "";
            string iv = informationalVersion.Trim();
            string head = iv, hash = "";
            int plus = iv.IndexOf('+');
            if (plus >= 0)
            {
                head = iv.Substring(0, plus);
                hash = iv.Substring(plus + 1);
                if (hash.Length > 8) hash = hash.Substring(0, 8);
            }
            string build = string.IsNullOrWhiteSpace(fourPart) ? head.TrimStart('v', 'V') : fourPart;
            string tail = string.IsNullOrEmpty(date) ? "" : "（" + date + "）";
            return L("s.AboutBuildFmt").Replace("{0}", build + (hash.Length == 0 ? "" : "+" + hash)) + tail;
        }

        /// <summary>修E：「检查更新」行说明 + 当前版本号（49 号起版本展示改由标题区与构建行承担，这一句保持原样不删条）。</summary>
        private static string VersionedDesc()
        {
            return L("s.AboutUpdateDesc") + "（" + L("s.AboutVersionNow") + " " + AppVersion.Full + "）";
        }

        /// <summary>修E 断言取用：并入说明文案后的「检查更新」行整句（含当前版本号）。</summary>
        internal string UpdateDescForTest { get { return UpdateDescText == null ? null : UpdateDescText.Text; } }
        /// <summary>修C 断言取用：跳转后本行下方那一句状态文字（未点过时为空且不可见）。</summary>
        internal string FeedbackNoteForTest { get { return FeedbackNoteText == null ? null : FeedbackNoteText.Text; } }
        internal bool FeedbackNoteShownForTest { get { return FeedbackNoteText != null && FeedbackNoteText.Visibility == Visibility.Visible; } }

        // ===== 49 号·版1：标题区与三按钮行 =====

        internal string TitleForTest { get { return TitleText == null ? null : TitleText.Text; } }
        internal string BuildLineForTest { get { return BuildText == null ? null : BuildText.Text; } }
        internal string LinkNoteForTest { get { return LinkNoteText == null ? null : LinkNoteText.Text; } }
        internal bool LinkNoteShownForTest { get { return LinkNoteText != null && LinkNoteText.Visibility == Visibility.Visible; } }

        private static string L(string key)
        {
            try
            {
                var o = Application.Current != null ? Application.Current.TryFindResource(key) : null;
                return o as string ?? key;
            }
            catch { return key; }
        }

        /// <summary>三枚按钮共用的呈现：成功与失败都只在本行下方说一句，绝不弹模态框（开工令已定）。</summary>
        internal string ShowLinkNote(AboutLinks.Outcome o, string okKey)
        {
            if (o == null) return "";
            string note = o.Ok
                ? L(okKey).Replace("{0}", (o.Created ? L("s.AboutCreatedFmt") + " " : "") + (o.Target ?? ""))
                : L("s.AboutOpenFailedFmt").Replace("{0}", o.Target ?? "");
            if (LinkNoteText != null)
            {
                LinkNoteText.Text = note;
                LinkNoteText.Visibility = Visibility.Visible;
            }
            return note;
        }

        private void OnOpenSite(object sender, RoutedEventArgs e) { PressSite(); }
        private void OnOpenDataDir(object sender, RoutedEventArgs e) { PressDataDir(); }
        private void OnOpenLog(object sender, RoutedEventArgs e) { PressLog(); }
        private void OnGitHub(object sender, RoutedEventArgs e) { PressGitHub(); }
        private void OnGitee(object sender, RoutedEventArgs e) { PressGitee(); }

        /// <summary>下面三枚就是按钮 Click 的实体，自检直接调它们（同一段代码，不复制逻辑）。
        /// 78 号令·B3：开源仓库双按钮复活（59 号随闭源口径移除的那一枚 GitHub，连同新增的 Gitee 一并回归）。</summary>
        internal string PressSite() { return ShowLinkNote(AboutLinks.OpenUrl(AboutLinks.HomeUrl), "s.AboutOpenedSite"); }
        internal string PressDataDir() { return ShowLinkNote(AboutLinks.OpenFolder(AboutLinks.DataDirectory, true), "s.AboutOpenedData"); }
        internal string PressLog() { return ShowLinkNote(AboutLinks.OpenFolder(AboutLinks.LogDirectory, true), "s.AboutOpenedLog"); }
        internal string PressGitHub() { return ShowLinkNote(AboutLinks.OpenUrl(AboutLinks.GitHubUrl), "s.AboutOpenedSite"); }
        internal string PressGitee() { return ShowLinkNote(AboutLinks.OpenUrl(AboutLinks.GiteeUrl), "s.AboutOpenedSite"); }

        /// <summary>78 号令·B3：仓库双按钮本体（断"在位"与"文字＝品牌名原文"）。</summary>
        internal Button GitHubButtonForTest { get { return BtnGitHub; } }
        internal Button GiteeButtonForTest { get { return BtnGitee; } }

        /// <summary>自检取用：三枚按钮本体（断"在位"与"文字对不对"）。</summary>
        internal Button SiteButtonForTest { get { return BtnSite; } }
        internal Button DataButtonForTest { get { return BtnData; } }
        internal Button LogButtonForTest { get { return BtnLog; } }
        internal Button CheckButtonForTest { get { return BtnCheckUpdate; } }
        internal FrameworkElement AutoSwitchForTest { get { return TglAutoCheck; } }
        /// <summary>渲染取证取用：「立即检查」那一整张卡（内联结果就长在它里面，出图＝真控件而非复刻）。</summary>
        internal FrameworkElement UpdateCardForTest { get { return UpdateCard; } }

        // ===== 49 号·版2/版3：自动检查更新开关与更新渠道 =====

        private void OnAutoCheckChanged(object sender, RoutedEventArgs e)
        {
            if (_wiring || TglAutoCheck == null) return;
            SettingsService.SetAutoCheckUpdate(TglAutoCheck.IsChecked == true);
        }

        // 52 号令·返修②：更新渠道卡与 OnChannelChanged 已随整卡移除（渠道锁定 stable，键保留读取兼容）。

        internal bool AutoCheckOnForTest { get { return TglAutoCheck != null && TglAutoCheck.IsChecked == true; } }
        internal void SetAutoCheckForTest(bool on) { if (TglAutoCheck != null) TglAutoCheck.IsChecked = on; }
        // 52 号令·返修②：ChannelBoxForTest/SetChannelForTest 已随渠道卡移除；渠道断言改判见 V493Tests（键兼容锁定 stable）。

        // ===== 49 号·版4：立即检查（内联结果，不再弹 MessageBox） =====

        /// <summary>
        /// V4.7 修整2 / 48 号修2：检查结果正文（"已是最新 / 发现新版本 / 检查更新失败"三种文案，失败按分类取中文）。
        /// 49 号起这份正文由<b>内联结果区</b>显示（原来弹 MessageBox），<b>函数本体一字未改</b>——
        /// 48 号的断言、弹窗渲染复刻断言改判后仍从这里取值。
        /// </summary>
        internal static string NoticeFor(UpdateChecker.Result r, Version current)
        {
            if (r == null) return L("s.UpdateFailedFmt");
            switch (r.Outcome)
            {
                case UpdateChecker.Outcome.Found:
                    string notes = string.IsNullOrWhiteSpace(r.Latest.Notes) ? "" : "\n" + L("s.UpdateNotesFmt").Replace("{0}", r.Latest.Notes);
                    return L("s.UpdateFoundFmt").Replace("{0}", r.Latest.Version).Replace("{1}", r.Latest.Date ?? "")
                        + notes + "\n\n" + L("s.UpdateAskDownload");
                case UpdateChecker.Outcome.Failed:
                    return r.Fail == UpdateChecker.FailKind.Timeout ? L("s.UpdateFailTimeout") : L("s.UpdateFailNetwork");
                default:   // UpToDate（含清单里没有任何可用 active 条目）
                    if (r.Unrecognized) return L("s.UpdateUnrecognized");
                    return L("s.UpdateLatestFmt").Replace("{0}", current == null ? AppVersion.Display : current.ToString(4));
            }
        }

        /// <summary>检查结果标题（三种情形同一个；49 号起内联呈现不再用标题，这一枚留给日志与旧断言）。</summary>
        internal static string NoticeTitle { get { return L("s.AboutUpdateTitle"); } }

        /// <summary>
        /// 49 号·版2：自动检查发现新版本时同步到内联结果区（与手动「立即检查」同一个呈现口径，只是多一句"自动"）。
        /// </summary>
        private void ShowAutoResult(UpdateChecker.Result r, Version current)
        {
            ShowInline(r, current, L("s.UpdateAutoPrefix"));
        }

        /// <summary>
        /// 49 号·版4：内联结果区取的那句。<b>除"发现新版本"之外三态逐字等于 <see cref="NoticeFor"/></b>（48 号文案原样复用）；
        /// "发现新版本"去掉末尾那句「是否打开下载页面？」——弹窗里那是 YesNo 的问句，内联区下面就有「打开下载页」按钮，
        /// 再问一句就成了自问自答。
        /// </summary>
        internal static string InlineFor(UpdateChecker.Result r, Version current)
        {
            if (r == null || r.Outcome != UpdateChecker.Outcome.Found) return NoticeFor(r, current);
            string notes = string.IsNullOrWhiteSpace(r.Latest.Notes) ? "" : "\n" + L("s.UpdateNotesFmt").Replace("{0}", r.Latest.Notes);
            return L("s.UpdateFoundFmt").Replace("{0}", r.Latest.Version).Replace("{1}", r.Latest.Date ?? "") + notes;
        }

        /// <summary>把检查结果写进内联结果区（自检与渲染都直接调这一段，同一条码路）。</summary>
        internal string ShowInline(UpdateChecker.Result r, Version current, string prefix)
        {
            string text = InlineFor(r, current);
            if (!string.IsNullOrEmpty(prefix)) text = prefix + text;
            if (UpdateResultText != null)
            {
                UpdateResultText.Text = text;
                UpdateResultText.Visibility = Visibility.Visible;
            }
            _found = r != null && r.Outcome == UpdateChecker.Outcome.Found ? r.Latest : null;
            if (_found != null) UpdateState.ReportFound(_found);   // 66 号令 T1：手动检查到新版同样点亮左下角橙色按钮（关掉自动检查也照样出）
            if (BtnOpenDownload != null)
                BtnOpenDownload.Visibility = _found == null ? Visibility.Collapsed : Visibility.Visible;
            return text;
        }

        /// <summary>内联呈现给渲染取证用（不经网络，直接给一段文案 + 是否显示「打开下载页」）。</summary>
        internal void ShowInlineForTest(string text, bool showDownload)
        {
            if (UpdateResultText != null)
            {
                UpdateResultText.Text = text;
                UpdateResultText.Visibility = Visibility.Visible;
            }
            if (BtnOpenDownload != null) BtnOpenDownload.Visibility = showDownload ? Visibility.Visible : Visibility.Collapsed;
        }

        internal string UpdateResultForTest { get { return UpdateResultText == null ? null : UpdateResultText.Text; } }
        internal bool UpdateResultShownForTest { get { return UpdateResultText != null && UpdateResultText.Visibility == Visibility.Visible; } }
        internal bool DownloadButtonShownForTest { get { return BtnOpenDownload != null && BtnOpenDownload.Visibility == Visibility.Visible; } }
        internal bool CheckButtonEnabledForTest { get { return BtnCheckUpdate == null || BtnCheckUpdate.IsEnabled; } }
        internal UpdateChecker.Entry FoundForTest { get { return _found; } }

        // ===== 68 号令取证口（T3 检查更新卡两列宽度） =====
        internal FrameworkElement UpdateHeaderPanelForTest { get { return UpdateHeaderPanel; } }
        internal FrameworkElement UpdateTitleBlockForTest { get { return UpdateTitleText; } }
        internal FrameworkElement UpdateResultBlockForTest { get { return UpdateResultText; } }

        /// <summary>
        /// 49 号·版4：立即检查＝按钮 + 内联结果。检查中先写"检查中…"并把按钮禁掉（防连点），
        /// 结果出来后按四态写内联文案；「发现新版本」才出现「打开下载页」小按钮。
        /// 全程不弹任何窗（48 号那三条 MessageBox 分支退役）。
        /// </summary>
        internal async Task CheckNowAsync()
        {
            if (_checkingUpdate) return;
            _checkingUpdate = true;
            if (BtnCheckUpdate != null) BtnCheckUpdate.IsEnabled = false;
            try
            {
                Version cur = typeof(AboutPage).Assembly.GetName().Version;
                if (UpdateResultText != null)
                {
                    UpdateResultText.Text = L("s.UpdateChecking");
                    UpdateResultText.Visibility = Visibility.Visible;
                }
                if (BtnOpenDownload != null) BtnOpenDownload.Visibility = Visibility.Collapsed;
                UpdateChecker.Result r = await UpdateChecker.CheckAsync(cur);   // 52 号令·返修②：渠道锁定 stable（两参 false 版）
                ShowInline(r, cur, "");
                if (r != null && r.Outcome == UpdateChecker.Outcome.Failed)
                    FileLogger.Write("手动检查更新失败：" + (r.Error ?? r.Fail.ToString()));
            }
            catch (Exception ex)
            {
                FileLogger.Write("手动检查更新异常：" + ex.Message);
                if (UpdateResultText != null)
                {
                    UpdateResultText.Text = L("s.UpdateFailNetwork");
                    UpdateResultText.Visibility = Visibility.Visible;
                }
            }
            finally
            {
                _checkingUpdate = false;
                if (BtnCheckUpdate != null) BtnCheckUpdate.IsEnabled = true;
            }
        }

        private async void OnCheckUpdate(object sender, RoutedEventArgs e) { await CheckNowAsync(); }

        /// <summary>「打开下载页」＝再过一次白名单（清单可能已被换掉）；被拒不打开，只在内联区说一句。</summary>
        private void OnOpenDownload(object sender, RoutedEventArgs e)
        {
            if (_found == null) return;
            if (UpdateChecker.OpenDownload(_found)) return;
            if (UpdateResultText != null)
            {
                UpdateResultText.Text = L("s.UpdateOpenFailed");
                UpdateResultText.Visibility = Visibility.Visible;
            }
        }

        /// <summary>自检取用：直接走「打开下载页」那一段（不合成点击事件）。</summary>
        internal string OpenDownloadForTest(UpdateChecker.Entry e)
        {
            _found = e;
            OnOpenDownload(null, null);
            return UpdateResultForTest;
        }

        /// <summary>
        /// V4.9 版本内修整第二批·修C：点「问题反馈」＝用系统浏览器打开官网反馈页。
        /// 52 号令·返修③：先读「自动打包日志」勾选态（settings FeedbackAutoLogs）——勾选时打包直传日志包、
        /// URL 带 logpkg；失败降级旧行为（跳转＋复制日志路径），三种结果各说各话不谎报。
        /// </summary>
        private void OnFeedback(object sender, RoutedEventArgs e)
        {
            ShowFeedbackJump(FeedbackSite.Open(CbkAutoLogs == null || CbkAutoLogs.IsChecked == true));
        }

        private void OnAutoLogsChanged(object sender, RoutedEventArgs e)
        {
            if (_wiring || CbkAutoLogs == null) return;
            SettingsService.SetFeedbackAutoLogs(CbkAutoLogs.IsChecked == true);
        }

        /// <summary>跳转结果的呈现（自检直接调这一段：注入 OpenOverride/UploadOverride 就不真开浏览器、不打真网络）。</summary>
        internal string ShowFeedbackJump(FeedbackSite.Jump j)
        {
            if (j == null) return "";
            string note;
            if (!j.BrowserLaunched)
                note = L("s.FbOpenFailed").Replace("{0}", j.Url ?? "");
            else if (j.LogPackageKey != null)
                note = L("s.FbLogPackSent");   // 打包上传成功（URL 已带 logpkg）
            else if (j.AutoLogAttempted && j.UploadError != null)
                note = L("s.FbLogPackFailedFmt").Replace("{0}", j.UploadError ?? "");
            else
                note = j.LogPathCopied ? L("s.FbCopied") : L("s.FbCopyFailed").Replace("{0}", j.LogDirectory ?? "");
            if (FeedbackNoteText != null)
            {
                FeedbackNoteText.Text = note;
                FeedbackNoteText.Visibility = Visibility.Visible;
            }
            return note;
        }

        // 73 号令：解锁区整块退役（状态行／机器码／输入解锁码／取得入口／解锁对话框与三档态），
        // 本页不再有"是否解锁"这一层概念；自检取用口随控件一并删除。
    }
}
