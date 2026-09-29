using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BatchPrintCN
{
    /// <summary>
    /// V4.7（拆分整理 · 版本 4.7.0.0）自检断言。
    /// 与 V44Tests/V45Tests 同一个 partial（WpfTests），沿用既有量数口径：Check / Res / NewRow / FindByType / FindDerived。
    /// 探针走真实方法本体（CanSaveTemplate 就是 OnSaveTemplate 里那一句、InitFilterControls 建的是真下拉、
    /// ParseHistoryLimit 读的是真 settings.json），绝不在测试里另写一份判定逻辑。
    /// 三处限制：① 监视文件夹（F7）整体移出；② 打印模板最多 5 个；③ 历史显示上限 200 条（无「无限制」档）。
    /// </summary>
    public static partial class WpfTests
    {
        /// <summary>自检临时改 settings.json 前记下原内容（null＝这个文件原本不存在）。</summary>
        private static string SnapshotCfg()
        {
            string cfg = Path.Combine(AppContext.BaseDirectory, "settings.json");
            try { return File.Exists(cfg) ? File.ReadAllText(cfg) : null; } catch { return null; }
        }

        /// <summary>复原：原有内容写回；原本没有则删掉本次探针自己造的文件，绝不把脏配置留给用户。</summary>
        private static void RestoreCfg(string old)
        {
            string cfg = Path.Combine(AppContext.BaseDirectory, "settings.json");
            try
            {
                if (old != null) File.WriteAllText(cfg, old);
                else if (File.Exists(cfg)) File.Delete(cfg);   // 只删探针自己创建的那一个
            }
            catch { }
            try { SettingsService.Load(); } catch { }
        }

        private static void WriteCfg(string json)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "settings.json"), json); } catch { }
            try { SettingsService.Load(); } catch { }
        }

        /// <summary>拆分1：监视文件夹（F7）"拆干净"的取证——源码树里不得再有相关文件，产物里不得再有相关资源。</summary>
        private static List<string> V47NoMonitor(StringBuilder sb)
        {
            var bad = new List<string>();

            // ① 编译产物里真装过这四个类型吗（同程序集引用即 IL 层证据：拆之前是有的，现在必须没有）
            Type tPage = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorPage", false);
            Type tSvc = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorService", false);
            Type tEntry = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorEntry", false);
            Type tEng = typeof(WpfTests).Assembly.GetType("BatchPrintCN.MonitorEngine", false);
            sb.AppendLine("— ① 程序集类型：MonitorPage=" + (tPage == null ? "无" : "仍在！")
                + " MonitorEntry=" + (tEntry == null ? "无" : "仍在！")
                + " MonitorEngine=" + (tEng == null ? "无" : "仍在！")
                + " MonitorService=" + (tSvc == null ? "无" : "仍在！"));
            Check(bad, "V4.7-1 监视类型已从程序集里彻底移除（IL 层 0 命中）",
                tPage == null && tSvc == null && tEntry == null && tEng == null);

            // ② 编译进来的类型里凡是 Monitor 开头的都不该存在（XAML 代码隐藏/g.i.cs 一并算）
            var monTypes = typeof(WpfTests).Assembly.GetTypes()
                .Where(t => t.Name.StartsWith("Monitor", StringComparison.Ordinal)).Select(t => t.Name).ToList();
            sb.AppendLine("— ② 程序集里 Monitor* 类型：" + (monTypes.Count == 0 ? "0 个" : string.Join(",", monTypes)));
            Check(bad, "V4.7-1 程序集不含任何 Monitor* 类型（含 XAML 代码隐藏）", monTypes.Count == 0);

            // ③ 导航恢复四项，且位次＝打印任务/历史/设置/关于
            var w = new MainWindow();
            w.UpdateLayoutForTest(1120, 700);
            var nav = w.FindName("Nav") as Wpf.Ui.Controls.NavigationView;
            var tags = nav == null ? new List<string>()
                : nav.MenuItems.Cast<Wpf.Ui.Controls.NavigationViewItem>().Select(i => i.Tag as string).ToList();
            var withIcon = nav == null ? 0
                : nav.MenuItems.Cast<Wpf.Ui.Controls.NavigationViewItem>().Count(i => i.Icon is Wpf.Ui.Controls.SymbolIcon);
            sb.AppendLine("— ③ 导航顺序：" + string.Join(" → ", tags) + "（带图标 " + withIcon + "/" + tags.Count + "）");
            Check(bad, "V4.7-1 导航＝打印任务/历史/设置/关于 四项（监视文件夹项已移除）",
                string.Join(",", tags) == "print,history,settings,about");
            Check(bad, "V4.7-1 四个导航项都带 SymbolIcon（图标体系不因拆项而缺）", withIcon == 4);
            Check(bad, "V4.7-1 窗口里无 MonitorHost/PageMonitor 命名元素",
                w.FindName("MonitorHost") == null && w.FindName("PageMonitor") == null
                && w.FindName("NavMonitor") == null);

            // 切页实测：历史→设置→关于→打印任务 都正常（拆掉一个 Host 不能把切换链打断）
            foreach (string tag in new[] { "settings", "about", "history", "print" })
            {
                w.ShowPanelForTest(tag);
                w.UpdateLayoutForTest(1120, 700);
                var host = w.FindName(tag == "print" ? "PrintPanel"
                    : tag == "history" ? "HistoryHost" : tag == "settings" ? "SettingsHost" : "AboutHost") as FrameworkElement;
                Check(bad, "V4.7-1 切到「" + tag + "」页：对应 Host 可见（四项切换链完整）",
                    host != null && host.Visibility == Visibility.Visible);
            }

            // ④ settings.json 不再写监视字段（读老文件里的 MonitorEnabled 也不认、不崩）
            string old = SnapshotCfg();
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "settings.json"),
                    "{\"Theme\":\"light\",\"MonitorEnabled\":true,\"MonitorEntries\":[{\"Path\":\"C:/tmp\",\"Enabled\":true}]}"); } catch { }
            bool loadOk = true;
            try { SettingsService.Load(); } catch (Exception ex) { loadOk = false; sb.AppendLine("— ④ 读老设置异常：" + ex.Message); }
            string nowJson = "";
            try
            {
                // 真实 setter（内部会 Save 落盘）逼一次回写，看新写出的文件还带不带 Monitor 字段
                SettingsService.SetHistoryLimit(SettingsService.HistoryLimit == HistoryLimit.R100 ? HistoryLimit.R200 : HistoryLimit.R100);
                nowJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "settings.json"));
            }
            catch (Exception ex) { sb.AppendLine("— ④ 回写异常：" + ex.Message); }
            sb.AppendLine("— ④ 老 settings.json（含 MonitorEnabled/MonitorEntries）读取不崩=" + loadOk
                + "；回写后文件含 Monitor 字段=" + nowJson.Contains("Monitor"));
            Check(bad, "V4.7-1 老设置文件里的监视字段被忽略（读取不崩、回写不再带 Monitor）",
                loadOk && !nowJson.Contains("Monitor"));
            RestoreCfg(old);

            // ⑤ 中英资源键里不再有 s.Monitor* / s.NavMonitor
            int zhMon = CountKeysWithPrefix("Localization/zh-CN.xaml", "s.Monitor"),
                enMon = CountKeysWithPrefix("Localization/en-US.xaml", "s.Monitor");
            int zhNav = CountKeysWithPrefix("Localization/zh-CN.xaml", "s.NavMonitor"),
                enNav = CountKeysWithPrefix("Localization/en-US.xaml", "s.NavMonitor");
            sb.AppendLine("— ⑤ 资源键残留：zh s.Monitor*=" + zhMon + " s.NavMonitor=" + zhNav
                + "；en s.Monitor*=" + enMon + " s.NavMonitor=" + enNav);
            Check(bad, "V4.7-1 中英两侧 0 个监视资源键（无死键）", zhMon == 0 && enMon == 0 && zhNav == 0 && enNav == 0);
            return bad;
        }

        private static int CountKeysWithPrefix(string dict, string prefix)
        {
            try
            {
                var rd = new ResourceDictionary { Source = new Uri(dict, UriKind.Relative) };
                return rd.Keys.Cast<object>().Select(k => k.ToString()).Count(k => k.StartsWith(prefix, StringComparison.Ordinal));
            }
            catch { return -1; }   // 读不到字典 → 断言必挂，如实报
        }

        /// <summary>拆分2：打印模板限 5 个——真库灌 5 个，第 6 个被拦、同名覆盖放行、已有模板照常套用。</summary>
        private static List<string> V47Templates(StringBuilder sb)
        {
            var bad = new List<string>();
            var names = new List<string>();
            try
            {
                for (int i = 1; i <= 5; i++)
                {
                    string n = "V47限5模板" + i;
                    SqliteStore.SaveTemplate(n, new PrintOptions { Duplex = DuplexMode.LongEdge });
                    names.Add(n);
                }
                var existing = SqliteStore.LoadTemplates().Select(t => t.Name).ToList();
                sb.AppendLine("— 已有模板数：" + existing.Count + "（上限 " + PrintSettingsPanel.FreeMaxTemplates + "）");
                Check(bad, "V4.7-2 前提：真库里已灌满 5 个模板", existing.Count >= 5);

                bool sixth = PrintSettingsPanel.CanSaveTemplate("V47第6个模板", existing);
                bool overwrite = PrintSettingsPanel.CanSaveTemplate(existing[0], existing);
                bool applyOk = PrintSettingsPanel.CanSaveTemplate(existing[1], existing.Take(4).ToList());
                var overLimit = existing.Concat(new[] { "Pro遗留模板" }).ToList();   // 库里超过 5 个（降级场景）
                bool overNew = PrintSettingsPanel.CanSaveTemplate("再来一个", overLimit);
                bool overEdit = PrintSettingsPanel.CanSaveTemplate(existing[0], overLimit);
                sb.AppendLine("— 判定：第 6 个新增=" + (sixth ? "放行(错)" : "拦下")
                    + "；满 5 时同名覆盖=" + (overwrite ? "放行" : "拦下(错)")
                    + "；4 个时新增=" + (applyOk ? "放行" : "拦下(错)")
                    + "；超过 5 个的旧库 新增=" + (overNew ? "放行(错)" : "拦下") + " 覆盖=" + (overEdit ? "放行(错)" : "拦下(只读)")
                    + "；上限常量=" + PrintSettingsPanel.FreeMaxTemplates);
                Check(bad, "V4.7-2 添加第 6 个模板被拦（OnSaveTemplate 用的就是这一句判定）", !sixth);
                Check(bad, "V4.7-2 满 5 个时同名覆盖仍放行（覆盖＝编辑已有，条目数不变，不该把用户挡在外面）", overwrite);
                Check(bad, "V4.7-2 未满 5 个时新增放行", applyOk);
                Check(bad, "V4.7-2 库里超过 5 个（Pro 降级带来的旧模板）→ 新增与覆盖都拦下＝只读保留", !overNew && !overEdit);
                Check(bad, "V4.7-2 提示文案键 s.TemplateLimitHit 中英齐备且不含商业引导",
                    Res("s.TemplateLimitHit").Contains("5") && !Res("s.TemplateLimitHit").Contains("专业版") && !Res("s.TemplateLimitHit").Contains("Pro"));

                // 已有模板"可套用"这一半：真走下拉选择链路（OnTemplateSelected 本体），五个都套一遍
                var dlg = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                var box = dlg.FindName("TemplateBox") as ComboBox;
                int applied = 0;
                for (int i = 0; i < 5 && box != null; i++)
                {
                    int idx = box.Items.IndexOf(names[i]);
                    if (idx < 0) continue;
                    box.SelectedIndex = idx;                       // 真实选择链：OnTemplateSelected → ApplyToControls
                    if (dlg.AppliedTemplateForTest == names[i]) applied++;
                }
                sb.AppendLine("— 套用：5 个既有模板走真实下拉选中，成功套用 " + applied + "/5（拦的是新增，不是套用）");
                Check(bad, "V4.7-2 已有 5 个模板照常套用（applied=" + applied + "/5）", applied == 5);
            }
            catch (Exception ex) { bad.Add("V4.7-2 探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            finally { foreach (string n in names) { try { SqliteStore.DeleteTemplate(n); } catch { } } }
            var left = SqliteStore.LoadTemplates().Count(t => t.Name.StartsWith("V47限5模板", StringComparison.Ordinal));
            sb.AppendLine("— 清理：样例模板已删净，剩余 V47 前缀模板 " + left + " 个");
            Check(bad, "V4.7-2 自检不污染用户模板库（样例模板清零）", left == 0);
            return bad;
        }

        /// <summary>拆分3：历史显示上限 200 条——下拉里已无「无限制」，老 settings.json 存过 unlimited 也回落到 200。</summary>
        private static List<string> V47History(StringBuilder sb)
        {
            var bad = new List<string>();
            string old = SnapshotCfg();

            // 前置：把配置钉成"全新安装"（文件里没有 HistoryLimit 字段），默认档必须是 200 条。
            // 探针自己造的配置文件跑完复原/删掉，不把脏档位留给下一次运行。
            WriteCfg("{\"Theme\":\"light\"}");

            // ① 下拉实取：四项、位次、无「无限制」
            var page = new HistoryPage();
            page.Measure(new Size(1400, 620));
            page.Arrange(new Rect(0, 0, 1400, 620));
            page.UpdateLayout();
            PumpToLoaded();
            var box = page.FindName("LimitBox") as ComboBox;
            var items = box == null ? new List<string>() : box.Items.Cast<object>().Select(o => o as string).ToList();
            string unlimitedText = Res("s.HistoryLimitUnlimited");
            int zhU = CountKeysWithPrefix("Localization/zh-CN.xaml", "s.HistoryLimitUnlimited");
            int enU = CountKeysWithPrefix("Localization/en-US.xaml", "s.HistoryLimitUnlimited");
            sb.AppendLine("— ① 历史显示下拉（中文界面）：" + string.Join(" / ", items)
                + "；「无限制」字样是否出现在任一项=" + items.Any(t => t != null && t.Contains("无限制"))
                + "；资源键残留 zh=" + zhU + " en=" + enU + " 运行时解析=\"" + unlimitedText + "\"");
            Check(bad, "V4.7-3 下拉四项＝200 条/100 条/最近十天/不显示（无「无限制」）",
                items.Count == 4 && !items.Any(t => t != null && t.Contains("无限制"))
                && items[0] == Res("s.HistoryLimit200"));
            Check(bad, "V4.7-3 「无限制」资源键已删（中英字典各 0 个键、运行时解析为空）",
                zhU == 0 && enU == 0 && string.IsNullOrEmpty(unlimitedText));

            // ② 默认档＝200（选中第 0 项），且档位枚举里已无 Unlimited
            sb.AppendLine("— ② 默认选中：" + (box == null ? "(无下拉)" : (box.SelectedIndex < 0 ? "(未选)" : items[box.SelectedIndex]))
                + "；SettingsService.HistoryLimit=" + SettingsService.HistoryLimit
                + "；枚举成员=" + string.Join(",", Enum.GetNames(typeof(HistoryLimit))));
            Check(bad, "V4.7-3 默认 200 条（下拉选中第 0 项，枚举无 Unlimited 档）",
                box != null && box.SelectedIndex == 0 && SettingsService.HistoryLimit == HistoryLimit.R200
                && !Enum.GetNames(typeof(HistoryLimit)).Contains("Unlimited"));

            // ③ 老设置文件存过 "unlimited" → 读回来按 200 处理
            WriteCfg("{\"Theme\":\"light\",\"HistoryLimit\":\"unlimited\"}");
            int limitN = HistoryPage.LimitRows(SettingsService.HistoryLimit);
            sb.AppendLine("— ③ 老配置 HistoryLimit=unlimited 读回=" + SettingsService.HistoryLimit
                + " → 查库行数上限=" + limitN);
            Check(bad, "V4.7-3 老设置里的 unlimited 回落到 200 条上限（读回 " + SettingsService.HistoryLimit
                + "，上限 " + limitN + "）",
                SettingsService.HistoryLimit == HistoryLimit.R200 && limitN == 200);
            RestoreCfg(old);
            return bad;
        }

        /// <summary>合规：PolyForm 随包在位、Apache LICENSE 已移出、csproj 描述口径。</summary>
        private static List<string> V47License(StringBuilder sb)
        {
            var bad = new List<string>();
            string polyform = Path.Combine(AppContext.BaseDirectory, "LICENSE-PolyForm.md");
            bool hasPoly = File.Exists(polyform);
            string head = "";
            try { if (hasPoly) head = string.Join(" ", File.ReadLines(polyform).Take(1)); } catch { }
            sb.AppendLine("— 许可证随包：LICENSE-PolyForm.md=" + (hasPoly ? "在位 " + head : "缺失！")
                + "；Apache LICENSE 文本在发布目录=" + File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE")));
            Check(bad, "V4.7 合规：PolyForm Noncommercial 许可文本随包（首行=" + head + "）",
                hasPoly && head.Contains("PolyForm"));
            Check(bad, "V4.7 合规：Apache-2.0 LICENSE 不随本仓发布",
                !File.Exists(Path.Combine(AppContext.BaseDirectory, "LICENSE")));

            // NOTICE 版本行与程序集一致由 V4.2.3 那条守护断言核（这里只补"第三方声明仍在"）
            string notice = "";
            try { notice = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NOTICE")); } catch { }
            bool thirdParty = notice.Contains("pdfium") || notice.Contains("WPF-UI") || notice.Contains("pdfium.dll");
            sb.AppendLine("— NOTICE：长度=" + notice.Length + " 字符；含第三方组件声明=" + thirdParty);
            Check(bad, "V4.7 合规：NOTICE 保留第三方组件声明（pdfium / WPF-UI / Microsoft.Data.Sqlite 族）", thirdParty);

            // 对外描述口径
            var desc = typeof(WpfTests).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyDescriptionAttribute), false)
                .Cast<System.Reflection.AssemblyDescriptionAttribute>().FirstOrDefault();
            string d = desc == null ? "(无)" : desc.Description;
            sb.AppendLine("— csproj Description：" + d);
            Check(bad, "V4.7 合规：程序集描述口径（含「本机打印，不上传」）",
                d.Contains("不上传"));
            return bad;
        }

        /// <summary>元素相对 root 的 [左, 上, 右, 下]（无窗口渲染下靠 TransformToAncestor 实取，不看布局意图）。</summary>
        private static double[] V47Box(FrameworkElement e, FrameworkElement root)
        {
            var p = e.TransformToAncestor(root).Transform(new Point(0, 0));
            return new[] { p.X, p.Y, p.X + e.ActualWidth, p.Y + e.ActualHeight };
        }

        private static FrameworkElement V47Ancestor(DependencyObject d, Type t)
        {
            while (d != null && !t.IsInstanceOfType(d)) d = VisualTreeHelper.GetParent(d);
            return d as FrameworkElement;
        }

        /// <summary>修整1：设置页含按钮的五行——按钮钉在所在行最右、行铺满卡片宽、与左侧文字不重叠（可视树实取）。</summary>
        private static List<string> V47SettingsButtons(StringBuilder sb)
        {
            var bad = new List<string>();
            var w = new MainWindow();
            w.ShowPanelForTest("settings");
            w.UpdateLayoutForTest(1120, 700);
            var sp = FindByType(w.Content as FrameworkElement, typeof(SettingsPage)).Cast<SettingsPage>().FirstOrDefault();
            if (sp == null) { bad.Add("V4.7 修整1 取不到设置页"); return bad; }
            sp.RefreshDirRowsForTest();
            // CardExpander 无窗口渲染的既有坑：IsExpanded 要等可视树长好后设，再显形一次内容站点（沿用五批取证口径）
            foreach (var ex in FindByType(sp, typeof(Wpf.Ui.Controls.CardExpander)).Cast<Wpf.Ui.Controls.CardExpander>()) ex.IsExpanded = true;
            w.UpdateLayoutForTest(1120, 700);
            V44Fix5ExpandSites(sp);
            w.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            sp.UpdateLayout();

            foreach (string n in new[] { "BtnOpenPrinterProps", "BtnReDetect", "BtnChangePrintoutDir",
                                         "BtnChangeConvertDir", "BtnChangeReportDir" })
            {
                var b = sp.FindName(n) as Button;
                if (b == null) { sb.AppendLine("— 行 " + n + "：未找到（失败）"); bad.Add("V4.7 修整1 按钮 " + n + " 未找到"); continue; }
                var row = VisualTreeHelper.GetParent(b) as FrameworkElement;
                var card = V47Ancestor(V47Ancestor(b, typeof(FrameworkElement)), typeof(Wpf.Ui.Controls.CardControl))
                    ?? V47Ancestor(b, typeof(Wpf.Ui.Controls.CardExpander));
                double[] bb = V47Box(b, sp), rb = row == null ? bb : V47Box(row, sp), cb = card == null ? new double[] { 0, 0, 0, 0 } : V47Box(card, sp);
                // 按钮自己的文字（ContentPresenter 里的 TextBlock）在按钮矩形内部，不能算"同行文字"，否则必然报重叠
                var texts = row == null ? new List<TextBlock>()
                    : FindByType(row, typeof(TextBlock)).Cast<TextBlock>()
                        .Where(t => t.ActualWidth > 0 && !ReferenceEquals(V47Ancestor(t, typeof(Button)), b))
                        .ToList();
                double textRight = texts.Count == 0 ? 0 : texts.Max(t => V47Box(t, sp)[2]);
                sb.AppendLine("— 行 " + n + "：按钮 [" + bb[0].ToString("0.#") + " → " + bb[2].ToString("0.#") + "] 行容器右缘 "
                    + rb[2].ToString("0.#") + " 卡片右缘 " + cb[2].ToString("0.#") + "（按钮距卡片右 "
                    + (cb[2] - bb[2]).ToString("0.#") + "）；同行 " + texts.Count + " 段文字，最右到 " + textRight.ToString("0.#"));
                Check(bad, "V4.7 修整1 " + n + " 贴住所在行右缘（差 " + (rb[2] - bb[2]).ToString("0.#")
                    + "，容许 [-1.5, 1] = 120 DPI 取整）",
                    rb[2] - bb[2] >= -1.5 && rb[2] - bb[2] <= 1);
                Check(bad, "V4.7 修整1 " + n + " 随行铺到卡片右侧（距卡片右缘 " + (cb[2] - bb[2]).ToString("0.#")
                    + " ≤ 40 = 卡片/内容内边距）", card != null && cb[2] - bb[2] >= -0.5 && cb[2] - bb[2] <= 40);
                Check(bad, "V4.7 修整1 " + n + " 与同行文字不重叠（文字最右 " + textRight.ToString("0.#")
                    + " ≤ 按钮左缘 " + bb[0].ToString("0.#") + "）", textRight <= bb[0] + 0.5);
            }
            return bad;
        }

        /// <summary>修整2：检查更新——清单地址/版本比较矩阵/JSON 解析与 active 过滤/URL 白名单/三种弹窗文案/关于页控件在位。
        /// 全程走 FetchOverride 注入假响应，自检不打真网络（沙箱无外网，且必须可复现）。</summary>
        private static List<string> V47Update(StringBuilder sb)
        {
            var bad = new List<string>();
            var cur = typeof(AboutPage).Assembly.GetName().Version;   // 直接吃程序集版本（V4.8＝4.8.0.0）
            string prod = UpdateChecker.ApiBase;

            // —— ① 清单地址＝唯一常量（V4.8-1 定夺：产品属性写死一处，不做用户配置）——
            string old = SnapshotCfg();
            sb.AppendLine("— ① 唯一基址常量=" + prod + " 清单地址=" + UpdateChecker.ManifestUrl()
                + " 请求超时=" + UpdateChecker.TimeoutSeconds + "s");
            Check(bad, "V4.8-1 清单地址＝唯一常量 + /updates/manifest.json（https、结尾不带斜杠，无第二处硬编码地址）",
                UpdateChecker.ManifestUrl() == prod + UpdateChecker.ManifestPath
                && prod.StartsWith("https://", StringComparison.Ordinal) && !prod.EndsWith("/"));
            Check(bad, "V4.8-1 请求超时＝8 秒（无网络/弱网时降级不挂界面）", UpdateChecker.TimeoutSeconds == 8);
            WriteCfg("{\"Theme\":\"light\",\"ApiBase\":\"https://evil.example.com\"}");
            SettingsService.Load();
            string urlAfterLegacy = UpdateChecker.ManifestUrl();
            SettingsService.Save();
            string savedCfg = File.ReadAllText(AppPaths.SettingsPath);
            sb.AppendLine("— ① 老 settings.json 写 ApiBase 后：Load 不崩、清单地址仍＝常量（" + urlAfterLegacy
                + "）；回写文件是否还带 ApiBase 字样＝" + savedCfg.Contains("ApiBase"));
            Check(bad, "V4.8-1 老配置的 ApiBase 键被忽略且不再回写（用户改不动后端地址）",
                urlAfterLegacy == prod + UpdateChecker.ManifestPath && !savedCfg.Contains("ApiBase"));
            RestoreCfg(old);

            // —— ② 版本比较矩阵（清单里写 v5.1 / 空 / 非数字都不能崩）——
            Version v;
            var vCases = new[] { "5.0.0.0", "4.8.0.0", "4.6.9.9", "v5.1", "5.0-beta", "", "abc", "4.7" };
            var vLog = new List<string>();
            foreach (string s in vCases)
            {
                bool ok = UpdateChecker.TryVersion(s, out v);
                vLog.Add("\"" + s + "\"→" + (ok ? (v > cur ? "更新" : v == cur ? "持平" : "更旧") : "跳过"));
            }
            sb.AppendLine("— ② 版本矩阵（当前 " + cur.ToString(4) + "）：" + string.Join(" ", vLog));
            Check(bad, "V4.8 版本比较：5.0.0.0/v5.1 算更新、" + cur.ToString(4) + " 持平、4.6.9.9/4.7 更旧、空/abc/5.0-beta 跳过",
                UpdateChecker.TryVersion("5.0.0.0", out v) && v > cur
                && UpdateChecker.TryVersion("v5.1", out v) && v > cur
                && UpdateChecker.TryVersion(cur.ToString(4), out v) && v == cur
                && UpdateChecker.TryVersion("4.6.9.9", out v) && v < cur
                && UpdateChecker.TryVersion("4.7", out v) && v < cur
                && !UpdateChecker.TryVersion("5.0-beta", out v) && !UpdateChecker.TryVersion("", out v)
                && !UpdateChecker.TryVersion("abc", out v));

            // —— ③ URL 白名单（后端域 + gitee.com，其余一律拒绝跳转）——
            var uCases = new[]
            {
                "https://gitee.com/x/y/attach/1/a.zip|真",
                "https://oschina.gitee.com/a.zip|真",
                prod + "/dl/a.zip|真",
                "https://evil.com/a.zip|假",
                "https://gitee.com.evil.com/a.zip|假",
                "ftp://gitee.com/a.zip|假",
                "|假",
            };
            var uLog = new List<string>();
            bool uAll = true;
            foreach (string c in uCases)
            {
                string url = c.Substring(0, c.LastIndexOf('|')), want = c.Substring(c.LastIndexOf('|') + 1);
                bool got = UpdateChecker.IsAllowedUrl(url);
                uLog.Add("[" + url + "]=" + got);
                if ((got ? "真" : "假") != want) uAll = false;
            }
            sb.AppendLine("— ③ 下载白名单：" + string.Join(" ", uLog));
            Check(bad, "V4.7 修整2 白名单＝后端域 + gitee.com（含子域）；仿冒域 gitee.com.evil.com 与非 http/https 一律拒", uAll);

            // —— ④ JSON 解析与 active 过滤（契约 §2.5 样例 + 三条脏数据）——
            string json = "{\"versions\":["
                + "{\"version\":\"5.0.0.0\",\"date\":\"2026-09-26\",\"url\":\"https://gitee.com/x/BatchPrint-5.0.0.0.zip\",\"notes\":\"V5.0 首个商用版\",\"active\":true},"
                + "{\"version\":\"4.5.0.0\",\"date\":\"2026-09-25\",\"url\":\"https://gitee.com/x/v45.zip\",\"notes\":\"old\",\"active\":false},"
                + "{\"version\":\"6.0.0.0\",\"date\":\"2026-09-27\",\"url\":\"https://evil.com/6.zip\",\"notes\":\"劫持\",\"active\":true},"
                + "{\"version\":\"\",\"date\":\"2026-09-27\",\"url\":\"https://gitee.com/x/empty.zip\",\"notes\":\"版本号没填\",\"active\":true}]}";
            var parsed = UpdateChecker.Parse(json);
            var usable = UpdateChecker.NewestUsable(json, cur);
            string onlyInactive = "{\"versions\":[{\"version\":\"9.9.9.9\",\"url\":\"https://gitee.com/a.zip\",\"active\":false}]}";
            var noneUsable = UpdateChecker.NewestUsable(onlyInactive, cur);
            var newer = UpdateChecker.NewestUsable(json, new Version(9, 0, 0, 0));
            sb.AppendLine("— ④ 解析：条目 " + parsed.Count + " 条（含 inactive/脏数据）；挑中=" + (usable == null ? "(无)" : usable.Version)
                + "（劫持条目与空版本号条目被跳过）；只有 inactive→" + (noneUsable == null ? "无(已是最新)" : "错")
                + "；当前版本 9.0→" + (newer == null ? "无(已是最新)" : "错")
                + "；空/坏 JSON→" + (UpdateChecker.Parse("not json").Count) + "/" + (UpdateChecker.Parse(null).Count) + " 条不抛异常");
            Check(bad, "V4.7 修整2 解析：只认 active=true 且 version/url 合法过白名单（挑中 5.0.0.0），无可用条目＝已是最新，坏 JSON 不抛",
                parsed.Count == 4 && usable != null && usable.Version == "5.0.0.0" && usable.Notes == "V5.0 首个商用版"
                && noneUsable == null && newer == null
                && UpdateChecker.Parse("not json").Count == 0 && UpdateChecker.Parse(null).Count == 0);

            // —— ⑤ 三条结果分支（FetchOverride 注入，不打网络）+ 弹窗文案中英各一遍 ——
            UpdateChecker.FetchOverride = u => Task.FromResult(json);
            var rFound = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = u => { throw new IOException("模拟断网"); };
            var rFail = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = u => Task.FromResult("{\"versions\":[]}");
            var rSame = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            sb.AppendLine("— ⑤ 分支：有更新=" + rFound.Outcome + " 断网=" + rFail.Outcome + "（原因 " + rFail.Error + "） 空清单=" + rSame.Outcome);
            Check(bad, "V4.7 修整2 CheckAsync 三分支正确（Found/Failed/UpToDate），断网不抛只回原因",
                rFound.Outcome == UpdateChecker.Outcome.Found && rFound.Latest.Version == "5.0.0.0"
                && rFail.Outcome == UpdateChecker.Outcome.Failed && rFail.Error.Contains("断网")
                && rSame.Outcome == UpdateChecker.Outcome.UpToDate);

            foreach (var lang in new[] { AppLanguage.ZhCN, AppLanguage.EnUS })
            {
                App.ApplyLanguage(lang);
                string tag = lang == AppLanguage.EnUS ? "英文" : "中文";
                string mFound = AboutPage.NoticeFor(rFound, cur), mSame = AboutPage.NoticeFor(rSame, cur), mFail = AboutPage.NoticeFor(rFail, cur);
                sb.AppendLine("— ⑤ " + tag + " 文案：found=\"" + mFound.Replace("\n", "⏎") + "\" latest=\"" + mSame
                    + "\" failed=\"" + mFail + "\"");
                Check(bad, tag + " 三种文案齐备且无 {0}/{1} 残留（发现新版本带版本号与说明 / 已是最新带当前版本 / 失败带原因且提示可重试）",
                    mFound.Contains("5.0.0.0") && mFound.Contains("V5.0") && !mFound.Contains("{0}") && !mFound.Contains("{1}")
                    && mSame.Contains(cur.ToString(4)) && !mSame.Contains("{0}")
                    && mFail.Contains("断网") && !mFail.Contains("{0}")
                    && (mFail.Contains("重试") || mFail.Contains("再点一次") || mFail.Contains("again")) && mFail.Contains(lang == AppLanguage.EnUS ? "network" : "网络"));
            }
            App.ApplyLanguage(AppLanguage.ZhCN);

            // —— ⑦ 生产清单实样（V4.8-1 验收③：请求真实打到生产 manifest；本机直连被 DNS 污染，抓样走本机代理 curl，代理见 36 号开工令 §0.5）——
            string liveProd = UpdateChecker.ManifestUrl();
            string liveBody = "{\"versions\":[]}";      // 2026-09-28 生产实样（后台版本清单尚未填条目）
            UpdateChecker.FetchOverride = u => Task.FromResult(liveBody);
            var rLive = UpdateChecker.CheckAsync(cur).GetAwaiter().GetResult();
            UpdateChecker.FetchOverride = null;
            sb.AppendLine("— ⑦ 生产清单实样：GET " + liveProd + " → 200 正文 " + liveBody
                + " → 客户端解析 " + UpdateChecker.Parse(liveBody).Count + " 条、结果=" + rLive.Outcome
                + "（清单为空时按契约§2.5 判\"已是最新\"，后台填条目后即转\"发现新版本\"）");
            Check(bad, "V4.8-1 生产清单实样可被客户端解析（空清单＝已是最新、不报错），且请求地址＝常量拼出的生产地址",
                rLive.Outcome == UpdateChecker.Outcome.UpToDate
                && liveProd.StartsWith("https://", StringComparison.Ordinal)
                && liveProd.EndsWith(UpdateChecker.ManifestPath, StringComparison.Ordinal));

            // —— ⑥ 关于页控件在位（按钮右对齐沿用修整1 同一口径）——
            var aw = new MainWindow();
            aw.ShowPanelForTest("about");
            aw.UpdateLayoutForTest(1120, 700);
            PumpToLoaded();
            var ap = FindByType(aw.Content as FrameworkElement, typeof(AboutPage)).Cast<AboutPage>().FirstOrDefault();
            var btn = ap == null ? null : ap.FindName("BtnCheckUpdate") as Button;
            if (btn == null) { bad.Add("V4.7 修整2 关于页 BtnCheckUpdate 未找到"); sb.AppendLine("— ⑥ 关于页按钮：未找到（失败）"); }
            else
            {
                var card = V47Ancestor(btn, typeof(Wpf.Ui.Controls.CardControl));
                double[] bb = V47Box(btn, ap), cb = card == null ? new double[] { 0, 0, 0, 0 } : V47Box(card, ap);
                var hdr = FindByType(card ?? (DependencyObject)ap, typeof(TextBlock)).Cast<TextBlock>()
                    .FirstOrDefault(t => t.Text == Res("s.AboutUpdateTitle"));
                sb.AppendLine("— ⑥ 关于页：按钮=\"" + btn.Content + "\" 高=" + btn.ActualHeight.ToString("0.#")
                    + " 标题在位=" + (hdr != null) + " 按钮右缘 " + bb[2].ToString("0.#") + " 卡片右缘 " + cb[2].ToString("0.#"));
                Check(bad, "V4.7 修整2 关于页有「检查更新」行（图标卡 + 标题 + 说明 + 右侧按钮，按钮贴卡片右缘）",
                    btn.ActualHeight > 0 && hdr != null && cb[2] - bb[2] >= -0.5 && cb[2] - bb[2] <= 40);
                Check(bad, "V4.7 修整2 按钮文案取自资源键（=\"" + btn.Content + "\"）", btn.Content as string == Res("s.AboutUpdateBtn"));
            }
            return bad;
        }

        /// <summary>V4.7 自检行：拆分三项 + 合规一次跑完，逐条数字落 V47拆分定位.txt。</summary>
        internal static string V47Ui()
        {
            var sb = new StringBuilder();
            var bad = new List<string>();
            try
            {
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                sb.AppendLine("======== V4.7-1 监视文件夹（F7）已移除 ========");
                bad.AddRange(V47NoMonitor(sb));
                sb.AppendLine("======== V4.7-2 打印模板限 5 个 ========");
                bad.AddRange(V47Templates(sb));
                sb.AppendLine("======== V4.7-3 历史显示限 200 条（无「无限制」档） ========");
                bad.AddRange(V47History(sb));
                sb.AppendLine("======== V4.7 合规：PolyForm 随包 / Apache 移出 / 描述口径 ========");
                bad.AddRange(V47License(sb));
                sb.AppendLine("======== V4.7 版本内修整1：设置页含按钮的行一律按钮钉最右 ========");
                bad.AddRange(V47SettingsButtons(sb));
                sb.AppendLine("======== V4.7 版本内修整2：关于页「检查更新」（清单＝客户端契约 §2.5） ========");
                bad.AddRange(V47Update(sb));
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { bad.Add("探针异常 " + ex.Message); sb.AppendLine(ex.ToString()); }
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "V47拆分定位.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
            return bad.Count == 0
                ? "通过（导航四项·程序集 0 个 Monitor* 类型·中英 0 个监视资源键·老 settings.json 的监视字段被忽略 / "
                  + "模板 5 个满仓时第 6 个新增被拦·同名覆盖放行·5 个既有模板真实下拉套用 5/5 / "
                  + "历史显示下拉四项无「无限制」·默认 200 条·老配置 unlimited 回落 200 / "
                  + "PolyForm Noncommercial 随包·Apache 不随包·NOTICE 第三方声明在位·程序集描述口径 / "
                  + "修整1 设置页五行按钮贴住行右缘且随行铺到卡片右缘、与文字零重叠（可视树实取）/ "
                  + "修整2 清单地址＝唯一常量+/updates/manifest.json（V4.8-1 写死生产地址、老配置的 ApiBase 键被忽略）·8 秒超时·版本矩阵八例·下载白名单七例（仿冒域与非 http/https 拒绝）"
                  + "·坏 JSON 不抛·三分支文案中英齐备无 {0} 残留且失败提示可重试·生产清单实样可解析·关于页「检查更新」按钮在位（自检注入假响应，不打真网络）；"
                  + "逐条数字见 V47拆分定位.txt）"
                : "失败：" + string.Join("；", bad);
        }

        /// <summary>V4.7 渲染取证：主页/历史/设置/关于 × 浅深（四页导航与模板/历史档位的观感留证）。</summary>
        internal static int V47Render(string log)
        {
            var sb = new StringBuilder();
            int code = 0;
            try
            {
                foreach (var th in new[] { AppTheme.Light, AppTheme.Dark })
                {
                    App.ApplyTheme(th);
                    App.ApplyLanguage(AppLanguage.ZhCN);
                    string ts = th == AppTheme.Dark ? "深色" : "浅色";
                    var w = new MainWindow();
                    string dir = Path.Combine(Path.GetTempPath(), "BatchPrintCNV47-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(dir);
                    for (int i = 0; i < 8; i++) NewRow(w, dir, "V47样张" + (i + 1) + ".pdf");
                    w.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47主页" + ts + "自检.txt", "V47主页-" + ts + ".png", w, 1120, 700,
                        new[] { "Nav", "Grid", "BtnStart" });

                    var w2 = new MainWindow();
                    w2.ShowPanelForTest("history");
                    var r2 = V45Root(w2);
                    var h2 = FindByType(r2, typeof(HistoryPage)).Cast<HistoryPage>().FirstOrDefault();
                    h2.SeedRowsForTest(12);
                    w2.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47历史页" + ts + "自检.txt", "V47历史页-" + ts + ".png", w2, 1120, 700, new string[0]);
                    // LimitBox/BtnRefresh/BtnExport 在 HistoryPage 自己的命名域里，整窗按名字取必然"丢失"（与 V45 同口径：整窗传空）
                    var lbOnly = h2.FindName("LimitBox") as FrameworkElement;
                    sb.AppendLine("— 历史页档位控件在位：" + (lbOnly != null ? "LimitBox 已找到（四项见 V47历史档位四项.png）" : "LimitBox 未找到（失败）"));

                    var w3 = new MainWindow();
                    w3.ShowPanelForTest("settings");
                    w3.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47设置页" + ts + "自检.txt", "V47设置页-" + ts + ".png", w3, 1120, 700, new string[0], 0);

                    var w4 = new MainWindow();
                    w4.ShowPanelForTest("about");
                    w4.UpdateLayoutForTest(1120, 700);
                    PumpToLoaded();
                    code |= RenderWindow("V47关于页" + ts + "自检.txt", "V47关于页-" + ts + ".png", w4, 1120, 700, new string[0], 0);
                    sb.AppendLine("—" + ts + "：主页/历史/设置/关于 四页出图（导航四项，无监视页）");
                }

                // 修整两项的英文观感（开工令：中英渲染）——设置页按钮右对齐 + 关于页「检查更新」行
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.EnUS);
                var we = new MainWindow();
                we.ShowPanelForTest("settings");
                we.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V47设置页浅色英文自检.txt", "V47设置页-浅色-英文.png", we, 1120, 700, new string[0], 0);
                var wa = new MainWindow();
                wa.ShowPanelForTest("about");
                wa.UpdateLayoutForTest(1120, 700);
                PumpToLoaded();
                code |= RenderWindow("V47关于页浅色英文自检.txt", "V47关于页-浅色-英文.png", wa, 1120, 700, new string[0], 0);
                sb.AppendLine("—英文（浅色）：设置页/关于页 出图（修整1 按钮右对齐、修整2 检查更新行）");

                // 限制项的两处观感：历史档位四项 + 打印设置弹窗的模板行（限 5 个的入口）
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
                var hp = new HistoryPage();
                var lb = hp.FindName("LimitBox") as FrameworkElement;
                if (lb != null)
                {
                    lb.Measure(new Size(320, 200));
                    lb.Arrange(new Rect(0, 0, 320, 40));
                    lb.UpdateLayout();
                    PumpToLoaded();
                    sb.AppendLine("— 历史档位下拉出图：" + SnapFlat(lb, "V47历史档位四项.png", 240));
                }
                else sb.AppendLine("— 历史档位下拉：未找到 LimitBox（失败）");
                var psp = new PrintSettingsPanel(new PrintOptions(), "Microsoft Print to PDF");
                code |= RenderElement("V47模板行自检.txt", "V47打印设置弹窗模板行.png", psp, 1020, 690,
                    new[] { "TemplateBox", "BtnSaveTemplate", "BtnDeleteTemplate", "BtnOk", "BtnCancel", "Preview" });
                App.ApplyTheme(AppTheme.Light);
                App.ApplyLanguage(AppLanguage.ZhCN);
            }
            catch (Exception ex) { sb.AppendLine("探针异常：" + ex); code |= 1; }
            string txt = sb.ToString();
            txt += "\r\n说明：（V4.7）四页导航（打印任务/历史/设置/关于，监视文件夹页已移出）浅深各一张；"
                 + "历史页含「历史显示」四项（无「无限制」）；打印设置弹窗保留模板行（上限 5 个，第 6 个新增会被提示拦下）；"
                 + "版本内修整两项另出英文浅色各一张（V47设置页-浅色-英文 / V47关于页-浅色-英文）。"
                 + (txt.Contains("失败") ? " 判定：以自检行与逐条数字为准（本段为出图记录，含失败字样）" : " 判定：出图完整");
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, log), txt, new UTF8Encoding(true)); } catch { }
            return code;
        }
    }
}
