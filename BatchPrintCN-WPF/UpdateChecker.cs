using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.7 修整2：关于页「检查更新」——GET {ApiBase}/updates/manifest.json（口径＝《work\docs\客户端契约-20260926.md》§2.5）。
    /// 只读清单、不上传任何本机信息；解析三铁律：只认 active=true、version/url 非法的条目跳过、没有可用条目＝"已是最新"；
    /// 跳转 URL 白名单＝后端域 + gitee.com（防清单被篡改后把用户导向任意站点）。
    /// 自检走 <see cref="FetchOverride"/> 注入假响应（不打真网络，保证 0/0 可复现），真机点按钮才走 HttpClient（8 秒超时）。
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>
        /// V4.8-1（36 号开工令定夺）：后端基址＝产品属性，写死这一处常量，全工程唯一一份地址字面量，
        /// 不做成 settings.json 用户可配置项（老 settings.json 里残留的 ApiBase 键读取时被忽略）。
        /// </summary>
        public const string ApiBase = "https://batchprint-api.xmmtr.workers.dev";

        public const string ManifestPath = "/updates/manifest.json";

        /// <summary>无网络/弱网降级口径：请求 8 秒超时后走"检查更新失败"分支（提示可重试），不挂界面。</summary>
        public const int TimeoutSeconds = 8;

        private static readonly TimeSpan TimeoutSpan = TimeSpan.FromSeconds(TimeoutSeconds);


        /// <summary>清单里客户端要用的四个字段（active 单独留作筛选依据）。</summary>
        public sealed class Entry
        {
            public string Version;
            public string Date;
            public string Url;
            public string Notes;
            public bool Active;
        }

        public enum Outcome { UpToDate, Found, Failed }

        public sealed class Result
        {
            public Outcome Outcome;
            public Entry Latest;      // Found 时非空
            public string Error;      // Failed 时的简短原因（面向用户，不含堆栈）

            private Result(Outcome o, Entry e, string err) { Outcome = o; Latest = e; Error = err; }
            public static Result UpToDate() { return new Result(Outcome.UpToDate, null, null); }
            public static Result Found(Entry e) { return new Result(Outcome.Found, e, null); }
            public static Result Failed(string err) { return new Result(Outcome.Failed, null, err); }
        }

        /// <summary>自检注入点：给一个"URL → 响应文本"的假实现，返回 null 视为请求失败。</summary>
        internal static Func<string, Task<string>> FetchOverride;

        private static HttpClient _http;

        private static HttpClient Http
        {
            get
            {
                if (_http == null)
                {
                    _http = new HttpClient { Timeout = TimeoutSpan };
                }
                return _http;
            }
        }

        /// <summary>清单完整地址（基址只有 <see cref="ApiBase"/> 这一处常量）。</summary>
        public static string ManifestUrl()
        {
            return ApiBase.TrimEnd('/') + ManifestPath;
        }

        /// <summary>解析 versions 数组；根结构不对/不是数组 → 空表（调用方按"已是最新"处理并记日志）。</summary>
        public static List<Entry> Parse(string json)
        {
            var list = new List<Entry>();
            if (string.IsNullOrWhiteSpace(json)) return list;
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    JsonElement arr;
                    if (doc.RootElement.ValueKind != JsonValueKind.Object
                        || !doc.RootElement.TryGetProperty("versions", out arr)
                        || arr.ValueKind != JsonValueKind.Array) return list;
                    foreach (var it in arr.EnumerateArray())
                    {
                        if (it.ValueKind != JsonValueKind.Object) continue;
                        var e = new Entry
                        {
                            Version = Str(it, "version"),
                            Date = Str(it, "date"),
                            Url = Str(it, "url"),
                            Notes = Str(it, "notes")
                        };
                        JsonElement act;
                        if (it.TryGetProperty("active", out act))
                            e.Active = act.ValueKind == JsonValueKind.True || act.ValueKind == JsonValueKind.Number && act.GetInt32() != 0;
                        list.Add(e);
                    }
                }
            }
            catch (Exception ex) { FileLogger.Write("UpdateChecker.Parse 失败：" + ex.Message); }
            return list;
        }

        private static string Str(JsonElement o, string name)
        {
            JsonElement v;
            return o.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        }

        /// <summary>下载跳转白名单：后端域（含其子域）+ gitee.com（含子域），且必须是 http/https 绝对地址。</summary>
        public static bool IsAllowedUrl(string url)
        {
            Uri u;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            string host = u.Host.ToLowerInvariant();
            if (host == "gitee.com" || host.EndsWith(".gitee.com", StringComparison.Ordinal)) return true;
            Uri b;
            if (Uri.TryCreate(ApiBase, UriKind.Absolute, out b))
            {
                string bh = b.Host.ToLowerInvariant();
                if (host == bh || host.EndsWith("." + bh, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>版本号能否参与比较（清单里写空、写 "v5.0-beta" 这类都要跳过，不能让解析崩）。</summary>
        public static bool TryVersion(string s, out Version v)
        {
            v = null;
            return !string.IsNullOrWhiteSpace(s) && Version.TryParse(s.Trim().TrimStart('v', 'V'), out v);
        }

        /// <summary>
        /// 取"该提示的那一条"：active=true 且 version/url 都合法且 url 过白名单，按版本号取最大
        /// （契约说服务端已按 active 降序·date 降序排好，这里再取最大值是防清单顺序被手工改坏，结果与契约一致）。
        /// 与当前版本比：不大于当前版本 → null（＝已是最新）。
        /// </summary>
        public static Entry NewestUsable(string json, Version current)
        {
            Entry best = null; Version bestV = null;
            foreach (var e in Parse(json))
            {
                if (!e.Active) continue;
                Version v;
                if (!TryVersion(e.Version, out v)) continue;
                if (!IsAllowedUrl(e.Url)) continue;
                if (current != null && v <= current) continue;
                if (bestV == null || v > bestV) { best = e; bestV = v; }
            }
            return best;
        }

        /// <summary>走一遍完整检查（自检经 FetchOverride 注入，真机走 HttpClient）。</summary>
        public static async Task<Result> CheckAsync(Version current)
        {
            string url = ManifestUrl();
            string json;
            try
            {
                var overrideFetch = FetchOverride;
                json = overrideFetch != null ? await overrideFetch(url).ConfigureAwait(false)
                                             : await Http.GetStringAsync(url).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FileLogger.Write("检查更新请求失败：" + ex.Message);
                return Result.Failed(ex.Message);
            }
            try
            {
                Entry e = NewestUsable(json, current);
                return e == null ? Result.UpToDate() : Result.Found(e);
            }
            catch (Exception ex)
            {
                FileLogger.Write("检查更新解析失败：" + ex.Message);
                return Result.Failed(ex.Message);
            }
        }

        /// <summary>用系统默认浏览器打开下载地址（再过一次白名单——按钮点下去时清单可能已被换掉）。</summary>
        public static bool OpenDownload(Entry e)
        {
            if (e == null || !IsAllowedUrl(e.Url)) return false;
            try
            {
                Process.Start(new ProcessStartInfo(e.Url) { UseShellExecute = true });
                FileLogger.Write("检查更新：已打开下载页 " + e.Url);
                return true;
            }
            catch (Exception ex)
            {
                FileLogger.Write("检查更新：打开下载页失败：" + ex.Message);
                return false;
            }
        }
    }
}
