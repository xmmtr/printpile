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
    /// 78 号令（合并 75 号）·A1：注册表彻底退场——旧版（v1.0/v1.1）为拦截绕过限时体验的用户而双写的
    /// 遗留键 <c>HKCU\Software\PrintPile</c>（TrialStart），在每次启动时一次性删除整棵树。
    /// 开源免费后本产品对注册表零写入；此处的"删除"是收尾动作，删完即无后续。
    ///
    /// 纪律：幂等（键不存在＝什么都不做）；任何异常（权限/安全软件拦截/虚拟化）＝静默跳过，
    /// 绝不弹框、绝不阻断启动、不新增任何 UI 文案与日志正文。
    /// 保留项：Inno 安装器自己的卸载注册项（HKCU\...\Uninstall\{AppId}_is1）不动——那是安装器本体
    /// 与控制面板卸载入口，属 78 号令 Rules 边界明确保留的对象；UpdateFlow 对它的只读探针也保留。
    /// </summary>
    internal static class LegacyCleanup
    {
        /// <summary>遗留根键相对 HKCU 的路径（70 号 TrialStore 当年写入的那一棵）。</summary>
        internal const string LegacyKeyPath = @"Software\PrintPile";

        /// <summary>删除遗留根键整棵树。键不存在＝无操作；异常＝静默吞掉。返回"删前该键是否存在"供自检对账。</summary>
        internal static bool Run()
        {
            try
            {
                if (!LegacyKeyExists()) return false;
                Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(LegacyKeyPath, throwOnMissingSubKey: false);
                return true;
            }
            catch { return false; }
        }

        /// <summary>遗留键当前是否存在（自检"预置→清理→消失"三步用；产品路径不依赖它）。</summary>
        internal static bool LegacyKeyExists()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(LegacyKeyPath))
                    return k != null;
            }
            catch { return false; }
        }
    }
}
