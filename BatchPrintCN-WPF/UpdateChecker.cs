using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.7 修整2：关于页「检查更新」——GET {ApiBase}/updates/manifest.json（口径＝《work\docs\客户端契约-20260926.md》§2.5）。
    /// 只读清单、不上传任何本机信息；解析三铁律：只认 active=true、version/url 非法的条目跳过、没有可用条目＝"已是最新"；
    /// 跳转 URL 白名单＝后端域 + gitee.com（防清单被篡改后把用户导向任意站点）。
    /// 自检走 <see cref="FetchOverride"/> 注入假响应（不打真网络，保证 0/0 可复现），真机点按钮才走 HttpClient（10 秒超时＋主/备域自动切换）。
    /// </summary>
    public static class UpdateChecker
    {
        /// <summary>
        /// V4.8-1（36 号开工令定夺）：后端基址＝产品属性，写死这一处常量，不做成 settings.json 用户可配置项
        /// （老 settings.json 里残留的 ApiBase 键读取时被忽略）。
        /// 48 号修1：原 Workers 域在中国大陆被 SNI 层封锁，故切到 Pages 域。
        /// 54 号令·B：基址**不再自带字面量**，一律取自 <see cref="AboutLinks.SiteBase"/>（国内主路＝需求方服务器域名，
        /// 占位期间指向占位域）；Cloudflare Pages 降为兜底域，但仍留在下载白名单里（下载链接仍走 CF）。
        /// </summary>
        public static readonly string ApiBase = AboutLinks.SiteBase;

        /// <summary>
        /// 48 号修1：旧 Workers 域**只作下载白名单**（过渡期线上旧清单里的 url 仍指向它），不作请求地址。
        /// 54 号口径：域名字面量只剩本常量与 AboutLinks 的两枚（SiteBase／SiteBaseCf），别处一律不许再写域名。
        /// </summary>
        public const string LegacyApiBase = "https://batchprint-api.xmmtr.workers.dev";

        /// <summary>
        /// 52 号令（D-14）：产品线分流——商业版走专用清单 /updates/manifest-commercial.json，与免费线 4.9.x 版本序列
        /// 彻底隔离（免费清单最新 4.9.2.0 对 1.0.0.0 恒"更新"，等于把商业版用户导向免费版降级，52 号验收缺陷）。
        /// 产品线取自程序集元数据 ProductFlavor（Directory.Build.props 一处定义，免费版劈分时改回 "free" 即切回原清单）；
        /// 未知值按免费清单兜底（宁可多提示也不许漏提示）。缓存一次，别处不许再写清单路径。
        /// </summary>
        public static string ProductFlavor
        {
            get
            {
                if (_flavor == null)
                {
                    _flavor = "free";
                    foreach (var m in typeof(UpdateChecker).Assembly.GetCustomAttributes(false).OfType<System.Reflection.AssemblyMetadataAttribute>())
                    {
                        if (m.Key == "ProductFlavor") { _flavor = string.Equals(m.Value, "commercial", StringComparison.OrdinalIgnoreCase) ? "commercial" : "free"; break; }
                    }
                }
                return _flavor;
            }
        }
        private static string _flavor;

        public static string ManifestPath
        {
            get { return ProductFlavor == "commercial" ? "/updates/manifest-commercial.json" : "/updates/manifest.json"; }
        }

        /// <summary>无网络/弱网降级口径：单次请求 10 秒超时（70 号·追补起配合候选地址自动切换），失败走"检查更新失败"分支（提示可重试），不挂界面。</summary>
        public const int TimeoutSeconds = 10;

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

        /// <summary>48 号修2：失败原因分类——界面只按这个枚举取资源键文案，异常原文一律不上屏（只进日志）。</summary>
        public enum FailKind { None, Timeout, Network }

        public sealed class Result
        {
            public Outcome Outcome;
            public Entry Latest;      // Found 时非空
            /// <summary>诊断用异常原文：**只写日志，禁止进弹窗**（48 号修2 口径；界面按 <see cref="Fail"/> 取资源键）。</summary>
            public string Error;
            public FailKind Fail;     // Failed 时的分类
            public bool Unrecognized; // 48 号修2·③：清单结构读不懂（Outcome 仍按 UpToDate 口径，只换文案并记日志）

            private Result(Outcome o, Entry e, string err, FailKind k, bool unrec) { Outcome = o; Latest = e; Error = err; Fail = k; Unrecognized = unrec; }
            public static Result UpToDate() { return new Result(Outcome.UpToDate, null, null, FailKind.None, false); }
            public static Result UpToDateUnrecognized(string detail) { return new Result(Outcome.UpToDate, null, detail, FailKind.None, true); }
            public static Result Found(Entry e) { return new Result(Outcome.Found, e, null, FailKind.None, false); }
            public static Result Failed(FailKind kind, string err) { return new Result(Outcome.Failed, null, err, kind, false); }
        }

        /// <summary>自检注入点：给一个"URL → 响应文本"的假实现，返回 null 视为请求失败。70 号·追补起注入实现抛异常＝该候选失败，调用方会按候选次序继续尝试。</summary>
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

        /// <summary>
        /// 70 号·追补（需求十五）：清单候选地址按序尝试——主域（SiteBase）→ 同站备用域（SiteBaseCf）→ 主域重试一次。
        /// 背景：国内到 Cloudflare 的链路存在秒级抖动，主域一次超时不代表备用域也不通。
        /// 域名字面量仍只取 AboutLinks 的两枚常量（源码树域名守恒门 b3 不受影响）。
        /// </summary>
        public static string[] ManifestCandidateUrls()
        {
            string path = ManifestPath;
            var list = new List<string>(2);
            foreach (var b in new[] { ApiBase, AboutLinks.SiteBaseCf })
            {
                string u = b.TrimEnd('/') + path;
                if (!list.Contains(u)) list.Add(u);
            }
            return list.ToArray();
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

        /// <summary>下载跳转白名单：当前后端域 + 旧 Worker 域（48 号修1 过渡期）+ gitee.com（均含子域），且必须是 http/https 绝对地址。</summary>
        public static bool IsAllowedUrl(string url)
        {
            Uri u;
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return false;
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return false;
            string host = u.Host.ToLowerInvariant();
            if (host == "gitee.com" || host.EndsWith(".gitee.com", StringComparison.Ordinal)) return true;
            // 64 号令·T1：主路域（printpile.cn）与同站第二域（www.printpile.cn）都放行；gitee.com 为历史遗留条目原样保留。
            return InDomain(host, ApiBase) || InDomain(host, AboutLinks.SiteBaseCf) || InDomain(host, LegacyApiBase);
        }

        /// <summary>host 是否等于该基址的域或其子域。</summary>
        private static bool InDomain(string host, string baseUrl)
        {
            Uri b;
            if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out b)) return false;
            string bh = b.Host.ToLowerInvariant();
            return host == bh || host.EndsWith("." + bh, StringComparison.Ordinal);
        }

        /// <summary>
        /// 48 号修2·③：清单读不懂＝正文为空／不是对象／没有 versions 数组（`{"versions":[]}` 是**合法**清单，
        /// 按契约 §2.5 判"已是最新"，不算读不懂）。
        /// </summary>
        internal static bool MalformedManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return true;
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    JsonElement arr;
                    return !(doc.RootElement.ValueKind == JsonValueKind.Object
                             && doc.RootElement.TryGetProperty("versions", out arr)
                             && arr.ValueKind == JsonValueKind.Array);
                }
            }
            catch (JsonException) { return true; }
        }

        /// <summary>48 号修2：异常 → 失败分类（超时／网络）。不认识的异常一律归"网络错误"，绝不把原文带给界面。</summary>
        internal static FailKind Classify(Exception ex)
        {
            if (ex is TaskCanceledException || ex is OperationCanceledException) return FailKind.Timeout;
            return FailKind.Network;      // HttpRequestException（含 HTTP 状态码/连接失败）、IOException、SocketException…
        }

        /// <summary>版本号能否参与比较（清单里写空、写 "v5.0-beta" 这类都要跳过，不能让解析崩）。</summary>
        public static bool TryVersion(string s, out Version v)
        {
            v = null;
            return !string.IsNullOrWhiteSpace(s) && Version.TryParse(s.Trim().TrimStart('v', 'V'), out v);
        }

        /// <summary>
        /// 49 号·版3：预发布条目的版本号——取 "-" 之前的主数字（"4.9.3.0-beta1" → 4.9.3.0）参与比较，
        /// 界面展示仍用原文。没有 "-" 后缀（那不是预发布条目）或主数字解析不出来 → false（调用方跳过，绝不崩）。
        /// </summary>
        public static bool TryPrereleaseVersion(string s, out Version v)
        {
            v = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim().TrimStart('v', 'V');
            int dash = t.IndexOf('-');
            if (dash <= 0) return false;                       // 无后缀，或 "-beta" 这种连主数字都没有
            return Version.TryParse(t.Substring(0, dash), out v);
        }

        /// <summary>49 号·版3：按渠道取版本号。正式版渠道＝上面 <see cref="TryVersion"/> 逐字同行为；测试版渠道额外接受带后缀的条目。</summary>
        public static bool TryVersionOf(string s, bool previewChannel, out Version v)
        {
            if (TryVersion(s, out v)) return true;
            if (!previewChannel) return false;
            Version pv;
            if (TryPrereleaseVersion(s, out pv)) { v = pv; return true; }
            v = null;
            return false;
        }

        /// <summary>
        /// 取"该提示的那一条"：active=true 且 version/url 都合法且 url 过白名单，按版本号取最大
        /// （契约说服务端已按 active 降序·date 降序排好，这里再取最大值是防清单顺序被手工改坏，结果与契约一致）。
        /// 与当前版本比：不大于当前版本 → null（＝已是最新）。
        /// </summary>
        public static Entry NewestUsable(string json, Version current)
        {
            return NewestUsable(json, current, false);
        }

        /// <summary>
        /// 49 号·版3：带渠道的版本。<paramref name="previewChannel"/>=false 时与上面两参版逐字同行为
        /// （预发布条目在主数字那一步就解析失败被跳过）；=true 时预发布条目按主数字参与比较，展示保留原文。
        /// </summary>
        public static Entry NewestUsable(string json, Version current, bool previewChannel)
        {
            Entry best = null; Version bestV = null;
            foreach (var e in Parse(json))
            {
                if (!e.Active) continue;
                Version v;
                if (!TryVersionOf(e.Version, previewChannel, out v)) continue;
                if (!IsAllowedUrl(e.Url)) continue;
                if (current != null && v <= current) continue;
                if (bestV == null || v > bestV) { best = e; bestV = v; }
            }
            return best;
        }

        /// <summary>走一遍完整检查（自检经 FetchOverride 注入，真机走 HttpClient）。</summary>
        public static Task<Result> CheckAsync(Version current)
        {
            return CheckAsync(current, false);
        }

        /// <summary>49 号·版3：带渠道的检查。<paramref name="previewChannel"/>=false 与两参版逐字同行为。</summary>
        public static async Task<Result> CheckAsync(Version current, bool previewChannel)
        {
            // 70 号·追补：按候选地址次序尝试（主域→备用域→主域重试），任一次拿到正文即进入解析；
            // 全部失败才报错。失败分类保持第一次（主域）的——界面文案稳定，不因备用域的异常种类漂移。
            string[] urls = ManifestCandidateUrls();
            Exception firstError = null;
            FailKind firstKind = FailKind.Network;
            for (int i = 0; i <= urls.Length; i++)
            {
                string url = urls[Math.Min(i, urls.Length - 1)];
                string json;
                try
                {
                    var overrideFetch = FetchOverride;
                    json = overrideFetch != null ? await overrideFetch(url).ConfigureAwait(false)
                                                 : await Http.GetStringAsync(url).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 48 号修2：原文只进日志；多记尝试序号与主机名，日志里能看出切换过域名
                    FileLogger.Write("检查更新请求失败（第 " + (i + 1) + " 次尝试，" + SafeHost(url) + "）：" + ex.Message);
                    if (firstError == null) { firstError = ex; firstKind = Classify(ex); }
                    continue;
                }
                return ProcessManifest(json, current, previewChannel);
            }
            return Result.Failed(firstKind, firstError == null ? null : firstError.Message);
        }

        /// <summary>响应正文 → 解析与结论（48 号修2 的"读不懂按已是最新"口径原样保留；成功路径与 70 号追补前逐字同行为）。</summary>
        private static Result ProcessManifest(string json, Version current, bool previewChannel)
        {
            if (MalformedManifest(json))
            {
                // 48 号修2·③：结构读不懂 → 结论仍按"已是最新版"给（不吓用户），但文案说明并记日志
                FileLogger.Write("检查更新：清单结构无法识别（正文长度 " + (json == null ? -1 : json.Length) + "），已按\"已是最新版\"口径处理");
                return Result.UpToDateUnrecognized(json == null ? "(空响应)" : json.Substring(0, Math.Min(json.Length, 200)));
            }
            try
            {
                Entry e = NewestUsable(json, current, previewChannel);
                return e == null ? Result.UpToDate() : Result.Found(e);
            }
            catch (Exception ex)
            {
                FileLogger.Write("检查更新解析失败：" + ex.Message);
                return Result.Failed(Classify(ex), ex.Message);
            }
        }

        /// <summary>日志用：只留主机名，异常原文不落界面。</summary>
        private static string SafeHost(string url)
        {
            Uri u;
            return Uri.TryCreate(url, UriKind.Absolute, out u) ? u.Host : "(无效地址)";
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
