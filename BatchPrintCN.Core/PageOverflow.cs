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
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace BatchPrintCN
{
    /// <summary>
    /// 45 号·改1（口径已按需求方拍板调整，原因如下，均已报备）：
    ///
    /// 开工令建议"用 pdfium 取页面对象边界（FPDFPage_GetObject / FPDFPageObj_GetBounds）与页面尺寸比"。实测两处走不通：
    /// ① 随包的这份 pdfium.dll（bblanchon.pdfium，2026-08-31 构建）里 <c>FPDFPageObj_GetBounds</c> 一调用就
    ///    AccessViolation 崩进程（文本对象、路径对象都崩；同族 GetType/GetMatrix 正常，GetRotatedBounds 恒返回 false），
    ///    而 AV 在 .NET 里是"已损坏状态"异常、catch 不住——用它等于给打印路径埋一颗必炸的雷；
    /// ② 更关键：拿附7 原件用同一条 Worker 通道实转，解析产物发现几何范围正好 0..595.3×0..841.9pt（=210×297mm），
    ///    **越出页面框 0.0mm**——超宽部分在 Word/WPS 转换那一刻就被裁掉了，PDF 里根本不存在"越界对象"。
    ///    （同案源 docx 实测：版心宽 184.6mm，表格栅格合计 189.8mm，超 5.2mm；PDF 里确有 68 个矩形画到 x&gt;197mm。）
    ///
    /// 故本文件改成量**两件事**（需求方拍板"两口径都做"）：
    /// A. <see cref="SourceLayoutProbe"/>：读源文档版面参数（docx 的页面宽/页边距 vs 表格栅格合计）——直接命中附7 病因；
    /// B. 本类的 PDF 墨迹体检：渲染原始页面框、扫像素求实际有墨范围，报两级结论——
    ///    ①真越出页面框（这类文档 PDF 里确实画到框外，照原样打必被裁）；
    ///    ②进入页边"不可打印带"（距页边 &lt; 5mm；多数激光/喷墨打印机这里打不出来，附7 属这一级）。
    ///
    /// 红线：全程只读。渲染只在这份内存副本里发生，文档从不 Save、不落盘，磁盘文件一字不改；
    /// 检测结论只喂日志、状态列黄警告与预览标注，**不参与任何打印决策**（不自动缩放、不代改文档）。
    /// </summary>
    internal static class PdfiumProbeNative
    {
        private const string Dll = "pdfium.dll";

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFPage_GetMediaBox(IntPtr page, out float left, out float bottom, out float right, out float top);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int width, int height, int rotate, int flags);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern int FPDFBitmap_GetStride(IntPtr bitmap);

        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
        public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

        /// <summary>把页面（按给定框，单位 pt）渲染成 32bpp 位图字节（BGRA），并给出宽高与跨距。</summary>
        public static byte[] Render(IntPtr page, double leftPt, double bottomPt, double rightPt, double topPt,
                                    double dpi, out int width, out int height, out int stride)
        {
            width = Math.Max(1, (int)Math.Ceiling((rightPt - leftPt) * dpi / 72.0));
            height = Math.Max(1, (int)Math.Ceiling((topPt - bottomPt) * dpi / 72.0));
            const long maxArea = 6000000L;      // 异常大的页面按像素上限缩（检测不该吃满内存）
            long area = (long)width * height;
            if (area > maxArea)
            {
                double f = Math.Sqrt((double)maxArea / area);
                width = Math.Max(1, (int)(width * f));
                height = Math.Max(1, (int)(height * f));
            }
            stride = 0;
            IntPtr bmp = FPDFBitmap_Create(width, height, 0);
            if (bmp == IntPtr.Zero) return null;
            try
            {
                FPDFBitmap_FillRect(bmp, 0, 0, width, height, 0xFFFFFFFF);
                FPDF_RenderPageBitmap(bmp, page, 0, 0, width, height, 0, 0x01);   // FPDF_ANNOT：批注也算内容
                stride = FPDFBitmap_GetStride(bmp);
                IntPtr buf = FPDFBitmap_GetBuffer(bmp);
                if (buf == IntPtr.Zero || stride <= 0) return null;
                byte[] px = new byte[stride * height];
                Marshal.Copy(buf, px, 0, px.Length);
                return px;
            }
            finally { FPDFBitmap_Destroy(bmp); }
        }
    }

    /// <summary>一页的位图（BGRA，自上而下），45 号·改3 预览用。</summary>
    public sealed class PageRaster
    {
        public int Width, Height, Stride;
        public double WidthPt, HeightPt;        // 页面框尺寸（pt），叠加层按它换算毫米
        public byte[] Pixels;
    }

    /// <summary>
    /// 45 号·改3：预览的渲染入口——与打印同一个 pdfium、同一份文档，只读渲染成位图。
    /// 每次自己开关文档句柄（不与打印线程共用句柄），只在工作线程之外的 UI 线程调用。
    /// </summary>
    public static class PdfRaster
    {
        public static PageRaster Rasterize(string pdfPath, int pageIndex, double dpi)
        {
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)) return null;
            IntPtr doc = IntPtr.Zero;
            try
            {
                doc = PdfiumNative.LoadDocument(pdfPath);
                if (doc == IntPtr.Zero) return null;
                IntPtr page = PdfiumNative.LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return null;
                try
                {
                    float l, b, r, t;
                    if (PdfiumProbeNative.FPDFPage_GetMediaBox(page, out l, out b, out r, out t) == 0 || !(r > l) || !(t > b))
                    {
                        l = 0; b = 0; r = PdfiumNative.GetPageWidth(page); t = PdfiumNative.GetPageHeight(page);
                    }
                    int w, h, stride;
                    byte[] px = PdfiumProbeNative.Render(page, l, b, r, t, dpi, out w, out h, out stride);
                    if (px == null) return null;
                    return new PageRaster { Width = w, Height = h, Stride = stride, Pixels = px, WidthPt = r - l, HeightPt = t - b };
                }
                finally { PdfiumNative.ClosePage(page); }
            }
            catch { return null; }
            finally { if (doc != IntPtr.Zero) PdfiumNative.CloseDocument(doc); }
        }

        /// <summary>
        /// 只取某一页的页面框尺寸（pt），不出图——预览要先按可视区算"适合页面"的比例，
        /// 再按那个比例渲染一次，避免先按 100% 白画一遍。
        /// </summary>
        public static bool PageSize(string pdfPath, int pageIndex, out double widthPt, out double heightPt)
        {
            widthPt = 0; heightPt = 0;
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)) return false;
            IntPtr doc = IntPtr.Zero;
            try
            {
                doc = PdfiumNative.LoadDocument(pdfPath);
                if (doc == IntPtr.Zero) return false;
                IntPtr page = PdfiumNative.LoadPage(doc, pageIndex);
                if (page == IntPtr.Zero) return false;
                try
                {
                    float l, b, r, t;
                    if (PdfiumProbeNative.FPDFPage_GetMediaBox(page, out l, out b, out r, out t) == 0 || !(r > l) || !(t > b))
                    {
                        l = 0; b = 0; r = PdfiumNative.GetPageWidth(page); t = PdfiumNative.GetPageHeight(page);
                    }
                    widthPt = r - l; heightPt = t - b;
                    return widthPt > 0 && heightPt > 0;
                }
                finally { PdfiumNative.ClosePage(page); }
            }
            catch { return false; }
            finally { if (doc != IntPtr.Zero) PdfiumNative.CloseDocument(doc); }
        }

        /// <summary>页数（打不开返回 0，调用方按"渲染不出来"处理，不谎报）。</summary>
        public static int PageCount(string pdfPath)
        {
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)) return 0;
            IntPtr doc = IntPtr.Zero;
            try
            {
                doc = PdfiumNative.LoadDocument(pdfPath);
                return doc == IntPtr.Zero ? 0 : PdfiumNative.GetPageCount(doc);
            }
            catch { return 0; }
            finally { if (doc != IntPtr.Zero) PdfiumNative.CloseDocument(doc); }
        }
    }

    /// <summary>超界的边。</summary>
    public enum OverflowEdge { Right = 0, Left = 1, Top = 2, Bottom = 3 }

    /// <summary>这一页的超界属于哪一级（附7 属 NearEdge：内容被转换器裁到页边）。</summary>
    public enum PageOverflowKind { None = 0, NearEdge = 1, BeyondPage = 2 }

    /// <summary>一页的版面体检结论（毫米；一位小数的写法由输出层统一）。</summary>
    public sealed class PageBoundsReport
    {
        public int Page;                                  // 1-based（给人看的那一页）
        public int Index;                                 // 0-based（pdfium 页序）
        public double WidthMm, HeightMm;                  // 页面框尺寸（MediaBox）

        /// <summary>真越出页面框的量（PDF 里确实画到框外）。</summary>
        public double OverRightMm, OverLeftMm, OverTopMm, OverBottomMm;

        /// <summary>进入页边"不可打印带"的深度（墨迹距页边小于带宽时的差额；0 = 没进带）。</summary>
        public double IntoRightMm, IntoLeftMm, IntoTopMm, IntoBottomMm;

        /// <summary>墨迹四边到页边的留白（毫米；负数＝已越出页面框）。</summary>
        public double GapRightMm, GapLeftMm, GapTopMm, GapBottomMm;

        /// <summary>真越界优先于贴边：只要有一边画到框外，就是 BeyondPage。</summary>
        public PageOverflowKind Kind
        {
            get
            {
                if (MaxOverMm > 0) return PageOverflowKind.BeyondPage;
                if (MaxIntoMm > 0) return PageOverflowKind.NearEdge;
                return PageOverflowKind.None;
            }
        }

        public bool HasOverflow { get { return Kind != PageOverflowKind.None; } }

        /// <summary>真越出页面框的最大值。</summary>
        public double MaxOverMm { get { return Math.Max(Math.Max(OverRightMm, OverLeftMm), Math.Max(OverTopMm, OverBottomMm)); } }

        /// <summary>进入不可打印带的最大值。</summary>
        public double MaxIntoMm { get { return Math.Max(Math.Max(IntoRightMm, IntoLeftMm), Math.Max(IntoTopMm, IntoBottomMm)); } }

        /// <summary>报给人看的那个数：真越界用越界量，否则用进带深度。</summary>
        public double MaxMm { get { return MaxOverMm > 0 ? MaxOverMm : MaxIntoMm; } }

        public OverflowEdge WorstEdge
        {
            get
            {
                double best = -1; OverflowEdge e = OverflowEdge.Right;
                foreach (var kv in EdgesOver())
                {
                    if (kv.Value > best) { best = kv.Value; e = kv.Key; }
                }
                return e;
            }
        }

        /// <summary>边的中文（日志行用；界面文案走资源键，双语口径见 JobLog 的注释）。</summary>
        public static string EdgeText(OverflowEdge e)
        {
            switch (e)
            {
                case OverflowEdge.Right: return "右缘";
                case OverflowEdge.Left: return "左缘";
                case OverflowEdge.Top: return "上缘";
                default: return "下缘";
            }
        }

        public string WorstEdgeText { get { return EdgeText(WorstEdge); } }

        /// <summary>有问题的边（按量从大到小）。真越界与进带共用一个量纲：越界量 / 进带深度。</summary>
        public List<KeyValuePair<OverflowEdge, double>> EdgesOver()
        {
            var list = new List<KeyValuePair<OverflowEdge, double>>();
            Add(list, OverflowEdge.Right, OverRightMm, IntoRightMm);
            Add(list, OverflowEdge.Left, OverLeftMm, IntoLeftMm);
            Add(list, OverflowEdge.Top, OverTopMm, IntoTopMm);
            Add(list, OverflowEdge.Bottom, OverBottomMm, IntoBottomMm);
            list.Sort((a, b) => b.Value.CompareTo(a.Value));
            return list;
        }

        private static void Add(List<KeyValuePair<OverflowEdge, double>> list, OverflowEdge e, double over, double into)
        {
            double v = over > 0 ? over : into;
            if (v > 0) list.Add(new KeyValuePair<OverflowEdge, double>(e, v));
        }
    }

    /// <summary>一份 PDF 的整档扫描结论。</summary>
    public sealed class OverflowScan
    {
        /// <summary>小于这个量的支出不算问题（像素边界有半格误差，细线与四舍五入会给零点几毫米）。</summary>
        public const double ToleranceMm = 0.5;

        /// <summary>页边不可打印带宽度：墨迹距页边小于它就提示"这一带多数打印机打不出来"。
        /// 5mm 是常见激光/喷墨打印机的机械边距量级（附7 原件墨迹正好贴到页边，属这一级）。</summary>
        public const double UnprintableBandMm = 5.0;

        /// <summary>扫描 DPI：144 → 一格约 0.18mm，够报"约 5.2mm"这种一位小数。</summary>
        public const double ScanDpi = 144;

        /// <summary>单次扫描的页数上限——检测不许把打印拖慢；超上限只扫前若干页并如实写进日志。</summary>
        public const int MaxPages = 60;

        public bool Scanned;              // false＝没扫成（打不开/渲染失败），此时不代表"没有超界"
        public string Error;
        public int PageCount;
        public int ScannedPages;
        public bool Truncated;            // 页数超上限，只扫了前 ScannedPages 页
        public double WidthMm, HeightMm;  // 首页页面框（整档一致时就是它）
        public bool MixedSizes;           // 页与页尺寸不一致
        public readonly List<PageBoundsReport> Pages = new List<PageBoundsReport>();   // 只收有问题的页

        public static readonly OverflowScan NotScanned = Fail("未扫描");

        public static OverflowScan Fail(string error) { return new OverflowScan { Scanned = false, Error = error }; }

        public bool HasOverflow { get { return Pages.Count > 0; } }
        public int OverflowPageCount { get { return Pages.Count; } }

        /// <summary>整档里"真越出页面框"的页数（区别于只是贴到不可打印带）。</summary>
        public int BeyondPageCount
        {
            get
            {
                int n = 0;
                foreach (PageBoundsReport p in Pages) if (p.Kind == PageOverflowKind.BeyondPage) n++;
                return n;
            }
        }

        /// <summary>纸张口径文字：认得的用常见名（"A4 竖版（210.0×297.0mm）"），认不出的照实报尺寸。</summary>
        public string PaperText
        {
            get
            {
                if (!Scanned) return "未扫描";
                string s = CommonName(WidthMm, HeightMm);
                string orient = HeightMm > WidthMm + 0.5 ? "竖版" : (WidthMm > HeightMm + 0.5 ? "横版" : "方形");
                string size = Mm(WidthMm) + "×" + Mm(HeightMm) + "mm";
                return (s ?? "自定义") + " " + orient + "（" + size + "）" + (MixedSizes ? "，页尺寸不一致" : "");
            }
        }

        private static string Mm(double v) { return v.ToString("0.0", CultureInfo.InvariantCulture); }

        /// <summary>毫米一位小数的公开写法（日志、界面、预览共用同一口径，免得各处四舍五入不一致）。</summary>
        public static string MmText(double v) { return Mm(v); }

        private static readonly string[][] CommonPaperNames = new[]
        {
            new[] { "A3", "297.0", "420.0" },
            new[] { "A4", "210.0", "297.0" },
            new[] { "A5", "148.0", "210.0" },
            new[] { "B5", "176.0", "250.0" },
            new[] { "Letter", "215.9", "279.4" },
            new[] { "Legal", "215.9", "355.6" },
            new[] { "Tabloid", "279.4", "431.8" },
        };

        /// <summary>常见纸张名（±2mm 内算同一种；长短边都认）。</summary>
        public static string CommonName(double wMm, double hMm)
        {
            foreach (string[] row in CommonPaperNames)
            {
                double a = double.Parse(row[1], CultureInfo.InvariantCulture);
                double b = double.Parse(row[2], CultureInfo.InvariantCulture);
                if ((Near(wMm, a) && Near(hMm, b)) || (Near(wMm, b) && Near(hMm, a))) return row[0];
            }
            return null;
        }

        private static bool Near(double x, double y) { return Math.Abs(x - y) <= 2.0; }
    }

    /// <summary>扫描入口：打开 PDF、逐页量墨迹边界、与页面框与不可打印带比。</summary>
    public static class PageOverflowScanner
    {
        private const double PtToMm = 25.4 / 72.0;

        /// <summary>
        /// 只读扫描。任何异常都收敛成 Scanned=false 的结论——检测失败不能影响打印，也不能谎报"没有超界"。
        /// </summary>
        public static OverflowScan Scan(string pdfPath)
        {
            if (string.IsNullOrEmpty(pdfPath) || !File.Exists(pdfPath)) return OverflowScan.Fail("文件不存在");
            IntPtr doc = IntPtr.Zero;
            try
            {
                doc = PdfiumNative.LoadDocument(pdfPath);
                if (doc == IntPtr.Zero) return OverflowScan.Fail("无法打开 PDF（错误码 " + PdfiumNative.GetLastError() + "）");
                var r = new OverflowScan { Scanned = true, PageCount = PdfiumNative.GetPageCount(doc) };
                int upto = Math.Min(r.PageCount, OverflowScan.MaxPages);
                r.Truncated = r.PageCount > upto;
                double firstW = 0, firstH = 0;
                for (int i = 0; i < upto; i++)
                {
                    PageBoundsReport pr = ScanPage(doc, i);
                    if (pr == null) continue;
                    if (r.ScannedPages == 0) { firstW = pr.WidthMm; firstH = pr.HeightMm; }
                    else if (Math.Abs(pr.WidthMm - firstW) > 1.0 || Math.Abs(pr.HeightMm - firstH) > 1.0) r.MixedSizes = true;
                    r.ScannedPages++;
                    if (pr.HasOverflow) r.Pages.Add(pr);
                }
                if (r.ScannedPages == 0) return OverflowScan.Fail("PDF 没有可扫描的页面");
                r.WidthMm = firstW; r.HeightMm = firstH;
                return r;
            }
            catch (Exception ex) { return OverflowScan.Fail(ex.Message); }
            finally { if (doc != IntPtr.Zero) PdfiumNative.CloseDocument(doc); }
        }

        /// <summary>单页：按原始页面框渲染一遍，扫出实际墨迹边界，与页面框、页边不可打印带各比一次。</summary>
        private static PageBoundsReport ScanPage(IntPtr doc, int index)
        {
            IntPtr page = PdfiumNative.LoadPage(doc, index);
            if (page == IntPtr.Zero) return null;
            try
            {
                float bl, bb, br, bt;
                if (PdfiumProbeNative.FPDFPage_GetMediaBox(page, out bl, out bb, out br, out bt) == 0
                    || !(br > bl) || !(bt > bb))
                {
                    // 取不到 MediaBox 时退回页面可视尺寸（pdfium 已按 CropBox 折算）
                    bl = 0; bb = 0; br = PdfiumNative.GetPageWidth(page); bt = PdfiumNative.GetPageHeight(page);
                }
                var rep = new PageBoundsReport
                {
                    Page = index + 1,
                    Index = index,
                    WidthMm = (br - bl) * PtToMm,
                    HeightMm = (bt - bb) * PtToMm,
                };
                int w, h, stride;
                byte[] px = PdfiumProbeNative.Render(page, bl, bb, br, bt, OverflowScan.ScanDpi, out w, out h, out stride);
                if (px == null || w < 3 || h < 3) return null;
                int minX, maxX, minY, maxY;
                if (!InkBounds(px, w, h, stride, out minX, out maxX, out minY, out maxY)) return null;   // 整页空白：不报问题
                double ptPerPxX = (br - bl) / w;
                double ptPerPxY = (bt - bb) / h;
                double inkLeft = bl + minX * ptPerPxX;
                double inkRight = bl + maxX * ptPerPxX;
                double inkTop = bt - minY * ptPerPxY;
                double inkBottom = bt - maxY * ptPerPxY;
                rep.GapLeftMm = (inkLeft - bl) * PtToMm;
                rep.GapRightMm = (br - inkRight) * PtToMm;
                rep.GapTopMm = (bt - inkTop) * PtToMm;
                rep.GapBottomMm = (inkBottom - bb) * PtToMm;
                rep.OverLeftMm = Over(-rep.GapLeftMm);
                rep.OverRightMm = Over(-rep.GapRightMm);
                rep.OverTopMm = Over(-rep.GapTopMm);
                rep.OverBottomMm = Over(-rep.GapBottomMm);
                rep.IntoLeftMm = Into(rep.GapLeftMm);
                rep.IntoRightMm = Into(rep.GapRightMm);
                rep.IntoTopMm = Into(rep.GapTopMm);
                rep.IntoBottomMm = Into(rep.GapBottomMm);
                return rep;
            }
            finally { PdfiumNative.ClosePage(page); }
        }

        /// <summary>越出页面框的量（毫米，过容差）。</summary>
        private static double Over(double mmBeyond)
        {
            if (!(mmBeyond > 0)) return 0;
            return mmBeyond < OverflowScan.ToleranceMm ? 0 : Math.Round(mmBeyond, 1);
        }

        /// <summary>进入不可打印带的深度（留白小于带宽时的差额，过容差）。</summary>
        private static double Into(double gapMm)
        {
            double into = OverflowScan.UnprintableBandMm - gapMm;
            if (!(into > 0)) return 0;
            return into < OverflowScan.ToleranceMm ? 0 : Math.Round(into, 1);
        }

        /// <summary>扫像素求实际墨迹边界（非白即有墨）；整页无墨返回 false。</summary>
        private static bool InkBounds(byte[] px, int w, int h, int stride, out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = w; maxX = -1; minY = h; maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int i = row + x * 4;
                    if (i + 2 >= px.Length) break;
                    if (px[i] != 0xFF || px[i + 1] != 0xFF || px[i + 2] != 0xFF)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            return maxX >= 0;
        }
    }
}
