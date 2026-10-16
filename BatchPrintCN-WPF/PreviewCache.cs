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
using System.Security.Cryptography;
using System.Text;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号·改3：预览用的转换产物缓存。
    ///
    /// 口径（需求方拍板"预览复用同一转换引擎与缓存"）：
    /// · 转换**就调打印那条同一个入口** <see cref="OfficeComPrinter.ConvertToPdfForPreview"/>（同一个 ResolveEngine、
    ///   同一个 Worker 子进程、同一套参数拼装）——预览看到的 PDF 与打印发出去的是同一份语义；
    /// · 产物按「源路径 + 最后写入时间 + 长度 + 类型 + 引擎偏好」做键，落在 程序目录\tmp\preview\ 下复用；
    ///   源文件一改，键就变，不会拿旧产物糊弄；
    /// · **打印路径不读这里的缓存**（红线：打印行为零改动）——所以"第二次预览不再转换"成立，
    ///   而"预览后点开始打印"仍会按原流程转换一次。这一条已写进 00-项目说明的报备。
    /// </summary>
    internal static class PreviewCache
    {
        /// <summary>目录：tmp\preview\（启动清扫只删 tmp 根下的文件，不动这个子目录）。</summary>
        private static string Dir
        {
            get
            {
                string d = Path.Combine(AppPaths.TmpDir, "preview");
                try { Directory.CreateDirectory(d); } catch { }
                return d;
            }
        }

        /// <summary>与打印同一份引擎偏好口径（auto/com/minipdf）。</summary>
        public static string EnginePreference()
        {
            return SettingsService.ConverterEngine == ConverterEngine.MiniPdf ? "minipdf"
                : SettingsService.ConverterEngine == ConverterEngine.Com ? "com" : "auto";
        }

        /// <summary>
        /// 取该文件"可预览的 PDF"。PDF 直入＝原文件本身；Office/文本＝走打印同一条转换通道（命中缓存则不转）。
        /// fromCache 如实回报是否复用，界面信息条与断言都看它。失败返回 null 并给 error。
        /// </summary>
        public static string EnsurePdf(PrintJob job, Action<string> log, out bool fromCache, out string error)
        {
            fromCache = false;
            error = null;
            if (job == null || string.IsNullOrEmpty(job.FilePath) || !File.Exists(job.FilePath))
            {
                error = "文件已不在原位";
                return null;
            }
            if (job.Kind == DocKind.Pdf) return job.FilePath;

            string key = KeyOf(job);
            string cached = Path.Combine(Dir, key + ".pdf");
            if (File.Exists(cached) && SafeLength(cached) > 0)
            {
                fromCache = true;
                return cached;
            }

            if (log != null) log("预览：按打印同一条转换通道出 PDF…");
            string made = null;
            try { made = OfficeComPrinter.ConvertToPdfForPreview(job, EnginePreference(), m => { if (log != null) log(m); }); }
            catch (Exception ex) { error = ex.Message; return null; }
            if (string.IsNullOrEmpty(made) || !File.Exists(made)) { error = "转换未产出 PDF"; return null; }
            try
            {
                File.Copy(made, cached, true);
                fromCache = false;
                return cached;
            }
            catch (Exception ex) { error = ex.Message; return made; }        // 存不下也至少把这次产物给预览用
            finally
            {
                try { if (made != null && made != cached && File.Exists(made)) File.Delete(made); } catch { }
            }
        }

        private static long SafeLength(string p)
        {
            try { return new FileInfo(p).Length; } catch { return 0; }
        }

        /// <summary>键：源路径 + 最后写入时间 + 字节数 + 类型 + 引擎偏好（源文件一改就换键，不复用旧产物）。</summary>
        public static string KeyOf(PrintJob job)
        {
            long ticks = 0, len = 0;
            try
            {
                var fi = new FileInfo(job.FilePath);
                ticks = fi.LastWriteTimeUtc.Ticks;
                len = fi.Length;
            }
            catch { }
            string raw = job.FilePath.ToLowerInvariant() + "|" + ticks + "|" + len + "|" + job.Kind + "|" + EnginePreference();
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var sb = new StringBuilder("pv-");
                for (int i = 0; i < 8; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>自检/清理用：缓存目录。</summary>
        public static string Directory4Test { get { return Dir; } }
    }
}
