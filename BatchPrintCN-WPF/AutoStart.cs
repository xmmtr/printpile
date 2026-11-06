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

namespace BatchPrintCN
{
    /// <summary>
    /// 80 号令（V1.2.1）·开机自启动。本产品对注册表的写入只有两处——本文件（Run 项）与
    /// LegacyCleanup.cs（清理旧版遗留键），注册表访问一律集中在这两个文件里，发布脚本 b7 门按文件名钉住。
    ///
    /// 语义（80 号令拍板，回执原文复述）：settings.json 存的是**用户意图**，应用每次启动都按意图对齐 Run 键——
    /// 意图为开而键缺失就补写、意图为关就确保无键；用户在系统里手工删掉这个键属于外部干预，
    /// 应用下次启动会按意图补回。便携版与安装版行为完全一致（HKCU 按用户生效，不区分形态），
    /// 程序目录搬家或换机后，由下次启动的这个对齐动作自动把路径修正。
    ///
    /// 只做"登录时把程序拉起来"这一件事：启动之后的窗口行为与用户手动双击完全一致，
    /// 不带"启动到托盘／最小化启动"这类派生行为（80 号令第五节明列不做）。
    /// </summary>
    internal static class AutoStart
    {
        /// <summary>Windows 的"当前用户登录时启动"项（HKCU，无需管理员权限）。</summary>
        internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        /// <summary>本产品在 Run 项里的值名，也是任务管理器「启动」页里显示的那个名字。</summary>
        internal const string ValueName = "PrintPile";

        /// <summary>
        /// Run 值的数据＝带引号的 exe 全路径。引号不是装饰：默认安装目录与「Program Files」都含空格，
        /// 不带引号会被 Windows 在第一个空格处截断，启动直接失败——所以这一枚单独做成纯函数好测。
        /// </summary>
        internal static string BuildRunKeyValue(string exePath)
        {
            return "\"" + (exePath ?? "").Trim() + "\"";
        }

        /// <summary>当前主程序全路径。单文件发布下 AppContext.BaseDirectory 指向解包临时目录，故取进程实际路径。</summary>
        internal static string CurrentExePath()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(Environment.ProcessPath)) return Environment.ProcessPath;
                return System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            }
            catch { return null; }
        }

        /// <summary>开＝写值，关＝删值（值本来就不在也算成功，幂等）。任何异常静默返回 false，绝不让设置页崩。</summary>
        internal static bool Apply(bool on)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return false;
                    if (on)
                    {
                        string exe = CurrentExePath();
                        if (string.IsNullOrWhiteSpace(exe)) return false;
                        key.SetValue(ValueName, BuildRunKeyValue(exe), Microsoft.Win32.RegistryValueKind.String);
                    }
                    else key.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>Run 值当前是否存在（读不到一律按"没有"处理，不抛）。</summary>
        internal static bool ReadState()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    if (key == null) return false;
                    return key.GetValue(ValueName) is string s && !string.IsNullOrWhiteSpace(s);
                }
            }
            catch { return false; }
        }

        /// <summary>Run 值的原文（不存在或读不到＝null）。与 <see cref="RestoreValue"/> 配成一对，供自检"先存后还"用。</summary>
        internal static string ReadValue()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath))
                    return key == null ? null : key.GetValue(ValueName) as string;
            }
            catch { return null; }
        }

        /// <summary>把 Run 值还原成给定原文（null 或空＝删掉）。注册表访问继续只留在本文件里（发布脚本 b7 门按文件钉）。</summary>
        internal static void RestoreValue(string original)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (string.IsNullOrWhiteSpace(original)) key.DeleteValue(ValueName, false);
                    else key.SetValue(ValueName, original, Microsoft.Win32.RegistryValueKind.String);
                }
            }
            catch { }
        }

        /// <summary>启动对齐：把 Run 键写成 settings.json 里那份意图的样子（外部手工删键会被补回）。返回"对齐后是否开着"。</summary>
        internal static bool SyncOnStartup()
        {
            bool want = SettingsService.AutoStart;
            Apply(want);
            return want;
        }
    }
}
