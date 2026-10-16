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
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace BatchPrintCN
{
    /// <summary>
    /// 52 号令·返修③：问题反馈日志自动打包直传。
    /// 打包＝日志目录全部 *.log/*.txt 按修改时间新→旧收进 zip（System.IO.Compression，net8 内置零依赖），
    /// 总体 ≤ <see cref="MaxBytes"/>；超限先丢最旧重打，仍超＝明确失败（不静默截半包）。
    /// 直传＝POST {ApiBase}/api/feedback-logs（multipart，zip 魔数校验在服务端），成功返回短期日志包 key；
    /// key 经 feedback.html?logpkg= 与反馈单关联，孤儿包 7 天过期（服务端口径）。
    /// 自检走 <see cref="UploadOverride"/> 注入（不打真网络）；打包本体真跑（临时目录，不碰真实日志）。
    /// </summary>
    internal static class LogPackager
    {
        /// <summary>zip 大小上限＝10MB（开工令口径）。</summary>
        public const int MaxBytes = 10 * 1024 * 1024;

        /// <summary>站点直传端点（挂在 AboutLinks.HomeUrl 同一域；路径唯一，别处不许再拼）。</summary>
        public const string UploadPath = "/api/feedback-logs";

        /// <summary>自检注入点：非 null 时代替真实 HTTP（返回 key 或抛异常）。</summary>
        internal static Func<byte[], string> UploadOverride = null;

        /// <summary>自检注入点：非 null 时代替真实日志目录（自检日志会把真实目录撑过 10MB，断言需要可控样张）。</summary>
        internal static Func<string> DirectoryOverride = null;

        /// <summary>打包目标目录（宿主传真实日志目录；自检可注入）。</summary>
        internal static string ResolveDirectory()
        {
            var fake = DirectoryOverride;
            return fake != null ? fake() : (FileLogger.LogDirectory ?? "");
        }

        private static HttpClient _http;
        private static HttpClient Http { get { return _http ?? (_http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) }); } }

        /// <summary>打包结果：zip 字节为空＝打包失败（Reason 给人话）。</summary>
        public sealed class Package
        {
            public byte[] Zip;
            public int FileCount;
            public long Size;
            public string Reason;   // 失败原因（只进日志与状态行，不弹窗）
            public bool Ok { get { return Zip != null && Zip.Length > 0; } }
        }

        /// <summary>
        /// 把日志目录打包为 zip。dir 为空/不存在 → 失败（Reason 说明）；候选文件＝*.log/*.txt 按修改时间新→旧；
        /// 全打超限 → 从最旧开始丢弃重打；只剩一个文件仍超限 → 失败（单文件超 10MB 明说）。
        /// </summary>
        public static Package Build(string dir)
        {
            var p = new Package();
            try
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                {
                    p.Reason = "日志目录不存在";
                    return p;
                }
                var candidates = Directory.GetFiles(dir)
                    .Where(f =>
                    {
                        string e = Path.GetExtension(f).ToLowerInvariant();
                        return e == ".log" || e == ".txt";
                    })
                    .Select(f => new FileInfo(f))
                    .Where(fi => fi.Length > 0)
                    .OrderByDescending(fi => fi.LastWriteTimeUtc)
                    .ToList();
                if (candidates.Count == 0)
                {
                    p.Reason = "日志目录没有可打包的日志文件";
                    return p;
                }
                // 超限丢最旧（从列表尾部），只剩一个仍超限＝失败
                var picked = candidates.ToList();
                while (picked.Count > 1 && picked.Sum(fi => fi.Length) > MaxBytes * 2L)
                {
                    // 粗剪枝：先按原始字节数和剪（zip 压缩后再精剪一轮），避免大量小文件时白压
                    picked.RemoveAt(picked.Count - 1);
                }
                for (int attempt = 0; attempt < picked.Count + 1; attempt++)
                {
                    byte[] zip = ZipFiles(picked);
                    if (zip != null && zip.Length <= MaxBytes)
                    {
                        p.Zip = zip;
                        p.FileCount = picked.Count;
                        p.Size = zip.Length;
                        return p;
                    }
                    if (picked.Count <= 1)
                    {
                        p.Reason = picked.Count == 1 && candidates.Count >= 1
                            ? "最新单个日志就超过 10MB 上限"
                            : "日志打包后超过 10MB 上限";
                        return p;
                    }
                    picked.RemoveAt(picked.Count - 1);   // 丢最旧重打
                }
                p.Reason = "日志打包未能压进 10MB 上限";
                return p;
            }
            catch (Exception ex)
            {
                p.Reason = "打包异常：" + ex.Message;
                return p;
            }
        }

        /// <summary>把选中的日志文件收进 zip（内存，不落盘）；空集/异常 → null。</summary>
        private static byte[] ZipFiles(List<FileInfo> files)
        {
            try
            {
                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        foreach (var fi in files)
                        {
                            // 日志目录文件名天然唯一（按时间命名），不做重名探测——Create 模式下读 Entries 会抛 InvalidOperationException（被外层吞成 null，2026-10-06 实战）
                            var entry = zip.CreateEntry(fi.Name, CompressionLevel.Optimal);
                            using (var es = entry.Open())
                            using (var fs = fi.OpenRead())
                            {
                                fs.CopyTo(es);
                            }
                        }
                    }
                    return ms.ToArray();
                }
            }
            catch { return null; }
        }

        /// <summary>上传 zip → 服务端 key。注入优先；真机走 HttpClient multipart（20 秒超时）。异常只抛给调用方说话，不带原文上屏。</summary>
        public static async Task<string> UploadAsync(byte[] zip, string appVersion)
        {
            var fake = UploadOverride;
            if (fake != null) return fake(zip);
            using (var form = new MultipartFormDataContent())
            {
                var fileContent = new ByteArrayContent(zip);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
                form.Add(fileContent, "logpkg", "logs.zip");
                form.Add(new StringContent(appVersion ?? ""), "app_version");
                var resp = await Http.PostAsync(AboutLinks.HomeUrl.TrimEnd('/') + UploadPath, form).ConfigureAwait(false);
                string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) throw new IOException("HTTP " + (int)resp.StatusCode);
                // 响应 {"ok":true,"key":"feedback-logs/..."}——手解两字段，避免引入 JSON 包
                string key = ExtractString(body, "key");
                bool ok = body.IndexOf("\"ok\":true", StringComparison.Ordinal) >= 0;
                if (!ok || string.IsNullOrWhiteSpace(key)) throw new IOException("响应缺少 key");
                return key;
            }
        }

        /// <summary>从极简 JSON 响应里取字符串字段（值不含转义引号——服务端只回 ASCII 键）。</summary>
        internal static string ExtractString(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string pat = "\"" + field + "\"";
            int k = json.IndexOf(pat, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = json.IndexOf(':', k + pat.Length);
            int q1 = json.IndexOf('"', colon + 1);
            int q2 = json.IndexOf('"', q1 + 1);
            if (q1 < 0 || q2 < 0) return null;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
    }
}
