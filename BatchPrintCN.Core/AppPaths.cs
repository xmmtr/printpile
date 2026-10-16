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
using System.IO;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.1 本地目录便携化：一切输出收进程序所在文件夹（便携惯例），不再写 %LOCALAPPDATA%。
    /// 目录规范（64 号起）：config\settings.json、logs\（Serilog 滚动30天）、tmp\（转换临时）、data\（history.db）、printout\（自动落盘/保留产物）。
    /// 迁移三段（全部"目标缺失才复制"，旧数据原样保留不删）：
    ///   ① 旧便携布局就地升级：{Root}\settings.json → config\settings.json（V1.0.0 测试版用户的存量数据）；
    ///   ② 旧 AppData 布局：%LOCALAPPDATA%\BatchPrintCN → config\settings.json 与 data\history.db（免费版存量）；
    ///   ③ 回退态（程序目录只读）：Root＝%LOCALAPPDATA%\PrintPile，迁移逻辑同②。
    /// 注意：必须在任何日志/设置/SQLite 使用前调用 Init()（App.OnStartup 开头 / Worker.Main 开头）。
    /// </summary>
    public static class AppPaths
    {
        public static bool Portable { get; private set; } = true;

        public static string Root { get; private set; }
        public static string LogsDir { get; private set; }
        public static string TmpDir { get; private set; }
        public static string DataDir { get; private set; }
        public static string PrintoutDir { get; private set; }
        public static string ConfigDir { get; private set; }
        public static string SettingsPath { get; private set; }
        /// <summary>回退根目录（程序目录不可写时用）：64 号起改用 PrintPile 名。</summary>
        private static string AppDataRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PrintPile");
            }
        }

        /// <summary>旧版 AppData 根（BatchPrintCN）：只作迁移源，原样保留不删。</summary>
        private static string LegacyAppDataRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "BatchPrintCN");
            }
        }

        public static void Init()
        {
            string baseDir = AppContext.BaseDirectory;
            try
            {
                Directory.CreateDirectory(Path.Combine(baseDir, "logs"));
                Portable = true;
            }
            catch { Portable = false; }   // 程序目录只读（如 Program Files）

            if (Portable)
            {
                Root = baseDir;
            }
            else
            {
                Root = AppDataRoot;
            }

            LogsDir = Path.Combine(Root, "logs");
            TmpDir = Path.Combine(Root, "tmp");
            DataDir = Path.Combine(Root, "data");
            PrintoutDir = Path.Combine(Root, "printout");
            ConfigDir = Path.Combine(Root, "config");
            SettingsPath = Path.Combine(ConfigDir, "settings.json");

            try { Directory.CreateDirectory(LogsDir); } catch { }
            try { Directory.CreateDirectory(TmpDir); } catch { }
            try { Directory.CreateDirectory(DataDir); } catch { }
            try { Directory.CreateDirectory(PrintoutDir); } catch { }
            try { Directory.CreateDirectory(ConfigDir); } catch { }

            MigrateLegacyData();
        }

        /// <summary>
        /// 64 号·T3：历史数据迁移（全部"目标缺失才复制"，两段来源，顺序＝先旧便携布局后旧 AppData，
        /// 就地升级 V1.0.0 测试版的存量 settings 优先于更早的免费版 AppData 数据）：
        ///   ① {Root}\settings.json（旧便携布局）→ config\settings.json；
        ///   ② %LOCALAPPDATA%\BatchPrintCN\settings.json → config\settings.json；同源 data\history.db → data\history.db。
        /// </summary>
        private static void MigrateLegacyData()
        {
            try
            {
                if (Portable) TryCopy(Path.Combine(Root, "settings.json"), SettingsPath);

                string old = LegacyAppDataRoot;
                if (Directory.Exists(old))
                {
                    TryCopy(Path.Combine(old, "settings.json"), SettingsPath);
                    TryCopy(Path.Combine(old, "data", "history.db"), Path.Combine(DataDir, "history.db"));
                }
                // 旧目录（旧便携 settings.json 与旧 AppData）原样保留，不删除
            }
            catch { }
        }

        /// <summary>目标缺失且源在位才复制（幂等：重复调用/重复启动零副作用）。</summary>
        private static void TryCopy(string src, string dst)
        {
            try
            {
                if (!File.Exists(dst) && File.Exists(src))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst);
                }
            }
            catch { }
        }

        /// <summary>数据目录说明（设置页提示用）：便携目录 / 回退目录。</summary>
        public static string Describe()
        {
            return Portable ? ("便携目录：" + Root) : ("回退目录（程序目录不可写）：" + Root);
        }
    }
}
