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

namespace BatchPrintCN
{
    /// <summary>
    /// 49 号·版1：关于页按钮背后的两件事——「用系统浏览器打开一个网址」与「用资源管理器打开一个目录」。
    /// 网址常量集中在这里；启动动作都留了自检注入点，所以四项自检里既不会真开浏览器，也不会去碰用户真实的
    /// data/logs 目录（"不存在先创建"那一步是真的，只有最后那一下启动被替换掉）。
    /// 64 号令（2026-10-10）：本类是**全工程官方站点基址的唯一出处**——页面与 API 走主路 <see cref="SiteBase"/>（printpile.cn），
    /// <see cref="SiteBaseCf"/>（www.printpile.cn）为同站第二域，两域都在下载白名单。
    /// 域名字面量清单（改域名只动 SiteBase / SiteBaseCf 两行）：SiteBase（主路）、SiteBaseCf（同站第二域）、
    /// UpdateChecker.LegacyApiBase（旧 Workers 域，只进白名单不参与请求）。
    /// 59 号令（2026-10-09）：GitHubUrl 随闭源口径删除，本类不再有 github.com 字面量（原"唯一一处"的清单条目一并收口）。
    /// 别处一律不许再写域名——HomeUrl/ContactUrl/FeedbackSite.UrlBase/UpdateChecker.ApiBase 全部由本类推导。
    /// </summary>
    public static class AboutLinks
    {
        /// <summary>
        /// ★★ 主路基址（全工程唯一出处）：2026-10-10 起＝https://printpile.cn（Cloudflare Pages 同站双域·国内主路，
        /// 更新清单、官网页面与下载直链都在此域）。
        /// </summary>
        public const string SiteBase = "https://printpile.cn";

        /// <summary>同站第二域（www）：<see cref="SiteBase"/> 的别名；下载白名单两域都放行（64 号令·T1）。</summary>
        public const string SiteBaseCf = "https://www.printpile.cn";

        /// <summary>官网首页（国内主路）。与兜底域同一用途——这一枚是"看产品"。</summary>
        public static readonly string HomeUrl = SiteBase;

        // 78 号令·B3：开源仓库双按钮（关于页"GitHub"/"Gitee"，按钮文字用品牌名原文、不占资源键）。
        // 真实地址已由需求方提供、77 号令（2026-10-15）回填：GitHub=xmmtr/BatchPrint、Gitee=xmmtr/batch-print（复用旧仓）。
        /// <summary>开源仓库（GitHub）——需求方 2026-10-15 定址。</summary>
        public const string GitHubUrl = "https://github.com/xmmtr/BatchPrint";
        /// <summary>开源仓库（Gitee 国内镜像）——需求方 2026-10-15 定址。</summary>
        public const string GiteeUrl = "https://gitee.com/xmmtr/batch-print";

        // 59 号令（2026-10-09 需求方拍板）：GitHubUrl 与「GitHub」按钮一并移除（全工程不再出现 github.com 字面量）。
        // 73 号令（2026-10-15）：产品许可落地页 ContactUrl（license.html）随解锁方案一并删除——本程序不再有收费入口。

        /// <summary>数据目录（便携化根目录下的 data\，与历史库、缓存同一处）。</summary>
        public static string DataDirectory { get { return AppPaths.DataDir; } }

        /// <summary>日志目录（沿用 FileLogger 的口径，与 41 号修E 保留的那一处入口同源）。</summary>
        public static string LogDirectory { get { return FileLogger.LogDirectory; } }

        /// <summary>自检注入点：非 null 时用它代替真的启动浏览器 / 资源管理器。</summary>
        internal static Action<string> OpenUrlOverride;
        internal static Action<string> OpenFolderOverride;

        /// <summary>一次启动动作的结果：成功与否、目标、失败原因（原因只进日志与状态文字，绝不弹模态框）。</summary>
        public sealed class Outcome
        {
            public bool Ok;
            public string Target;
            public string Error;
            /// <summary>本次是否由"目录不存在→先创建"这条路走出来的（断言与日志都取这个）。</summary>
            public bool Created;
        }

        /// <summary>用系统默认浏览器打开网址（同 41/48 号那一句 UseShellExecute 写法）。</summary>
        public static Outcome OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return Failed(null, "地址为空");
            try
            {
                var fake = OpenUrlOverride;
                if (fake != null) fake(url);
                else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                FileLogger.Write("关于页：已用系统浏览器打开 " + url);
                return new Outcome { Ok = true, Target = url };
            }
            catch (Exception ex)
            {
                FileLogger.Write("关于页：打开地址失败 " + url + " —— " + ex.Message);
                return Failed(url, ex.Message);
            }
        }

        /// <summary>用资源管理器打开目录；<paramref name="createIfMissing"/>＝目录不存在时先建再开（数据目录首次运行就是这种情况）。</summary>
        public static Outcome OpenFolder(string dir, bool createIfMissing)
        {
            if (string.IsNullOrWhiteSpace(dir)) return Failed(null, "目录为空");
            try
            {
                var o = new Outcome { Target = dir };
                if (!Directory.Exists(dir))
                {
                    if (!createIfMissing) return Failed(dir, "目录不存在");
                    Directory.CreateDirectory(dir);
                    o.Created = true;
                }
                var fake = OpenFolderOverride;
                if (fake != null) fake(dir);
                else Process.Start(new ProcessStartInfo("explorer.exe", "\"" + dir + "\"") { UseShellExecute = true });
                FileLogger.Write("关于页：已打开目录 " + dir + (o.Created ? "（本次先创建）" : ""));
                o.Ok = true;
                return o;
            }
            catch (Exception ex)
            {
                FileLogger.Write("关于页：打开目录失败 " + dir + " —— " + ex.Message);
                return Failed(dir, ex.Message);
            }
        }

        private static Outcome Failed(string target, string error)
        {
            return new Outcome { Ok = false, Target = target, Error = error };
        }
    }
}
