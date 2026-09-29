using System;
using System.IO;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.1 本地目录便携化：一切输出收进程序所在文件夹（便携惯例），不再写 %LOCALAPPDATA%。
    /// 目录规范：settings.json（根）、logs\（Serilog 滚动30天）、tmp\（转换临时）、data\（history.db）、printout\（自动落盘/保留产物）。
    /// 首次运行迁移旧 AppData 数据（原样保留不删）；程序目录只读无法写时兜底回退 %LOCALAPPDATA%\BatchPrintCN\ 并置 Portable=false。
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
        public static string SettingsPath { get; private set; }

        private static string AppDataRoot
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
                MigrateFromAppData();
            }
            else
            {
                Root = AppDataRoot;
            }

            LogsDir = Path.Combine(Root, "logs");
            TmpDir = Path.Combine(Root, "tmp");
            DataDir = Path.Combine(Root, "data");
            PrintoutDir = Path.Combine(Root, "printout");
            SettingsPath = Path.Combine(Root, "settings.json");

            try { Directory.CreateDirectory(LogsDir); } catch { }
            try { Directory.CreateDirectory(TmpDir); } catch { }
            try { Directory.CreateDirectory(DataDir); } catch { }
            try { Directory.CreateDirectory(PrintoutDir); } catch { }
        }

        /// <summary>首次运行：程序目录缺 settings.json / data\history.db 而旧 AppData 有 → 自动复制（旧数据原样保留不删）。</summary>
        private static void MigrateFromAppData()
        {
            try
            {
                string old = AppDataRoot;
                if (!Directory.Exists(old)) return;

                if (!File.Exists(Path.Combine(Root, "settings.json")) && File.Exists(Path.Combine(old, "settings.json")))
                {
                    try { File.Copy(Path.Combine(old, "settings.json"), Path.Combine(Root, "settings.json")); }
                    catch { }
                }
                if (!File.Exists(Path.Combine(Root, "data", "history.db")) && File.Exists(Path.Combine(old, "data", "history.db")))
                {
                    try { Directory.CreateDirectory(Path.Combine(Root, "data")); File.Copy(Path.Combine(old, "data", "history.db"), Path.Combine(Root, "data", "history.db")); }
                    catch { }
                }
                // 旧 AppData 原样保留，不删除
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
