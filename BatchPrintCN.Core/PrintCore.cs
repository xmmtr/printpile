using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BatchPrintCN
{
    public enum DocKind { Pdf, Word, Excel, Ppt, Text, Image }

    public enum DuplexMode { Simplex, LongEdge, ShortEdge }

    public enum OrientationMode { Auto, Portrait, Landscape }

    public enum ColorMode { Color, BlackWhite }

    /// <summary>F6（V4.3）：奇偶页（按原 PDF 页码 1 基奇偶过滤，独立于双面）。</summary>
    public enum PageParityMode { All, Odd, Even }

    /// <summary>F6（V4.3）：缩放模式。FitPage=适合页面（现状默认）/ None=不缩放（原尺寸）/
    /// ShrinkOnly=缩小适配（只缩不放）/ StretchFill=拉伸适配（填满可放）/ Custom=按 ScalePercent。</summary>
    public enum ScaleMode { FitPage, None, ShrinkOnly, StretchFill, Custom }

    public class PrintOptions
    {
        public DuplexMode Duplex = DuplexMode.Simplex;
        public OrientationMode Orientation = OrientationMode.Auto;
        public ColorMode Color = ColorMode.Color;
        public PaperKind Paper = PaperKind.A4;
        public string PaperDisplayName = "A4";
        public bool FitToPage = true;
        public int ScalePercent = 100;
        public int PagesPerSheet = 1;

        // F6（V4.3）：打印参数全集——全部带默认值 = 现状行为；旧模板缺字段时反序列化取默认
        public PageParityMode PageParity = PageParityMode.All;   // 奇偶页（默认全部）
        public string PaperSourceName = null;                    // 纸盒名（null/空 = 自动，走打印机默认纸盒）
        public bool Booklet = false;                             // 小册子（骑马订页序 + 强制 2 版横排）
        /// <summary>F6 缩放模式（可空：旧模板 JSON 无此字段 → 按 FitToPage 反推，保证旧模板行为不变）。</summary>
        public ScaleMode? Scale = ScaleMode.FitPage;

        /// <summary>生效缩放模式：新字段缺省（旧模板）时由 FitToPage 反推——FitToPage=true→适合页面，false→自定义百分比。</summary>
        public ScaleMode EffectiveScale
        {
            get { return Scale ?? (FitToPage ? ScaleMode.FitPage : ScaleMode.Custom); }
        }

        // V3.2：由 UI 在启动打印前注入
        public bool KeepConvertedPdf = false;          // 保留转换产物（中间 PDF）
        public string ConvertOutputDirectory = null;   // 转换产物输出目录（null=默认）
        public string PrintoutDirectory = null;        // Microsoft Print to PDF 自动落盘目录（null=默认）

        /// <summary>F5（V4.4）：逐文件覆盖用——复制一份当前参数再按覆盖项改，全局实例本身绝不被改。</summary>
        public PrintOptions Clone() { return (PrintOptions)MemberwiseClone(); }

        /// <summary>
        /// 生效缩放的中文口径（45 号·改2 抽出来给作业摘要行与 Summary() 共用一处，避免两处措辞走偏；字符串与抽出前逐字一致）。
        /// </summary>
        public string ScalingText()
        {
            return EffectiveScale == ScaleMode.FitPage ? "缩放到合适页面"
                : EffectiveScale == ScaleMode.None ? "不缩放"
                : EffectiveScale == ScaleMode.ShrinkOnly ? "缩小适配"
                : EffectiveScale == ScaleMode.StretchFill ? "拉伸适配"
                : ScalePercent + "%";
        }

        public string Summary()
        {
            string duplex = Duplex == DuplexMode.LongEdge ? "双面-长边" : Duplex == DuplexMode.ShortEdge ? "双面-短边" : "单面";
            string orient = Orientation == OrientationMode.Portrait ? "纵向" : Orientation == OrientationMode.Landscape ? "横向" : "自动";
            string color = Color == ColorMode.BlackWhite ? "黑白" : "彩色";
            string scale = ScalingText();
            string np = Booklet ? " / 小册子" : (PagesPerSheet > 1 ? " / 每页" + PagesPerSheet + "版" : "");
            string parity = PageParity == PageParityMode.Odd ? " / 仅奇数页" : PageParity == PageParityMode.Even ? " / 仅偶数页" : "";
            string src = string.IsNullOrWhiteSpace(PaperSourceName) ? "" : " / 纸盒:" + PaperSourceName;
            return duplex + " / " + orient + " / " + color + " / " + PaperDisplayName + " / " + scale + np + parity + src;
        }
    }

    public static class FileClassifier
    {
        private static readonly HashSet<string> Images = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".jpe", ".jfif", ".bmp", ".tif", ".tiff", ".gif", ".webp", ".emf", ".wmf" };

        public static bool IsSupported(string path)
        {
            try { return GetKind(path) != null; }
            catch { return false; }
        }

        public static DocKind? GetKind(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            byte[] head = ReadHead(path, 8);
            if (head == null) return null;   // 文件不可读视为不支持

            if (ext == ".pdf") return HasPrefix(head, 0x25, 0x50, 0x44, 0x46, 0x2D) ? DocKind.Pdf : null;          // %PDF-
            if (ext == ".docx") return HasPrefix(head, 0x50, 0x4B, 0x03, 0x04) ? DocKind.Word : null;                // PK\x03\x04
            if (ext == ".xlsx") return HasPrefix(head, 0x50, 0x4B, 0x03, 0x04) ? DocKind.Excel : null;
            if (ext == ".pptx") return HasPrefix(head, 0x50, 0x4B, 0x03, 0x04) ? DocKind.Ppt : null;
            if (ext == ".doc" || ext == ".xls" || ext == ".ppt")
                return HasPrefix(head, 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1) ? (ext == ".doc" ? DocKind.Word : ext == ".xls" ? DocKind.Excel : DocKind.Ppt) : null;   // OLE 复合文档
            if (ext == ".rtf") return HasPrefix(head, 0x7B, 0x5C, 0x72, 0x74, 0x66) ? DocKind.Word : null;           // {\rtf
            // 无固定签名的文本类（txt/csv/odt/ods/odp）按扩展名接受
            if (ext == ".txt") return DocKind.Text;
            if (ext == ".csv") return DocKind.Excel;
            if (ext == ".odt") return DocKind.Word;
            if (ext == ".ods") return DocKind.Excel;
            if (ext == ".odp") return DocKind.Ppt;
            if (Images.Contains(ext)) return HasImageHeader(ext, head) ? DocKind.Image : null;
            return null;
        }

        // Magic Number：常见格式文件头签名校验（V3.1 卫生项）
        private static byte[] ReadHead(string path, int count)
        {
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    byte[] buf = new byte[count];
                    int n = fs.Read(buf, 0, count);
                    return n > 0 ? buf : null;
                }
            }
            catch { return null; }
        }

        private static bool HasPrefix(byte[] head, params byte[] sig)
        {
            if (head == null || head.Length < sig.Length) return false;
            for (int i = 0; i < sig.Length; i++) if (head[i] != sig[i]) return false;
            return true;
        }

        private static bool HasImageHeader(string ext, byte[] h)
        {
            if (ext == ".png") return HasPrefix(h, 0x89, 0x50, 0x4E, 0x47);
            if (ext == ".jpg" || ext == ".jpeg" || ext == ".jpe" || ext == ".jfif") return HasPrefix(h, 0xFF, 0xD8, 0xFF);
            if (ext == ".bmp") return HasPrefix(h, 0x42, 0x4D);
            if (ext == ".gif") return HasPrefix(h, 0x47, 0x49, 0x46, 0x38);
            if (ext == ".tif" || ext == ".tiff") return HasPrefix(h, 0x49, 0x49, 0x2A, 0x00) || HasPrefix(h, 0x4D, 0x4D, 0x00, 0x2A);
            if (ext == ".webp") return HasPrefix(h, 0x52, 0x49, 0x46, 0x46) && h.Length >= 12 && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50;
            // emf/wmf 头结构复杂，按扩展名接受
            if (ext == ".emf" || ext == ".wmf") return true;
            return false;
        }

        public static PrintJob CreateJob(string path)
        {
            DocKind? kind = GetKind(path);
            if (kind == null) return null;
            return new PrintJob { FilePath = path, Kind = kind.Value, Size = new FileInfo(path).Length };
        }

        public static string KindText(DocKind k)
        {
            switch (k)
            {
                case DocKind.Pdf: return "PDF";
                case DocKind.Word: return "Word";
                case DocKind.Excel: return "Excel";
                case DocKind.Ppt: return "PPT";
                case DocKind.Text: return "文本";
                case DocKind.Image: return "图片";
                default: return "其他";
            }
        }
    }

    public class PrintJob
    {
        public string FilePath { get; set; }
        public DocKind Kind { get; set; }
        public long Size { get; set; }
        public int Copies { get; set; } = 1;
        public string RangeText { get; set; }
        public int[] PageIndexes { get; set; }
        public string DisplayName { get; set; }   // 打印队列/日志显示名；为 null 时回退 Name
        /// <summary>F5（V4.4）：该文件自己的参数覆盖（null = 全跟随全局）。份数/页码仍走 Copies/RangeText 两列，
        /// 由 UI 在启动打印前按 EffectiveOptions.Copies/RangeText 合并写入。</summary>
        public PerFileOverride Override { get; set; }
        public string Name { get { return Path.GetFileName(FilePath); } }
        public string Ext { get { return (Path.GetExtension(FilePath) ?? "").ToLowerInvariant(); } }

        /// <summary>
        /// 45 号·改1/改2：本次打印的"版面体检"结果（超界扫描、实际转换程序、转换耗时）。
        /// **只写不读**——打印决策一行都不看它，专供日志行、状态列黄警告与预览窗口显示；不参与序列化、不影响模板与历史。
        /// </summary>
        public PrintDiagnostics Diag { get; set; }
    }

    /// <summary>单文件打印结果记录（R3：打印完成后输出统计表格用）。</summary>
    public sealed class PrintRecord
    {
        public string Name;      // 文件名（含扩展名）
        public string Ext;       // 文件后缀（.xlsx/.doc/.pdf 等）
        public int Copies;       // 份数
        public int Pages;        // 页数（未知记 0）
        public bool Success;     // 打印状态
        public DateTime Time;    // 打印时间
        public string Printer;   // 目标打印机
        public string Engine;    // V3.1：转换引擎（com / minipdf / com(回退)；非 Office 文件记 "-"）
        public string SourceFolder;   // V4.2：打印时源文件所在目录（取不到记空串，历史页旧记录显示 "—"）
    }

    /// <summary>一批打印任务的运行结果（R2 剩余任务 + R3 统计记录）。</summary>
    public sealed class PrintRunResult
    {
        public int Ok;
        public int Fail;
        public List<PrintJob> Remaining = new List<PrintJob>();   // 停止后尚未打印的文件
        public List<PrintRecord> Records = new List<PrintRecord>(); // 每个文件的统计记录
    }

    internal static class PdfiumNative
    {
        private const string Dll = "pdfium.dll";
        private const int FPDF_ANNOT = 0x01;
        private static bool _init;

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDF_InitLibrary();

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDF_LoadDocument([MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string password);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDF_GetPageCount(IntPtr doc);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern float FPDF_GetPageWidthF(IntPtr page);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern float FPDF_GetPageHeightF(IntPtr page);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int width, int height, int rotate, int flags);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDFBitmap_GetStride(IntPtr bitmap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDFBitmap_Destroy(IntPtr bitmap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDF_ClosePage(IntPtr page);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern void FPDF_CloseDocument(IntPtr doc);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        private static extern int FPDF_GetLastError();

        public static void EnsureInit()
        {
            if (_init) return;
            FPDF_InitLibrary();
            _init = true;
        }

        public static int GetPageCount(IntPtr doc) { return FPDF_GetPageCount(doc); }
        public static IntPtr LoadDocument(string path) { return LoadDocument(path, null); }
        public static IntPtr LoadDocument(string path, string password)
        {
            EnsureInit();
            return FPDF_LoadDocument(path, password);
        }
        public static IntPtr LoadPage(IntPtr doc, int index) { return FPDF_LoadPage(doc, index); }
        public static float GetPageWidth(IntPtr page) { return FPDF_GetPageWidthF(page); }
        public static float GetPageHeight(IntPtr page) { return FPDF_GetPageHeightF(page); }
        public static void ClosePage(IntPtr page) { FPDF_ClosePage(page); }
        public static void CloseDocument(IntPtr doc) { FPDF_CloseDocument(doc); }
        public static int GetLastError() { return FPDF_GetLastError(); }

        public static Bitmap Render(IntPtr doc, int index, int width, int height)
        {
            IntPtr page = FPDF_LoadPage(doc, index);
            if (page == IntPtr.Zero) throw new Exception("PDF 页面加载失败（错误码 " + FPDF_GetLastError() + "）");
            try
            {
                IntPtr bmp = FPDFBitmap_Create(width, height, 0);
                if (bmp == IntPtr.Zero) throw new Exception("创建渲染位图失败");
                try
                {
                    FPDFBitmap_FillRect(bmp, 0, 0, width, height, 0xFFFFFFFF);
                    FPDF_RenderPageBitmap(bmp, page, 0, 0, width, height, 0, FPDF_ANNOT);
                    return CopyToManaged(bmp, width, height);
                }
                finally { FPDFBitmap_Destroy(bmp); }
            }
            finally { FPDF_ClosePage(page); }
        }

        private static Bitmap CopyToManaged(IntPtr bmp, int width, int height)
        {
            IntPtr src = FPDFBitmap_GetBuffer(bmp);
            int stride = FPDFBitmap_GetStride(bmp);
            var dst = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            BitmapData data = dst.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
            try
            {
                int dstStride = data.Stride;
                int rowBytes = Math.Min(stride, dstStride);
                byte[] row = new byte[rowBytes];
                for (int y = 0; y < height; y++)
                {
                    Marshal.Copy(IntPtr.Add(src, y * stride), row, 0, rowBytes);
                    Marshal.Copy(row, 0, IntPtr.Add(data.Scan0, y * dstStride), rowBytes);
                }
            }
            finally { dst.UnlockBits(data); }
            return dst;
        }
    }

    /// <summary>PDF 需要密码才能打开（加密文档），引导用户输入密码。</summary>
    public sealed class PdfPasswordRequiredException : Exception
    {
        public PdfPasswordRequiredException(string message) : base(message) { }
    }

    public sealed class PdfiumFile : IDisposable
    {
        private IntPtr _doc;
        private bool _disposed;

        private PdfiumFile(IntPtr doc) { _doc = doc; }

        public static PdfiumFile Open(string path) { return Open(path, null); }

        public static PdfiumFile Open(string path, string password)
        {
            IntPtr doc = PdfiumNative.LoadDocument(path, password);
            if (doc == IntPtr.Zero)
            {
                int err = PdfiumNative.GetLastError();
                // pdfium 错误码：1=FPDF_ERR_SUCCESS, 2=FPDF_ERR_PASSWORD(需密码/密码错误), 3=FPDF_ERR_SECURITY(权限不足)
                if (err == 2)
                    throw new PdfPasswordRequiredException(string.IsNullOrEmpty(password)
                        ? "PDF 已加密，需要密码：" + Path.GetFileName(path)
                        : "PDF 密码不正确或已加密（权限不足）：" + Path.GetFileName(path));
                if (err == 3)
                    throw new Exception("PDF 权限不足（不允许打印/复制）：" + Path.GetFileName(path));
                throw new Exception("无法打开 PDF（可能已损坏）：" + Path.GetFileName(path) + "（错误码 " + err + "）");
            }
            return new PdfiumFile(doc);
        }

        public int PageCount { get { return PdfiumNative.GetPageCount(_doc); } }

        public SizeF GetPageSizePt(int index)
        {
            IntPtr page = PdfiumNative.LoadPage(_doc, index);
            if (page == IntPtr.Zero) throw new Exception("PDF 页面加载失败");
            try { return new SizeF(PdfiumNative.GetPageWidth(page), PdfiumNative.GetPageHeight(page)); }
            finally { PdfiumNative.ClosePage(page); }
        }

        public Bitmap RenderPage(int index, float dpi)
        {
            SizeF pt = GetPageSizePt(index);
            int w = Math.Max(1, (int)Math.Round(pt.Width * dpi / 72f));
            int h = Math.Max(1, (int)Math.Round(pt.Height * dpi / 72f));
            const int maxPx = 9600;
            if (w > maxPx || h > maxPx)
            {
                float scale = Math.Min((float)maxPx / w, (float)maxPx / h);
                w = Math.Max(1, (int)(w * scale));
                h = Math.Max(1, (int)(h * scale));
            }
            // M6：限制单页渲染像素总量（约 1200 万像素），超大页面自动降采样，避免内存峰值过高
            const long maxArea = 12000000L;
            long area = (long)w * h;
            if (area > maxArea)
            {
                double f = Math.Sqrt((double)maxArea / area);
                w = Math.Max(1, (int)(w * f));
                h = Math.Max(1, (int)(h * f));
            }
            return PdfiumNative.Render(_doc, index, w, h);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_doc != IntPtr.Zero) { PdfiumNative.CloseDocument(_doc); _doc = IntPtr.Zero; }
        }
    }

    public static class OfficeComPrinter
    {
        private static readonly HashSet<string> SupportedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".doc", ".docx", ".odt", ".rtf", ".xls", ".xlsx", ".ods", ".csv", ".ppt", ".pptx", ".odp", ".txt" };

        public static bool CanHandle(PrintJob job)
        {
            return SupportedExt.Contains(job.Ext);
        }

        /// <summary>
        /// F1：解析实际引擎（pref 取 auto / minipdf / com）。
        /// 返回值含标注：com / com(自动) / com(回退) / minipdf / minipdf(自动-未检测到Office/WPS) / 无COM不可转
        /// </summary>
        public static string ResolveEngine(string pref, string filePath)
        {
            string ext = Path.GetExtension(filePath ?? "").ToLowerInvariant();
            bool minipdfOk = ext == ".docx" || ext == ".xlsx" || ext == ".pptx";
            if (string.Equals(pref, "minipdf", StringComparison.OrdinalIgnoreCase))
                return minipdfOk ? "minipdf" : "com(回退)";
            if (string.Equals(pref, "com", StringComparison.OrdinalIgnoreCase))
                return "com";
            // auto：有 COM 用 COM；无 COM 用 MiniPdf（仅支持格式）；老格式无 COM → 明确不可转
            if (EngineDetector.HasCom) return "com(自动)";
            return minipdfOk ? "minipdf(自动-未检测到Office/WPS)" : "无COM不可转";
        }

        // 兼容旧调用（WinForms 备用工程仍可使用）
        public static bool TryPrint(PrintJob job, BatchPrinter.LogHandler log)
        {
            var (ok, _) = TryPrint(job, null, null, null, log);
            return ok;
        }

        /// <summary>
        /// 无感打印通道（V2.3）+ 双引擎（V3.1）：Office 文档静默转 PDF（Worker 子进程：COM 或 MiniPdf）→ pdfium 直打进队列，零弹窗。
        /// 双面/黑白/纸张/缩放/份数/页码统一生效（pdfium 通道）。
        /// </summary>
        public static (bool success, int pages) TryPrint(PrintJob job, string printer, PrintOptions opt, Func<bool> isCancelled, BatchPrinter.LogHandler log)
            => TryPrint(job, printer, opt, isCancelled, log, "com");

        public static (bool success, int pages) TryPrint(PrintJob job, string printer, PrintOptions opt, Func<bool> isCancelled, BatchPrinter.LogHandler log, string engine)
        {
            if (!CanHandle(job)) return (false, 0);
            try
            {
                return PrintViaPdfExport(job, printer, opt, isCancelled, log, engine);
            }
            catch (Exception ex)
            {
                log("Office/WPS 转 PDF 失败（将按失败处理）：" + ex.Message);
                return (false, 0);
            }
        }

        // 无感通道主流程：导出临时 PDF → 复用 pdfium 打印 → 清理临时文件
        private static (bool success, int pages) PrintViaPdfExport(PrintJob job, string printer, PrintOptions opt, Func<bool> isCancelled, BatchPrinter.LogHandler log, string engine)
        {
            if (Cancelled(isCancelled, log)) return (false, 0);
            string tmp = ExportToPdf(job, engine, log);
            if (string.IsNullOrEmpty(tmp) || !File.Exists(tmp)) return (false, 0);
            int pages = 0;
            try { using (var t = PdfiumFile.Open(tmp)) pages = t.PageCount; } catch { }
            // 45 号·改1：转换完成后对生成的 PDF 做一次**只读**版面体检（超界检测），结论只进日志与界面，不参与打印决策。
            // 整段包起来：观测失败绝不能变成打印失败（扫描本身已内部吞异常，这里连日志回调一起兜）。
            try
            {
                var d = DiagOf(job);
                d.Pages = pages;
                d.Overflow = PageOverflowScanner.Scan(tmp);
                string shown = job.DisplayName ?? job.Name;
                log(JobLog.ConversionDone(shown, d.EngineName, d.ConvertSeconds, d.Overflow));
                foreach (string w in JobLog.OverflowWarnings(d.Overflow)) log(w);
                string nf = JobLog.ScanFailedNote(shown, d.Overflow);
                if (nf != null) log(nf);
            }
            catch { }
            try
            {
                var pdfJob = new PrintJob
                {
                    FilePath = tmp,
                    Kind = DocKind.Pdf,
                    Copies = Math.Max(1, job.Copies),
                    RangeText = job.RangeText,   // 全角/半角标点由 pdfium 的 ParseRange 统一解析（兼容 1，3-5）
                    PageIndexes = null,
                    DisplayName = job.Name,      // R1：打印队列/日志显示原文件名（而非 office-<GUID>.pdf）
                    Diag = new PrintDiagnostics { Silent = true },   // 45 号·改2：内层通道不再重复打体检行与作业摘要行
                };
                var bp = new BatchPrinter(printer, opt, log, null, null);
                var result = bp.Run(new List<PrintJob> { pdfJob });
                return (result.Ok >= 1, pages);
            }
            finally
            {
                // V3.2：开启"保留转换产物"时复制中间 PDF 到指定目录（原文件名.引擎名.pdf），否则删除
                KeepOrDeleteConverted(tmp, job, engine, opt, log);
            }
        }

        // V3.2：保留转换产物开关——各引擎转换出的中间 PDF 按 原文件名.引擎名.pdf 落盘，供双引擎保真对比
        private static void KeepOrDeleteConverted(string tmp, PrintJob job, string engine, PrintOptions opt, BatchPrinter.LogHandler log)
        {
            try
            {
                if (opt != null && opt.KeepConvertedPdf && File.Exists(tmp))
                {
                    string dir = string.IsNullOrWhiteSpace(opt.ConvertOutputDirectory)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchPrintCN", "printout")
                        : opt.ConvertOutputDirectory;
                    Directory.CreateDirectory(dir);
                    string ext = Path.GetExtension(job.FilePath ?? "").ToLowerInvariant();
                    bool minipdfOk = ext == ".docx" || ext == ".xlsx" || ext == ".pptx";
                    string actual = string.Equals(engine, "minipdf", StringComparison.OrdinalIgnoreCase) && minipdfOk ? "minipdf" : "com";
                    string baseName = Path.GetFileNameWithoutExtension(job.Name);
                    if (string.IsNullOrWhiteSpace(baseName)) baseName = "converted";
                    string dest = BatchPrinter.UniquePath(Path.Combine(dir, baseName + "." + actual + ".pdf"));
                    File.Copy(tmp, dest);
                    log("已保留转换产物（引擎 " + actual + "）：" + dest);
                }
            }
            catch (Exception ex)
            {
                log("保留转换产物失败：" + ex.Message);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }   // 清理自管临时文件
            }
        }

        /// <summary>45 号·改2：摘要行的"引擎"字段——Office 文件报"哪家程序（通道）"，非 Office 如实报不涉及转换。</summary>
        internal static string EngineForLog(PrintJob job, string channel)
        {
            string app = job != null && job.Diag != null ? job.Diag.EngineName : null;
            if (!string.IsNullOrWhiteSpace(app)) return app + "（" + channel + "）";
            return channel;
        }

        /// <summary>
        /// 45 号·改3：预览用的转换入口——直接复用打印那条 <see cref="ExportToPdf"/>（同一个 ResolveEngine、
        /// 同一个 Worker 子进程、同一套参数拼装），所以"预览所见"与"打印所得"出自同一次转换语义。
        /// 打印路径不调用它、也不读预览的产物，打印行为零改动。
        /// </summary>
        public static string ConvertToPdfForPreview(PrintJob job, string engine, BatchPrinter.LogHandler log)
        {
            return ExportToPdf(job, engine, log);
        }

        /// <summary>45 号：取（没有就建）该文件的观测挂点。只写不读，打印决策不看它。</summary>
        internal static PrintDiagnostics DiagOf(PrintJob job)
        {
            if (job.Diag == null) job.Diag = new PrintDiagnostics();
            return job.Diag;
        }

        /// <summary>
        /// 45 号·改2：从转换子进程的 stdout 里取"实际用了哪家"（Worker 回报一行 <c>ENGINE=…</c>）。
        /// 取不到就返回 null——日志会如实写"未回报"，不猜。
        /// </summary>
        internal static string WorkerEngineName(string stdout)
        {
            if (string.IsNullOrEmpty(stdout)) return null;
            foreach (string raw in stdout.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("ENGINE=", StringComparison.OrdinalIgnoreCase))
                {
                    string v = line.Substring("ENGINE=".Length).Trim();
                    if (v.Length > 0) return v;
                }
            }
            return null;
        }

        // V3.0：导出改为独立 Worker 子进程（PrintPileWorker.exe）；V3.1：--engine 双引擎；F1：auto 自动解析；64 号：发布态 Worker 位于 bin\ 子目录（双路径探测）
        private static string ExportToPdf(PrintJob job, string engine, BatchPrinter.LogHandler log)
        {
            string tmpDir = AppPaths.TmpDir;   // V4.1 便携化：程序目录\tmp\
            try { Directory.CreateDirectory(tmpDir); } catch { log("无法创建临时目录：" + tmpDir); return null; }
            string outPath = Path.Combine(tmpDir, "office-" + Guid.NewGuid().ToString("N") + ".pdf");
            // 64 号：单文件双包布局——发布态 Worker 在 bin\ 子目录；开发态平铺兼容
            string workerFlat = Path.Combine(AppContext.BaseDirectory, "PrintPileWorker.exe");
            string workerBin = Path.Combine(AppContext.BaseDirectory, "bin", "PrintPileWorker.exe");
            string worker = File.Exists(workerFlat) ? workerFlat : (File.Exists(workerBin) ? workerBin : null);
            if (worker == null) { log("未找到转换子进程 PrintPileWorker.exe（已探测 " + workerFlat + " 与 " + workerBin + "），无法转换 Office 文档。"); return null; }
            string kind = job.Kind == DocKind.Excel ? "excel" : job.Kind == DocKind.Ppt ? "ppt" : "word";

            // F1：实际引擎解析（auto/com/minipdf → 含标注的引擎串）
            string resolved = ResolveEngine(engine, job.FilePath);
            if (resolved == "无COM不可转")
            {
                log("引擎：无 COM 可用——未检测到 Office/WPS，且 MiniPdf 不支持 .doc/.xls/.ppt 老格式；建议将文件另存为 docx/xlsx/pptx，或安装 WPS/Office 后重试。");
                return null;
            }
            string actual = resolved.StartsWith("minipdf") ? "minipdf" : "com";
            if (!resolved.StartsWith(actual)) { }
            log("引擎：" + resolved + "；启动转换子进程静默转 PDF…");
            try
            {
                var psi = new ProcessStartInfo(worker)
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    // 45 号·改2：读子进程回报的"实际用了哪家"（Microsoft Word / WPS 文字…）——Worker 平时不写 stdout，
                    // 只多这一行机器可读输出；参数、等待、退出码判定一字未改。
                    RedirectStandardOutput = true,
                    StandardOutputEncoding = new UTF8Encoding(false),
                };
                psi.ArgumentList.Add("--in"); psi.ArgumentList.Add(job.FilePath);
                psi.ArgumentList.Add("--out"); psi.ArgumentList.Add(outPath);
                psi.ArgumentList.Add("--kind"); psi.ArgumentList.Add(kind);
                psi.ArgumentList.Add("--engine"); psi.ArgumentList.Add(actual);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                string engineName = null;
                using (Process p = Process.Start(psi))
                {
                    if (p == null) { log("转换子进程启动失败。"); return null; }
                    if (!p.WaitForExit(90000))
                    {
                        try { p.Kill(); } catch { }
                        log("转换子进程超时（90 秒），已终止该任务。提示：加密的 Office 文档无法自动转换，请先在 Office/WPS 中解锁后另存，再打印。");
                        return null;
                    }
                    try { engineName = OfficeExport.EngineDisplayName(WorkerEngineName(p.StandardOutput.ReadToEnd())); } catch { }
                    log("转换子进程退出码：" + p.ExitCode);
                }
                sw.Stop();
                bool made = File.Exists(outPath) && new FileInfo(outPath).Length > 0;
                if (made)
                {
                    var d = DiagOf(job);
                    d.EngineName = engineName;
                    d.ConvertSeconds = sw.ElapsedMilliseconds / 1000.0;
                }
                return made ? outPath : null;
            }
            catch (Exception ex)
            {
                log("启动转换子进程异常：" + ex.Message);
                return null;
            }
        }

        // M3：段间/文件间取消检查
        private static bool Cancelled(Func<bool> isCancelled, BatchPrinter.LogHandler log)
        {
            if (isCancelled != null && isCancelled())
            {
                log("已由用户请求停止（当前文件打印完即停）。");
                return true;
            }
            return false;
        }
    }

    public partial class BatchPrinter
    {
        public delegate void LogHandler(string line);
        private readonly LogHandler _log;
        private readonly Action<int> _onJobDone;
        private readonly string _printer;
        private readonly PrintOptions _opt;
        private PrintOptions _eff;   // F5（V4.4）：当前 job 的生效参数（PrintOne 里解析，跑完清空）
        private readonly Func<string, string> _askPassword;   // 加密 PDF 密码回调（返回 null/空 = 取消该文件）
        public volatile bool CancelRequested;
        public string ConverterEngine = "com";   // V3.1：目标转换引擎（com / minipdf），由 UI 按设置注入
        public Action<PrintProgress> Progress;   // V4.2.3 G2：结构化进度回调（null 安全；日志文案仍照旧写）

        public BatchPrinter(string printer, PrintOptions options, LogHandler log, Action<int> onJobDone)
            : this(printer, options, log, onJobDone, null) { }

        public BatchPrinter(string printer, PrintOptions options, LogHandler log, Action<int> onJobDone, Func<string, string> askPassword)
        {
            _printer = printer;
            _opt = options ?? new PrintOptions();
            _log = log ?? delegate { };
            _onJobDone = onJobDone ?? delegate { };
            _askPassword = askPassword;
        }

        /// <summary>F5（V4.4）：打印通道统一读这个——有逐文件覆盖时是解析后的副本，否则就是全局实例。</summary>
        private PrintOptions E { get { return _eff ?? _opt; } }

        /// <summary>V4.2：安全取源文件所在目录（路径无效/为空返回空串，不影响打印主流程）。</summary>
        private static string SafeGetDir(string path)
        {
            try { return string.IsNullOrEmpty(path) ? "" : (Path.GetDirectoryName(path) ?? ""); }
            catch { return ""; }
        }

        /// <summary>V4.2.3 G2：向 UI 发结构化进度。回调里的异常一律吞掉——进度显示不得影响打印主流程。</summary>
        private void Report(PrintStage stage, PrintRecord rec, string reason)
        {
            Action<PrintProgress> h = Progress;
            if (h == null) return;
            try
            {
                h(new PrintProgress { Stage = stage, FileName = rec.Name, Engine = rec.Engine, Reason = reason, Copies = rec.Copies, Pages = rec.Pages });
            }
            catch { }
        }

        public PrintRunResult Run(IReadOnlyList<PrintJob> jobs)
        {
            int ok = 0, fail = 0, done = 0;
            var records = new List<PrintRecord>();
            foreach (PrintJob job in jobs)
            {
                if (CancelRequested) { _log("已由用户请求停止，剩余任务不再发送。"); break; }
                string actualEngine = ActualEngine(job);
                var rec = new PrintRecord
                {
                    Name = job.DisplayName ?? job.Name,
                    Ext = Path.GetExtension(job.FilePath) ?? "",
                    Copies = job.Copies,
                    Printer = _printer,
                    Time = DateTime.Now,
                    Engine = actualEngine,
                    SourceFolder = SafeGetDir(job.FilePath)   // V4.2：源文件目录入历史
                };
                try
                {
                    // A-1（V4.4 修整）：这里取的是"该文件的生效摘要"，不是全局摘要——E 属性在 _eff 赋值前会退回全局，
                    // 所以按 job 现算一次 Resolve（纯函数，不改打印行为）。
                    _log("正在打印：" + rec.Name + "（" + FileClassifier.KindText(job.Kind) + "，份数 " + job.Copies + (actualEngine != "-" ? "，引擎 " + actualEngine : "")
                        + (job.Override != null && job.Override.HasAny ? "，本文件自定义参数：" + EffectiveOptions.Resolve(_opt, job.Override).Summary() : "") + "）");
                    Report(PrintStage.Started, rec, null);
                    rec.Pages = PrintOne(job);
                    rec.Success = true;
                    ok++;
                    _log("完成：" + rec.Name);
                    // 45 号·改1：PDF 直入没有"转换完成"这一步，体检就在打印成功后补一次（同样只读、同样只进日志与界面）；
                    // Office 通道已在转换产物上扫过 B 口径，这里只补 A 口径（源文档版面体检）。整段兜异常：观测失败不得变成打印失败。
                    // d.Silent＝Office 无感通道的**内层**那一台 BatchPrinter（打临时 PDF 的那次），它不再重复打一遍体检行与摘要行。
                    bool silent = job.Diag != null && job.Diag.Silent;
                    try
                    {
                        var d = OfficeComPrinter.DiagOf(job);
                        d.Pages = rec.Pages;
                        if (!silent)
                        {
                            if (d.Overflow == null && job.Kind == DocKind.Pdf) d.Overflow = PageOverflowScanner.Scan(job.FilePath);
                            if (d.SourceLayout == null) d.SourceLayout = SourceLayoutProbe.Probe(job.FilePath, job.Kind);
                            if (job.Kind == DocKind.Pdf && d.Overflow != null) _log(JobLog.LayoutProbe(rec.Name, d.Overflow));
                            foreach (string w in JobLog.OverflowWarnings(d.Overflow)) _log(w);
                            string sl = JobLog.SourceLayoutWarning(d.SourceLayout);
                            if (sl != null) _log(sl);
                            if (job.Kind == DocKind.Pdf)
                            {
                                string nf = JobLog.ScanFailedNote(rec.Name, d.Overflow);
                                if (nf != null) _log(nf);
                            }
                        }
                    }
                    catch { }
                    // 45 号·改2：逐文件作业摘要行（既有"完成：{名}"一行未改，本行新增）
                    if (!silent)
                    {
                        try
                        {
                            _log(JobLog.Summary(rec.Name, FileClassifier.KindText(job.Kind), OfficeComPrinter.EngineForLog(job, actualEngine), rec.Pages,
                                EffectiveOptions.Resolve(_opt, job.Override), job.Copies, _printer, "成功"));
                        }
                        catch { }
                    }
                    Report(PrintStage.Completed, rec, null);
                }
                catch (Exception ex)
                {
                    fail++;
                    rec.Success = false;
                    _log("失败：" + rec.Name + " → " + ex.Message);
                    if (job.Diag == null || !job.Diag.Silent)
                    {
                        try
                        {
                            _log(JobLog.Summary(rec.Name, FileClassifier.KindText(job.Kind), OfficeComPrinter.EngineForLog(job, actualEngine), rec.Pages,
                                EffectiveOptions.Resolve(_opt, job.Override), job.Copies, _printer, "失败"));
                        }
                        catch { }
                    }
                    Report(PrintStage.Failed, rec, ex.Message);
                }
                records.Add(rec);
                done++;
                _onJobDone(done);
            }
            var result = new PrintRunResult { Ok = ok, Fail = fail, Records = records };
            result.Remaining = jobs.Skip(done).ToList();
            return result;
        }

        // F1：计算该文件实际使用的转换引擎（含自动检测标注；非 Office 文件记 "-"）
        private string ActualEngine(PrintJob job)
        {
            if (!OfficeComPrinter.CanHandle(job)) return "-";
            return OfficeComPrinter.ResolveEngine(ConverterEngine, job.FilePath);
        }

        private int PrintOne(PrintJob job)
        {
            _eff = EffectiveOptions.Resolve(_opt, job.Override);   // F5（V4.4）：本文件的生效参数（无覆盖即全局实例本身）
            try
            {
                switch (job.Kind)
                {
                    case DocKind.Pdf:
                        return PrintPdfFile(job.FilePath, job);
                    case DocKind.Image:
                        return PrintImage(job);
                    default:
                        return PrintOfficeDocument(job);
                }
            }
            finally { _eff = null; }
        }

        private void ApplyPrinter(PrintDocument doc, PrintJob job)
        {
            doc.PrintController = new StandardPrintController();
            doc.DefaultPageSettings.Margins = new Margins(0, 0, 0, 0);
            if (!string.IsNullOrWhiteSpace(_printer))
            {
                string[] installed = PrinterSettings.InstalledPrinters.Cast<string>().ToArray();
                if (Array.IndexOf(installed, _printer) < 0)
                    throw new Exception("找不到打印机：" + _printer);
                doc.PrinterSettings.PrinterName = _printer;
            }
            if (job.Copies > 1) doc.PrinterSettings.Copies = (short)Math.Min(999, Math.Max(1, job.Copies));
            try
            {
                if (E.Duplex == DuplexMode.LongEdge) doc.PrinterSettings.Duplex = Duplex.Vertical;
                else if (E.Duplex == DuplexMode.ShortEdge) doc.PrinterSettings.Duplex = Duplex.Horizontal;
                else doc.PrinterSettings.Duplex = Duplex.Simplex;
            }
            catch { }
            try
            {
                PaperSize size = doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                    .FirstOrDefault(p => p.Kind == E.Paper);
                if (size != null) doc.DefaultPageSettings.PaperSize = size;
            }
            catch { }

            // F6（V4.3）：纸盒选择——按名称匹配当前打印机的 PaperSources；找不到/为空（自动）则不设置，
            // 走打印机默认纸盒，绝不抛异常影响打印主流程。F5（V4.4）起按各文件生效值取。
            try
            {
                if (!string.IsNullOrWhiteSpace(E.PaperSourceName))
                {
                    PaperSource src = doc.PrinterSettings.PaperSources.Cast<PaperSource>()
                        .FirstOrDefault(p => string.Equals(p.SourceName, E.PaperSourceName, StringComparison.OrdinalIgnoreCase));
                    if (src != null) doc.DefaultPageSettings.PaperSource = src;
                    else _log("未找到纸盒「" + E.PaperSourceName + "」，使用打印机默认纸盒。");
                }
            }
            catch { }

            // V3.2：Microsoft Print to PDF 自动落盘（零弹窗）
            TrySetPrintToFile(doc, job);
        }

        /// <summary>
        /// V3.2：所选打印机为 Microsoft Print to PDF 时，设置 PrintToFile + PrintFileName，
        /// 按 原文件名.pdf 自动命名（重名加序号）保存到可配置目录，全程零弹窗；
        /// 路径失败（不支持/异常）则回退正常系统弹窗并记日志。
        /// </summary>
        private void TrySetPrintToFile(PrintDocument doc, PrintJob job)
        {
            if (string.IsNullOrWhiteSpace(_printer)) return;
            if (_printer.IndexOf("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase) < 0) return;
            try
            {
                string dir = string.IsNullOrWhiteSpace(_opt.PrintoutDirectory)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchPrintCN", "printout")
                    : _opt.PrintoutDirectory;
                Directory.CreateDirectory(dir);
                string baseName = Path.GetFileNameWithoutExtension(job.DisplayName ?? job.Name);
                if (string.IsNullOrWhiteSpace(baseName)) baseName = "printout";
                string dest = UniquePath(Path.Combine(dir, baseName + ".pdf"));
                doc.PrinterSettings.PrintToFile = true;
                doc.PrinterSettings.PrintFileName = dest;
            }
            catch (Exception ex)
            {
                _log("Print to PDF 自动落盘失败，回退系统保存对话框：" + ex.Message);
                try { doc.PrinterSettings.PrintToFile = false; } catch { }
            }
        }

        internal static string UniquePath(string path)
        {
            if (!File.Exists(path)) return path;
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 2; ; i++)
            {
                string cand = Path.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(cand)) return cand;
            }
        }

        /// <summary>
        /// V4.9 修整·修1：解析入口的全角归一化——只改字符形态（全角数字 ０-９ → 半角、全角减号 － → 半角），
        /// 分隔符与其余判定一律不动。原来 int.TryParse 拒绝全角数字，用户填「３－５」等于没填（静默丢掉），
        /// 这里只做"多认对"，不引入任何"认错"：归一化后仍交给同一套 TryParse/区间逻辑。
        /// </summary>
        internal static string NormalizeRangeInput(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= '０' && c <= '９') sb.Append((char)('0' + (c - '０')));
                else if (c == '－') sb.Append('-');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>V4.9 修整·修3：原输入里是否含全角数字/全角减号（＝这一条范围是靠归一化才认出来的，气泡要加注）。</summary>
        public static bool UsesFullWidthInput(string text)
        {
            return !string.IsNullOrEmpty(text) && NormalizeRangeInput(text) != text;
        }

        /// <summary>
        /// V4.9 修整·修2/3/5：范围文本 → 1 基页码（升序去重，不按总页数过滤）。
        /// ParseRange 与本函数共用这一份解析（引擎打的与屏上说的同源），呈现层由此知道"解析出几页、几个超出"。
        /// 上限护栏：单个区间最多展开到 ParsedPageCap——用户连打成 "1-999999999" 时，逐键联动的解析
        /// 不能去构造十亿元素集合（打印阶段原来也有这个隐患，只是没被逐键触发放大）。真实文档页数远小于该值，
        /// 过滤后结果与不设限完全一致，故打印行为零变化。
        /// </summary>
        public const int ParsedPageCap = 100000;

        public static List<int> ParsedPages(string text)
        {
            var set = new SortedSet<int>();
            foreach (string part in NormalizeRangeInput(text ?? "").Split(new[] { ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.Contains('-'))
                {
                    string[] se = p.Split('-');
                    int a, b;
                    if (se.Length == 2 && int.TryParse(se[0], out a) && int.TryParse(se[1], out b))
                    {
                        int lo = Math.Max(1, Math.Min(a, b)), hi = Math.Min(ParsedPageCap, Math.Max(a, b));
                        for (int i = lo; i <= hi; i++) set.Add(i);
                    }
                }
                else
                {
                    int n;
                    if (int.TryParse(p, out n) && n >= 1 && n <= ParsedPageCap) set.Add(n);
                }
            }
            return set.ToList();
        }

        public static int[] ParseRange(string text, int total)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            return ParsedPages(text).Where(n => n <= total).Select(n => n - 1).ToArray();
        }
    }
}
