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
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// 52 号令·步骤 4（2026-10-06 需求方拍板）：令牌制主题引擎。
    /// 三条边界（自检断言钉住）：① 主题只提供 12 个语义色令牌的<b>颜色值</b>，不执行任何代码、不注入 XAML 模板；
    /// ② 全部界面继续消费既有主题键（DynamicResource），ThemeService 按"令牌→既有键"映射表在运行时改值——
    ///    页面 XAML 零改动、WPF-UI 基座深浅照旧走 App.ApplyTheme（mode=dark 深色基座＋标题栏/系统控件跟随）；
    /// ③ 内置 8 主题配色逐字取自《主题系统原型-20261005.html》THEMES 表（需求方过目基准），不可删除。
    /// 持久化：settings.json 新键 ThemeName（主题名）；旧 Theme 键继续当"深浅基座模式"存（迁移兼容：dark→深色，light→浅色）。
    /// </summary>
    public static class ThemeService
    {
        /// <summary>12 个语义色令牌（与原型/《.bptheme 规范 v0.1》逐字一致）。</summary>
        public static readonly string[] TokenKeys =
        {
            "bg.window", "bg.card", "bg.subtle", "bg.nav",
            "text.primary", "text.secondary",
            "accent", "accent.text", "border",
            "status.success", "status.warning", "status.danger",
        };

        /// <summary>一个主题：12 令牌色值＋来源标签。Tokens 值均为 #RRGGBB。</summary>
        public sealed class ThemeDef
        {
            public string Name;
            public string Tag = "内置";                 // 内置 / 用户导入
            public bool IsDark;                          // mode：深色基座联动
            public string SourceFile;                    // 用户主题的 .bptheme 全路径（内置＝null）
            public Dictionary<string, string> Tokens = new Dictionary<string, string>();
        }

        /// <summary>主题应用结果（UI 状态行用）。</summary>
        public enum ApplyResult { Ok, NotFound, Invalid }

        // ===== 内置 8 主题（配色逐字取原型 THEMES 表）=====

        private static ThemeDef Def(string name, string tag, bool dark, string json)
        {
            var d = new ThemeDef { Name = name, Tag = tag, IsDark = dark };
            // 紧凑写法 "k:v;k:v" 逐对解析（与原型 THEMES 表逐字同值）
            foreach (var pair in json.Split(';'))
            {
                int c = pair.IndexOf(':');
                if (c <= 0) continue;
                d.Tokens[pair.Substring(0, c).Trim()] = pair.Substring(c + 1).Trim();
            }
            return d;
        }

        public static readonly List<ThemeDef> Builtin = new List<ThemeDef>
        {
            Def("浅色", "内置", false,
                "bg.window:#f3f3f3;bg.card:#ffffff;bg.subtle:#f7f7f7;bg.nav:#ebebeb;text.primary:#1a1a1a;text.secondary:#6b6b6b;accent:#0067c0;accent.text:#ffffff;border:#e0e0e0;status.success:#0f7b3e;status.warning:#b06a00;status.danger:#c42b1c"),
            Def("深色", "内置", true,
                "bg.window:#202020;bg.card:#2b2b2b;bg.subtle:#333333;bg.nav:#272727;text.primary:#f2f2f2;text.secondary:#a0a0a0;accent:#60cdff;accent.text:#003a5c;border:#3d3d3d;status.success:#6ccb8f;status.warning:#e8b45a;status.danger:#ff99a4"),
            Def("海盐蓝", "示例 · 社区", false,
                "bg.window:#eef4fb;bg.card:#ffffff;bg.subtle:#f4f8fd;bg.nav:#e3edf8;text.primary:#17324d;text.secondary:#5d7893;accent:#1f6fb2;accent.text:#ffffff;border:#d5e2ef;status.success:#0f7b3e;status.warning:#b06a00;status.danger:#c42b1c"),
            Def("森绿", "示例 · 社区", false,
                "bg.window:#eff5ef;bg.card:#ffffff;bg.subtle:#f5faf5;bg.nav:#e2ede2;text.primary:#1d3323;text.secondary:#5f7a66;accent:#2e7d4f;accent.text:#ffffff;border:#d3e3d6;status.success:#2e7d4f;status.warning:#b06a00;status.danger:#c42b1c"),
            Def("暖沙", "示例 · 社区", false,
                "bg.window:#f7f1e8;bg.card:#fffdf9;bg.subtle:#faf5ec;bg.nav:#efe6d7;text.primary:#3d2f1e;text.secondary:#8a7658;accent:#b0722a;accent.text:#ffffff;border:#e6dbc8;status.success:#4d7c2a;status.warning:#b06a00;status.danger:#b3402e"),
            Def("紫夜", "示例 · 社区", true,
                "bg.window:#1e1a2a;bg.card:#2a2438;bg.subtle:#332c44;bg.nav:#262033;text.primary:#efeaf8;text.secondary:#a89fc0;accent:#a78bfa;accent.text:#241a3d;border:#3d3552;status.success:#7fd1a0;status.warning:#e8c06a;status.danger:#ff9db1"),
            Def("樱粉", "示例 · 社区", false,
                "bg.window:#faf0f3;bg.card:#ffffff;bg.subtle:#fdf5f7;bg.nav:#f5e3ea;text.primary:#3d1f2a;text.secondary:#93687a;accent:#c2557f;accent.text:#ffffff;border:#efd8e0;status.success:#3f8f5f;status.warning:#b06a00;status.danger:#c42b5c"),
            Def("高对比黑", "示例 · 无障碍", true,
                "bg.window:#000000;bg.card:#0a0a0a;bg.subtle:#141414;bg.nav:#0d0d0d;text.primary:#ffffff;text.secondary:#e0e0e0;accent:#ffd400;accent.text:#000000;border:#4d4d4d;status.success:#00e676;status.warning:#ffd400;status.danger:#ff5252"),
        };

        // ===== 令牌 → 既有主题键映射（一个令牌喂多个既有键；派生键在 Apply 时生成）=====

        /// <summary>单值映射：令牌 → 直接吃这个令牌色的既有键（键＝主题字典里的键名）。</summary>
        private static readonly Dictionary<string, string[]> TokenToKeys = new Dictionary<string, string[]>
        {
            { "bg.window",      new[] { "WindowBgBrush", "PreviewAreaBrush" } },
            { "bg.card",        new[] { "CardBgBrush", "CardAltBgBrush", "InputBgBrush", "InputBgHoverBrush", "LogBgBrush", "GridRowBgBrush" } },
            { "bg.subtle",      new[] { "GridHeaderBgBrush", "GridRowHoverBrush" } },
            { "bg.nav",         new[] { "SidebarBgBrush" } },
            { "text.primary",   new[] { "TextPrimaryBrush", "FocusRingBrush", "BtnLightAreaTextBrush" } },
            { "text.secondary", new[] { "TextSecondaryBrush", "TextFaintBrush" } },
            { "accent",         new[] { "AccentBrush", "ActionGradientBrush", "ActionGradientHoverBrush", "BtnAccentTextBrush" } },
            { "accent.text",    new[] { "TextOnAccentBrush", "BtnSolidTextBrush" } },
            { "border",         new[] { "BorderBrush", "BorderStrongBrush" } },
            { "status.success", new[] { "SuccessBrush", "StatusOkTextBrush" } },
            { "status.warning", new[] { "StatusWarnTextBrush" } },
            { "status.danger",  new[] { "DangerBrush", "StatusFailTextBrush" } },
        };

        /// <summary>当前选中的主题（含用户主题；App 启动时按 settings 恢复）。</summary>
        public static ThemeDef Current;

        /// <summary>用户主题目录（data\themes\）。</summary>
        public static string UserThemesDirectory
        {
            get { return Path.Combine(AppPaths.DataDir, "themes"); }
        }

        /// <summary>枚举全部可选主题：内置在前＋用户主题（启动时扫描）。</summary>
        public static List<ThemeDef> All()
        {
            var list = Builtin.Select(b => b).ToList();
            list.AddRange(_user);
            return list;
        }

        private static readonly List<ThemeDef> _user = new List<ThemeDef>();

        /// <summary>用户主题 .bptheme 里的 theme.json 是否通过结构校验（安全边界：只收颜色令牌值）。</summary>
        internal static bool LastParseStrict;

        /// <summary>启动时扫描用户主题目录（缺目录先建；坏文件跳过并留痕）。</summary>
        public static void ScanUserThemes()
        {
            _user.Clear();
            try
            {
                Directory.CreateDirectory(UserThemesDirectory);
                foreach (var f in Directory.GetFiles(UserThemesDirectory, "*.bptheme").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var d = ParseBpTheme(File.ReadAllBytes(f));
                        if (d != null) { d.Tag = "用户导入"; d.SourceFile = f; _user.Add(d); }
                    }
                    catch (Exception ex) { FileLogger.Write("主题包加载失败（跳过）：" + Path.GetFileName(f) + " — " + ex.Message); }
                }
            }
            catch (Exception ex) { FileLogger.Write("用户主题目录扫描失败：" + ex.Message); }
        }

        // ===== .bptheme 解析（zip：theme.json 必需 + preview.png 可选）=====

        /// <summary>
        /// 解析 .bptheme（zip 字节）。规则（52 号令·Rules 4③）：
        /// zip 魔数 PK\x03\x04 → 打开 → theme.json 必需 → format 必须 "bptheme/0.1" →
        /// name 必填、mode ∈ {light, dark} → tokens 12 键逐个 #RGB 校验，<b>缺键回退该 mode 内置默认</b>（调用方按返回值知悉）。
        /// 解析失败/格式不符 → null。主题数据只有颜色字符串，绝不执行代码。
        /// </summary>
        public static ThemeDef ParseBpTheme(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B || bytes[2] != 0x03 || bytes[3] != 0x04)
                return null;   // zip 魔数
            try
            {
                using (var ms = new MemoryStream(bytes))
                using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
                {
                    var entry = zip.GetEntry("theme.json");
                    if (entry == null) return null;
                    string json;
                    using (var es = entry.Open())
                    using (var sr = new StreamReader(es, Encoding.UTF8))
                        json = sr.ReadToEnd();
                    using (var doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.ValueKind != JsonValueKind.Object) return null;
                        string format = GetStr(root, "format");
                        if (format != "bptheme/0.1") return null;
                        string name = GetStr(root, "name");
                        if (string.IsNullOrWhiteSpace(name)) return null;
                        string mode = GetStr(root, "mode");
                        bool dark = string.Equals(mode, "dark", StringComparison.OrdinalIgnoreCase);
                        if (!dark && !string.Equals(mode, "light", StringComparison.OrdinalIgnoreCase)) return null;
                        if (!root.TryGetProperty("tokens", out var tk) || tk.ValueKind != JsonValueKind.Object) return null;
                        var fallback = Builtin.First(b => b.IsDark == dark);
                        var d = new ThemeDef { Name = name.Trim(), Tag = "用户导入", IsDark = dark };
                        LastParseStrict = true;
                        foreach (var key in TokenKeys)
                        {
                            string raw = null;
                            if (tk.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String) raw = v.GetString();
                            string color = NormalizeHex(raw);
                            if (color == null)
                            {
                                d.Tokens[key] = fallback.Tokens[key];   // 缺键/坏值回退该 mode 内置默认
                                if (raw != null) LastParseStrict = false;   // 写了但非法 → 严格性降级（报备用）
                            }
                            else d.Tokens[key] = color;
                        }
                        // 重名与内置冲突时保留原名（调用方导入时负责改名去重），这里不静默改名
                        return d;
                    }
                }
            }
            catch { return null; }
        }

        /// <summary>#RRGGBB 规范化（可带 #；长度 7）；非法返回 null。</summary>
        internal static string NormalizeHex(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim().TrimStart('#');
            if (s.Length != 6) return null;
            foreach (char c in s) if (!Uri.IsHexDigit(c)) return null;
            return "#" + s.ToUpperInvariant();
        }

        private static string GetStr(JsonElement o, string name)
        {
            return o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        /// <summary>导入 .bptheme：校验 → 存 data\themes\（重名自动加序号）→ 返回 ThemeDef；null＝拒绝。</summary>
        public static ThemeDef Import(byte[] bytes, out string error)
        {
            error = null;
            var d = ParseBpTheme(bytes);
            if (d == null) { error = "theme-invalid"; return null; }
            try
            {
                Directory.CreateDirectory(UserThemesDirectory);
                string baseName = string.Join("_", d.Name.Split(Path.GetInvalidFileNameChars())).Trim();
                if (baseName.Length == 0) baseName = "theme";
                string path = Path.Combine(UserThemesDirectory, baseName + ".bptheme");
                int n = 1;
                while (File.Exists(path)) path = Path.Combine(UserThemesDirectory, baseName + "." + (n++) + ".bptheme");
                File.WriteAllBytes(path, bytes);
                d.SourceFile = path;
                d.Tag = "用户导入";
                _user.Add(d);
                return d;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>按名找主题（内置＋用户）。</summary>
        public static ThemeDef Find(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return All().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>默认主题＝浅色（老配置迁移：dark→深色，light→浅色）。</summary>
        public static ThemeDef DefaultTheme { get { return Builtin[0]; } }

        // ===== 应用 =====

        /// <summary>
        /// 应用主题：先按 mode 切 WPF-UI 深浅基座（App.ApplyTheme 既有路径：标题栏/系统控件跟随），
        /// 再把 12 令牌色（含派生）写进 Application.Resources 顶层同名键——DynamicResource 全局即时生效。
        /// 返回 Ok / NotFound（没有这个名字）/ Invalid（参数为空）。
        /// </summary>
        public static ApplyResult Apply(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ApplyResult.Invalid;
            var d = Find(name);
            if (d == null) return ApplyResult.NotFound;
            return Apply(d);
        }

        public static ApplyResult Apply(ThemeDef d)
        {
            if (d == null) return ApplyResult.Invalid;
            App.ApplyTheme(d.IsDark ? AppTheme.Dark : AppTheme.Light);
            ApplyTokens(d);
            Current = d;
            return ApplyResult.Ok;
        }

        /// <summary>把令牌色（含派生键）写进 Application 资源顶层（键必须在 LightTheme/DarkTheme 里都有——GridRowBgBrush 教训）。</summary>
        internal static void ApplyTokens(ThemeDef d)
        {
            var res = Application.Current == null ? null : Application.Current.Resources;
            if (res == null) return;
            var solid = new Dictionary<string, Color>();
            // ① 单值映射
            foreach (var pair in TokenToKeys)
            {
                var c = ToColor(d.Tokens.TryGetValue(pair.Key, out var v) ? v : null);
                if (!c.HasValue) continue;
                foreach (var key in pair.Value) solid[key] = c.Value;
            }
            // ② 派生键（主题字典里存在、映射表没有的观感键——由 accent/status 派生）
            var accent = ToColor(d.Tokens.TryGetValue("accent", out var av) ? av : null);
            var danger = ToColor(d.Tokens.TryGetValue("status.danger", out var dv) ? dv : null);
            var success = ToColor(d.Tokens.TryGetValue("status.success", out var sv) ? sv : null);
            var primary = ToColor(d.Tokens.TryGetValue("text.primary", out var pv) ? pv : null);
            var border = ToColor(d.Tokens.TryGetValue("border", out var bv) ? bv : null);
            if (accent.HasValue)
            {
                solid["AccentHoverBrush"] = Lighten(accent.Value, 0.12);
                solid["AccentPressedBrush"] = Darken(accent.Value, 0.10);
                solid["AccentSoftBrush"] = Alpha(accent.Value, 0.14);
                solid["GridRowSelectedBrush"] = Alpha(accent.Value, 0.12);
                solid["TipBgBrush"] = Alpha(accent.Value, 0.14);
                solid["TipBorderBrush"] = Alpha(accent.Value, 0.35);
                solid["TipTextBrush"] = Darken(accent.Value, d.IsDark ? -0.20 : 0.25);
                // Element Plus 按钮主色组（原 Styles.xaml 固定色板，52 号令·D-15 起随 accent 令牌变）
                solid["ElPrimaryBrush"] = accent.Value;
                solid["ElPrimaryHoverBrush"] = Lighten(accent.Value, 0.18);
                solid["ElPrimaryActiveBrush"] = Darken(accent.Value, 0.12);
                solid["ElDefaultHoverBgBrush"] = Alpha(accent.Value, 0.10);
                solid["ElDefaultHoverBorderBrush"] = Alpha(accent.Value, 0.40);
            }
            if (success.HasValue)
            {
                solid["ElSuccessBrush"] = success.Value;
                solid["ElSuccessHoverBrush"] = Lighten(success.Value, 0.16);
                solid["ElSuccessActiveBrush"] = Darken(success.Value, 0.12);
            }
            if (danger.HasValue)
            {
                solid["DangerBorderBrush"] = Alpha(danger.Value, 0.55);
                solid["DangerSoftBrush"] = Alpha(danger.Value, 0.08);
                solid["ElDangerBrush"] = danger.Value;
                solid["ElDangerHoverBrush"] = Lighten(danger.Value, 0.16);
                solid["ElDangerActiveBrush"] = Darken(danger.Value, 0.12);
            }
            var warning = ToColor(d.Tokens.TryGetValue("status.warning", out var wv) ? wv : null);
            if (warning.HasValue)
            {
                solid["ElWarningBrush"] = warning.Value;
                solid["ElWarningHoverBrush"] = Lighten(warning.Value, 0.16);
                solid["ElWarningActiveBrush"] = Darken(warning.Value, 0.12);
            }
            var secondary = ToColor(d.Tokens.TryGetValue("text.secondary", out var scv) ? scv : null);
            if (secondary.HasValue)
            {
                solid["ElInfoBrush"] = secondary.Value;
                solid["ElInfoHoverBrush"] = Lighten(secondary.Value, 0.10);
                solid["ElInfoActiveBrush"] = Darken(secondary.Value, 0.10);
            }
            var card = ToColor(d.Tokens.TryGetValue("bg.card", out var ccv) ? ccv : null);
            if (card.HasValue && border.HasValue)
            {
                solid["ElDefaultBgBrush"] = card.Value;
                solid["ElDefaultBorderBrush"] = border.Value;
                // 白底按钮字色＝text.primary（浅色主题）——原拍板 #606266/#201F1E 即该令牌的旧值来源
            }
            if (accent.HasValue && success.HasValue && danger.HasValue && warning.HasValue)
            {
                solid["ElDefaultTextBrush"] = d.IsDark && accent.HasValue
                    ? solid["BtnSolidTextBrush"]
                    : solid["BtnLightAreaTextBrush"];
            }
            if (danger.HasValue)
            {
                solid["DangerBorderBrush"] = Alpha(danger.Value, 0.55);
                solid["DangerSoftBrush"] = Alpha(danger.Value, 0.08);
            }
            if (success.HasValue) solid["SuccessSoftBrush"] = Alpha(success.Value, 0.12);
            if (primary.HasValue) solid["ScrollerThumbBrush"] = Alpha(primary.Value, 0.35);
            if (border.HasValue) solid["ButtonBackgroundPressed"] = border.Value;
            if (primary.HasValue && border.HasValue)
            {
                // 按钮悬停渐变（WPF-UI 覆盖键）：上下两端由 text/border 派生
                var top = d.IsDark ? Lighten(border.Value, 0.05) : Lighten(border.Value, 0.05);
                var bottom = d.IsDark ? Lighten(border.Value, 0.12) : Darken(border.Value, 0.06);
                var gb = new LinearGradientBrush(top, bottom, 90);
                gb.Freeze();
                res["ButtonBackgroundPointerOver"] = gb;
            }
            // ③ 逐键替换（App 资源顶层键优先于 merged 主题字典）
            foreach (var pair in solid)
            {
                var b = new SolidColorBrush(pair.Value);
                b.Freeze();
                res[pair.Key] = b;
            }
        }

        /// <summary>颜色解析（#RRGGBB）。非法返回 null。</summary>
        internal static Color? ToColor(string hex)
        {
            string s = NormalizeHex(hex);
            if (s == null) return null;
            byte r = Convert.ToByte(s.Substring(1, 2), 16);
            byte g = Convert.ToByte(s.Substring(3, 2), 16);
            byte b = Convert.ToByte(s.Substring(5, 2), 16);
            return Color.FromRgb(r, g, b);
        }

        /// <summary>变亮 amount∈[0,1]；负值变暗。</summary>
        internal static Color Lighten(Color c, double amount) { return Shift(c, amount); }
        internal static Color Darken(Color c, double amount) { return Shift(c, -Math.Abs(amount)); }

        private static Color Shift(Color c, double amount)
        {
            // 简易亮度位移：向白（amount>0）或向黑（amount<0）线性插值，避免 HSL 转换误差
            byte f(byte v)
            {
                double target = amount >= 0 ? 255 : 0;
                double k = Math.Abs(amount);
                return (byte)Math.Round(v + (target - v) * k);
            }
            var r = Color.FromRgb(f(c.R), f(c.G), f(c.B));
            return r;
        }

        /// <summary>带透明度的同色（soft 底）。</summary>
        private static Color Alpha(Color c, double a) { return Color.FromArgb((byte)Math.Round(255 * a), c.R, c.G, c.B); }
    }
}
