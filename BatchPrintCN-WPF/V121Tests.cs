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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace BatchPrintCN
{
    /// <summary>
    /// 80 号令（V1.2.1）断言面：开机自启动。
    ///
    /// 钉的是四件事：①拼路径这个纯函数（引号丢了就启动不了，必须单独钉）；②**真实 HKCU 往返**——
    /// 不打桩，直接对 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 的 `PrintPile` 值做
    /// 开→读到→关→读不到；③幂等（连开两次值不变、连关两次不报错）；④老配置无键读作 false 且回写带上。
    ///
    /// 现场纪律：本机那枚值**进来先存、出去必还**（老板机上可能本来就开着自启动，测试不许把它抹掉）；
    /// settings.json 同样先存后还。全程不弹框、不打网络。
    /// </summary>
    public static partial class WpfTests
    {
        internal static string V121Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            string oldCfg = null, savedRun = null;
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                savedRun = AutoStart.ReadValue();
                try { if (File.Exists(AppPaths.SettingsPath)) oldCfg = File.ReadAllText(AppPaths.SettingsPath); } catch { }

                // ===== ① 纯函数：Run 值数据＝带引号的 exe 全路径（路径含空格时引号就是功能） =====
                string v1 = AutoStart.BuildRunKeyValue(@"C:\Program Files\PrintPile\PrintPile.exe");
                string v2 = AutoStart.BuildRunKeyValue(@"  C:\Users\me\my tools\a b.exe  ");
                string v3 = AutoStart.BuildRunKeyValue(null);
                sb.AppendLine("— ① BuildRunKeyValue：空格路径=\"" + v1 + "\"；首尾空白剪掉=\"" + v2 + "\"；null=\"" + v3 + "\"");
                Check(bad, "80 号 纯函数：BuildRunKeyValue 一律给「一对引号包住的全路径」（含空格不截断、空白剪净、null 也不抛）",
                    v1 == "\"C:\\Program Files\\PrintPile\\PrintPile.exe\"" && v2 == "\"C:\\Users\\me\\my tools\\a b.exe\"" && v3 == "\"\"");

                // ===== ② 真实 HKCU 往返（不打桩）：开→读到→关→读不到 =====
                bool a1 = AutoStart.Apply(true);
                bool onAfter = AutoStart.ReadState();
                string written = AutoStart.ReadValue();
                bool a2 = AutoStart.Apply(false);
                bool offAfter = AutoStart.ReadState();
                string want = AutoStart.BuildRunKeyValue(AutoStart.CurrentExePath());
                sb.AppendLine("— ② 真注册表往返：Apply(true)=" + a1 + " 读到开=" + onAfter + " 值=\"" + written + "\""
                    + "；Apply(false)=" + a2 + " 读到开=" + offAfter + "（值名 " + AutoStart.ValueName + "，路径 "
                    + AutoStart.RunKeyPath + "）");
                Check(bad, "80 号 真注册表往返：Apply(true) 后 ReadState 为开且值＝当前 exe 带引号全路径，Apply(false) 后 ReadState 为关",
                    a1 && onAfter && a2 && !offAfter && written == want);

                // ===== ③ 幂等：连开两次值一字不差、连关两次不报错 =====
                AutoStart.Apply(true);
                string first = AutoStart.ReadValue();
                bool i1 = AutoStart.Apply(true);
                string second = AutoStart.ReadValue();
                bool i2 = AutoStart.Apply(false);
                bool i3 = AutoStart.Apply(false);
                sb.AppendLine("— ③ 幂等：连开两次不重复写=" + (i1 && first == second) + "；连关两次不报错=" + (i2 && i3)
                    + "；收尾读到开=" + AutoStart.ReadState());
                Check(bad, "80 号 幂等：开→再开值不变（不追加不重复写），关→再关不抛不报错",
                    i1 && i2 && i3 && first == second && !AutoStart.ReadState());

                // ===== ④ settings.json：老配置无这枚键 → 读作 false；回写一律带上 =====
                bool loadOk = true, defaultOff = false, writtenBack = false, roundTrip = false;
                try
                {
                    File.WriteAllText(AppPaths.SettingsPath, "{\"Theme\":\"light\",\"Language\":\"zh\"}");
                    SettingsService.Load();
                    defaultOff = SettingsService.AutoStart == false;
                    SettingsService.SetAutoStart(true);
                    string txt = File.ReadAllText(AppPaths.SettingsPath);
                    writtenBack = txt.Contains("AutoStart");
                    SettingsService.SetAutoStart(false);
                    SettingsService.Load();
                    roundTrip = SettingsService.AutoStart == false;
                }
                catch (Exception ex) { loadOk = false; sb.AppendLine("— ④ 老配置往返异常：" + ex.Message); }
                sb.AppendLine("— ④ settings.json：无键读作关=" + defaultOff + "；回写带上该键=" + writtenBack
                    + "；开→关再读回=" + roundTrip + "（DTO 现 " + PersistKeyCount() + " 枚）");
                Check(bad, "80 号 设置兼容：老配置缺该键读作 false（默认关），保存后一律带上；开关联动回写读回一致",
                    loadOk && defaultOff && writtenBack && roundTrip);
                Check(bad, "80 号 键账：settings.json 持久化 DTO 12 枚（73 号 11 枚 ＋ 本令 autoStart 一枚）",
                    PersistKeyCount() == 12);

                // ===== ⑤ 设置页开关：控件在可视树实取、默认态＝关；翻一下真的动注册表 =====
                var sp = new SettingsPage();
                sp.Measure(new Size(900, 620)); sp.Arrange(new Rect(0, 0, 900, 620)); sp.UpdateLayout();
                PumpToLoaded();
                var tgl = sp.AutoStartBoxForTest as FrameworkElement;
                bool shownOff = sp.AutoStartShownForTest == false;
                sp.SetAutoStartForTest(true); PumpToLoaded();
                bool uiOn = AutoStart.ReadState();
                sp.SetAutoStartForTest(false); PumpToLoaded();
                bool uiOff = !AutoStart.ReadState();
                sb.AppendLine("— ⑤ 设置页：开关在位=" + (tgl != null) + " 默认态=关=" + shownOff
                    + "；UI 打开→注册表出现=" + uiOn + "；UI 关闭→注册表消失=" + uiOff);
                Check(bad, "80 号 设置页开关：控件实取在位、默认关，且翻动真的写/删 HKCU 那枚值（UI→注册表同一条路）",
                    tgl != null && shownOff && uiOn && uiOff);

                // ===== ⑥ 版本：1.2.1.0（对外三位 1.2.1） =====
                string fv = null;
                try { fv = FileVersionInfo.GetVersionInfo(Process.GetCurrentProcess().MainModule.FileName).FileVersion; } catch { }
                sb.AppendLine("— ⑥ 版本：FileVersion=" + fv + " AssemblyVersion=" + AppVersion.Full + " 界面＝" + AppVersion.Display);
                Check(bad, "80 号 版本：FileVersion／AssemblyVersion 均 1.2.1.0，界面显示 v1.2.1",
                    fv == "1.2.1.0" && AppVersion.Full == "1.2.1.0" && AppVersion.Display == "v1.2.1");
            }
            catch (Exception ex)
            {
                sb.AppendLine("探针异常：" + ex);
                bad.Add("异常 " + ex.Message);
            }
            finally
            {
                AutoStart.RestoreValue(savedRun);   // 本机原值必还（原来没有＝保持没有）
                try
                {
                    if (oldCfg != null) File.WriteAllText(AppPaths.SettingsPath, oldCfg);
                    SettingsService.Load();
                }
                catch { }
            }
            WriteDiag("80号开机自启动定位.txt", sb.ToString());
            return bad.Count == 0
                ? "通过（80 号：①带引号全路径纯函数三例 ②真实 HKCU 开→读到→关→读不到 ③连开两次值不变·连关两次不抛 ④老配置缺键读作关且回写带上·DTO 12 枚 ⑤设置页开关实取在位默认关·翻动真的写删注册表 ⑥FileVersion=1.2.1.0；本机原值测毕已还原；逐条数字见 80号开机自启动定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>settings.json 持久化 DTO 的属性枚数（反射实取，不手数）。</summary>
        private static int PersistKeyCount()
        {
            var t = typeof(SettingsService).GetNestedType("Persist", System.Reflection.BindingFlags.NonPublic);
            return t == null ? -1 : t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Length;
        }

        /// <summary>80 号令渲染取证：设置页「关闭行为＋开机自启动」那一卡，浅/深/英文三张。</summary>
        internal static int V121Render(string log)
        {
            int code = 0;
            var sb = new StringBuilder();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                code |= RenderControl("80号设置页自启动浅色.txt", "80号设置页-开机自启动-浅色.png", new SettingsPage(), 900, 640);
                sb.AppendLine("① 浅色·中文：关闭行为卡底部第二行＝开机自启动（标题＋一行灰说明＋右侧开关，默认关）");

                App.ApplyTheme(AppTheme.Dark);
                code |= RenderControl("80号设置页自启动深色.txt", "80号设置页-开机自启动-深色.png", new SettingsPage(), 900, 640);
                sb.AppendLine("② 深色：同一行换主题（字色与卡片底色跟随，无钉死色值）");

                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.EnUS);
                code |= RenderControl("80号设置页自启动英文.txt", "80号设置页-开机自启动-英文.png", new SettingsPage(), 900, 640);
                sb.AppendLine("③ 英文·浅色：Start with Windows 一行取到英文值，卡片不因此撑破");

                App.ApplyLanguage(AppLanguage.ZhCN);
                App.ApplyTheme(AppTheme.Light);
            }
            catch (Exception ex) { sb.AppendLine("渲染异常：" + ex); code |= 1; }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
