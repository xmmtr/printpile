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
using System.Text.Json;

namespace BatchPrintCN
{
    public enum AppTheme { Light, Dark }
    public enum AppLanguage { ZhCN, EnUS }
    public enum ConverterEngine { Auto, MiniPdf, Com }   // F1：默认 Auto（自动检测，无 Office/WPS 时兜底 MiniPdf）
    public enum HistoryLimit { R200, R100, TenDays, Hidden, Unlimited }   // V4.1：历史显示档位（默认 200 条）；51 号回补的 Unlimited 自 73 号令起对所有用户一视同仁——档位只是显示档位，程序里不再有对它设闸的那一层
    public enum CloseBehavior { Ask, Minimize, Exit }          // V4.9-1：点标题栏 ✕ 的行为（默认每次询问）
    public enum UpdateChannel { Stable, Preview }              // 49 号·版3：更新渠道（默认正式版；测试版额外提示带 -beta/-rc 后缀的条目）

    /// <summary>
    /// 应用设置（语言/主题/转换引擎/打印统计）持久化到 %LOCALAPPDATA%\BatchPrintCN\settings.json，
    /// 变更即时保存；UI 通过事件感知切换（XAML 用 DynamicResource 自动刷新）。
    /// </summary>
    public static class SettingsService
    {
        private static readonly object Sync = new object();
        private static AppTheme _theme = AppTheme.Light;
        private static AppLanguage? _language;      // 修整8（V4.4）：null = 未显式设置过，首启跟随系统 UI 语言
        private static bool _printReport;
        private static HistoryLimit _historyLimit = HistoryLimit.R200;   // V4.1：历史显示档位
        private static string _lastPrinter;   // F15（V4.3）：最近一次选择的打印机（主页与设置页双向同步的数据源）
        // V4.8-1：后端基址不再是设置项（写死在 UpdateChecker.ApiBase 一处常量），原 _apiBase 字段与 settings.json 键已移除
        private static CloseBehavior _closeBehavior = CloseBehavior.Ask;   // V4.9-1：✕ 关闭行为（老配置无此键＝ask）
        // 49 号·版2/版3：自动检查更新（老配置无键＝开）、上一次自动检查发起的时刻（ISO 字符串）、更新渠道（老配置无键＝正式版）
        private static bool _autoCheckUpdate = true;
        private static string _lastAutoCheckUtc;
        private static UpdateChannel _updateChannel = UpdateChannel.Stable;
        // 52 号令·步骤 4：主题名（ThemeService 的主题选择；旧配置迁移：Theme=dark→深色、light→浅色）。
        private static string _themeName;
        // 52 号令·返修③：问题反馈自动打包日志上传（默认开，用户可关）。缺键＝开（老配置升级不改行为）。
        private static bool _feedbackAutoLogs = true;
        // V4.9 修整第二批·修D：转换引擎 / 统计目录 / 保留转换产物 / 转换产物目录 / 打印输出目录五项的设置项已随
        // 设置页四块一起退役——下面的 getter 直接给"既有默认值"，打印与转换流程一行没改（引擎＝自动、目录＝便携默认）。


        public static AppTheme Theme { get { return _theme; } }

        /// <summary>修整8（V4.4）：当前语言。用户在设置页显式设置过（settings.json 里有语言值）就以保存值为准，
        /// 永不被动覆盖；没设置过则跟随系统 UI 语言（zh* → 简体中文，其余 → 英文）。</summary>
        public static AppLanguage Language { get { return ResolveEffective(_language, System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName); } }

        /// <summary>修整8：生效语言 = 已保存值优先，无保存值才按系统语言码解析（纯函数，供自检模拟 culture）。</summary>
        internal static AppLanguage ResolveEffective(AppLanguage? saved, string uiCultureTwoLetterIso)
        {
            if (saved.HasValue) return saved.Value;
            return FromCulture(uiCultureTwoLetterIso);
        }

        /// <summary>修整8：本应用只有简中 + 英文两套资源，zh* （zh-CN / zh-Hans / zh-TW…）统一映射简体中文，其余一律英文。</summary>
        internal static AppLanguage FromCulture(string twoLetterIso)
        {
            return string.Equals((twoLetterIso ?? "").Trim(), "zh", StringComparison.OrdinalIgnoreCase)
                ? AppLanguage.ZhCN : AppLanguage.EnUS;
        }

        /// <summary>修整8：settings.json 里的语言值 → 已设置态；空或未知值当作"没设置过"，回退系统语言。</summary>
        internal static AppLanguage? ParseSavedLanguage(string saved)
        {
            string s = (saved ?? "").Trim().ToLowerInvariant();
            if (s.StartsWith("zh", StringComparison.Ordinal)) return AppLanguage.ZhCN;
            if (s.StartsWith("en", StringComparison.Ordinal)) return AppLanguage.EnUS;
            return null;
        }

        /// <summary>F1：转换引擎。修D 之后界面不再提供手选，固定「自动」——有本机 WPS/Office 走 COM，
        /// 没有则兜底 MiniPdf（探测与兜底逻辑在 EngineDetector / OfficeComPrinter 里，一行没动）。</summary>
        private static readonly ConverterEngine _engine = ConverterEngine.Auto;   // F1：默认自动（修D 起界面不再提供手选）
        public static ConverterEngine ConverterEngine { get { return _engine; } }
        public static bool PrintReport { get { return _printReport; } }
        /// <summary>统计 CSV 落盘目录（修D：不再可选，＝既有默认＝日志目录）。</summary>
        public static string ReportDirectory
        {
            get { return FileLogger.LogDirectory; }
        }
        public static HistoryLimit HistoryLimit { get { return _historyLimit; } }

        /// <summary>V4.9-1：点 ✕ 的行为（Ask=每次询问〔默认〕/ Minimize=最小化到系统托盘〔41 号·修A 起〕/ Exit=直接退出）。</summary>
        public static CloseBehavior CloseBehavior { get { return _closeBehavior; } }

        public static void SetCloseBehavior(CloseBehavior b)
        {
            if (_closeBehavior == b) return;
            _closeBehavior = b;
            Save();
        }

        /// <summary>V4.9-1：settings.json 的 CloseBehavior 值 → 枚举；缺键/空/未知值一律按 Ask（老配置升级不改变行为）。</summary>
        internal static CloseBehavior ParseCloseBehavior(string saved)
        {
            string s = (saved ?? "").Trim().ToLowerInvariant();
            if (s == "minimize") return CloseBehavior.Minimize;
            if (s == "exit") return CloseBehavior.Exit;
            return CloseBehavior.Ask;
        }

        /// <summary>V4.9-1：枚举 → settings.json 值（写文件用，与 ParseCloseBehavior 一一对应）。</summary>
        internal static string CloseBehaviorToKey(CloseBehavior b)
        {
            return b == CloseBehavior.Minimize ? "minimize" : b == CloseBehavior.Exit ? "exit" : "ask";
        }

        // ===== 49 号·版2/版3：自动检查更新与更新渠道 =====

        /// <summary>49 号·版2：是否自动检查更新（默认开；关掉之后只剩手动「立即检查」）。</summary>
        public static bool AutoCheckUpdate { get { return _autoCheckUpdate; } }

        /// <summary>49 号·版2：老配置里没这个键（读出来是 null）→ 按默认开，不因为升级而静默关掉。</summary>
        internal static bool ParseAutoCheck(bool? saved) { return saved ?? true; }

        public static void SetAutoCheckUpdate(bool on)
        {
            if (_autoCheckUpdate == on) return;
            _autoCheckUpdate = on;
            Save();
        }

        /// <summary>49 号·版2：上一次自动检查**发起**的时刻（UTC）；从没发起过＝null（第一次加载后就会查）。</summary>
        public static DateTime? LastAutoCheckUtc { get { return ParseUtc(_lastAutoCheckUtc); } }

        /// <summary>49 号·版2：写回发起时刻（round-trip 到分钟就够，但存的是完整 ISO，避免时区/精度歧义）。</summary>
        public static void SetLastAutoCheckUtc(DateTime utc)
        {
            string v = utc.ToUniversalTime().ToString("O");
            if (_lastAutoCheckUtc == v) return;
            _lastAutoCheckUtc = v;
            Save();
        }

        /// <summary>49 号·版2：settings.json 里的时刻串 → UTC 时间；空/写坏了都当"没检查过"（宁可多查一次，不能永远不查）。</summary>
        internal static DateTime? ParseUtc(string saved)
        {
            DateTime d;
            if (string.IsNullOrWhiteSpace(saved)) return null;
            if (DateTime.TryParse(saved, System.Globalization.CultureInfo.InvariantCulture,
                                  System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                                  out d)) return d.ToUniversalTime();
            return null;
        }

        /// <summary>49 号·版3：更新渠道（stable＝现行为；preview＝预发布条目也参与比较）。</summary>
        public static UpdateChannel UpdateChannel { get { return _updateChannel; } }

        public static void SetUpdateChannel(UpdateChannel c)
        {
            if (_updateChannel == c) return;
            _updateChannel = c;
            Save();
        }

        /// <summary>49 号·版3：settings.json 值 → 渠道；缺键/空/未知值一律按正式版（老配置升级不改变行为）。</summary>
        internal static UpdateChannel ParseChannel(string saved)
        {
            string s = (saved ?? "").Trim().ToLowerInvariant();
            return s == "preview" ? UpdateChannel.Preview : UpdateChannel.Stable;
        }

        // ===== 73 号令：解锁码与限时体验起点两枚键随功能退场（DTO 13 → 11 枚）=====
        // 老用户 settings.json 里残留的 "LicenseCode"/"TrialStart" 不在 DTO 里＝读取时忽略、下次保存自然消失；
        // 不主动改写用户已有的文件内容，也不因为多出来的键报错（与 V4.8 那枚 ApiBase 的处置同一口径）。


        // 73 号令：68 号那枚"全新安装"判据（FreshInstallThisRun／IsFreshInstall）随限时体验弹窗一起删除——
        // 它唯一的用途就是决定"首启要不要弹那一枪"，弹窗没了，判据留着就是死代码。

        // ===== 52 号令·返修③：反馈自动打包日志 =====

        /// <summary>问题反馈时是否自动打包日志上传（默认开；缺键/空按开）。</summary>
        public static bool FeedbackAutoLogs { get { return _feedbackAutoLogs; } }

        // ===== 52 号令·步骤 4：主题名 =====

        /// <summary>当前主题名（settings.json 的 ThemeName 键；null/缺键＝按旧 Theme 键迁移：dark→深色，light→浅色）。</summary>
        public static string ThemeName { get { return _themeName; } }

        /// <summary>写主题名并同时同步深浅基座模式键（Theme）——两键并存：ThemeName 是选择，Theme 是 mode（WPF-UI 基座用）。</summary>
        public static void SetThemeName(string name)
        {
            string v = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (_themeName == v) return;
            _themeName = v;
            Save();
        }

        /// <summary>旧配置迁移：无 ThemeName 时按 Theme 键换算主题名（dark→深色，light/缺→浅色）。</summary>
        internal static string ResolveThemeName(string saved, AppTheme mode)
        {
            if (!string.IsNullOrWhiteSpace(saved)) return saved.Trim();
            return mode == AppTheme.Dark ? "深色" : "浅色";
        }

        public static void SetFeedbackAutoLogs(bool on)
        {
            if (_feedbackAutoLogs == on) return;
            _feedbackAutoLogs = on;
            Save();
        }

        /// <summary>49 号·版3：渠道 → settings.json 值（与 ParseChannel 一一对应）。</summary>
        internal static string ChannelToKey(UpdateChannel c)
        {
            return c == UpdateChannel.Preview ? "preview" : "stable";
        }

        /// <summary>F15（V4.3）：最近选择的打印机（null=未选过，UI 回退系统默认打印机）。</summary>
        public static string LastPrinter { get { return _lastPrinter; } }

        /// <summary>V3.2：是否保留转换中间产物。修D 起界面不再提供开关，固定默认关（不影响转换与打印，只是不落那份对比件）。</summary>
        public static bool KeepConvertedPdf { get { return false; } }

        /// <summary>V3.2：Microsoft Print to PDF 自动落盘目录（修D：不再可选，＝既有默认便携目录 程序目录\printout\）。</summary>
        public static string PrintoutDirectory
        {
            get { return DefaultPrintoutDirectory; }
        }

        /// <summary>V3.2：保留转换产物（中间 PDF）输出目录（修D：不再可选，默认同上）。</summary>
        public static string ConvertOutputDirectory
        {
            get { return DefaultPrintoutDirectory; }
        }

        public static string DefaultPrintoutDirectory
        {
            get { return AppPaths.PrintoutDir; }   // V4.1 便携化：程序目录\printout\
        }

        public static event Action ThemeChanged;
        public static event Action LanguageChanged;
        public static event Action ReportSettingsChanged;
        public static event Action LastPrinterChanged;   // F15：打印机选择变化（主页 ⇄ 设置页双向同步）

        private sealed class Persist
        {
            public string Theme { get; set; }
            public string Language { get; set; }
            public bool PrintReport { get; set; }
            public string HistoryLimit { get; set; }   // V4.1
            public string LastPrinter { get; set; }    // F15（V4.3）：最近选择的打印机
            public string CloseBehavior { get; set; }  // V4.9-1：✕ 关闭行为 ask|minimize|exit（缺键＝ask；minimize 语义 V4.9 修整第二批·修A 升级为"最小化到托盘"）
            public bool? AutoCheckUpdate { get; set; }  // 49 号·版2：自动检查更新（**可空**——老配置无此键要按默认开，非空 bool 会被 JSON 默认成 false 就反了）
            public string LastAutoCheckUtc { get; set; }  // 49 号·版2：上次自动检查发起时刻（ISO UTC 字符串，缺键＝从没查过）
            public string UpdateChannel { get; set; }     // 49 号·版3：更新渠道 stable|preview（缺键＝正式版）
            public bool? FeedbackAutoLogs { get; set; }   // 52 号令·返修③：反馈自动打包日志（**可空**——缺键按默认开，同 AutoCheckUpdate 口径）
            public string ThemeName { get; set; }         // 52 号令·步骤 4：主题名（缺键＝旧 Theme 键迁移）
            // V4.8-1（36 号令定夺）：后端基址是产品属性不是用户配置，不再有 ApiBase 键——
            // 老 settings.json 里残留的 "ApiBase" 因不在本 DTO 里，读取时被忽略、下次保存即消失。
            // V4.9 修整第二批·修D（同一口径）：Engine / ReportDirectory / KeepConvertedPdf / ConvertOutputDirectory /
            // PrintoutDirectory 五枚键随设置页四块一起退役——老配置里残留的这五枚读取时被忽略（不迁移、不报错），
            // 下次保存自然消失；运行期取值一律走 SettingsService 的默认值（引擎自动、目录＝程序目录\printout\ 与日志目录）。
        }

        private static string SettingsPath
        {
            get { return AppPaths.SettingsPath; }   // V4.1 便携化：程序目录\settings.json
        }

        public static void Load()
        {
            try
            {
                // 迁移过来的内容照读不误（老用户的语言/主题/档位仍然跟着走）；本目录没有这份文件就直接用默认值。
                if (!File.Exists(SettingsPath)) return;
                var p = JsonSerializer.Deserialize<Persist>(File.ReadAllText(SettingsPath));
                if (p == null) return;
                if (string.Equals(p.Theme, "dark", StringComparison.OrdinalIgnoreCase)) _theme = AppTheme.Dark;
                _language = ParseSavedLanguage(p.Language);   // 修整8：没有已保存语言值时保持"未设置"，由 Language 按系统语言解析
                _printReport = p.PrintReport;
                // V4.9 修整第二批·修D：这里原来还读 Engine / ReportDirectory / KeepConvertedPdf /
                // ConvertOutputDirectory / PrintoutDirectory 五枚键——随设置页四块一起退役，
                // 老配置里残留的这五枚读不到就自然忽略（不迁移、不报错），取值走上面的默认。
                _historyLimit = ParseHistoryLimit(p.HistoryLimit);
                _lastPrinter = string.IsNullOrWhiteSpace(p.LastPrinter) ? null : p.LastPrinter.Trim();
                _closeBehavior = ParseCloseBehavior(p.CloseBehavior);   // V4.9-1：老配置没这个键 → Ask
                // 49 号·版2/版3：三枚新键。老配置里都没有 → 自动检查＝开、渠道＝正式版、上次检查时刻＝没查过
                _autoCheckUpdate = ParseAutoCheck(p.AutoCheckUpdate);
                _lastAutoCheckUtc = string.IsNullOrWhiteSpace(p.LastAutoCheckUtc) ? null : p.LastAutoCheckUtc.Trim();
                _updateChannel = ParseChannel(p.UpdateChannel);
                // 52 号令·返修③：老配置无此键＝开（ParseAutoCheck 同款口径）
                _feedbackAutoLogs = p.FeedbackAutoLogs ?? true;
                _themeName = string.IsNullOrWhiteSpace(p.ThemeName) ? null : p.ThemeName.Trim();
            }
            catch { /* 损坏的设置文件忽略，使用默认 */ }
        }

        private static HistoryLimit ParseHistoryLimit(string s)
        {
            // V4.7 拆分3 曾把老设置里的 "unlimited" 回落到 200；51 号回补 Unlimited 档（当时仅解锁后可选）；
            // 73 号令起「无限制」对所有用户可用（HistoryPage 那道封顶闸已删）——所以这里照实读回，不做回落。
            if (string.Equals(s, "100", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.R100;
            if (string.Equals(s, "tendays", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.TenDays;
            if (string.Equals(s, "hidden", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.Hidden;
            if (string.Equals(s, "unlimited", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.Unlimited;
            return HistoryLimit.R200;
        }

        public static void Save()
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                    var p = new Persist
                    {
                        Theme = _theme == AppTheme.Dark ? "dark" : "light",
                        Language = _language == AppLanguage.EnUS ? "en" : "zh",
                        PrintReport = _printReport,
                        HistoryLimit = _historyLimit == HistoryLimit.R100 ? "100"
                            : _historyLimit == HistoryLimit.TenDays ? "tendays"
                            : _historyLimit == HistoryLimit.Hidden ? "hidden"
                            : _historyLimit == HistoryLimit.Unlimited ? "unlimited" : "200",
                        LastPrinter = _lastPrinter,
                        CloseBehavior = CloseBehaviorToKey(_closeBehavior),   // V4.9-1
                        AutoCheckUpdate = _autoCheckUpdate,                   // 49 号·版2
                        LastAutoCheckUtc = _lastAutoCheckUtc,                 // 49 号·版2（没查过就写 null，读回来仍是"没查过"）
                        UpdateChannel = ChannelToKey(_updateChannel),         // 49 号·版3
                        FeedbackAutoLogs = _feedbackAutoLogs,                 // 52 号令·返修③
                        ThemeName = _themeName                                // 52 号令·步骤 4
                    };
                    File.WriteAllText(SettingsPath, JsonSerializer.Serialize(p));
                }
            }
            catch { }
        }

        public static void SetTheme(AppTheme theme)
        {
            if (_theme == theme) return;
            _theme = theme;
            Save();
            ThemeChanged?.Invoke();
        }

        public static void SetLanguage(AppLanguage lang)
        {
            if (_language == lang) return;
            _language = lang;   // 修整8：显式设置后成为"已设置"态，后续启动不再被系统语言覆盖
            Save();
            LanguageChanged?.Invoke();
        }

        public static void SetPrintReport(bool enabled)
        {
            if (_printReport == enabled) return;
            _printReport = enabled;
            Save();
            ReportSettingsChanged?.Invoke();
        }

        public static void SetHistoryLimit(HistoryLimit limit)
        {
            if (_historyLimit == limit) return;
            _historyLimit = limit;
            Save();
        }

        /// <summary>F15（V4.3）：记录最近选择的打印机（主页/设置页两处入口统一走这里），变化时广播同步。</summary>
        public static void SetLastPrinter(string name)
        {
            string v = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            if (_lastPrinter == v) return;
            _lastPrinter = v;
            Save();
            LastPrinterChanged?.Invoke();
        }
    }
}
