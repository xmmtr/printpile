using System;
using System.IO;
using System.Linq;
using System.Text;
using Serilog;
using Serilog.Events;

namespace BatchPrintCN
{
    /// <summary>
    /// V3.0 日志升级（P0-②）：内部走 Serilog——按天滚动文件（%LOCALAPPDATA%\BatchPrintCN\logs\app-YYYYMMDD.log）、
    /// 保留最近 30 天、多线程安全、编码 UTF-8。对外 API 与旧版完全兼容（Write/LogDirectory），
    /// 并新增 RecentLines 供崩溃诊断弹窗读取。写失败静默（日志不影响主流程）。
    /// </summary>
    public static class FileLogger
    {
        private static readonly object Sync = new object();
        private static ILogger _logger;

        private static ILogger Logger
        {
            get
            {
                if (_logger == null)
                {
                    lock (Sync)
                    {
                        if (_logger == null)
                        {
                            _logger = new LoggerConfiguration()
                                .MinimumLevel.Information()
                                .WriteTo.File(
                                    Path.Combine(LogDirectory, "app-.log"),
                                    rollingInterval: RollingInterval.Day,
                                    retainedFileCountLimit: 30,
                                    shared: true,
                                    encoding: Encoding.UTF8,
                                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Message:lj}{NewLine}{Exception}")
                                .CreateLogger();
                        }
                    }
                }
                return _logger;
            }
        }

        public static string LogDirectory
        {
            get
            {
                return AppPaths.LogsDir;   // V4.1 便携化：程序目录\logs\
            }
        }

        public static void Write(string line)
        {
            try { Logger.Information(line); }
            catch { }
        }

        /// <summary>最近 N 行日志文本（崩溃诊断弹窗用）。</summary>
        public static string RecentLines(int maxLines = 20)
        {
            try
            {
                string dir = LogDirectory;
                if (!Directory.Exists(dir)) return "(尚无日志文件)";
                FileInfo file = new DirectoryInfo(dir).GetFiles("app-*.log").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (file == null) return "(尚无日志文件)";
                string[] lines = File.ReadAllLines(file.FullName, Encoding.UTF8);
                if (lines.Length <= maxLines) return string.Join(Environment.NewLine, lines);
                var sb = new StringBuilder();
                sb.AppendLine("（仅显示最近 " + maxLines + " 行，完整日志见文件）");
                for (int i = lines.Length - maxLines; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return sb.ToString();
            }
            catch (Exception ex) { return "(读取日志失败：" + ex.Message + ")"; }
        }
    }
}
