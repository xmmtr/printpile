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
    public enum HistoryLimit { R200, R100, TenDays, Hidden }   // V4.1：历史显示档位（默认 200 条）；V4.7 拆分3：无「无限制」档

    /// <summary>
    /// 应用设置（语言/主题/转换引擎/打印统计）持久化到 %LOCALAPPDATA%\BatchPrintCN\settings.json，
    /// 变更即时保存；UI 通过事件感知切换（XAML 用 DynamicResource 自动刷新）。
    /// </summary>
    public static class SettingsService
    {
        private static readonly object Sync = new object();
        private static AppTheme _theme = AppTheme.Light;
        private static AppLanguage? _language;      // 修整8（V4.4）：null = 未显式设置过，首启跟随系统 UI 语言
        private static ConverterEngine _engine = ConverterEngine.Auto;   // F1：默认自动
        private static bool _printReport;
        private static string _reportDirectory;
        private static bool _keepConvertedPdf;            // V3.2：保留转换中间 PDF
        private static string _convertOutputDirectory;    // V3.2：中间产物输出目录
        private static string _printoutDirectory;         // V3.2：Microsoft Print to PDF 自动落盘目录
        private static HistoryLimit _historyLimit = HistoryLimit.R200;   // V4.1：历史显示档位
        private static string _lastPrinter;   // F15（V4.3）：最近一次选择的打印机（主页与设置页双向同步的数据源）
        // V4.8-1：后端基址不再是设置项（写死在 UpdateChecker.ApiBase 一处常量），原 _apiBase 字段与 settings.json 键已移除


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

        public static ConverterEngine ConverterEngine { get { return _engine; } }
        public static bool PrintReport { get { return _printReport; } }
        public static string ReportDirectory
        {
            get { return string.IsNullOrWhiteSpace(_reportDirectory) ? FileLogger.LogDirectory : _reportDirectory; }
        }
        public static HistoryLimit HistoryLimit { get { return _historyLimit; } }

        /// <summary>F15（V4.3）：最近选择的打印机（null=未选过，UI 回退系统默认打印机）。</summary>
        public static string LastPrinter { get { return _lastPrinter; } }

        public static bool KeepConvertedPdf { get { return _keepConvertedPdf; } }

        /// <summary>V3.2：Microsoft Print to PDF 自动落盘目录（默认 %LOCALAPPDATA%\BatchPrintCN\printout\）。</summary>
        public static string PrintoutDirectory
        {
            get { return string.IsNullOrWhiteSpace(_printoutDirectory) ? DefaultPrintoutDirectory : _printoutDirectory; }
        }

        /// <summary>V3.2：保留转换产物（中间 PDF）输出目录（默认同 PrintoutDirectory）。</summary>
        public static string ConvertOutputDirectory
        {
            get { return string.IsNullOrWhiteSpace(_convertOutputDirectory) ? DefaultPrintoutDirectory : _convertOutputDirectory; }
        }

        public static string DefaultPrintoutDirectory
        {
            get { return AppPaths.PrintoutDir; }   // V4.1 便携化：程序目录\printout\
        }

        public static event Action ThemeChanged;
        public static event Action LanguageChanged;
        public static event Action EngineChanged;
        public static event Action ReportSettingsChanged;
        public static event Action LastPrinterChanged;   // F15：打印机选择变化（主页 ⇄ 设置页双向同步）

        private sealed class Persist
        {
            public string Theme { get; set; }
            public string Language { get; set; }
            public string Engine { get; set; }
            public bool PrintReport { get; set; }
            public string ReportDirectory { get; set; }
            public bool KeepConvertedPdf { get; set; }
            public string ConvertOutputDirectory { get; set; }
            public string PrintoutDirectory { get; set; }
            public string HistoryLimit { get; set; }   // V4.1
            public string LastPrinter { get; set; }    // F15（V4.3）：最近选择的打印机
            // V4.8-1（36 号令定夺）：后端基址是产品属性不是用户配置，不再有 ApiBase 键——
            // 老 settings.json 里残留的 "ApiBase" 因不在本 DTO 里，读取时被忽略、下次保存即消失。
        }

        private static string SettingsPath
        {
            get { return AppPaths.SettingsPath; }   // V4.1 便携化：程序目录\settings.json
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return;
                var p = JsonSerializer.Deserialize<Persist>(File.ReadAllText(SettingsPath));
                if (p == null) return;
                if (string.Equals(p.Theme, "dark", StringComparison.OrdinalIgnoreCase)) _theme = AppTheme.Dark;
                _language = ParseSavedLanguage(p.Language);   // 修整8：没有已保存语言值时保持"未设置"，由 Language 按系统语言解析
                if (string.Equals(p.Engine, "minipdf", StringComparison.OrdinalIgnoreCase)) _engine = ConverterEngine.MiniPdf;
                else if (string.Equals(p.Engine, "com", StringComparison.OrdinalIgnoreCase)) _engine = ConverterEngine.Com;
                else _engine = ConverterEngine.Auto;
                _printReport = p.PrintReport;
                _reportDirectory = p.ReportDirectory;
                _keepConvertedPdf = p.KeepConvertedPdf;
                _convertOutputDirectory = p.ConvertOutputDirectory;
                _printoutDirectory = p.PrintoutDirectory;
                _historyLimit = ParseHistoryLimit(p.HistoryLimit);
                _lastPrinter = string.IsNullOrWhiteSpace(p.LastPrinter) ? null : p.LastPrinter.Trim();
            }
            catch { /* 损坏的设置文件忽略，使用默认 */ }
        }

        private static HistoryLimit ParseHistoryLimit(string s)
        {
            // V4.7 拆分3：老设置文件里存的 "unlimited" 一律回落到 200 条上限，
            // 用户看不到、也选不到「无限制」。
            if (string.Equals(s, "100", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.R100;
            if (string.Equals(s, "tendays", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.TenDays;
            if (string.Equals(s, "hidden", StringComparison.OrdinalIgnoreCase)) return HistoryLimit.Hidden;
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
                        Engine = _engine == ConverterEngine.MiniPdf ? "minipdf" : _engine == ConverterEngine.Com ? "com" : "auto",
                        PrintReport = _printReport,
                        ReportDirectory = _reportDirectory,
                        KeepConvertedPdf = _keepConvertedPdf,
                        ConvertOutputDirectory = _convertOutputDirectory,
                        PrintoutDirectory = _printoutDirectory,
                        HistoryLimit = _historyLimit == HistoryLimit.R100 ? "100"
                            : _historyLimit == HistoryLimit.TenDays ? "tendays"
                            : _historyLimit == HistoryLimit.Hidden ? "hidden" : "200",
                        LastPrinter = _lastPrinter
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

        public static void SetConverterEngine(ConverterEngine engine)
        {
            if (_engine == engine) return;
            _engine = engine;
            Save();
            EngineChanged?.Invoke();
        }

        public static void SetPrintReport(bool enabled)
        {
            if (_printReport == enabled) return;
            _printReport = enabled;
            Save();
            ReportSettingsChanged?.Invoke();
        }

        public static void SetReportDirectory(string dir)
        {
            string v = string.IsNullOrWhiteSpace(dir) ? null : dir.Trim();
            if (_reportDirectory == v) return;
            _reportDirectory = v;
            Save();
            ReportSettingsChanged?.Invoke();
        }

        public static void SetKeepConvertedPdf(bool enabled)
        {
            if (_keepConvertedPdf == enabled) return;
            _keepConvertedPdf = enabled;
            Save();
        }

        public static void SetConvertOutputDirectory(string dir)
        {
            string v = string.IsNullOrWhiteSpace(dir) ? null : dir.Trim();
            if (_convertOutputDirectory == v) return;
            _convertOutputDirectory = v;
            Save();
        }

        public static void SetPrintoutDirectory(string dir)
        {
            string v = string.IsNullOrWhiteSpace(dir) ? null : dir.Trim();
            if (_printoutDirectory == v) return;
            _printoutDirectory = v;
            Save();
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
