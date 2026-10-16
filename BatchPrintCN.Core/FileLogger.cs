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

        /// <summary>最近 N 行日志文本（崩溃诊断弹窗用）。
        /// 共享模式必须带 ReadWrite：Serilog 的 shared 文件汇一直持有写句柄，原来用 File.ReadAllLines
        /// （只给 FileShare.Read）在同进程里也会抛"being used by another process"，
        /// 导致诊断弹窗与反馈窗口的"附带运行日志"取到一句读取失败而不是日志（V4.9 修6 断言实测撞出）。</summary>
        public static string RecentLines(int maxLines = 20)
        {
            try
            {
                string dir = LogDirectory;
                if (!Directory.Exists(dir)) return "(尚无日志文件)";
                FileInfo file = new DirectoryInfo(dir).GetFiles("app-*.log").OrderByDescending(f => f.LastWriteTime).FirstOrDefault();
                if (file == null) return "(尚无日志文件)";
                var lines = ReadShared(file.FullName);
                if (lines.Length <= maxLines) return string.Join(Environment.NewLine, lines);
                var sb = new StringBuilder();
                sb.AppendLine("（仅显示最近 " + maxLines + " 行，完整日志见文件）");
                for (int i = lines.Length - maxLines; i < lines.Length; i++) sb.AppendLine(lines[i]);
                return sb.ToString();
            }
            catch (Exception ex) { return "(读取日志失败：" + ex.Message + ")"; }
        }

        private static string[] ReadShared(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sr = new StreamReader(fs, Encoding.UTF8))
            {
                string text = sr.ReadToEnd();
                return text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            }
        }
    }
}
