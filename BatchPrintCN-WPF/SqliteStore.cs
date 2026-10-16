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
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace BatchPrintCN
{
    /// <summary>
    /// F2 本地库（SQLite）：历史打印记录 + 打印模板。
    /// 库文件：程序目录\data\history.db（V4.1 便携化；首次访问自动建库建表，幂等迁移）。
    /// </summary>
    public static class SqliteStore
    {
        private static readonly object Sync = new object();

        /// <summary>
        /// V4.2 修整修复：PrintOptions 全是公开字段（无属性），System.Text.Json 默认只序列化属性，
        /// 早先模板存成 "{}" → 套用后参数全回默认值。必须开 IncludeFields。
        /// </summary>
        private static readonly JsonSerializerOptions JsonOpt = new JsonSerializerOptions { IncludeFields = true };

        /// <summary>V4.2.3 B1-c：最近一次 LoadTemplates 发现的"旧版空模板（{}）"数量，供 UI 提示重存。</summary>
        public static int EmptyTemplateCount { get; private set; }

        private static string DbPath
        {
            get { return Path.Combine(AppPaths.DataDir, "history.db"); }   // V4.1 便携化：程序目录\data\history.db
        }

        private static string ConnStr { get { return "Data Source=" + DbPath; } }

        /// <summary>确保数据库与表结构存在（首次调用建库，含迁移说明：后续加列用 ALTER TABLE ... 幂等执行）。</summary>
        public static void EnsureDb()
        {
            lock (Sync)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(DbPath));
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        EnsureSchema(conn);   // V4.2：抽公共逻辑，供临时库自检复用
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.EnsureDb 失败：" + ex.Message); }
            }
        }

        /// <summary>
        /// V4.2：建表 + 幂等迁移（对已连接库执行）。
        /// 新库直接带 folder 列；v4.1 旧库经 PRAGMA table_info 检查后 ALTER TABLE 补 folder，重复执行不报错。
        /// </summary>
        internal static void EnsureSchema(SqliteConnection conn)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS print_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    ext TEXT NOT NULL DEFAULT '',
    copies INTEGER NOT NULL DEFAULT 1,
    pages INTEGER NOT NULL DEFAULT 0,
    success INTEGER NOT NULL DEFAULT 0,
    time TEXT NOT NULL,
    printer TEXT NOT NULL DEFAULT '',
    engine TEXT NOT NULL DEFAULT '',
    folder TEXT NOT NULL DEFAULT ''
);
CREATE TABLE IF NOT EXISTS print_templates (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL UNIQUE,
    options_json TEXT NOT NULL,
    created_at TEXT NOT NULL,
    builtin INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS app_meta (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_history_time ON print_history(time);
CREATE INDEX IF NOT EXISTS idx_history_printer ON print_history(printer);";
                cmd.ExecuteNonQuery();
            }
            // V4.2 幂等迁移：旧库补 folder 列（SQLite 的 ALTER TABLE 无 IF NOT EXISTS，先查 table_info）
            if (!HasColumn(conn, "print_history", "folder"))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "ALTER TABLE print_history ADD COLUMN folder TEXT NOT NULL DEFAULT ''";
                    cmd.ExecuteNonQuery();
                }
            }
            // 68 号 T1：旧库补 builtin 列（内置默认模板标记，1＝内置、不吃免费版 5 个自建名额）
            if (!HasColumn(conn, "print_templates", "builtin"))
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "ALTER TABLE print_templates ADD COLUMN builtin INTEGER NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                }
            }
        }

        /// <summary>V4.2：检查表列是否存在（PRAGMA table_info）。</summary>
        internal static bool HasColumn(SqliteConnection conn, string table, string column)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA table_info(" + table + ")";
                using (var rd = cmd.ExecuteReader())
                {
                    while (rd.Read())
                        if (string.Equals(rd.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        /// <summary>V4.2 断言①：正式库 EnsureDb 后 folder 列存在（返回检查结果文本）。</summary>
        public static string FolderColumnCheck()
        {
            try
            {
                EnsureDb();
                using (var conn = new SqliteConnection(ConnStr))
                {
                    conn.Open();
                    return HasColumn(conn, "print_history", "folder") ? "通过（print_history.folder 列存在）" : "失败：folder 列缺失";
                }
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        /// <summary>
        /// 68 号 T1 断言：正式库 EnsureDb 后 print_templates.builtin 列在位（幂等迁移跑到了）、
        /// 且 app_meta 表可写读（标记都落库里，不占 settings.json 键账）。
        /// </summary>
        public static string BuiltinSchemaCheck()
        {
            try
            {
                EnsureDb();
                bool col;
                using (var conn = new SqliteConnection(ConnStr))
                {
                    conn.Open();
                    col = HasColumn(conn, "print_templates", "builtin");
                }
                if (!col) return "失败：print_templates.builtin 列缺失";
                string probe = "probe-" + Guid.NewGuid().ToString("N");
                SetMeta(probe, "1");
                string back = GetMeta(probe);
                using (var conn = new SqliteConnection(ConnStr))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM app_meta WHERE key=$k";
                        cmd.Parameters.AddWithValue("$k", probe);
                        cmd.ExecuteNonQuery();
                    }
                }
                return string.Equals(back, "1", StringComparison.Ordinal)
                    ? "通过（builtin 列在位＋app_meta 可写读）" : "失败：app_meta 读写不通";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        public static void InsertHistory(PrintRecord r)
        {
            if (r == null) return;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO print_history(name, ext, copies, pages, success, time, printer, engine, folder) VALUES($n,$e,$c,$p,$s,$t,$r,$g,$f)";
                            cmd.Parameters.AddWithValue("$n", r.Name ?? "");
                            cmd.Parameters.AddWithValue("$e", r.Ext ?? "");
                            cmd.Parameters.AddWithValue("$c", r.Copies);
                            cmd.Parameters.AddWithValue("$p", r.Pages);
                            cmd.Parameters.AddWithValue("$s", r.Success ? 1 : 0);
                            cmd.Parameters.AddWithValue("$t", r.Time.ToString("yyyy-MM-dd HH:mm:ss"));
                            cmd.Parameters.AddWithValue("$r", r.Printer ?? "");
                            cmd.Parameters.AddWithValue("$g", r.Engine ?? "-");
                            cmd.Parameters.AddWithValue("$f", r.SourceFolder ?? "");   // V4.2：源文件目录
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.InsertHistory 失败：" + ex.Message); }
            }
        }

        public sealed class HistoryRow
        {
            public string Name { get; set; }
            public string Ext { get; set; }
            public int Copies { get; set; }
            public int Pages { get; set; }
            public bool Success { get; set; }
            public DateTime Time { get; set; }
            public string Printer { get; set; }
            public string Engine { get; set; }
            public string Folder { get; set; }   // V4.2：所在文件夹（旧记录为空串，UI 显示 "—"）
            /// <summary>
            /// V4.5-2（决2，需求方 2026-09-25 拍板）：状态文案改走结构化口径 —— 判定只看 Success 布尔，
            /// 文字按当前界面语言取资源键（中文「成功/失败」、英文「Success/Failed」），
            /// 存储层不再直出中文（英文界面该列此前一直是中文＝本条要修的账）。
            /// 资源键取不到（未加载语言字典）时回退中文原值，界面不会出现空白；
            /// CSV 导出口径不变（沿用本属性＝导出语言跟界面语言，交付说明已留痕）。
            /// </summary>
            public string StatusText
            {
                get
                {
                    string key = Success ? "s.HistStatusOk" : "s.HistStatusFail";
                    string v = null;
                    try { v = System.Windows.Application.Current != null ? System.Windows.Application.Current.TryFindResource(key) as string : null; }
                    catch { v = null; }
                    if (!string.IsNullOrEmpty(v)) return v;
                    return Success ? "成功" : "失败";
                }
            }
            /// <summary>UI 显示用：旧记录（folder 为空）显示 "—"。</summary>
            public string FolderText { get { return string.IsNullOrEmpty(Folder) ? "—" : Folder; } }
        }

        public static List<HistoryRow> QueryHistory(string printerFilter = null, string range = "all", int limit = 500)
        {
            var list = new List<HistoryRow>();
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            var sql = "SELECT name,ext,copies,pages,success,time,printer,engine,folder FROM print_history WHERE 1=1";
                            if (!string.IsNullOrWhiteSpace(printerFilter)) { sql += " AND printer=$p"; cmd.Parameters.AddWithValue("$p", printerFilter); }
                            if (range == "today") sql += " AND time >= date('now','localtime')";
                            else if (range == "week") sql += " AND time >= datetime('now','localtime','-7 days')";
                            else if (range == "tendays") sql += " AND time >= datetime('now','localtime','-10 days')";   // V4.1 档位
                            sql += " ORDER BY id DESC";
                            if (limit > 0) { sql += " LIMIT $l"; cmd.Parameters.AddWithValue("$l", limit); }   // limit<=0 无限制
                            cmd.CommandText = sql;
                            using (var rd = cmd.ExecuteReader())
                            {
                                while (rd.Read())
                                {
                                    list.Add(new HistoryRow
                                    {
                                        Name = rd.GetString(0),
                                        Ext = rd.GetString(1),
                                        Copies = rd.GetInt32(2),
                                        Pages = rd.GetInt32(3),
                                        Success = rd.GetInt32(4) == 1,
                                        Time = DateTime.TryParse(rd.GetString(5), out var t) ? t : DateTime.MinValue,
                                        Printer = rd.GetString(6),
                                        Engine = rd.GetString(7),
                                        Folder = rd.IsDBNull(8) ? "" : rd.GetString(8)   // V4.2
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.QueryHistory 失败：" + ex.Message); }
            }
            return list;
        }

        public static List<string> QueryPrinters()
        {
            var list = new List<string>();
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT DISTINCT printer FROM print_history WHERE printer != '' ORDER BY printer";
                            using (var rd = cmd.ExecuteReader()) { while (rd.Read()) list.Add(rd.GetString(0)); }
                        }
                    }
                }
                catch { }
            }
            return list;
        }

        public static void SaveTemplate(string name, PrintOptions opt)
        {
            if (string.IsNullOrWhiteSpace(name) || opt == null) return;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            // 68 号 T1：用户「另存为模板」永远存成自建模板（builtin=0）。
                            // 同名撞上内置默认模板时按"用户接管这一枚"处理（此后它计入免费版 5 个名额），
                            // 不再用 INSERT OR REPLACE（那会把整行删重插、id 跳号，也分不清来源）。
                            cmd.CommandText = @"INSERT INTO print_templates(name, options_json, created_at, builtin)
VALUES($n,$j,$t,0)
ON CONFLICT(name) DO UPDATE SET options_json=excluded.options_json, created_at=excluded.created_at, builtin=0";
                            cmd.Parameters.AddWithValue("$n", name.Trim());
                            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(opt, JsonOpt));
                            cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.SaveTemplate 失败：" + ex.Message); }
            }
        }

        /// <summary>
        /// 68 号 T1：写入一枚<b>内置默认模板</b>（builtin=1）。已存在同名（无论是内置还是用户自建）就不动，
        /// 所以本方法幂等、可反复调用（首启预置与「恢复默认模板」共用同一条码路），且绝不覆盖用户数据。
        /// </summary>
        public static bool SaveBuiltinTemplate(string name, PrintOptions opt)
        {
            if (string.IsNullOrWhiteSpace(name) || opt == null) return false;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"INSERT INTO print_templates(name, options_json, created_at, builtin)
VALUES($n,$j,$t,1) ON CONFLICT(name) DO NOTHING";
                            cmd.Parameters.AddWithValue("$n", name.Trim());
                            cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(opt, JsonOpt));
                            cmd.Parameters.AddWithValue("$t", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            return cmd.ExecuteNonQuery() > 0;
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.SaveBuiltinTemplate 失败：" + ex.Message); return false; }
            }
        }

        /// <summary>68 号 T1：用户自建模板名（builtin=0）——免费版 5 个名额只按这一份计数。</summary>
        public static List<string> UserTemplateNames()
        {
            var list = new List<string>();
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT name FROM print_templates WHERE builtin=0 ORDER BY created_at DESC";
                            using (var rd = cmd.ExecuteReader())
                                while (rd.Read()) list.Add(rd.GetString(0));
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.UserTemplateNames 失败：" + ex.Message); }
            }
            return list;
        }

        /// <summary>68 号 T1：这一枚是不是内置默认模板（删除内置模板要走「恢复默认模板」回填，所以这个标记要能查）。</summary>
        public static bool IsBuiltinTemplate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT COUNT(*) FROM print_templates WHERE name=$n AND builtin=1";
                            cmd.Parameters.AddWithValue("$n", name.Trim());
                            return (long)cmd.ExecuteScalar() > 0;
                        }
                    }
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// 68 号：库内小标记（app_meta 表）。<b>刻意不放 settings.json</b>——本令的键账只允许 +1（TrialStart），
        /// 而"内置模板预置过没有"这类跨启动标记需要一处不占设置键账的落点。
        /// </summary>
        public static string GetMeta(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT value FROM app_meta WHERE key=$k";
                            cmd.Parameters.AddWithValue("$k", key.Trim());
                            object o = cmd.ExecuteScalar();
                            return o == null || o is DBNull ? null : Convert.ToString(o);
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.GetMeta 失败：" + ex.Message); return null; }
            }
        }

        /// <summary>68 号：写库内小标记（幂等，存在即覆盖）。</summary>
        public static void SetMeta(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "INSERT INTO app_meta(key, value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
                            cmd.Parameters.AddWithValue("$k", key.Trim());
                            cmd.Parameters.AddWithValue("$v", value ?? "");
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.SetMeta 失败：" + ex.Message); }
            }
        }

        public static List<(string Name, PrintOptions Options)> LoadTemplates()
        {
            var list = new List<(string, PrintOptions)>();
            EmptyTemplateCount = 0;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "SELECT name, options_json FROM print_templates ORDER BY created_at DESC";
                            using (var rd = cmd.ExecuteReader())
                            {
                                while (rd.Read())
                                {
                                    try
                                    {
                                        string name = rd.GetString(0);
                                        string json = (rd.GetString(1) ?? "").Trim();
                                        // V4.2.3 B1-c：V4.0~V4.2 因未开 IncludeFields 存成 "{}" 的空模板，
                                        // 读回 null 交给 UI 提示重存（与"参数恰好全为默认值"区分开）。
                                        if (json.Length == 0 || json.Replace(" ", "") == "{}")
                                        {
                                            EmptyTemplateCount++;
                                            list.Add((name, null));
                                            continue;
                                        }
                                        var opt = JsonSerializer.Deserialize<PrintOptions>(json, JsonOpt);
                                        if (opt != null) list.Add((name, opt));
                                    }
                                    catch { }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { FileLogger.Write("SqliteStore.LoadTemplates 失败：" + ex.Message); }
            }
            return list;
        }

        public static void DeleteTemplate(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            lock (Sync)
            {
                try
                {
                    using (var conn = new SqliteConnection(ConnStr))
                    {
                        conn.Open();
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = "DELETE FROM print_templates WHERE name=$n";
                            cmd.Parameters.AddWithValue("$n", name.Trim());
                            cmd.ExecuteNonQuery();
                        }
                    }
                }
                catch { }
            }
        }

        /// <summary>V4.1 自检：临时库真实读写冒烟（建表/插入/查询/删除），返回结果文本。不污染正式库。</summary>
        public static string SelfTest(string tempDir)
        {
            try
            {
                Directory.CreateDirectory(tempDir);
                string db = Path.Combine(tempDir, "selftest-" + Guid.NewGuid().ToString("N") + ".db");
                using (var conn = new SqliteConnection("Data Source=" + db))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "CREATE TABLE t(id INTEGER PRIMARY KEY, name TEXT);";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO t(name) VALUES('自检');";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM t;";
                        long cnt = (long)cmd.ExecuteScalar();
                        if (cnt != 1) return "失败：插入/查询计数异常 " + cnt;
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "DELETE FROM t;";
                        cmd.ExecuteNonQuery();
                    }
                }
                try { if (File.Exists(db)) File.Delete(db); } catch { }
                return "通过（建表/插入/查询/删除）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }

        /// <summary>
        /// V4.2 断言①自检：临时库模拟 v4.1 旧表（无 folder）→ EnsureSchema 幂等迁移 → 验证 folder 列出现、
        /// 旧行 folder 为空串（UI 显示 "—"）、重复执行不报错。不污染正式库。
        /// </summary>
        public static string SelfTestV42(string tempDir)
        {
            try
            {
                Directory.CreateDirectory(tempDir);
                string db = Path.Combine(tempDir, "selftest-v42-" + Guid.NewGuid().ToString("N") + ".db");
                using (var conn = new SqliteConnection("Data Source=" + db))
                {
                    conn.Open();

                    // 1. 建 v4.1 旧 schema（无 folder）并插入 2 行旧数据
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
CREATE TABLE print_history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    ext TEXT NOT NULL DEFAULT '',
    copies INTEGER NOT NULL DEFAULT 1,
    pages INTEGER NOT NULL DEFAULT 0,
    success INTEGER NOT NULL DEFAULT 0,
    time TEXT NOT NULL,
    printer TEXT NOT NULL DEFAULT '',
    engine TEXT NOT NULL DEFAULT ''
);";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO print_history(name, ext, copies, pages, success, time, printer, engine) VALUES('旧文档.docx','.docx',1,2,1,'2026-09-17 10:00:00','Microsoft Print to PDF','com')";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO print_history(name, ext, copies, pages, success, time, printer, engine) VALUES('旧表.xlsx','.xlsx',1,1,0,'2026-09-17 10:05:00','Microsoft Print to PDF','minipdf')";
                        cmd.ExecuteNonQuery();
                    }

                    // 2. 幂等迁移（跑两遍，第二遍验证不报错）
                    EnsureSchema(conn);
                    EnsureSchema(conn);

                    // 3. 验证 folder 列存在且旧行为空串
                    if (!HasColumn(conn, "print_history", "folder")) return "失败：迁移后 folder 列缺失";
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT COUNT(*) FROM print_history WHERE folder = ''";
                        long cnt = (long)cmd.ExecuteScalar();
                        if (cnt != 2) return "失败：旧行 folder 应为空串，实际 " + cnt;
                    }

                    // 4. 验证新写入（含 folder）可插入并读回
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "INSERT INTO print_history(name, ext, copies, pages, success, time, printer, engine, folder) VALUES('新文档.docx','.docx',1,1,1,'2026-09-17 11:00:00','Microsoft Print to PDF','minipdf','C:\\docs\\报告')";
                        cmd.ExecuteNonQuery();
                    }
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "SELECT folder FROM print_history WHERE name='新文档.docx'";
                        object v = cmd.ExecuteScalar();
                        if (v == null || (string)v != "C:\\docs\\报告") return "失败：folder 写入/读回异常";
                    }
                }
                try { if (File.Exists(db)) File.Delete(db); } catch { }
                return "通过（旧库迁移/幂等/旧行空串/新行读写）";
            }
            catch (Exception ex) { return "失败：" + ex.Message; }
        }
    }
}
